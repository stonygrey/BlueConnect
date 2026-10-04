/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* EDHOC responder (RFC 9528, method 3 static-static DH, cipher suite 2:
 * P-256 ECDH, SHA-256, AES-CCM-16-64-128 with 8-byte tag) over PSA crypto.
 * Mirrors the host EdhocLink and the StonyGrey.Edhoc library, whose RFC 9529
 * trace-2 vector tests are the conformance baseline. The wire dialect inside
 * a 0x04 frame:
 *
 *   Msg1   0x01: EDHOC message_1 CBOR       phone -> device   ~38 B
 *   Msg2   0x02: EDHOC message_2 CBOR       device -> phone    45 B (kid)
 *   Msg3   0x03: EDHOC message_3 CBOR       phone -> device    19 B (kid)
 *   Status 0x04: code                       device -> phone     2 B
 *
 * The initiator's kid names the privilege slot (0x01 low, 0x02 high); the
 * tier is granted only when MAC_3 verifies against that slot's pinned
 * credential. Alternatively an initiator with nothing pinned sends the
 * enrollment EAD_1 (label 3737): both ends then carry ID_CRED by value
 * ({14: CCS}, so messages 2 and 3 grow by a CCS), MAC_3 verifies the
 * initiator against its own embedded credential, and the session runs at
 * privilege NONE as a "candidate" until a button ceremony (link_pair) pins
 * the credential into an unlocked slot. A Msg1 in any phase resets the
 * session and starts over (the resync rule — the responder is stateful).
 * Status(OK) after message_3 is the key confirmation EDHOC's optional
 * message_4 would provide. Transport frames (0x02 type byte) then use
 * AES-256-GCM keys exported from the EDHOC exporter (label 32768), with the
 * Noise link's nonce discipline.
 *
 * CBOR is hand-rolled: the message shapes are tiny and fixed, and a strict
 * bespoke parser (shortest-form ints, definite lengths, no trailing data) is
 * both smaller and harder to confuse than a generic decoder on this
 * unauthenticated surface.
 */
#include "cred_ui.h"
#include "edhoc.h"
#include "verdigris.h"

#include <stdint.h>
#include <string.h>

#include <zephyr/kernel.h>
#include <zephyr/logging/log.h>
#include <zephyr/settings/settings.h>

#include <psa/crypto.h>

#include <ocrypto_curve_p256.h>

LOG_MODULE_REGISTER(edhoc);

#define HASH_LEN 32
#define COORD_LEN 32
#define PUB_LEN 65 /* uncompressed SEC1 */
#define MAC_LEN 8
#define CCM_KEY_LEN 16
#define CCM_NONCE_LEN 13
#define CCM_TAG_LEN 8

/* Message kinds inside a 0x04 frame. */
#define MSG_M1 0x01
#define MSG_M2 0x02
#define MSG_M3 0x03
#define MSG_STATUS 0x04

#define STATUS_OK 0x00
#define STATUS_AUTH_FAILED 0x01
#define STATUS_NO_CREDENTIAL 0x02
#define STATUS_RATE_LIMITED 0x03
#define STATUS_PROTOCOL_ERROR 0x04

#define STATUS_LEN 2

#define METHOD_STATIC_STATIC 3
#define SUITE 2

/* EDHOC_KDF info labels, RFC 9528 §4.1.2 Table 1. */
#define LBL_KEYSTREAM_2 0
#define LBL_SALT_3E2M 1
#define LBL_MAC_2 2
#define LBL_K_3 3
#define LBL_IV_3 4
#define LBL_SALT_4E3M 5
#define LBL_MAC_3 6
#define LBL_PRK_OUT 7
#define LBL_PRK_EXPORTER 10

/* Private-use exporter label for the transport keys (host twin:
 * BlueConnectEdhoc.TransportExporterLabel).
 */
#define LBL_TRANSPORT 32768

/* PLAINTEXT_2 = C_R(1) ‖ ID_CRED_R ‖ bstr8 MAC_2(9). ID_CRED_R is the kid
 * byte (1) against a pinned peer, or {14: CCS} (2 + device_cred_len) when
 * the initiator asked to enroll — so the enrolling sizes are variable.
 */
#define PLAINTEXT2_MAX (1 + 2 + EDHOC_CRED_MAX + 1 + MAC_LEN)
/* message_2 = bstr(G_Y(32) ‖ CIPHERTEXT_2). */
#define MESSAGE2_MAX (2 + COORD_LEN + PLAINTEXT2_MAX)
/* PLAINTEXT_3 = ID_CRED_I ({14: CCS} at largest) ‖ bstr8 MAC_3(9). */
#define PLAINTEXT3_MAX (2 + EDHOC_CRED_MAX + 1 + MAC_LEN)

/* EAD_1 label announcing that the initiator has no pinned credential and
 * wants both ends to exchange them by value (host twin:
 * BlueConnectEdhoc.EnrollmentEadLabel). Value-less and non-critical.
 */
#define EAD_ENROLL_LABEL 3737

BUILD_ASSERT(1 + MESSAGE2_MAX <= LINK_HS_OUT_CAP,
	     "message_2 must fit the dispatcher's reply buffer");
BUILD_ASSERT(2 + COORD_LEN + PLAINTEXT2_MAX <= 0xff,
	     "CIPHERTEXT_2 must fit a one-byte CBOR bstr length");

/* Forgery throttle, same policy as the PAKE link. Every Msg1 that is about
 * to be answered counts as an attempt (an abandoned handshake is a probe);
 * a MAC_3-confirmed session resets the counter, so legitimate per-command
 * handshakes never accumulate. After RATE_LIMIT_FREE_ATTEMPTS consecutive
 * unconfirmed attempts, further Msg1 frames are refused with exponential
 * backoff (min(2^n, MAX) seconds). The count is persisted (ek/fails); the
 * lockout deadline is uptime-relative.
 */
#define RATE_LIMIT_FREE_ATTEMPTS 3
#define RATE_LIMIT_MAX_SECONDS 32

/* ---- Device credential (ek/key, ek/cred) ---- */

static uint8_t device_sk[COORD_LEN];
static bool have_key;
static uint8_t device_cred[EDHOC_CRED_MAX];
static uint16_t device_cred_len;
static psa_key_id_t static_key; /* imported from device_sk at init/keygen */

/* ---- Pinned initiator credentials (ek/peer_lo, ek/peer_hi) ---- */

struct peer_slot {
	bool present;
	uint16_t len;
	uint8_t ccs[EDHOC_CRED_MAX]; /* verbatim CRED_I bytes */
	uint8_t kid;
	uint8_t pub[PUB_LEN]; /* 0x04 ‖ x ‖ y parsed from the CCS */
};

static struct peer_slot peer_lo;
static struct peer_slot peer_hi;

/* Slot provenance (ek/lock_lo, ek/lock_hi): true = installed over USB by
 * `ek set-peer`. Ceremony and remote Unpair then refuse; `ek clear` and
 * the Button 3 hold (possession) still drop the lock. Ceremony-pinned
 * slots are unlocked: a matching button confirmation may replace them.
 */
static bool lock_lo;
static bool lock_hi;

/* ---- Session state (single connection: BT_MAX_CONN=1) ---- */

struct cipher_state {
	psa_key_id_t key; /* 0 = no key */
	uint64_t nonce;
};

enum edhoc_phase {
	EK_IDLE,
	EK_M2_SENT,
	EK_ESTABLISHED,
};

static struct {
	enum edhoc_phase phase;
	psa_key_id_t eph_key;         /* our ephemeral Y */
	uint8_t th[HASH_LEN];         /* TH_3 once message_2 is out */
	uint8_t prk3e2m[HASH_LEN];
	enum link_privilege tier;     /* granted at message_3 */
	struct cipher_state recv;     /* initiator -> responder */
	struct cipher_state send;     /* responder -> initiator */
	bool enrolling;               /* EAD_1 3737: credentials go by value */
	/* An enrolling initiator's MAC_3-verified but unpinned credential.
	 * The session runs at LINK_PRIV_NONE until a pairing ceremony pins
	 * it into a tier slot.
	 */
	bool candidate;
	uint8_t cand_ccs[EDHOC_CRED_MAX];
	uint16_t cand_len;
	uint8_t cand_kid;
	uint8_t cand_pub[PUB_LEN];
} session;

/* Pairing-window plumbing, same shape as the Noise link. */
#define PAIR_WINDOW_SECONDS 30
static K_SEM_DEFINE(pair_confirm, 0, 1);
/* 0 = idle, otherwise LINK_PRIV_LOW / LINK_PRIV_HIGH for the armed window. */
static atomic_t pair_armed;
/* Valid after pair_confirm is given: 1 = matching privilege, 0 = mismatch. */
static atomic_t pair_btn_ok;

static uint32_t consecutive_failures;
static int64_t locked_out_until_ms;

static int store_save(const char *key, const void *val, size_t len);
static void store_delete(const char *key);
static void persist_failures(void);
static void arm_lockout(void);

/* ---- Minimal strict CBOR (deterministic subset the dialect uses) ---- */

struct cur {
	const uint8_t *p;
	const uint8_t *end;
};

static bool cur_take(struct cur *c, uint8_t *b)
{
	if (c->p >= c->end) {
		return false;
	}
	*b = *c->p++;
	return true;
}

static bool cur_bytes(struct cur *c, const uint8_t **p, size_t len)
{
	if ((size_t)(c->end - c->p) < len) {
		return false;
	}
	*p = c->p;
	c->p += len;
	return true;
}

/* A small unsigned int in shortest form (we never need beyond 255). */
static bool cbor_read_uint(struct cur *c, uint32_t *out)
{
	uint8_t b;

	if (!cur_take(c, &b)) {
		return false;
	}
	if (b <= 0x17) {
		*out = b;
		return true;
	}
	if (b == 0x18) {
		if (!cur_take(c, &b) || b < 24) {
			return false; /* non-shortest form */
		}
		*out = b;
		return true;
	}
	if (b == 0x19) {
		uint8_t hi;

		if (!cur_take(c, &hi) || !cur_take(c, &b)) {
			return false;
		}
		*out = ((uint32_t)hi << 8) | b;
		return *out > 0xff; /* non-shortest form otherwise */
	}
	return false;
}

/* A byte string with a definite shortest-form length up to 255. */
static bool cbor_read_bstr(struct cur *c, const uint8_t **p, size_t *len)
{
	uint8_t b;

	if (!cur_take(c, &b)) {
		return false;
	}
	if (b >= 0x40 && b <= 0x57) {
		*len = b - 0x40;
	} else if (b == 0x58) {
		if (!cur_take(c, &b) || b < 24) {
			return false; /* non-shortest form */
		}
		*len = b;
	} else {
		return false;
	}
	return cur_bytes(c, p, *len);
}

static bool identifier_identity_byte(uint8_t b)
{
	return b <= 0x17 || (b >= 0x20 && b <= 0x37);
}

/* A connection identifier or compact kid (RFC 9528 §3.3.2): a 1-byte CBOR
 * int is its own encoding; otherwise a bstr that must not have been
 * int-encodable (surplus encodings are rejected, RFC 9529 §4.1.2).
 */
static bool cbor_read_identifier(struct cur *c, uint8_t *out, size_t out_cap, size_t *out_len)
{
	const uint8_t *p;
	size_t len;
	uint8_t b;

	if (c->p >= c->end) {
		return false;
	}
	b = *c->p;
	if (identifier_identity_byte(b)) {
		c->p++;
		out[0] = b;
		*out_len = 1;
		return true;
	}
	if (!cbor_read_bstr(c, &p, &len)) {
		return false;
	}
	if (len == 0 || len > out_cap || (len == 1 && identifier_identity_byte(p[0]))) {
		return false;
	}
	memcpy(out, p, len);
	*out_len = len;
	return true;
}

static size_t cbor_put_uint(uint8_t *out, uint32_t v)
{
	if (v <= 0x17) {
		out[0] = (uint8_t)v;
		return 1;
	}
	if (v <= 0xff) {
		out[0] = 0x18;
		out[1] = (uint8_t)v;
		return 2;
	}
	out[0] = 0x19;
	out[1] = (uint8_t)(v >> 8);
	out[2] = (uint8_t)v;
	return 3;
}

static size_t cbor_put_bstr_hdr(uint8_t *out, size_t len)
{
	if (len <= 0x17) {
		out[0] = 0x40 + (uint8_t)len;
		return 1;
	}
	out[0] = 0x58;
	out[1] = (uint8_t)len;
	return 2;
}

/* ---- PSA helpers ---- */

static void destroy_key(psa_key_id_t *key)
{
	if (*key) {
		psa_destroy_key(*key);
		*key = 0;
	}
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

static void write_be64(uint8_t nonce[12], uint64_t n)
{
	memset(nonce, 0, 4);
	for (int i = 0; i < 8; i++) {
		nonce[4 + i] = (uint8_t)(n >> (8 * (7 - i)));
	}
}

static bool cs_encrypt(struct cipher_state *cs, const uint8_t *pt, size_t pt_len,
		       uint8_t *out, size_t *out_len)
{
	uint8_t nonce[12];
	size_t written;

	if (!cs->key || cs->nonce == UINT64_MAX) {
		return false;
	}

	write_be64(nonce, cs->nonce);
	if (psa_aead_encrypt(cs->key, PSA_ALG_GCM, nonce, sizeof(nonce), NULL, 0,
			     pt, pt_len, out, pt_len + LINK_TAG_LEN, &written) != PSA_SUCCESS) {
		/* Consume the nonce even on failure: GCM must never reuse
		 * (nonce, key) with a different plaintext.
		 */
		cs->nonce++;
		return false;
	}

	cs->nonce++;
	*out_len = written;
	return true;
}

static bool cs_decrypt(struct cipher_state *cs, const uint8_t *ct, size_t ct_len,
		       uint8_t *out, size_t *out_len)
{
	uint8_t nonce[12];
	size_t written;

	if (!cs->key || ct_len < LINK_TAG_LEN || cs->nonce == UINT64_MAX) {
		return false;
	}

	write_be64(nonce, cs->nonce);
	if (psa_aead_decrypt(cs->key, PSA_ALG_GCM, nonce, sizeof(nonce), NULL, 0,
			     ct, ct_len, out, ct_len - LINK_TAG_LEN, &written) != PSA_SUCCESS) {
		return false;
	}

	cs->nonce++;
	*out_len = written;
	return true;
}

static bool sha256(const uint8_t *data, size_t len, uint8_t out[HASH_LEN])
{
	size_t olen;

	return psa_hash_compute(PSA_ALG_SHA_256, data, len, out, HASH_LEN, &olen) ==
		       PSA_SUCCESS && olen == HASH_LEN;
}

/* EDHOC_Extract( salt, IKM ) = HMAC-SHA-256( salt, IKM ). RFC 9528 §4.1.1. */
static bool hmac_sha256(const uint8_t *key, size_t key_len,
			const uint8_t *data, size_t data_len, uint8_t out[HASH_LEN])
{
	psa_key_attributes_t attr = PSA_KEY_ATTRIBUTES_INIT;
	psa_key_id_t id = 0;
	size_t olen;
	bool ok;

	psa_set_key_type(&attr, PSA_KEY_TYPE_HMAC);
	psa_set_key_usage_flags(&attr, PSA_KEY_USAGE_SIGN_MESSAGE);
	psa_set_key_algorithm(&attr, PSA_ALG_HMAC(PSA_ALG_SHA_256));

	if (psa_import_key(&attr, key, key_len, &id) != PSA_SUCCESS) {
		return false;
	}

	ok = psa_mac_compute(id, PSA_ALG_HMAC(PSA_ALG_SHA_256), data, data_len,
			     out, HASH_LEN, &olen) == PSA_SUCCESS && olen == HASH_LEN;
	psa_destroy_key(id);
	return ok;
}

/* context_2 = C_R(1) + ID_CRED ({14: CCS} at largest) + bstr TH(34) +
 * CRED(≤EDHOC_CRED_MAX); context_3 is the same minus C_R.
 */
#define CONTEXT_MAX (1 + 2 + EDHOC_CRED_MAX + 2 + HASH_LEN + EDHOC_CRED_MAX)
/* Scratch large enough for every EDHOC_KDF info sequence this dialect
 * produces: label(≤3) + bstr(context ≤ 3+CONTEXT_MAX) + length(≤3).
 */
#define INFO_MAX (3 + 3 + CONTEXT_MAX + 3)

/* HKDF-Expand (RFC 5869 §2.3), enough for out_len ≤ 8·HASH_LEN — the
 * largest ask is KEYSTREAM_2 over an enrolling PLAINTEXT_2. This is
 * EDHOC_Expand; going through HMAC directly avoids any PSA HKDF dependency.
 */
static bool hkdf_expand(const uint8_t prk[HASH_LEN], const uint8_t *info, size_t info_len,
			uint8_t *out, size_t out_len)
{
	static uint8_t block[HASH_LEN + INFO_MAX + 1];
	uint8_t t[HASH_LEN];
	size_t have = 0;
	uint8_t counter = 1;

	if (info_len > INFO_MAX || out_len > 8 * HASH_LEN || out_len == 0) {
		return false;
	}

	while (have < out_len) {
		size_t pos = 0;

		if (counter > 1) {
			memcpy(block, t, HASH_LEN);
			pos = HASH_LEN;
		}
		memcpy(block + pos, info, info_len);
		pos += info_len;
		block[pos++] = counter;

		if (!hmac_sha256(prk, HASH_LEN, block, pos, t)) {
			return false;
		}

		size_t take = MIN(out_len - have, (size_t)HASH_LEN);

		memcpy(out + have, t, take);
		have += take;
		counter++;
	}

	memset(block, 0, sizeof(block));
	memset(t, 0, sizeof(t));
	return true;
}

/* EDHOC_KDF( PRK, label, context, length ), info = ( label, bstr context,
 * length ) as a CBOR sequence. RFC 9528 §4.1.2.
 */
static bool edhoc_kdf(const uint8_t prk[HASH_LEN], uint32_t label,
		      const uint8_t *context, size_t context_len,
		      uint8_t *out, size_t out_len)
{
	static uint8_t info[INFO_MAX];
	size_t pos = 0;

	if (context_len > CONTEXT_MAX || 3 + 2 + context_len + 2 > sizeof(info)) {
		return false;
	}

	pos += cbor_put_uint(info + pos, label);
	pos += cbor_put_bstr_hdr(info + pos, context_len);
	if (context_len > 0) {
		memcpy(info + pos, context, context_len);
		pos += context_len;
	}
	pos += cbor_put_uint(info + pos, out_len);

	return hkdf_expand(prk, info, pos, out, out_len);
}

static bool ct_equal(const uint8_t *a, const uint8_t *b, size_t n)
{
	uint8_t acc = 0;

	for (size_t i = 0; i < n; i++) {
		acc |= a[i] ^ b[i];
	}
	return acc == 0;
}

/* ECDH x-coordinate agreement. peer is either the 33-byte compressed form
 * (0x02 ‖ x — the y sign is irrelevant to the shared x-coordinate, RFC 9528
 * §9.5) or the 65-byte uncompressed form. PSA takes only the uncompressed form; ecdh_x
 * decompresses x-only peers via ocrypto, and the boot probe verifies the
 * whole path loudly on every boot.
 */
static bool ecdh_x(psa_key_id_t priv, const uint8_t *peer, size_t peer_len,
		   uint8_t out[COORD_LEN])
{
	uint8_t full[PUB_LEN];
	size_t olen;

	/* PSA's Weierstrass import takes only the uncompressed form, so an
	 * x-only peer (EDHOC's G_X) is decompressed via ocrypto first; either
	 * root of y yields the same shared x. The boot probe checks this path.
	 */
	if (peer_len == 1 + COORD_LEN && (peer[0] == 0x02 || peer[0] == 0x03)) {
		ocrypto_cp_p256 pt;

		if (ocrypto_curve_p256_from32bytes(&pt, peer + 1) != 0) {
			return false;
		}
		full[0] = 0x04;
		ocrypto_curve_p256_to64bytes(full + 1, &pt);
		peer = full;
		peer_len = sizeof(full);
	}

	return psa_raw_key_agreement(PSA_ALG_ECDH, priv, peer, peer_len,
				     out, COORD_LEN, &olen) == PSA_SUCCESS && olen == COORD_LEN;
}

static bool generate_p256_key(psa_key_id_t *id, bool exportable)
{
	psa_key_attributes_t attr = PSA_KEY_ATTRIBUTES_INIT;

	psa_set_key_type(&attr, PSA_KEY_TYPE_ECC_KEY_PAIR(PSA_ECC_FAMILY_SECP_R1));
	psa_set_key_bits(&attr, 256);
	psa_set_key_usage_flags(&attr, PSA_KEY_USAGE_DERIVE |
				       (exportable ? PSA_KEY_USAGE_EXPORT : 0));
	psa_set_key_algorithm(&attr, PSA_ALG_ECDH);

	return psa_generate_key(&attr, id) == PSA_SUCCESS;
}

static bool import_p256_private(const uint8_t sk[COORD_LEN], psa_key_id_t *id)
{
	psa_key_attributes_t attr = PSA_KEY_ATTRIBUTES_INIT;

	psa_set_key_type(&attr, PSA_KEY_TYPE_ECC_KEY_PAIR(PSA_ECC_FAMILY_SECP_R1));
	psa_set_key_bits(&attr, 256);
	psa_set_key_usage_flags(&attr, PSA_KEY_USAGE_DERIVE);
	psa_set_key_algorithm(&attr, PSA_ALG_ECDH);

	return psa_import_key(&attr, sk, COORD_LEN, id) == PSA_SUCCESS;
}

/* ---- Session lifecycle ---- */

void link_reset(void)
{
	destroy_key(&session.eph_key);
	destroy_key(&session.recv.key);
	destroy_key(&session.send.key);
	memset(&session, 0, sizeof(session));
	atomic_clear(&pair_armed);
}

bool edhoc_session_active(void)
{
	return session.phase == EK_ESTABLISHED;
}

enum link_privilege link_privilege(void)
{
	return session.phase == EK_ESTABLISHED ? session.tier : LINK_PRIV_NONE;
}

/* The advertised status bitmap: slot bits plus the EDHOC scheme bits, so
 * clients can route to the right tab before connecting.
 */
static uint8_t secured_flags(void)
{
	return LINK_STATUS_EDHOC |
	       (peer_lo.present ? LINK_STATUS_LOW_SLOT : 0) |
	       (peer_hi.present ? LINK_STATUS_HIGH_SLOT : 0);
}

/* ---- Rate limiting (same shape as the PAKE link) ---- */

static bool rate_limited(void)
{
	return locked_out_until_ms != 0 && k_uptime_get() < locked_out_until_ms;
}

static void arm_lockout(void)
{
	uint32_t over;
	uint32_t seconds;

	if (consecutive_failures < RATE_LIMIT_FREE_ATTEMPTS) {
		locked_out_until_ms = 0;
		return;
	}

	over = consecutive_failures - (RATE_LIMIT_FREE_ATTEMPTS - 1);
	seconds = over >= 5 ? RATE_LIMIT_MAX_SECONDS : (1u << over);
	locked_out_until_ms = k_uptime_get() + (int64_t)seconds * 1000;
	LOG_WRN("EDHOC attempt %u unconfirmed; locked out for %u s",
		consecutive_failures, seconds);
}

static void persist_failures(void)
{
	if (consecutive_failures == 0) {
		(void)settings_delete("ek/fails");
		return;
	}

	store_save("ek/fails", &consecutive_failures, sizeof(consecutive_failures));
}

static void note_attempt(void)
{
	consecutive_failures++;
	arm_lockout();
	persist_failures();
}

static void note_success(void)
{
	consecutive_failures = 0;
	locked_out_until_ms = 0;
	persist_failures();
}

void edhoc_forget_failures(void)
{
	note_success();
}

/* ---- Credentials ---- */

/* The device CCS, byte-identical to the host library's
 * EdhocCredential.CreateCcs(kid, subject: null, x, y):
 * {8: {1: {1: 2, 2: h'kid', -1: 1, -2: h'x', -3: h'y'}}}.
 */
static size_t build_device_cred(const uint8_t pub[PUB_LEN], uint8_t *out)
{
	static const uint8_t head[] = {
		0xa1, 0x08, 0xa1, 0x01, 0xa5,
		0x01, 0x02,                   /* kty: EC2 */
		0x02, 0x41, EDHOC_KID_DEVICE, /* kid */
		0x20, 0x01,                   /* crv: P-256 */
		0x21, 0x58, 0x20,             /* x */
	};
	size_t pos = 0;

	memcpy(out + pos, head, sizeof(head));
	pos += sizeof(head);
	memcpy(out + pos, pub + 1, COORD_LEN);
	pos += COORD_LEN;
	out[pos++] = 0x22; /* y */
	out[pos++] = 0x58;
	out[pos++] = 0x20;
	memcpy(out + pos, pub + 1 + COORD_LEN, COORD_LEN);
	pos += COORD_LEN;
	return pos;
}

/* Strict parse of a provisioned CCS: extracts the 1-byte kid and the P-256
 * public key. Accepts the host library's CreateCcs output (optional sub
 * claim, cnf/COSE_Key with kty EC2, crv P-256, x, y).
 */
static bool parse_peer_ccs(const uint8_t *ccs, size_t len,
			   uint8_t *kid, uint8_t pub[PUB_LEN])
{
	struct cur c = { .p = ccs, .end = ccs + len };
	const uint8_t *x = NULL;
	const uint8_t *y = NULL;
	const uint8_t *kid_p = NULL;
	size_t n;
	uint8_t b;

	if (!cur_take(&c, &b) || b < 0xa1 || b > 0xb7) { /* small definite map */
		return false;
	}

	for (uint8_t claims = b - 0xa0; claims > 0; claims--) {
		if (!cur_take(&c, &b)) {
			return false;
		}
		if (b == 0x02) { /* sub: text string, skipped */
			if (!cur_take(&c, &b)) {
				return false;
			}
			if (b >= 0x60 && b <= 0x77) {
				n = b - 0x60;
			} else if (b == 0x78) {
				if (!cur_take(&c, &b) || b < 24) {
					return false;
				}
				n = b;
			} else {
				return false;
			}
			if ((size_t)(c.end - c.p) < n) {
				return false;
			}
			c.p += n;
		} else if (b == 0x08) { /* cnf */
			uint8_t cose_keys;

			if (!cur_take(&c, &b) || b != 0xa1) { /* {1: COSE_Key} */
				return false;
			}
			if (!cur_take(&c, &b) || b != 0x01) {
				return false;
			}
			if (!cur_take(&c, &b) || b < 0xa1 || b > 0xb7) {
				return false;
			}
			for (cose_keys = b - 0xa0; cose_keys > 0; cose_keys--) {
				if (!cur_take(&c, &b)) {
					return false;
				}
				switch (b) {
				case 0x01: /* kty */
					if (!cur_take(&c, &b) || b != 0x02) {
						return false;
					}
					break;
				case 0x02: /* kid: 1-byte bstr */
					if (!cbor_read_bstr(&c, &kid_p, &n) || n != 1) {
						return false;
					}
					break;
				case 0x20: /* crv */
					if (!cur_take(&c, &b) || b != 0x01) {
						return false;
					}
					break;
				case 0x21: /* x */
					if (!cbor_read_bstr(&c, &x, &n) || n != COORD_LEN) {
						return false;
					}
					break;
				case 0x22: /* y */
					if (!cbor_read_bstr(&c, &y, &n) || n != COORD_LEN) {
						return false;
					}
					break;
				default:
					return false;
				}
			}
		} else {
			return false;
		}
	}

	if (c.p != c.end || !kid_p || !x || !y) {
		return false;
	}

	{
		ocrypto_cp_p256 pt;
		uint8_t xy[2 * COORD_LEN];

		memcpy(xy, x, COORD_LEN);
		memcpy(xy + COORD_LEN, y, COORD_LEN);
		if (ocrypto_curve_p256_from64bytes(&pt, xy) != 0) {
			return false;
		}
	}

	*kid = kid_p[0];
	pub[0] = 0x04;
	memcpy(pub + 1, x, COORD_LEN);
	memcpy(pub + 1 + COORD_LEN, y, COORD_LEN);
	return true;
}

static struct peer_slot *slot_for(enum link_privilege privilege)
{
	if (privilege == LINK_PRIV_HIGH) {
		return &peer_hi;
	}
	if (privilege == LINK_PRIV_LOW) {
		return &peer_lo;
	}
	return NULL;
}

static struct peer_slot *slot_for_kid(uint8_t kid)
{
	if (kid == EDHOC_KID_LOW && peer_lo.present) {
		return &peer_lo;
	}
	if (kid == EDHOC_KID_HIGH && peer_hi.present) {
		return &peer_hi;
	}
	return NULL;
}

/* ---- The EDHOC exchange ---- */

static size_t status_reply(uint8_t *out, uint8_t code)
{
	out[0] = MSG_STATUS;
	out[1] = code;
	return STATUS_LEN;
}

/* A random 1-byte connection identifier from the CBOR-int identity range. */
static bool random_c_r(uint8_t *out)
{
	uint8_t r;

	if (psa_generate_random(&r, 1) != PSA_SUCCESS) {
		return false;
	}
	r %= 48;
	*out = r < 24 ? r : 0x20 + (r - 24);
	return true;
}

static bool handle_msg1(const uint8_t *msg, size_t msg_len, uint8_t *out, size_t *out_len)
{
	struct cur c = { .p = msg, .end = msg + msg_len };
	static uint8_t ctx[CONTEXT_MAX];
	static uint8_t scratch[2 + HASH_LEN + PLAINTEXT2_MAX + EDHOC_CRED_MAX];
	static uint8_t pt2[PLAINTEXT2_MAX];
	uint8_t peer_pub[1 + COORD_LEN];
	uint8_t pub[PUB_LEN];
	uint8_t th2[HASH_LEN];
	uint8_t prk2e[HASH_LEN];
	uint8_t shared[COORD_LEN];
	uint8_t mac2[MAC_LEN];
	uint8_t c_r;
	bool enrolling = false;
	const uint8_t *g_x;
	uint32_t v;
	size_t n;
	size_t pos;
	size_t pt2_len;

	if (rate_limited()) {
		*out_len = status_reply(out, STATUS_RATE_LIMITED);
		return true;
	}

	/* A fresh message_1 supersedes whatever session existed (per-command
	 * sessions over a link the phone stack kept alive, or a peer
	 * resyncing after abandoning a handshake).
	 */
	if (session.phase != EK_IDLE) {
		LOG_INF("Handshake restart; superseding the current session");
		link_reset();
	}

	/* method */
	if (!cbor_read_uint(&c, &v) || v != METHOD_STATIC_STATIC) {
		goto malformed;
	}

	/* SUITES_I: an int, or an array of >= 2 ints with the selected suite
	 * last (a 1-element array is a surplus encoding, RFC 9529 §4.1.3).
	 */
	if (c.p < c.end && *c.p >= 0x82 && *c.p <= 0x97) {
		uint8_t count = *c.p - 0x80;

		c.p++;
		for (v = UINT32_MAX; count > 0; count--) {
			if (!cbor_read_uint(&c, &v)) {
				goto malformed;
			}
		}
	} else if (!cbor_read_uint(&c, &v)) {
		goto malformed;
	}
	if (v != SUITE) {
		LOG_WRN("Selected cipher suite %u unsupported", v);
		goto malformed;
	}

	/* G_X */
	if (!cbor_read_bstr(&c, &g_x, &n) || n != COORD_LEN) {
		goto malformed;
	}

	/* C_I: parsed for validity; this profile does not use it further. */
	{
		uint8_t c_i[16];

		if (!cbor_read_identifier(&c, c_i, sizeof(c_i), &n)) {
			goto malformed;
		}
	}

	/* EAD_1: this profile understands exactly one item — the value-less
	 * enrollment flag. Anything else is rejected.
	 */
	if (c.p != c.end) {
		if (!cbor_read_uint(&c, &v) || v != EAD_ENROLL_LABEL || c.p != c.end) {
			LOG_WRN("Unexpected EAD_1 / trailing data in message_1");
			goto malformed;
		}
		enrolling = true;
	}
	session.enrolling = enrolling;

	/* Count this answered message_1 as an attempt; MAC_3 success forgives. */
	note_attempt();

	if (!generate_p256_key(&session.eph_key, false)) {
		goto internal;
	}
	if (psa_export_public_key(session.eph_key, pub, sizeof(pub), &n) != PSA_SUCCESS ||
	    n != PUB_LEN) {
		goto internal;
	}

	/* TH_2 = H( G_Y, H(message_1) ), both as CBOR bstrs. */
	scratch[0] = 0x58;
	scratch[1] = 0x20;
	memcpy(scratch + 2, pub + 1, COORD_LEN); /* G_Y = x-coordinate */
	scratch[2 + COORD_LEN] = 0x58;
	scratch[3 + COORD_LEN] = 0x20;
	if (!sha256(msg, msg_len, scratch + 4 + COORD_LEN) ||
	    !sha256(scratch, 4 + 2 * HASH_LEN, th2)) {
		goto internal;
	}

	/* PRK_2e = Extract( TH_2, G_XY ); the peer point arrives x-only. */
	peer_pub[0] = 0x02;
	memcpy(peer_pub + 1, g_x, COORD_LEN);
	if (!ecdh_x(session.eph_key, peer_pub, sizeof(peer_pub), shared) ||
	    !hmac_sha256(th2, HASH_LEN, shared, COORD_LEN, prk2e)) {
		LOG_WRN("G_X rejected");
		goto malformed_after_attempt;
	}

	/* PRK_3e2m = Extract( SALT_3e2m, G_RX ). */
	if (!edhoc_kdf(prk2e, LBL_SALT_3E2M, th2, HASH_LEN, session.prk3e2m, HASH_LEN) ||
	    !ecdh_x(static_key, peer_pub, sizeof(peer_pub), shared) ||
	    !hmac_sha256(session.prk3e2m, HASH_LEN, shared, COORD_LEN, session.prk3e2m)) {
		goto internal;
	}

	/* MAC_2 over context_2 = << C_R, ID_CRED_R, TH_2, CRED_R >>. */
	if (!random_c_r(&c_r)) {
		goto internal;
	}
	pos = 0;
	ctx[pos++] = c_r;
	if (enrolling) {
		/* ID_CRED_R = {14: CCS}: the credential travels by value. */
		ctx[pos++] = 0xa1;
		ctx[pos++] = 0x0e;
		memcpy(ctx + pos, device_cred, device_cred_len);
		pos += device_cred_len;
	} else {
		ctx[pos++] = 0xa1; /* ID_CRED_R = {4: h'kid'} */
		ctx[pos++] = 0x04;
		ctx[pos++] = 0x41;
		ctx[pos++] = EDHOC_KID_DEVICE;
	}
	ctx[pos++] = 0x58;
	ctx[pos++] = 0x20;
	memcpy(ctx + pos, th2, HASH_LEN);
	pos += HASH_LEN;
	memcpy(ctx + pos, device_cred, device_cred_len);
	pos += device_cred_len;
	if (!edhoc_kdf(session.prk3e2m, LBL_MAC_2, ctx, pos, mac2, MAC_LEN)) {
		goto internal;
	}

	/* PLAINTEXT_2 = ( C_R, ID_CRED_R, MAC_2 ), XORed with KEYSTREAM_2. */
	pt2[0] = c_r;
	pt2_len = 1;
	if (enrolling) {
		pt2[pt2_len++] = 0xa1; /* {14: CCS}, same bytes as the MAC */
		pt2[pt2_len++] = 0x0e;
		memcpy(pt2 + pt2_len, device_cred, device_cred_len);
		pt2_len += device_cred_len;
	} else {
		pt2[pt2_len++] = EDHOC_KID_DEVICE; /* 0x0A: its own CBOR int */
	}
	pt2[pt2_len++] = 0x48;
	memcpy(pt2 + pt2_len, mac2, MAC_LEN);
	pt2_len += MAC_LEN;
	{
		static uint8_t keystream[PLAINTEXT2_MAX];

		if (!edhoc_kdf(prk2e, LBL_KEYSTREAM_2, th2, HASH_LEN,
			       keystream, pt2_len)) {
			goto internal;
		}
		out[0] = MSG_M2;
		out[1] = 0x58;
		out[2] = (uint8_t)(COORD_LEN + pt2_len);
		memcpy(out + 3, pub + 1, COORD_LEN);
		for (size_t i = 0; i < pt2_len; i++) {
			out[3 + COORD_LEN + i] = pt2[i] ^ keystream[i];
		}
	}

	/* TH_3 = H( TH_2, PLAINTEXT_2, CRED_R ) becomes the session transcript. */
	scratch[0] = 0x58;
	scratch[1] = 0x20;
	memcpy(scratch + 2, th2, HASH_LEN);
	memcpy(scratch + 2 + HASH_LEN, pt2, pt2_len);
	memcpy(scratch + 2 + HASH_LEN + pt2_len, device_cred, device_cred_len);
	if (!sha256(scratch, 2 + HASH_LEN + pt2_len + device_cred_len, session.th)) {
		goto internal;
	}

	memset(prk2e, 0, sizeof(prk2e));
	memset(shared, 0, sizeof(shared));
	session.phase = EK_M2_SENT;
	*out_len = 1 + 2 + COORD_LEN + pt2_len;
	return true;

malformed:
	LOG_WRN("Malformed message_1 (%u bytes)", (unsigned)msg_len);
	link_reset();
	*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
	return true;

malformed_after_attempt:
	link_reset();
	*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
	return true;

internal:
	LOG_ERR("message_1 processing failed internally");
	link_reset();
	*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
	return true;
}

static bool ccm8_open(const uint8_t key[CCM_KEY_LEN], const uint8_t nonce[CCM_NONCE_LEN],
		      const uint8_t *aad, size_t aad_len,
		      const uint8_t *ct, size_t ct_len, uint8_t *out, size_t *out_len)
{
	psa_key_attributes_t attr = PSA_KEY_ATTRIBUTES_INIT;
	psa_key_id_t id = 0;
	bool ok;

	psa_set_key_type(&attr, PSA_KEY_TYPE_AES);
	psa_set_key_bits(&attr, 128);
	psa_set_key_usage_flags(&attr, PSA_KEY_USAGE_DECRYPT);
	psa_set_key_algorithm(&attr, PSA_ALG_AEAD_WITH_SHORTENED_TAG(PSA_ALG_CCM, CCM_TAG_LEN));

	if (psa_import_key(&attr, key, CCM_KEY_LEN, &id) != PSA_SUCCESS) {
		return false;
	}

	ok = psa_aead_decrypt(id, PSA_ALG_AEAD_WITH_SHORTENED_TAG(PSA_ALG_CCM, CCM_TAG_LEN),
			      nonce, CCM_NONCE_LEN, aad, aad_len, ct, ct_len,
			      out, ct_len - CCM_TAG_LEN, out_len) == PSA_SUCCESS;
	psa_destroy_key(id);
	return ok;
}

static bool handle_msg3(const uint8_t *msg, size_t msg_len, uint8_t *out, size_t *out_len)
{
	struct cur c = { .p = msg, .end = msg + msg_len };
	static uint8_t ctx[CONTEXT_MAX];
	static uint8_t scratch[2 + HASH_LEN + PLAINTEXT3_MAX + EDHOC_CRED_MAX];
	static uint8_t pt3[PLAINTEXT3_MAX];
	uint8_t a3[2 + 9 + 1 + 2 + HASH_LEN]; /* ["Encrypt0", h'', bstr TH_3] */
	uint8_t k3[CCM_KEY_LEN];
	uint8_t iv3[CCM_NONCE_LEN];
	uint8_t prk4e3m[HASH_LEN];
	uint8_t th4[HASH_LEN];
	uint8_t mac3[MAC_LEN];
	uint8_t shared[COORD_LEN];
	uint8_t kid[16];
	uint8_t cand_kid = 0;
	uint8_t cand_pub[PUB_LEN];
	struct peer_slot *peer = NULL;
	const uint8_t *cand_ccs = NULL;
	size_t cand_len = 0;
	const uint8_t *cred;
	size_t cred_len;
	const uint8_t *peer_point;
	const uint8_t *ct3;
	const uint8_t *mac_p;
	size_t ct_len;
	size_t pt_len;
	size_t kid_len;
	size_t n;
	size_t pos;

	if (session.phase != EK_M2_SENT) {
		LOG_WRN("message_3 out of sequence");
		link_reset();
		*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
		return true;
	}

	/* message_3 = ( CIPHERTEXT_3 ), one bstr. */
	if (!cbor_read_bstr(&c, &ct3, &ct_len) || c.p != c.end ||
	    ct_len <= CCM_TAG_LEN || ct_len > CCM_TAG_LEN + sizeof(pt3)) {
		LOG_WRN("Malformed message_3");
		link_reset();
		*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
		return true;
	}

	/* Open CIPHERTEXT_3 with K_3/IV_3 and A_3 = Enc_structure(TH_3). */
	pos = 0;
	a3[pos++] = 0x83;
	a3[pos++] = 0x68;
	memcpy(a3 + pos, "Encrypt0", 9 - 1);
	pos += 8;
	a3[pos++] = 0x40;
	a3[pos++] = 0x58;
	a3[pos++] = 0x20;
	memcpy(a3 + pos, session.th, HASH_LEN);
	pos += HASH_LEN;

	if (!edhoc_kdf(session.prk3e2m, LBL_K_3, session.th, HASH_LEN, k3, sizeof(k3)) ||
	    !edhoc_kdf(session.prk3e2m, LBL_IV_3, session.th, HASH_LEN, iv3, sizeof(iv3))) {
		goto internal;
	}

	if (!ccm8_open(k3, iv3, a3, pos, ct3, ct_len, pt3, &pt_len)) {
		LOG_WRN("message_3 failed authentication");
		link_reset();
		*out_len = status_reply(out, STATUS_AUTH_FAILED);
		return true;
	}

	/* PLAINTEXT_3 = ( ID_CRED_I, MAC_3 ); no EAD in this profile. The
	 * ID_CRED is a bare kid against a pinned slot, or {14: CCS} when the
	 * initiator asked to enroll in message_1: the MAC bstr (0x48 + 8) is
	 * the fixed tail, so the CCS is everything between it and the map
	 * header.
	 */
	if (pt_len >= 2 && pt3[0] == 0xa1) {
		if (!session.enrolling || pt3[1] != 0x0e ||
		    pt_len < 2 + 1 + 1 + MAC_LEN ||
		    pt3[pt_len - 1 - MAC_LEN] != 0x48) {
			LOG_WRN("Malformed PLAINTEXT_3");
			link_reset();
			*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
			return true;
		}
		cand_ccs = pt3 + 2;
		cand_len = pt_len - 2 - (1 + MAC_LEN);
		mac_p = pt3 + pt_len - MAC_LEN;
		if (!parse_peer_ccs(cand_ccs, cand_len, &cand_kid, cand_pub) ||
		    (cand_kid != EDHOC_KID_LOW && cand_kid != EDHOC_KID_HIGH)) {
			LOG_WRN("Enrolling credential rejected: not a tier-kid P-256 CCS");
			link_reset();
			*out_len = status_reply(out, STATUS_NO_CREDENTIAL);
			return true;
		}
	} else {
		struct cur p = { .p = pt3, .end = pt3 + pt_len };

		if (!cbor_read_identifier(&p, kid, sizeof(kid), &kid_len) ||
		    !cbor_read_bstr(&p, &mac_p, &n) || n != MAC_LEN || p.p != p.end) {
			LOG_WRN("Malformed PLAINTEXT_3");
			link_reset();
			*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
			return true;
		}

		peer = kid_len == 1 ? slot_for_kid(kid[0]) : NULL;
		if (!peer) {
			LOG_WRN("No credential for the presented kid");
			link_reset();
			*out_len = status_reply(out, STATUS_NO_CREDENTIAL);
			return true;
		}
	}

	cred = cand_ccs ? cand_ccs : peer->ccs;
	cred_len = cand_ccs ? cand_len : peer->len;
	peer_point = cand_ccs ? cand_pub : peer->pub;

	/* PRK_4e3m = Extract( SALT_4e3m, G_IY ). */
	if (!edhoc_kdf(session.prk3e2m, LBL_SALT_4E3M, session.th, HASH_LEN,
		       prk4e3m, HASH_LEN) ||
	    !ecdh_x(session.eph_key, peer_point, PUB_LEN, shared) ||
	    !hmac_sha256(prk4e3m, HASH_LEN, shared, COORD_LEN, prk4e3m)) {
		goto internal;
	}

	/* MAC_3 over context_3 = << ID_CRED_I, TH_3, CRED_I >>. */
	pos = 0;
	if (cand_ccs) {
		ctx[pos++] = 0xa1; /* {14: CCS}, same bytes as the plaintext */
		ctx[pos++] = 0x0e;
		memcpy(ctx + pos, cand_ccs, cand_len);
		pos += cand_len;
	} else {
		ctx[pos++] = 0xa1;
		ctx[pos++] = 0x04;
		ctx[pos++] = 0x41;
		ctx[pos++] = peer->kid;
	}
	ctx[pos++] = 0x58;
	ctx[pos++] = 0x20;
	memcpy(ctx + pos, session.th, HASH_LEN);
	pos += HASH_LEN;
	memcpy(ctx + pos, cred, cred_len);
	pos += cred_len;
	if (!edhoc_kdf(prk4e3m, LBL_MAC_3, ctx, pos, mac3, MAC_LEN)) {
		goto internal;
	}

	if (!ct_equal(mac3, mac_p, MAC_LEN)) {
		LOG_WRN("MAC_3 mismatch; the initiator's key is not this slot's");
		link_reset();
		*out_len = status_reply(out, STATUS_AUTH_FAILED);
		return true;
	}

	/* TH_4 = H( TH_3, PLAINTEXT_3, CRED_I ), then PRK_out -> exporter. */
	scratch[0] = 0x58;
	scratch[1] = 0x20;
	memcpy(scratch + 2, session.th, HASH_LEN);
	memcpy(scratch + 2 + HASH_LEN, pt3, pt_len);
	memcpy(scratch + 2 + HASH_LEN + pt_len, cred, cred_len);
	if (!sha256(scratch, 2 + HASH_LEN + pt_len + cred_len, th4)) {
		goto internal;
	}

	{
		uint8_t prk_out[HASH_LEN];
		uint8_t prk_exporter[HASH_LEN];
		uint8_t keys[2 * LINK_KEY_LEN];
		bool ok;

		ok = edhoc_kdf(prk4e3m, LBL_PRK_OUT, th4, HASH_LEN, prk_out, HASH_LEN) &&
		     edhoc_kdf(prk_out, LBL_PRK_EXPORTER, NULL, 0, prk_exporter, HASH_LEN) &&
		     edhoc_kdf(prk_exporter, LBL_TRANSPORT, NULL, 0, keys, sizeof(keys)) &&
		     import_aead_key(keys, &session.recv.key) &&
		     import_aead_key(keys + LINK_KEY_LEN, &session.send.key);

		memset(prk_out, 0, sizeof(prk_out));
		memset(prk_exporter, 0, sizeof(prk_exporter));
		memset(keys, 0, sizeof(keys));
		if (!ok) {
			goto internal;
		}
	}

	destroy_key(&session.eph_key);
	memset(session.prk3e2m, 0, sizeof(session.prk3e2m));
	memset(prk4e3m, 0, sizeof(prk4e3m));
	memset(shared, 0, sizeof(shared));

	if (cand_ccs) {
		/* An enrolling handshake authenticates only itself: no
		 * pinned trust was demonstrated, so it grants no privilege
		 * and does not forgive rate-limit attempts until a pairing
		 * ceremony pins the credential.
		 */
		memcpy(session.cand_ccs, cand_ccs, cand_len);
		session.cand_len = (uint16_t)cand_len;
		session.cand_kid = cand_kid;
		memcpy(session.cand_pub, cand_pub, PUB_LEN);
		session.candidate = true;
		session.tier = LINK_PRIV_NONE;
		session.phase = EK_ESTABLISHED;
		LOG_INF("EDHOC candidate session established (kid 0x%02x awaits pairing)",
			cand_kid);
	} else {
		session.tier = peer->kid == EDHOC_KID_HIGH ? LINK_PRIV_HIGH : LINK_PRIV_LOW;
		session.phase = EK_ESTABLISHED;
		note_success();
		LOG_INF("EDHOC session established (%s privilege)",
			session.tier == LINK_PRIV_HIGH ? "HIGH" : "low");
	}
	*out_len = status_reply(out, STATUS_OK);
	return true;

internal:
	LOG_ERR("message_3 processing failed internally");
	link_reset();
	*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
	return true;
}

bool link_handshake(uint8_t frame_type, const uint8_t *in, size_t in_len,
		    uint8_t *out, size_t out_cap, size_t *out_len)
{
	*out_len = 0;

	if (frame_type != LINK_TYPE_EDHOC) {
		LOG_WRN("Frame type 0x%02x is not this link's handshake dialect", frame_type);
		link_reset();
		return false;
	}

	if (in_len < 2 || out_cap < 1 + MESSAGE2_MAX) {
		link_reset();
		return false;
	}

	if (!have_key || device_cred_len == 0) {
		LOG_WRN("No device credential; run `ek keygen`");
		*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
		return true;
	}

	switch (in[0]) {
	case MSG_M1:
		return handle_msg1(in + 1, in_len - 1, out, out_len);
	case MSG_M3:
		return handle_msg3(in + 1, in_len - 1, out, out_len);
	default:
		LOG_WRN("Unknown EDHOC message kind 0x%02x", in[0]);
		link_reset();
		*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
		return true;
	}
}

/* ---- Transport ---- */

bool link_encrypt(const uint8_t *in, size_t in_len,
		  uint8_t *out, size_t out_cap, size_t *out_len)
{
	if (session.phase != EK_ESTABLISHED || out_cap < in_len + LINK_TAG_LEN) {
		return false;
	}

	return cs_encrypt(&session.send, in, in_len, out, out_len);
}

bool link_decrypt(const uint8_t *in, size_t in_len,
		  uint8_t *out, size_t out_cap, size_t *out_len)
{
	if (session.phase != EK_ESTABLISHED || in_len < LINK_TAG_LEN ||
	    out_cap < in_len - LINK_TAG_LEN) {
		return false;
	}

	if (!cs_decrypt(&session.recv, in, in_len, out, out_len)) {
		LOG_WRN("Frame failed authentication; dropping session");
		link_reset();
		return false;
	}

	return true;
}

/* ---- Pairing ceremony (enrolled candidates only) ---- */

enum link_privilege edhoc_armed(void)
{
	return (enum link_privilege)atomic_get(&pair_armed);
}

void edhoc_confirm(enum link_privilege pressed)
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
	static uint8_t to_pin[EDHOC_CRED_MAX];
	uint8_t to_pin_pub[PUB_LEN];
	uint16_t to_pin_len;
	uint8_t to_pin_kid;
	bool high = privilege == LINK_PRIV_HIGH;
	struct peer_slot *slot = high ? &peer_hi : &peer_lo;
	uint8_t expected = high ? EDHOC_KID_HIGH : EDHOC_KID_LOW;
	bool confirmed = false;
	bool matched;

	if (session.phase != EK_ESTABLISHED || !session.candidate ||
	    session.cand_len == 0) {
		return LINK_PAIR_NO_SESSION;
	}

	/* Snapshot now: disconnect (another thread) can zero the session. */
	to_pin_len = session.cand_len;
	memcpy(to_pin, session.cand_ccs, to_pin_len);
	to_pin_kid = session.cand_kid;
	memcpy(to_pin_pub, session.cand_pub, PUB_LEN);

	if (to_pin_kid != expected) {
		LOG_WRN("Pair(%s) refused: the enrolled credential names the other tier",
			high ? "high" : "low");
		return LINK_PAIR_TIER_MISMATCH;
	}

	if (high ? lock_hi : lock_lo) {
		LOG_WRN("Pair(%s) refused: slot is provisioned over USB",
			high ? "high" : "low");
		return LINK_PAIR_LOCKED;
	}

	/* Ceremony-made pins are replaceable: matching confirmation is the
	 * possession proof. The same CCS re-confirming is a no-op pin.
	 */
	if (slot->present &&
	    (slot->len != to_pin_len || memcmp(slot->ccs, to_pin, to_pin_len) != 0)) {
		LOG_INF("Pair(%s): replacing the existing occupant",
			high ? "high" : "low");
	}

	k_sem_reset(&pair_confirm);
	atomic_set(&pair_btn_ok, 0);
	atomic_set(&pair_armed, high ? LINK_PRIV_HIGH : LINK_PRIV_LOW);
	LOG_INF("Pairing window open (%s privilege)", high ? "HIGH" : "low");
	cred_ui_window_begin(privilege);

	for (int slice = 0; slice < PAIR_WINDOW_SECONDS * 4; slice++) {
		cred_ui_window_tick(slice, high);
		if (k_sem_take(&pair_confirm, K_MSEC(250)) == 0) {
			confirmed = true;
			break;
		}
		if (session.phase != EK_ESTABLISHED) {
			break;
		}
	}

	matched = atomic_get(&pair_btn_ok) != 0;

	atomic_set(&pair_armed, 0);
	cred_ui_window_end();

	if (!confirmed || session.phase != EK_ESTABLISHED) {
		LOG_WRN("Pairing window expired unconfirmed");
		return LINK_PAIR_UNCONFIRMED;
	}

	if (!matched) {
		LOG_WRN("Pair(%s) refused: privilege confirmation mismatch",
			high ? "high" : "low");
		return LINK_PAIR_TIER_MISMATCH;
	}

	memcpy(slot->ccs, to_pin, to_pin_len);
	slot->len = to_pin_len;
	slot->kid = to_pin_kid;
	memcpy(slot->pub, to_pin_pub, PUB_LEN);
	slot->present = true;
	if (high) {
		lock_hi = false;
	} else {
		lock_lo = false;
	}
	store_save(high ? "ek/peer_hi" : "ek/peer_lo", slot->ccs, slot->len);
	store_delete(high ? "ek/lock_hi" : "ek/lock_lo");
	verdigris_set_secured(secured_flags());

	/* The candidate is now this slot's occupant; the session it arrived
	 * on gains the tier it just pinned.
	 */
	session.candidate = false;
	session.tier = privilege;
	note_success();
	LOG_INF("Paired: initiator credential pinned (%s privilege)",
		high ? "HIGH" : "low");
	return LINK_PAIR_OK;
}

/* No enrollment on this link: the factory enrollment key is a Noise slot. */
bool link_enroll(const uint8_t key[32])
{
	ARG_UNUSED(key);
	return false;
}

bool link_unpair_slot(enum link_privilege privilege)
{
	if ((privilege == LINK_PRIV_HIGH && lock_hi) ||
	    (privilege == LINK_PRIV_LOW && lock_lo)) {
		/* USB-provisioned: only USB possession clears it. */
		return false;
	}

	if (!edhoc_has_peer(privilege)) {
		return true; /* nothing pinned: an idempotent no-op */
	}

	edhoc_clear_peer(privilege);
	return true;
}

/* ---- Provisioning API ---- */

bool edhoc_have_key(void)
{
	return have_key;
}

const uint8_t *edhoc_device_cred(size_t *len)
{
	if (!have_key || device_cred_len == 0) {
		return NULL;
	}
	*len = device_cred_len;
	return device_cred;
}

bool edhoc_keygen(bool force)
{
	psa_key_id_t fresh = 0;
	uint8_t pub[PUB_LEN];
	size_t n;

	if (have_key && !force) {
		return false;
	}

	if (!generate_p256_key(&fresh, true)) {
		LOG_ERR("Key generation failed");
		return false;
	}

	if (psa_export_key(fresh, device_sk, sizeof(device_sk), &n) != PSA_SUCCESS ||
	    n != COORD_LEN ||
	    psa_export_public_key(fresh, pub, sizeof(pub), &n) != PSA_SUCCESS ||
	    n != PUB_LEN) {
		LOG_ERR("Key export failed");
		psa_destroy_key(fresh);
		return false;
	}

	destroy_key(&static_key);
	static_key = fresh;
	have_key = true;
	device_cred_len = (uint16_t)build_device_cred(pub, device_cred);

	store_save("ek/key", device_sk, sizeof(device_sk));
	store_save("ek/cred", device_cred, device_cred_len);
	LOG_INF("Device credential generated (%u-byte CCS)", device_cred_len);
	return true;
}

bool edhoc_has_peer(enum link_privilege privilege)
{
	struct peer_slot *slot = slot_for(privilege);

	return slot != NULL && slot->present;
}

bool edhoc_set_peer(enum link_privilege privilege, const uint8_t *ccs, size_t len)
{
	struct peer_slot *slot = slot_for(privilege);
	uint8_t expected = privilege == LINK_PRIV_HIGH ? EDHOC_KID_HIGH : EDHOC_KID_LOW;
	uint8_t kid;
	uint8_t pub[PUB_LEN];

	if (!slot || len == 0 || len > EDHOC_CRED_MAX) {
		return false;
	}

	if (!parse_peer_ccs(ccs, len, &kid, pub)) {
		LOG_ERR("Rejected credential: not a P-256 CCS");
		return false;
	}

	if (kid != expected) {
		LOG_ERR("Rejected credential: kid 0x%02x does not name this slot", kid);
		return false;
	}

	memcpy(slot->ccs, ccs, len);
	slot->len = (uint16_t)len;
	slot->kid = kid;
	memcpy(slot->pub, pub, PUB_LEN);
	slot->present = true;
	store_save(privilege == LINK_PRIV_HIGH ? "ek/peer_hi" : "ek/peer_lo", ccs, len);

	/* USB installation locks the slot: no ceremony may replace it and no
	 * remote unpair may clear it.
	 */
	if (privilege == LINK_PRIV_HIGH) {
		lock_hi = true;
	} else {
		lock_lo = true;
	}
	store_save(privilege == LINK_PRIV_HIGH ? "ek/lock_hi" : "ek/lock_lo",
		   &(uint8_t){1}, 1);

	verdigris_set_secured(secured_flags());
	LOG_INF("Provisioned the %s credential (usb)",
		privilege == LINK_PRIV_HIGH ? "high" : "low");
	return true;
}

bool edhoc_slot_locked(enum link_privilege privilege)
{
	return privilege == LINK_PRIV_HIGH ? lock_hi : lock_lo;
}

void edhoc_clear_peer(enum link_privilege privilege)
{
	struct peer_slot *slot = slot_for(privilege);

	if (!slot || !slot->present) {
		return;
	}

	memset(slot, 0, sizeof(*slot));
	store_delete(privilege == LINK_PRIV_HIGH ? "ek/peer_hi" : "ek/peer_lo");
	if (privilege == LINK_PRIV_HIGH ? lock_hi : lock_lo) {
		if (privilege == LINK_PRIV_HIGH) {
			lock_hi = false;
		} else {
			lock_lo = false;
		}
		store_delete(privilege == LINK_PRIV_HIGH ? "ek/lock_hi" : "ek/lock_lo");
	}
	verdigris_set_secured(secured_flags());
	LOG_INF("Cleared the %s credential", privilege == LINK_PRIV_HIGH ? "high" : "low");
}

void edhoc_clear_peers(void)
{
	edhoc_clear_peer(LINK_PRIV_LOW);
	edhoc_clear_peer(LINK_PRIV_HIGH);
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

static int restore_peer(struct peer_slot *slot, const char *what,
			size_t len, settings_read_cb read_cb, void *cb_arg)
{
	if (len == 0 || len > EDHOC_CRED_MAX) {
		return 0;
	}

	if (read_cb(cb_arg, slot->ccs, len) != (ssize_t)len) {
		return 0;
	}

	if (!parse_peer_ccs(slot->ccs, len, &slot->kid, slot->pub)) {
		LOG_ERR("Stored %s credential is corrupt; ignoring", what);
		return 0;
	}

	slot->len = (uint16_t)len;
	slot->present = true;
	LOG_INF("Restored %s credential", what);
	return 0;
}

static int store_set(const char *name, size_t len, settings_read_cb read_cb, void *cb_arg)
{
	if (store_leaf(name, "key") && len == sizeof(device_sk)) {
		if (read_cb(cb_arg, device_sk, sizeof(device_sk)) == sizeof(device_sk)) {
			have_key = true;
			LOG_INF("Restored the device key");
		}
		return 0;
	}

	if (store_leaf(name, "cred") && len > 0 && len <= EDHOC_CRED_MAX) {
		if (read_cb(cb_arg, device_cred, len) == (ssize_t)len) {
			device_cred_len = (uint16_t)len;
			LOG_INF("Restored the device credential");
		}
		return 0;
	}

	if (store_leaf(name, "peer_lo")) {
		return restore_peer(&peer_lo, "low-privilege", len, read_cb, cb_arg);
	}

	if (store_leaf(name, "peer_hi")) {
		return restore_peer(&peer_hi, "high-privilege", len, read_cb, cb_arg);
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

	if (store_leaf(name, "fails") && len == sizeof(consecutive_failures)) {
		if (read_cb(cb_arg, &consecutive_failures,
			    sizeof(consecutive_failures)) == sizeof(consecutive_failures)) {
			LOG_INF("Restored EDHOC failure count %u", consecutive_failures);
		}
		return 0;
	}

	return 0;
}

static int store_export(int (*cb)(const char *name, const void *val, size_t val_len))
{
	int rc;

	if (have_key) {
		rc = cb("ek/key", device_sk, sizeof(device_sk));
		if (rc) {
			return rc;
		}
	}
	if (device_cred_len != 0) {
		rc = cb("ek/cred", device_cred, device_cred_len);
		if (rc) {
			return rc;
		}
	}
	if (peer_lo.present) {
		rc = cb("ek/peer_lo", peer_lo.ccs, peer_lo.len);
		if (rc) {
			return rc;
		}
	}
	if (peer_hi.present) {
		rc = cb("ek/peer_hi", peer_hi.ccs, peer_hi.len);
		if (rc) {
			return rc;
		}
	}
	if (lock_lo) {
		rc = cb("ek/lock_lo", &(uint8_t){1}, 1);
		if (rc) {
			return rc;
		}
	}
	if (lock_hi) {
		rc = cb("ek/lock_hi", &(uint8_t){1}, 1);
		if (rc) {
			return rc;
		}
	}
	if (consecutive_failures != 0) {
		return cb("ek/fails", &consecutive_failures, sizeof(consecutive_failures));
	}
	return 0;
}

static int store_commit(void)
{
	LOG_INF("EDHOC settings committed; key=%s lo=%s hi=%s",
		have_key ? "yes" : "no", peer_lo.present ? "yes" : "no",
		peer_hi.present ? "yes" : "no");
	return 0;
}

SETTINGS_STATIC_HANDLER_DEFINE(edhoc_settings, "ek", NULL, store_set, store_commit,
			       store_export);

/* ---- Boot-time probes for the two unproven Oberon behaviors ---- */

static void probe_crypto(void)
{
	/* AES-CCM with the suite's shortened 8-byte tag. */
	{
		psa_key_attributes_t attr = PSA_KEY_ATTRIBUTES_INIT;
		psa_key_id_t id = 0;
		uint8_t key[CCM_KEY_LEN] = { 1 };
		uint8_t nonce[CCM_NONCE_LEN] = { 2 };
		uint8_t ct[16 + CCM_TAG_LEN];
		uint8_t pt[16] = { 3 };
		uint8_t back[16];
		size_t n;
		bool ok = false;

		psa_set_key_type(&attr, PSA_KEY_TYPE_AES);
		psa_set_key_bits(&attr, 128);
		psa_set_key_usage_flags(&attr, PSA_KEY_USAGE_ENCRYPT | PSA_KEY_USAGE_DECRYPT);
		psa_set_key_algorithm(&attr,
				      PSA_ALG_AEAD_WITH_SHORTENED_TAG(PSA_ALG_CCM, CCM_TAG_LEN));

		if (psa_import_key(&attr, key, sizeof(key), &id) == PSA_SUCCESS) {
			ok = psa_aead_encrypt(id,
					      PSA_ALG_AEAD_WITH_SHORTENED_TAG(PSA_ALG_CCM, CCM_TAG_LEN),
					      nonce, sizeof(nonce), NULL, 0, pt, sizeof(pt),
					      ct, sizeof(ct), &n) == PSA_SUCCESS &&
			     psa_aead_decrypt(id,
					      PSA_ALG_AEAD_WITH_SHORTENED_TAG(PSA_ALG_CCM, CCM_TAG_LEN),
					      nonce, sizeof(nonce), NULL, 0, ct, n,
					      back, sizeof(back), &n) == PSA_SUCCESS &&
			     n == sizeof(pt) && memcmp(pt, back, n) == 0;
			psa_destroy_key(id);
		}

		if (!ok) {
			LOG_ERR("PROBE FAILED: AES-CCM-16-64-128 (8-byte tag) unsupported "
				"by this PSA build — EDHOC message_3 cannot work");
		}
	}

	/* Compressed-point peer keys in psa_raw_key_agreement (G_X arrives as
	 * an x-coordinate only; the sign of y is irrelevant to the shared x).
	 */
	{
		psa_key_id_t a = 0;
		psa_key_id_t b = 0;
		uint8_t pub_a[PUB_LEN];
		uint8_t compressed[1 + COORD_LEN];
		uint8_t s1[COORD_LEN];
		uint8_t s2[COORD_LEN];
		size_t n;
		bool ok = false;

		if (generate_p256_key(&a, false) && generate_p256_key(&b, false) &&
		    psa_export_public_key(a, pub_a, sizeof(pub_a), &n) == PSA_SUCCESS &&
		    n == PUB_LEN) {
			compressed[0] = 0x02;
			memcpy(compressed + 1, pub_a + 1, COORD_LEN);
			ok = ecdh_x(b, pub_a, PUB_LEN, s1) &&
			     ecdh_x(b, compressed, sizeof(compressed), s2) &&
			     memcmp(s1, s2, COORD_LEN) == 0;
		}

		destroy_key(&a);
		destroy_key(&b);
		if (!ok) {
			LOG_ERR("PROBE FAILED: compressed-point ECDH unsupported by this "
				"PSA build — EDHOC message_1 G_X cannot be used");
		}
	}
}

/* Called from main() after settings_load(). */
void edhoc_init(void)
{
	psa_status_t status = psa_crypto_init();

	if (status != PSA_SUCCESS) {
		LOG_ERR("psa_crypto_init failed (%d)", (int)status);
		return;
	}

	probe_crypto();

	if (have_key) {
		if (!import_p256_private(device_sk, &static_key)) {
			LOG_ERR("Stored device key failed to import");
			have_key = false;
		} else if (device_cred_len == 0) {
			/* Key restored but no stored CCS (interrupted keygen):
			 * rebuild deterministically.
			 */
			uint8_t pub[PUB_LEN];
			size_t n;

			if (psa_export_public_key(static_key, pub, sizeof(pub), &n) ==
				    PSA_SUCCESS && n == PUB_LEN) {
				device_cred_len = (uint16_t)build_device_cred(pub, device_cred);
				store_save("ek/cred", device_cred, device_cred_len);
			}
		}
	}

	if (!have_key) {
		/* First boot: the device issues itself a credential. */
		(void)edhoc_keygen(true);
	}

	if (consecutive_failures >= RATE_LIMIT_FREE_ATTEMPTS) {
		arm_lockout();
	}
	verdigris_set_secured(secured_flags());
	LOG_INF("EDHOC link ready; key=%s peers lo=%s hi=%s fails=%u",
		have_key ? "yes" : "no", peer_lo.present ? "yes" : "no",
		peer_hi.present ? "yes" : "no", consecutive_failures);
}
