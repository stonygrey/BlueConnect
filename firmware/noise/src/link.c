/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* Noise_XX_25519_AESGCM_SHA256 responder on PSA Crypto (Oberon/CryptoCell).
 * Mirrors the host SecureLink. Pairing buttons/LEDs/shell live in pairing_ui.
 * Optional client endorsement (compact Ed25519 blob in XX message 3) is
 * verified here; it never grants privilege.
 */
#include "link.h"
#include "pairing_ui.h"
#include "verdigris.h"

#include <stdint.h>
#include <string.h>

#include <zephyr/kernel.h>
#include <zephyr/logging/log.h>
#include <zephyr/settings/settings.h>

#include <psa/crypto.h>

LOG_MODULE_REGISTER(link);

#define PROTOCOL_NAME "Noise_XX_25519_AESGCM_SHA256"
#define IK_PROTOCOL_NAME "Noise_IK_25519_AESGCM_SHA256"
#define PROLOGUE "BlueConnectLink/1"

#define HASH_LEN 32
#define PAIR_WINDOW_SECONDS 30

/* Handshake messages 2 and 3 carry this single ignored byte instead of an
 * empty payload: the host's Android AES-GCM rejects zero-length inputs.
 */
static const uint8_t hs_payload[1] = { 0x5A };

/* Compact client endorsement (XX message 3 payload when present):
 *   version (1) || subject X25519 public key (32) || Ed25519 signature (64)
 * Signature is PURE_EDDSA over version || subject, under the provisioned
 * CA root. Binding: subject must equal the initiator static just proven.
 */
#define ENDORSEMENT_VERSION 0x01
#define ENDORSEMENT_SIG_LEN 64
#define ENDORSEMENT_LEN (1 + LINK_KEY_LEN + ENDORSEMENT_SIG_LEN)

/* Exact on-wire sizes. XX message 1 is e plus an empty unencrypted payload.
 * A pinned client opens with IK instead (it already knows this device's
 * static key): message 1 is e + enc(s) + enc(payload) — 97 bytes, so the
 * first handshake frame's length selects the pattern unambiguously.
 * XX message 3 is dummy-sized (0x5A) or cert-sized; both are accepted.
 */
#define HS_MSG1_LEN LINK_KEY_LEN
#define HS_MSG2_LEN (LINK_KEY_LEN + LINK_KEY_LEN + LINK_TAG_LEN + \
		     sizeof(hs_payload) + LINK_TAG_LEN)
#define HS_MSG3_LEN (LINK_KEY_LEN + LINK_TAG_LEN + sizeof(hs_payload) + LINK_TAG_LEN)
#define HS_MSG3_CERT_LEN (LINK_KEY_LEN + LINK_TAG_LEN + ENDORSEMENT_LEN + \
			  LINK_TAG_LEN)
#define HS_IK_MSG1_LEN (LINK_KEY_LEN + LINK_KEY_LEN + LINK_TAG_LEN + \
			sizeof(hs_payload) + LINK_TAG_LEN)
#define HS_IK_MSG2_LEN (LINK_KEY_LEN + sizeof(hs_payload) + LINK_TAG_LEN)

static bool key_is_zero(const uint8_t key[LINK_KEY_LEN])
{
	uint8_t acc = 0;

	for (size_t i = 0; i < LINK_KEY_LEN; i++) {
		acc |= key[i];
	}

	return acc == 0;
}

/* ---- Persistent identity and pin (settings: vg/sk, vg/pin_lo, vg/pin_hi) ---- */

static uint8_t static_priv[LINK_KEY_LEN];
static uint8_t static_pub[LINK_KEY_LEN];
static bool have_identity;
/* One pinned client key per privilege tier. */
static uint8_t pin_lo[LINK_KEY_LEN];
static bool have_pin_lo;
static uint8_t pin_hi[LINK_KEY_LEN];
static bool have_pin_hi;
/* USB-provisioned slots are locked: no ceremony may replace or clear them
 * remotely; only USB possession (vg unpair / vg set-pin) touches them.
 */
static bool lock_lo;
static bool lock_hi;
/* Optional endorsement CA root (Ed25519 public). Empty = skip cert checks. */
static uint8_t ca_root[LINK_KEY_LEN];
static bool have_ca_root;

/* ---- Session state (single connection: BT_MAX_CONN=1) ---- */

struct cipher_state {
	psa_key_id_t key; /* 0 = no key */
	uint64_t nonce;
};

struct symmetric_state {
	uint8_t ck[HASH_LEN];
	uint8_t h[HASH_LEN];
	struct cipher_state cs;
};

enum handshake_phase {
	HS_IDLE,
	HS_SENT_MSG2,
	HS_ESTABLISHED,
};

static struct {
	enum handshake_phase phase;
	struct symmetric_state ss;
	psa_key_id_t e_priv;
	uint8_t remote_static[LINK_KEY_LEN];
	struct cipher_state recv; /* initiator -> responder */
	struct cipher_state send; /* responder -> initiator */
	bool endorsed; /* endorsement verified this session; not privilege */
} session;

static int store_save(const char *key, const void *val, size_t len);
static void store_delete(const char *key);

/* ---- Pairing ceremony state ---- */

static K_SEM_DEFINE(pair_confirm, 0, 1);
/* 0 = idle, otherwise LINK_PRIV_LOW / LINK_PRIV_HIGH for the armed window. */
static atomic_t pair_armed;
/* Valid after pair_confirm is given: 1 = matching privilege, 0 = mismatch. */
static atomic_t pair_btn_ok;

/* ---- Small PSA helpers ---- */

static void destroy_key(psa_key_id_t *key)
{
	if (*key) {
		psa_destroy_key(*key);
		*key = 0;
	}
}

static bool sha256(const uint8_t *a, size_t a_len, const uint8_t *b, size_t b_len,
		   uint8_t out[HASH_LEN])
{
	psa_hash_operation_t op = PSA_HASH_OPERATION_INIT;
	size_t out_len;

	if (psa_hash_setup(&op, PSA_ALG_SHA_256) != PSA_SUCCESS ||
	    psa_hash_update(&op, a, a_len) != PSA_SUCCESS ||
	    (b_len && psa_hash_update(&op, b, b_len) != PSA_SUCCESS) ||
	    psa_hash_finish(&op, out, HASH_LEN, &out_len) != PSA_SUCCESS) {
		psa_hash_abort(&op);
		return false;
	}

	return true;
}

static bool hmac_sha256(const uint8_t key[HASH_LEN], const uint8_t *data, size_t data_len,
			uint8_t out[HASH_LEN])
{
	psa_key_attributes_t attr = PSA_KEY_ATTRIBUTES_INIT;
	psa_key_id_t id;
	size_t out_len;
	psa_status_t status;

	psa_set_key_type(&attr, PSA_KEY_TYPE_HMAC);
	psa_set_key_bits(&attr, HASH_LEN * 8);
	psa_set_key_usage_flags(&attr, PSA_KEY_USAGE_SIGN_MESSAGE);
	psa_set_key_algorithm(&attr, PSA_ALG_HMAC(PSA_ALG_SHA_256));

	if (psa_import_key(&attr, key, HASH_LEN, &id) != PSA_SUCCESS) {
		return false;
	}

	status = psa_mac_compute(id, PSA_ALG_HMAC(PSA_ALG_SHA_256), data, data_len,
				 out, HASH_LEN, &out_len);
	psa_destroy_key(id);
	return status == PSA_SUCCESS;
}

/* Noise HKDF: two outputs. */
static bool hkdf2(const uint8_t ck[HASH_LEN], const uint8_t *ikm, size_t ikm_len,
		  uint8_t out1[HASH_LEN], uint8_t out2[HASH_LEN])
{
	uint8_t temp[HASH_LEN];
	uint8_t buf[HASH_LEN + 1];

	if (!hmac_sha256(ck, ikm, ikm_len, temp)) {
		return false;
	}

	buf[0] = 0x01;
	if (!hmac_sha256(temp, buf, 1, out1)) {
		return false;
	}

	memcpy(buf, out1, HASH_LEN);
	buf[HASH_LEN] = 0x02;
	return hmac_sha256(temp, buf, HASH_LEN + 1, out2);
}

static bool import_aead_key(const uint8_t key[LINK_KEY_LEN], psa_key_id_t *id)
{
	psa_key_attributes_t attr = PSA_KEY_ATTRIBUTES_INIT;

	psa_set_key_type(&attr, PSA_KEY_TYPE_AES);
	psa_set_key_bits(&attr, 256);
	psa_set_key_usage_flags(&attr, PSA_KEY_USAGE_ENCRYPT | PSA_KEY_USAGE_DECRYPT);
	psa_set_key_algorithm(&attr, PSA_ALG_GCM);

	return psa_import_key(&attr, key, LINK_KEY_LEN, id) == PSA_SUCCESS;
}

static bool import_x25519(const uint8_t priv[LINK_KEY_LEN], psa_key_id_t *id)
{
	psa_key_attributes_t attr = PSA_KEY_ATTRIBUTES_INIT;

	psa_set_key_type(&attr, PSA_KEY_TYPE_ECC_KEY_PAIR(PSA_ECC_FAMILY_MONTGOMERY));
	psa_set_key_bits(&attr, 255);
	psa_set_key_usage_flags(&attr, PSA_KEY_USAGE_DERIVE | PSA_KEY_USAGE_EXPORT);
	psa_set_key_algorithm(&attr, PSA_ALG_ECDH);

	return psa_import_key(&attr, priv, LINK_KEY_LEN, id) == PSA_SUCCESS;
}

static bool generate_x25519(psa_key_id_t *id, uint8_t pub[LINK_KEY_LEN])
{
	psa_key_attributes_t attr = PSA_KEY_ATTRIBUTES_INIT;
	size_t pub_len;

	psa_set_key_type(&attr, PSA_KEY_TYPE_ECC_KEY_PAIR(PSA_ECC_FAMILY_MONTGOMERY));
	psa_set_key_bits(&attr, 255);
	psa_set_key_usage_flags(&attr, PSA_KEY_USAGE_DERIVE | PSA_KEY_USAGE_EXPORT);
	psa_set_key_algorithm(&attr, PSA_ALG_ECDH);

	if (psa_generate_key(&attr, id) != PSA_SUCCESS) {
		return false;
	}

	return psa_export_public_key(*id, pub, LINK_KEY_LEN, &pub_len) == PSA_SUCCESS &&
	       pub_len == LINK_KEY_LEN;
}

static bool dh(psa_key_id_t priv, const uint8_t peer_pub[LINK_KEY_LEN],
	       uint8_t out[LINK_KEY_LEN])
{
	size_t out_len;

	if (key_is_zero(peer_pub)) {
		return false;
	}

	if (psa_raw_key_agreement(PSA_ALG_ECDH, priv, peer_pub, LINK_KEY_LEN,
				  out, LINK_KEY_LEN, &out_len) != PSA_SUCCESS ||
	    out_len != LINK_KEY_LEN) {
		return false;
	}

	/* Contributory DH: a low-order peer public key yields an all-zero
	 * shared secret (RFC 7748). Refuse it the way the host does.
	 */
	if (key_is_zero(out)) {
		memset(out, 0, LINK_KEY_LEN);
		return false;
	}

	return true;
}

/* RFC 8032: s must be in [0, L). Oberon's PSA PURE_EDDSA path has been
 * observed to take a data bus fault on a non-canonical s (a flipped
 * signature byte) instead of returning PSA_ERROR_INVALID_SIGNATURE.
 */
static const uint8_t ed25519_l[32] = {
	0xed, 0xd3, 0xf5, 0x5c, 0x1a, 0x63, 0x12, 0x58,
	0xd6, 0x9c, 0xf7, 0xa2, 0xde, 0xf9, 0xde, 0x14,
	0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
	0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10,
};

static bool ed25519_scalar_canonical(const uint8_t s[32])
{
	for (int i = 31; i >= 0; i--) {
		if (s[i] < ed25519_l[i]) {
			return true;
		}
		if (s[i] > ed25519_l[i]) {
			return false;
		}
	}
	return false;
}

static bool ed25519_verify(const uint8_t pub[LINK_KEY_LEN], const uint8_t *msg,
			   size_t msg_len, const uint8_t sig[ENDORSEMENT_SIG_LEN])
{
	psa_key_attributes_t attr = PSA_KEY_ATTRIBUTES_INIT;
	psa_key_id_t id;
	psa_status_t status;
	uint8_t pub_copy[LINK_KEY_LEN];
	uint8_t sig_copy[ENDORSEMENT_SIG_LEN];
	uint8_t msg_copy[1 + LINK_KEY_LEN];

	if (msg_len > sizeof(msg_copy) || sig == NULL || pub == NULL || msg == NULL) {
		return false;
	}
	if (!ed25519_scalar_canonical(sig + 32)) {
		return false;
	}

	memcpy(pub_copy, pub, LINK_KEY_LEN);
	memcpy(sig_copy, sig, ENDORSEMENT_SIG_LEN);
	memcpy(msg_copy, msg, msg_len);

	psa_set_key_type(&attr, PSA_KEY_TYPE_ECC_PUBLIC_KEY(PSA_ECC_FAMILY_TWISTED_EDWARDS));
	psa_set_key_bits(&attr, 255);
	psa_set_key_usage_flags(&attr, PSA_KEY_USAGE_VERIFY_MESSAGE);
	psa_set_key_algorithm(&attr, PSA_ALG_PURE_EDDSA);

	if (psa_import_key(&attr, pub_copy, LINK_KEY_LEN, &id) != PSA_SUCCESS) {
		return false;
	}

	status = psa_verify_message(id, PSA_ALG_PURE_EDDSA, msg_copy, msg_len,
				    sig_copy, ENDORSEMENT_SIG_LEN);
	psa_destroy_key(id);
	return status == PSA_SUCCESS;
}

static bool endorsement_verify(const uint8_t cert[ENDORSEMENT_LEN],
				   const uint8_t rs[LINK_KEY_LEN])
{
	const uint8_t *subject = cert + 1;
	const uint8_t *sig = cert + 1 + LINK_KEY_LEN;

	if (memcmp(subject, rs, LINK_KEY_LEN) != 0) {
		LOG_WRN("Endorsement subject does not match the proven static");
		return false;
	}

	if (!ed25519_verify(ca_root, cert, 1 + LINK_KEY_LEN, sig)) {
		LOG_WRN("Endorsement signature is invalid");
		return false;
	}

	return true;
}

/* Dummy 0x5A is always fine (missing cert is not a failure). A cert blob is
 * verified only when a root is loaded; failure aborts. Other payloads fail
 * closed if a root is loaded, and are rejected without a root (today's
 * 0x5A-only handshake).
 */
static bool accept_xx_payload(const uint8_t *payload, size_t len,
			      const uint8_t rs[LINK_KEY_LEN])
{
	if (len == sizeof(hs_payload) && payload[0] == hs_payload[0]) {
		session.endorsed = false;
		return true;
	}

	if (!have_ca_root) {
		return false;
	}

	if (len != ENDORSEMENT_LEN || payload[0] != ENDORSEMENT_VERSION) {
		LOG_WRN("Handshake payload is not dummy and not a cert; aborting");
		return false;
	}

	if (!endorsement_verify(payload, rs)) {
		LOG_WRN("Endorsement failed verification; aborting handshake");
		return false;
	}

	session.endorsed = true;
	LOG_INF("Endorsement verified (endorsed, no privilege granted)");
	return true;
}

/* ---- Noise symmetric state ---- */

static void write_be64(uint8_t nonce[12], uint64_t n)
{
	memset(nonce, 0, 4);
	for (int i = 0; i < 8; i++) {
		nonce[4 + i] = (uint8_t)(n >> (8 * (7 - i)));
	}
}

static bool cs_encrypt(struct cipher_state *cs, const uint8_t *ad, size_t ad_len,
		       const uint8_t *pt, size_t pt_len, uint8_t *out, size_t *out_len)
{
	uint8_t nonce[12];
	size_t written;

	if (!cs->key) {
		/* Pre-key payloads are empty in this protocol. */
		if (pt_len != 0) {
			return false;
		}
		*out_len = 0;
		return true;
	}

	if (cs->nonce == UINT64_MAX) {
		return false;
	}

	write_be64(nonce, cs->nonce);
	if (psa_aead_encrypt(cs->key, PSA_ALG_GCM, nonce, sizeof(nonce), ad, ad_len,
			     pt, pt_len, out, pt_len + LINK_TAG_LEN, &written) != PSA_SUCCESS) {
		return false;
	}

	cs->nonce++;
	*out_len = written;
	return true;
}

static bool cs_decrypt(struct cipher_state *cs, const uint8_t *ad, size_t ad_len,
		       const uint8_t *ct, size_t ct_len, uint8_t *out, size_t *out_len)
{
	uint8_t nonce[12];
	size_t written;

	if (!cs->key) {
		/* Pre-key payloads are empty. A non-empty remainder used to
		 * memcpy attacker-controlled bytes into a 32-byte scratch.
		 */
		if (ct_len != 0) {
			return false;
		}
		*out_len = 0;
		return true;
	}

	if (ct_len < LINK_TAG_LEN || cs->nonce == UINT64_MAX) {
		return false;
	}

	write_be64(nonce, cs->nonce);
	if (psa_aead_decrypt(cs->key, PSA_ALG_GCM, nonce, sizeof(nonce), ad, ad_len,
			     ct, ct_len, out, ct_len - LINK_TAG_LEN, &written) != PSA_SUCCESS) {
		return false;
	}

	cs->nonce++;
	*out_len = written;
	return true;
}

static void ss_init(struct symmetric_state *ss, const char *protocol_name)
{
	size_t name_len = strlen(protocol_name);

	memset(ss, 0, sizeof(*ss));
	memcpy(ss->h, protocol_name, name_len); /* name <= 32 bytes: pad with zeros */
	memcpy(ss->ck, ss->h, HASH_LEN);
}

static bool ss_mix_hash(struct symmetric_state *ss, const uint8_t *data, size_t len)
{
	return sha256(ss->h, HASH_LEN, data, len, ss->h);
}

static bool ss_mix_key(struct symmetric_state *ss, const uint8_t ikm[LINK_KEY_LEN])
{
	uint8_t temp_k[HASH_LEN];

	if (!hkdf2(ss->ck, ikm, LINK_KEY_LEN, ss->ck, temp_k)) {
		return false;
	}

	destroy_key(&ss->cs.key);
	ss->cs.nonce = 0;
	bool ok = import_aead_key(temp_k, &ss->cs.key);

	memset(temp_k, 0, sizeof(temp_k));
	return ok;
}

static bool ss_encrypt_and_hash(struct symmetric_state *ss, const uint8_t *pt, size_t pt_len,
				uint8_t *out, size_t *out_len)
{
	if (!cs_encrypt(&ss->cs, ss->h, HASH_LEN * (ss->cs.key ? 1 : 0), pt, pt_len, out, out_len)) {
		return false;
	}

	return ss_mix_hash(ss, out, *out_len);
}

static bool ss_decrypt_and_hash(struct symmetric_state *ss, const uint8_t *ct, size_t ct_len,
				uint8_t *out, size_t *out_len)
{
	if (!cs_decrypt(&ss->cs, ss->h, HASH_LEN * (ss->cs.key ? 1 : 0), ct, ct_len, out, out_len)) {
		return false;
	}

	return ss_mix_hash(ss, ct, ct_len);
}

/* ---- Session lifecycle ---- */

void link_reset(void)
{
	destroy_key(&session.ss.cs.key);
	destroy_key(&session.e_priv);
	destroy_key(&session.recv.key);
	destroy_key(&session.send.key);
	memset(&session, 0, sizeof(session));
	atomic_clear(&pair_armed);
}

bool link_session_active(void)
{
	return session.phase == HS_ESTABLISHED;
}

const uint8_t *link_remote_key(void)
{
	return session.phase == HS_ESTABLISHED ? session.remote_static : NULL;
}

bool link_is_paired(void)
{
	return have_pin_lo || have_pin_hi;
}

/* The advertised status bitmap: slot bits; the scheme bit stays clear —
 * clear means Noise XX.
 */
static uint8_t secured_flags(void)
{
	return (have_pin_lo ? LINK_STATUS_LOW_SLOT : 0) |
	       (have_pin_hi ? LINK_STATUS_HIGH_SLOT : 0);
}

enum link_privilege link_privilege(void)
{
	if (session.phase != HS_ESTABLISHED) {
		return LINK_PRIV_NONE;
	}

	if (have_pin_hi && memcmp(session.remote_static, pin_hi, LINK_KEY_LEN) == 0) {
		return LINK_PRIV_HIGH;
	}

	if (have_pin_lo && memcmp(session.remote_static, pin_lo, LINK_KEY_LEN) == 0) {
		return LINK_PRIV_LOW;
	}

	return LINK_PRIV_NONE;
}

/* ---- Handshake ---- */

static bool handshake_msg1(const uint8_t *in, size_t in_len,
			   uint8_t *out, size_t out_cap, size_t *out_len)
{
	uint8_t e_pub[LINK_KEY_LEN];
	uint8_t shared[LINK_KEY_LEN];
	size_t enc_len, payload_len, total = 0;
	uint8_t scratch[HASH_LEN];
	size_t scratch_len;

	if (in_len != HS_MSG1_LEN || out_cap < HS_MSG2_LEN) {
		return false;
	}

	ss_init(&session.ss, PROTOCOL_NAME);
	if (!ss_mix_hash(&session.ss, (const uint8_t *)PROLOGUE, strlen(PROLOGUE))) {
		return false;
	}

	/* -> e (+ empty payload, unencrypted pre-key) */
	uint8_t re[LINK_KEY_LEN];
	memcpy(re, in, LINK_KEY_LEN);
	if (key_is_zero(re) ||
	    !ss_mix_hash(&session.ss, re, LINK_KEY_LEN) ||
	    !ss_decrypt_and_hash(&session.ss, in + LINK_KEY_LEN, 0,
				 scratch, &scratch_len)) {
		return false;
	}

	/* <- e, ee, s, es (+ 0x5A encrypted payload) */
	if (!generate_x25519(&session.e_priv, e_pub) ||
	    !ss_mix_hash(&session.ss, e_pub, LINK_KEY_LEN)) {
		return false;
	}
	memcpy(out, e_pub, LINK_KEY_LEN);
	total = LINK_KEY_LEN;

	if (!dh(session.e_priv, re, shared) || !ss_mix_key(&session.ss, shared)) {
		memset(shared, 0, sizeof(shared));
		return false;
	}

	if (!ss_encrypt_and_hash(&session.ss, static_pub, LINK_KEY_LEN, out + total, &enc_len)) {
		return false;
	}
	total += enc_len;

	psa_key_id_t s_priv;
	if (!import_x25519(static_priv, &s_priv)) {
		return false;
	}
	bool ok = dh(s_priv, re, shared);
	psa_destroy_key(s_priv);
	if (!ok || !ss_mix_key(&session.ss, shared)) {
		memset(shared, 0, sizeof(shared));
		return false;
	}

	if (!ss_encrypt_and_hash(&session.ss, hs_payload, sizeof(hs_payload),
				 out + total, &payload_len)) {
		return false;
	}
	total += payload_len;

	memset(shared, 0, sizeof(shared));
	*out_len = total;
	session.phase = HS_SENT_MSG2;
	return true;
}

static bool handshake_msg3(const uint8_t *in, size_t in_len)
{
	uint8_t shared[LINK_KEY_LEN];
	uint8_t rs[LINK_KEY_LEN];
	uint8_t payload[ENDORSEMENT_LEN];
	size_t rs_len, payload_len, payload_ct_len;
	uint8_t k1[HASH_LEN], k2[HASH_LEN];
	const size_t static_ct = LINK_KEY_LEN + LINK_TAG_LEN;

	if (in_len < HS_MSG3_LEN) {
		return false;
	}

	payload_ct_len = in_len - static_ct;
	if (payload_ct_len < LINK_TAG_LEN ||
	    payload_ct_len - LINK_TAG_LEN > ENDORSEMENT_LEN) {
		return false;
	}

	/* -> s, se (+ dummy 0x5A or endorsement) */
	if (!ss_decrypt_and_hash(&session.ss, in, static_ct, rs, &rs_len) ||
	    rs_len != LINK_KEY_LEN || key_is_zero(rs)) {
		return false;
	}

	if (!dh(session.e_priv, rs, shared) || !ss_mix_key(&session.ss, shared)) {
		memset(shared, 0, sizeof(shared));
		return false;
	}

	if (!ss_decrypt_and_hash(&session.ss, in + static_ct, payload_ct_len,
				 payload, &payload_len)) {
		memset(shared, 0, sizeof(shared));
		return false;
	}

	if (!accept_xx_payload(payload, payload_len, rs)) {
		memset(shared, 0, sizeof(shared));
		return false;
	}

	/* Any proven key may hold a session; the dispatcher gates every command
	 * by the key's privilege slot (an unknown key may only Pair). This is
	 * what turns a mismatched pairing into an immediate, explicit error for
	 * the client instead of a silent drop and a timeout. A verified
	 * endorsement marks the session endorsed and does not change this.
	 */

	/* Split. First key = initiator->responder. */
	if (!hkdf2(session.ss.ck, NULL, 0, k1, k2)) {
		return false;
	}

	bool ok = import_aead_key(k1, &session.recv.key) &&
		  import_aead_key(k2, &session.send.key);
	memset(k1, 0, sizeof(k1));
	memset(k2, 0, sizeof(k2));
	if (!ok) {
		return false;
	}

	memcpy(session.remote_static, rs, LINK_KEY_LEN);
	destroy_key(&session.e_priv);
	destroy_key(&session.ss.cs.key);
	memset(shared, 0, sizeof(shared));
	session.phase = HS_ESTABLISHED;
	LOG_INF("Secure session established (privilege %d endorsed %d)",
		link_privilege(), session.endorsed);
	return true;
}

/* A pinned client's abbreviated handshake (Noise IK): its message 1 both
 * proves the client's key and shows it already holds ours, and our reply
 * completes the session — one round trip instead of two, and this device's
 * static key never travels. A client whose pin is stale (this device was
 * wiped or rotated) fails the first decrypt and gets silence, exactly like
 * any forged frame; its app surfaces the reset from the advertisement.
 */
static bool handshake_ik(const uint8_t *in, size_t in_len,
			 uint8_t *out, size_t out_cap, size_t *out_len)
{
	uint8_t e_pub[LINK_KEY_LEN];
	uint8_t re[LINK_KEY_LEN];
	uint8_t rs[LINK_KEY_LEN];
	uint8_t shared[LINK_KEY_LEN];
	uint8_t scratch[HASH_LEN];
	uint8_t k1[HASH_LEN], k2[HASH_LEN];
	size_t rs_len, scratch_len, enc_len, total = 0;
	psa_key_id_t s_priv;
	bool ok;

	if (in_len != HS_IK_MSG1_LEN || out_cap < HS_IK_MSG2_LEN) {
		return false;
	}

	ss_init(&session.ss, IK_PROTOCOL_NAME);
	if (!ss_mix_hash(&session.ss, (const uint8_t *)PROLOGUE, strlen(PROLOGUE))) {
		return false;
	}

	/* Pre-message: <- s (this device's static, known to the initiator). */
	if (!ss_mix_hash(&session.ss, static_pub, LINK_KEY_LEN)) {
		return false;
	}

	/* -> e, es, s, ss (+ 0x5A encrypted payload) */
	memcpy(re, in, LINK_KEY_LEN);
	if (key_is_zero(re) || !ss_mix_hash(&session.ss, re, LINK_KEY_LEN)) {
		return false;
	}

	if (!import_x25519(static_priv, &s_priv)) {
		return false;
	}

	ok = dh(s_priv, re, shared) && ss_mix_key(&session.ss, shared);
	if (!ok) {
		psa_destroy_key(s_priv);
		memset(shared, 0, sizeof(shared));
		return false;
	}

	if (!ss_decrypt_and_hash(&session.ss, in + LINK_KEY_LEN,
				 LINK_KEY_LEN + LINK_TAG_LEN, rs, &rs_len) ||
	    rs_len != LINK_KEY_LEN || key_is_zero(rs)) {
		psa_destroy_key(s_priv);
		return false;
	}

	ok = dh(s_priv, rs, shared) && ss_mix_key(&session.ss, shared);
	psa_destroy_key(s_priv);
	if (!ok) {
		memset(shared, 0, sizeof(shared));
		return false;
	}

	if (!ss_decrypt_and_hash(&session.ss,
				 in + LINK_KEY_LEN + LINK_KEY_LEN + LINK_TAG_LEN,
				 sizeof(hs_payload) + LINK_TAG_LEN, scratch, &scratch_len) ||
	    scratch_len != sizeof(hs_payload) || scratch[0] != hs_payload[0]) {
		return false;
	}

	/* <- e, ee, se (+ 0x5A encrypted payload) */
	if (!generate_x25519(&session.e_priv, e_pub) ||
	    !ss_mix_hash(&session.ss, e_pub, LINK_KEY_LEN)) {
		return false;
	}
	memcpy(out, e_pub, LINK_KEY_LEN);
	total = LINK_KEY_LEN;

	if (!dh(session.e_priv, re, shared) || !ss_mix_key(&session.ss, shared) ||
	    !dh(session.e_priv, rs, shared) || !ss_mix_key(&session.ss, shared)) {
		memset(shared, 0, sizeof(shared));
		return false;
	}

	if (!ss_encrypt_and_hash(&session.ss, hs_payload, sizeof(hs_payload),
				 out + total, &enc_len)) {
		return false;
	}
	total += enc_len;

	/* Split. First key = initiator->responder. */
	if (!hkdf2(session.ss.ck, NULL, 0, k1, k2)) {
		return false;
	}

	ok = import_aead_key(k1, &session.recv.key) &&
	     import_aead_key(k2, &session.send.key);
	memset(k1, 0, sizeof(k1));
	memset(k2, 0, sizeof(k2));
	if (!ok) {
		return false;
	}

	memcpy(session.remote_static, rs, LINK_KEY_LEN);
	destroy_key(&session.e_priv);
	destroy_key(&session.ss.cs.key);
	memset(shared, 0, sizeof(shared));
	*out_len = total;
	session.phase = HS_ESTABLISHED;
	LOG_INF("Secure session established via IK (privilege %d)", link_privilege());
	return true;
}

/* The first frame of a handshake: its length selects the pattern. */
static bool start_handshake(const uint8_t *in, size_t in_len,
			    uint8_t *out, size_t out_cap, size_t *out_len)
{
	if (in_len == HS_MSG1_LEN) {
		return handshake_msg1(in, in_len, out, out_cap, out_len);
	}

	if (in_len == HS_IK_MSG1_LEN) {
		return handshake_ik(in, in_len, out, out_cap, out_len);
	}

	return false;
}

bool link_handshake(uint8_t frame_type, const uint8_t *in, size_t in_len,
			      uint8_t *out, size_t out_cap, size_t *out_len)
{
	bool ok;

	*out_len = 0;

	if (frame_type != LINK_TYPE_HANDSHAKE) {
		LOG_WRN("Frame type 0x%02x is not this link's handshake dialect", frame_type);
		link_reset();
		return false;
	}

	switch (session.phase) {
	case HS_IDLE:
		ok = start_handshake(in, in_len, out, out_cap, out_len);
		break;
	case HS_SENT_MSG2:
		if (in_len == HS_MSG1_LEN || in_len == HS_IK_MSG1_LEN) {
			/* The client abandoned the XX in flight and started
			 * over (same resync rule as below).
			 */
			LOG_INF("Handshake restart; superseding the handshake in flight");
			link_reset();
			ok = start_handshake(in, in_len, out, out_cap, out_len);
		} else {
			ok = handshake_msg3(in, in_len);
		}
		break;
	default:
		/* Established: the only writer is the connected central, so a
		 * fresh message 1 (either pattern's) is the client superseding
		 * its own session (per-command sessions over a link the phone
		 * stack kept alive). Anything else is treated like a forged
		 * sealed frame — drop the session, do not start over.
		 */
		if (in_len == HS_MSG1_LEN || in_len == HS_IK_MSG1_LEN) {
			LOG_INF("Handshake restart; superseding the established session");
			link_reset();
			ok = start_handshake(in, in_len, out, out_cap, out_len);
		} else {
			LOG_WRN("Handshake frame on an established session; dropping session");
			ok = false;
		}
		break;
	}

	if (!ok) {
		link_reset();
	}

	return ok;
}

/* ---- Transport ---- */

bool link_encrypt(const uint8_t *in, size_t in_len,
			    uint8_t *out, size_t out_cap, size_t *out_len)
{
	if (session.phase != HS_ESTABLISHED || out_cap < in_len + LINK_TAG_LEN) {
		return false;
	}

	return cs_encrypt(&session.send, NULL, 0, in, in_len, out, out_len);
}

bool link_decrypt(const uint8_t *in, size_t in_len,
			    uint8_t *out, size_t out_cap, size_t *out_len)
{
	if (session.phase != HS_ESTABLISHED || in_len < LINK_TAG_LEN ||
	    out_cap < in_len - LINK_TAG_LEN) {
		return false;
	}

	if (!cs_decrypt(&session.recv, NULL, 0, in, in_len, out, out_len)) {
		LOG_WRN("Frame failed authentication; dropping session");
		link_reset();
		return false;
	}

	return true;
}

/* ---- Pairing ceremony ---- */

enum link_privilege link_armed(void)
{
	return (enum link_privilege)atomic_get(&pair_armed);
}

void link_confirm(enum link_privilege pressed)
{
	int needed = atomic_get(&pair_armed);

	if (needed == LINK_PRIV_NONE) {
		return;
	}

	atomic_set(&pair_btn_ok, needed == (int)pressed ? 1 : 0);
	k_sem_give(&pair_confirm);
}

enum link_pair_result link_pair(enum link_privilege privilege)
{
	uint8_t to_pin[LINK_KEY_LEN];
	bool high = privilege == LINK_PRIV_HIGH;
	uint8_t *slot = high ? pin_hi : pin_lo;
	bool *have_slot = high ? &have_pin_hi : &have_pin_lo;
	bool confirmed = false;
	bool matched;

	if (session.phase != HS_ESTABLISHED) {
		return LINK_PAIR_NO_SESSION;
	}

	/* Snapshot now: disconnect (another thread) can zero session. */
	memcpy(to_pin, session.remote_static, LINK_KEY_LEN);
	if (key_is_zero(to_pin)) {
		return LINK_PAIR_NO_SESSION;
	}

	if (high ? lock_hi : lock_lo) {
		LOG_WRN("Pair(%s) refused: slot is provisioned over USB",
			high ? "high" : "low");
		return LINK_PAIR_LOCKED;
	}

	/* Ceremony-made pins are replaceable: matching confirmation is the
	 * possession proof. Same key re-confirming is a no-op pin.
	 */
	if (*have_slot && memcmp(slot, to_pin, LINK_KEY_LEN) != 0) {
		LOG_INF("Pair(%s): replacing the existing occupant",
			high ? "high" : "low");
	}

	k_sem_reset(&pair_confirm);
	atomic_set(&pair_btn_ok, 0);
	atomic_set(&pair_armed, high ? LINK_PRIV_HIGH : LINK_PRIV_LOW);
	LOG_INF("Pairing window open (%s privilege)", high ? "HIGH" : "low");
	pairing_ui_window_begin(privilege);

	for (int slice = 0; slice < PAIR_WINDOW_SECONDS * 4; slice++) {
		pairing_ui_window_tick(slice, high);
		if (k_sem_take(&pair_confirm, K_MSEC(250)) == 0) {
			confirmed = true;
			break;
		}
		if (session.phase != HS_ESTABLISHED) {
			break;
		}
	}

	matched = atomic_get(&pair_btn_ok) != 0;

	atomic_set(&pair_armed, 0);
	pairing_ui_window_end();

	if (!confirmed || session.phase != HS_ESTABLISHED) {
		LOG_WRN("Pairing window expired unconfirmed");
		return LINK_PAIR_UNCONFIRMED;
	}

	if (!matched) {
		LOG_WRN("Pair(%s) refused: privilege confirmation mismatch",
			high ? "high" : "low");
		return LINK_PAIR_TIER_MISMATCH;
	}

	memcpy(slot, to_pin, LINK_KEY_LEN);
	*have_slot = true;
	store_save(high ? "vg/pin_hi" : "vg/pin_lo", slot, LINK_KEY_LEN);
	verdigris_set_secured(secured_flags());
	LOG_INF("Paired: client key pinned (%s privilege)", high ? "HIGH" : "low");
	return LINK_PAIR_OK;
}

bool link_unpair_slot(enum link_privilege privilege)
{
	if ((privilege == LINK_PRIV_HIGH && lock_hi) ||
	    (privilege == LINK_PRIV_LOW && lock_lo)) {
		/* USB-provisioned: only USB possession clears it. */
		return false;
	}

	if (privilege == LINK_PRIV_HIGH && have_pin_hi) {
		have_pin_hi = false;
		memset(pin_hi, 0, sizeof(pin_hi));
		store_delete("vg/pin_hi");
	} else if (privilege == LINK_PRIV_LOW && have_pin_lo) {
		have_pin_lo = false;
		memset(pin_lo, 0, sizeof(pin_lo));
		store_delete("vg/pin_lo");
	} else {
		return true; /* nothing pinned: an idempotent no-op */
	}

	verdigris_set_secured(secured_flags());
	LOG_INF("Unpaired: %s slot cleared", privilege == LINK_PRIV_HIGH ? "high" : "low");
	return true;
}

void link_clear_slot(enum link_privilege privilege)
{
	/* Possession (USB shell or button hold): the lock falls first so the
	 * slot clear goes through.
	 */
	if (privilege == LINK_PRIV_HIGH) {
		if (lock_hi) {
			lock_hi = false;
			store_delete("vg/lock_hi");
		}
	} else if (lock_lo) {
		lock_lo = false;
		store_delete("vg/lock_lo");
	}
	link_unpair_slot(privilege);
}

void link_unpair(void)
{
	link_clear_slot(LINK_PRIV_LOW);
	link_clear_slot(LINK_PRIV_HIGH);
}

bool link_slot_locked(enum link_privilege privilege)
{
	return privilege == LINK_PRIV_HIGH ? lock_hi : lock_lo;
}

bool link_set_pin(enum link_privilege privilege, const uint8_t key[LINK_KEY_LEN])
{
	bool high = privilege == LINK_PRIV_HIGH;
	static const uint8_t one = 1;

	if (key_is_zero(key)) {
		return false;
	}

	memcpy(high ? pin_hi : pin_lo, key, LINK_KEY_LEN);
	if (high) {
		have_pin_hi = true;
		lock_hi = true;
	} else {
		have_pin_lo = true;
		lock_lo = true;
	}
	store_save(high ? "vg/pin_hi" : "vg/pin_lo", key, LINK_KEY_LEN);
	store_save(high ? "vg/lock_hi" : "vg/lock_lo", &one, 1);
	verdigris_set_secured(secured_flags());
	LOG_INF("Provisioned: client key pinned over USB (%s privilege)",
		high ? "HIGH" : "low");
	return true;
}

bool link_has_ca_root(void)
{
	return have_ca_root;
}

const uint8_t *link_ca_root(void)
{
	return have_ca_root ? ca_root : NULL;
}

bool link_session_endorsed(void)
{
	return session.phase == HS_ESTABLISHED && session.endorsed;
}

bool link_set_ca_root(const uint8_t root[LINK_KEY_LEN])
{
	if (key_is_zero(root)) {
		return false;
	}

	memcpy(ca_root, root, LINK_KEY_LEN);
	have_ca_root = true;
	store_save("vg/ca_root", ca_root, LINK_KEY_LEN);
	LOG_INF("Endorsement CA root provisioned over USB");
	return true;
}

void link_clear_ca_root(void)
{
	if (!have_ca_root) {
		return;
	}

	have_ca_root = false;
	memset(ca_root, 0, sizeof(ca_root));
	store_delete("vg/ca_root");
	LOG_INF("Endorsement CA root cleared");
}

bool link_has_identity(void)
{
	return have_identity;
}

const uint8_t *link_static_public(void)
{
	return have_identity ? static_pub : NULL;
}

bool link_has_pin(enum link_privilege privilege)
{
	if (privilege == LINK_PRIV_HIGH) {
		return have_pin_hi;
	}
	if (privilege == LINK_PRIV_LOW) {
		return have_pin_lo;
	}
	return false;
}

const uint8_t *link_pin(enum link_privilege privilege)
{
	if (privilege == LINK_PRIV_HIGH && have_pin_hi) {
		return pin_hi;
	}
	if (privilege == LINK_PRIV_LOW && have_pin_lo) {
		return pin_lo;
	}
	return NULL;
}

/* ---- Persistence ---- */

static int store_save(const char *key, const void *val, size_t len)
{
	int rc = settings_save_one(key, val, len);

	if (rc) {
		LOG_ERR("settings_save_one(%s) failed (%d)", key, rc);
	}
	return rc;
}

static void store_delete(const char *key)
{
	int rc = settings_delete(key);

	if (rc) {
		LOG_ERR("settings_delete(%s) failed (%d)", key, rc);
	}
}

static bool store_leaf(const char *name, const char *leaf)
{
	const char *next;

	return settings_name_steq(name, leaf, &next) && next == NULL;
}

static int store_set(const char *name, size_t len, settings_read_cb read_cb, void *cb_arg)
{
	if (store_leaf(name, "sk") && len == LINK_KEY_LEN) {
		if (read_cb(cb_arg, static_priv, LINK_KEY_LEN) == LINK_KEY_LEN) {
			have_identity = true;
			LOG_INF("Restored device identity");
		}
		return 0;
	}

	if (store_leaf(name, "pin_lo") && len == LINK_KEY_LEN) {
		if (read_cb(cb_arg, pin_lo, LINK_KEY_LEN) == LINK_KEY_LEN &&
		    !key_is_zero(pin_lo)) {
			have_pin_lo = true;
			LOG_INF("Restored low-privilege pin");
		}
		return 0;
	}

	if (store_leaf(name, "lock_lo") && len == 1) {
		uint8_t v;

		if (read_cb(cb_arg, &v, 1) == 1) {
			lock_lo = v != 0;
		}
		return 0;
	}

	if (store_leaf(name, "lock_hi") && len == 1) {
		uint8_t v;

		if (read_cb(cb_arg, &v, 1) == 1) {
			lock_hi = v != 0;
		}
		return 0;
	}

	if (store_leaf(name, "pin_hi") && len == LINK_KEY_LEN) {
		if (read_cb(cb_arg, pin_hi, LINK_KEY_LEN) == LINK_KEY_LEN &&
		    !key_is_zero(pin_hi)) {
			have_pin_hi = true;
			LOG_INF("Restored high-privilege pin");
		}
		return 0;
	}

	if (store_leaf(name, "ca_root") && len == LINK_KEY_LEN) {
		if (read_cb(cb_arg, ca_root, LINK_KEY_LEN) == LINK_KEY_LEN &&
		    !key_is_zero(ca_root)) {
			have_ca_root = true;
			LOG_INF("Restored endorsement CA root");
		}
		return 0;
	}

	return 0;
}

static int store_export(int (*cb)(const char *name, const void *val, size_t val_len))
{
	int rc;

	if (have_identity) {
		rc = cb("vg/sk", static_priv, LINK_KEY_LEN);
		if (rc) {
			return rc;
		}
	}
	if (have_pin_lo) {
		rc = cb("vg/pin_lo", pin_lo, LINK_KEY_LEN);
		if (rc) {
			return rc;
		}
	}
	if (have_pin_hi) {
		rc = cb("vg/pin_hi", pin_hi, LINK_KEY_LEN);
		if (rc) {
			return rc;
		}
	}
	if (lock_lo) {
		static const uint8_t one = 1;

		rc = cb("vg/lock_lo", &one, 1);
		if (rc) {
			return rc;
		}
	}
	if (lock_hi) {
		static const uint8_t one = 1;

		rc = cb("vg/lock_hi", &one, 1);
		if (rc) {
			return rc;
		}
	}
	if (have_ca_root) {
		rc = cb("vg/ca_root", ca_root, LINK_KEY_LEN);
		if (rc) {
			return rc;
		}
	}
	return 0;
}

static int store_commit(void)
{
	LOG_INF("Link settings committed; identity=%s lo=%s hi=%s ca=%s",
		have_identity ? "yes" : "no",
		have_pin_lo ? "yes" : "no",
		have_pin_hi ? "yes" : "no",
		have_ca_root ? "yes" : "no");
	return 0;
}

SETTINGS_STATIC_HANDLER_DEFINE(link_settings, "vg", NULL, store_set, store_commit,
			       store_export);

static void derive_public_key(void)
{
	psa_key_id_t id;
	size_t pub_len;

	if (import_x25519(static_priv, &id)) {
		psa_export_public_key(id, static_pub, LINK_KEY_LEN, &pub_len);
		psa_destroy_key(id);
	}
}

bool link_keygen(bool force)
{
	psa_key_id_t id;
	uint8_t pub[LINK_KEY_LEN];
	uint8_t priv[LINK_KEY_LEN];
	size_t priv_len;

	if (have_identity && !force) {
		return false;
	}

	if (!generate_x25519(&id, pub)) {
		LOG_ERR("Identity generation failed");
		return false;
	}

	if (psa_export_key(id, priv, LINK_KEY_LEN, &priv_len) != PSA_SUCCESS ||
	    priv_len != LINK_KEY_LEN) {
		LOG_ERR("Identity export failed");
		psa_destroy_key(id);
		return false;
	}
	psa_destroy_key(id);

	link_reset();
	memcpy(static_priv, priv, LINK_KEY_LEN);
	memcpy(static_pub, pub, LINK_KEY_LEN);
	memset(priv, 0, sizeof(priv));
	have_identity = true;
	store_save("vg/sk", static_priv, LINK_KEY_LEN);
	LOG_INF("Rotated the device identity; pinned phones will alarm next session");
	return true;
}

/* Called from main() after settings_load(). */
void link_init(void)
{
	psa_status_t status = psa_crypto_init();

	if (status != PSA_SUCCESS) {
		LOG_ERR("psa_crypto_init failed (%d)", (int)status);
		return;
	}

	if (!have_identity) {
		psa_key_id_t id;

		if (!generate_x25519(&id, static_pub)) {
			LOG_ERR("Failed to generate the device identity");
			return;
		}

		size_t priv_len;
		if (psa_export_key(id, static_priv, LINK_KEY_LEN, &priv_len) != PSA_SUCCESS ||
		    priv_len != LINK_KEY_LEN) {
			LOG_ERR("Failed to export the device identity");
			psa_destroy_key(id);
			return;
		}
		psa_destroy_key(id);

		have_identity = true;
		if (store_save("vg/sk", static_priv, LINK_KEY_LEN) == 0) {
			LOG_INF("Generated and stored device link identity");
		} else {
			LOG_ERR("Generated a RAM-only identity; pairing will not survive reset");
		}
	} else {
		derive_public_key();
	}

	verdigris_set_secured(secured_flags());
	LOG_INF("Link security ready; pub=%02X%02X%02X%02X... paired lo=%s hi=%s ca=%s",
		static_pub[0], static_pub[1], static_pub[2], static_pub[3],
		have_pin_lo ? "yes" : "no", have_pin_hi ? "yes" : "no",
		have_ca_root ? "yes" : "no");
}
