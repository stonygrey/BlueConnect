/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* SPAKE2+ verifier (RFC 9383 P256-SHA256 flavor) as a state machine over the
 * PSA PAKE API — PSA_ALG_SPAKE2P_HMAC(SHA-256) on secp256r1, computed by the
 * Oberon driver. Mirrors the host PakeLink. The wire dialect inside a 0x03
 * frame:
 *
 *   Start   0x01: ver ‖ tier ‖ shareP(65)     phone -> device   68 B
 *   Respond 0x02: shareV(65) ‖ confirmV(32)   device -> phone   98 B
 *   Confirm 0x03: confirmP(32)                phone -> device   33 B
 *   Status  0x04: code                        device -> phone    2 B
 *
 * The requested tier selects which verifier slot's w0/L the exchange runs
 * against; the tier is granted only if key confirmation succeeds. Transport
 * frames (0x02 type byte) then use AES-256-GCM keys HKDF'd from the PAKE
 * shared secret, with the Noise link's nonce discipline.
 */
#include "pake.h"
#include "verdigris.h"

#include <stdint.h>
#include <string.h>

#include <zephyr/kernel.h>
#include <zephyr/logging/log.h>
#include <zephyr/settings/settings.h>

#include <psa/crypto.h>

LOG_MODULE_REGISTER(pake);

#define CONTEXT "BlueConnectPake/1"
#define TRANSPORT_INFO "BlueConnectPake/1 transport"

#define HASH_LEN 32
#define POINT_LEN 65

/* Message kinds inside a 0x03 frame. */
#define MSG_START 0x01
#define MSG_RESPOND 0x02
#define MSG_CONFIRM 0x03
#define MSG_STATUS 0x04

#define WIRE_VERSION 0x01

#define TIER_LOW 0x01
#define TIER_HIGH 0x02

#define STATUS_OK 0x00
#define STATUS_AUTH_FAILED 0x01
#define STATUS_NO_VERIFIER 0x02
#define STATUS_RATE_LIMITED 0x03
#define STATUS_PROTOCOL_ERROR 0x04

#define START_LEN (3 + POINT_LEN)
#define RESPOND_LEN (1 + POINT_LEN + HASH_LEN)
#define CONFIRM_LEN (1 + HASH_LEN)
#define STATUS_LEN 2

BUILD_ASSERT(RESPOND_LEN <= LINK_HS_OUT_CAP, "Respond must fit the dispatcher's reply buffer");

/* Online-guessing throttle. Every Start that is about to run against a
 * verifier is one code guess — whether or not the prover ever sends Confirm
 * (an honest wrong-code prover detects the mismatch locally at confirmV and
 * goes quiet, so unfinished attempts must count). A confirmed session
 * resets the counter, so legitimate per-command handshakes never
 * accumulate; after RATE_LIMIT_FREE_ATTEMPTS consecutive unconfirmed
 * attempts, further Start frames are refused with exponential backoff
 * (min(2^n, MAX) seconds) and never answered with a Respond. The failure
 * count is persisted (pk/fails); a reboot does not wipe it. The lockout
 * deadline itself is uptime-relative, so a reboot of an already-over-budget
 * counter starts a fresh backoff window immediately.
 */
#define RATE_LIMIT_FREE_ATTEMPTS 3
#define RATE_LIMIT_MAX_SECONDS 32

static const uint8_t context[] = CONTEXT; /* without the NUL */

/* ---- Persistent verifier slots (settings: pk/ver_lo, pk/ver_hi) ---- */

static uint8_t ver_lo[PAKE_VERIFIER_LEN];
static bool have_ver_lo;
static uint8_t ver_hi[PAKE_VERIFIER_LEN];
static bool have_ver_hi;

/* ---- Session state (single connection: BT_MAX_CONN=1) ---- */

struct cipher_state {
	psa_key_id_t key; /* 0 = no key */
	uint64_t nonce;
};

enum pake_phase {
	PK_IDLE,
	PK_RESPONDED,
	PK_ESTABLISHED,
};

static struct {
	enum pake_phase phase;
	psa_pake_operation_t op;
	enum link_privilege tier;     /* requested at Start, granted at Confirm */
	struct cipher_state recv;     /* prover -> verifier */
	struct cipher_state send;     /* verifier -> prover */
} session;

static uint32_t consecutive_failures;
static int64_t locked_out_until_ms;

static int store_save(const char *key, const void *val, size_t len);
static void store_delete(const char *key);
static void persist_failures(void);
static void arm_lockout(void);

/* ---- Small PSA helpers (same idioms as the Noise link) ---- */

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

/* ---- Session lifecycle ---- */

void link_reset(void)
{
	psa_pake_abort(&session.op);
	destroy_key(&session.recv.key);
	destroy_key(&session.send.key);
	memset(&session, 0, sizeof(session));
	session.op = psa_pake_operation_init();
}

bool pake_session_active(void)
{
	return session.phase == PK_ESTABLISHED;
}

enum link_privilege link_privilege(void)
{
	return session.phase == PK_ESTABLISHED ? session.tier : LINK_PRIV_NONE;
}

/* The advertised status bitmap: slot bits plus the PAKE scheme bit, so
 * clients can route to the right tab before connecting.
 */
static uint8_t secured_flags(void)
{
	return LINK_STATUS_PAKE |
	       (have_ver_lo ? LINK_STATUS_LOW_SLOT : 0) |
	       (have_ver_hi ? LINK_STATUS_HIGH_SLOT : 0);
}

/* ---- Rate limiting ---- */

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
	LOG_WRN("PAKE attempt %u unconfirmed; locked out for %u s",
		consecutive_failures, seconds);
}

static void persist_failures(void)
{
	if (consecutive_failures == 0) {
		(void)settings_delete("pk/fails");
		return;
	}

	store_save("pk/fails", &consecutive_failures, sizeof(consecutive_failures));
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

void pake_forget_failures(void)
{
	note_success();
}

/* ---- The SPAKE2+ exchange ---- */

static psa_status_t pake_begin(const uint8_t *verifier, const uint8_t *share_p,
			       uint8_t *out, size_t *out_len)
{
	psa_key_attributes_t attr = PSA_KEY_ATTRIBUTES_INIT;
	psa_pake_cipher_suite_t suite = PSA_PAKE_CIPHER_SUITE_INIT;
	psa_key_id_t password = 0;
	psa_status_t status;
	size_t written;

	psa_pake_cs_set_algorithm(&suite, PSA_ALG_SPAKE2P_HMAC(PSA_ALG_SHA_256));
	psa_pake_cs_set_primitive(&suite, PSA_PAKE_PRIMITIVE(PSA_PAKE_PRIMITIVE_TYPE_ECC,
							     PSA_ECC_FAMILY_SECP_R1, 256));
	psa_pake_cs_set_key_confirmation(&suite, PSA_PAKE_CONFIRMED_KEY);

	psa_set_key_type(&attr, PSA_KEY_TYPE_SPAKE2P_PUBLIC_KEY(PSA_ECC_FAMILY_SECP_R1));
	psa_set_key_usage_flags(&attr, PSA_KEY_USAGE_DERIVE);
	psa_set_key_algorithm(&attr, PSA_ALG_SPAKE2P_HMAC(PSA_ALG_SHA_256));

	status = psa_import_key(&attr, verifier, PAKE_VERIFIER_LEN, &password);
	if (status != PSA_SUCCESS) {
		LOG_ERR("Verifier import failed (%d)", (int)status);
		return status;
	}

	status = psa_pake_setup(&session.op, password, &suite);
	/* The operation holds its own reference; ours can go either way now. */
	psa_destroy_key(password);
	if (status != PSA_SUCCESS) {
		LOG_ERR("psa_pake_setup failed (%d)", (int)status);
		return status;
	}

	status = psa_pake_set_role(&session.op, PSA_PAKE_ROLE_SERVER);
	if (status != PSA_SUCCESS) {
		LOG_ERR("psa_pake_set_role failed (%d)", (int)status);
		return status;
	}

	/* The BlueConnect profile uses empty idProver/idVerifier; PSA's
	 * defaults are empty, so set_user/set_peer are deliberately not
	 * called. If Oberon rejects that, both sides adopt "client"/"server"
	 * (one constant here, one in BlueConnectPake.cs).
	 */
	status = psa_pake_set_context(&session.op, context, sizeof(context) - 1);
	if (status != PSA_SUCCESS) {
		LOG_ERR("psa_pake_set_context failed (%d)", (int)status);
		return status;
	}

	status = psa_pake_input(&session.op, PSA_PAKE_STEP_KEY_SHARE, share_p, POINT_LEN);
	if (status != PSA_SUCCESS) {
		LOG_WRN("shareP rejected (%d)", (int)status);
		return status;
	}

	out[0] = MSG_RESPOND;
	status = psa_pake_output(&session.op, PSA_PAKE_STEP_KEY_SHARE,
				 out + 1, POINT_LEN, &written);
	if (status != PSA_SUCCESS || written != POINT_LEN) {
		LOG_ERR("shareV output failed (%d)", (int)status);
		return status != PSA_SUCCESS ? status : PSA_ERROR_GENERIC_ERROR;
	}

	status = psa_pake_output(&session.op, PSA_PAKE_STEP_CONFIRM,
				 out + 1 + POINT_LEN, HASH_LEN, &written);
	if (status != PSA_SUCCESS || written != HASH_LEN) {
		LOG_ERR("confirmV output failed (%d)", (int)status);
		return status != PSA_SUCCESS ? status : PSA_ERROR_GENERIC_ERROR;
	}

	*out_len = RESPOND_LEN;
	return PSA_SUCCESS;
}

/* K_shared -> two AES-256-GCM transport keys, the host PakeLink's split:
 * bytes 0-31 prover->verifier (our recv), 32-63 verifier->prover (our send).
 */
static bool derive_transport_keys(void)
{
	psa_key_attributes_t attr = PSA_KEY_ATTRIBUTES_INIT;
	psa_key_derivation_operation_t kdf = PSA_KEY_DERIVATION_OPERATION_INIT;
	psa_key_id_t shared = 0;
	uint8_t keys[2 * LINK_KEY_LEN];
	bool ok = false;

	psa_set_key_type(&attr, PSA_KEY_TYPE_DERIVE);
	psa_set_key_usage_flags(&attr, PSA_KEY_USAGE_DERIVE);
	psa_set_key_algorithm(&attr, PSA_ALG_HKDF(PSA_ALG_SHA_256));

	if (psa_pake_get_shared_key(&session.op, &attr, &shared) != PSA_SUCCESS) {
		LOG_ERR("psa_pake_get_shared_key failed");
		return false;
	}

	if (psa_key_derivation_setup(&kdf, PSA_ALG_HKDF(PSA_ALG_SHA_256)) == PSA_SUCCESS &&
	    psa_key_derivation_input_key(&kdf, PSA_KEY_DERIVATION_INPUT_SECRET,
					 shared) == PSA_SUCCESS &&
	    psa_key_derivation_input_bytes(&kdf, PSA_KEY_DERIVATION_INPUT_INFO,
					   (const uint8_t *)TRANSPORT_INFO,
					   sizeof(TRANSPORT_INFO) - 1) == PSA_SUCCESS &&
	    psa_key_derivation_output_bytes(&kdf, keys, sizeof(keys)) == PSA_SUCCESS) {
		ok = import_aead_key(keys, &session.recv.key) &&
		     import_aead_key(keys + LINK_KEY_LEN, &session.send.key);
	}

	psa_key_derivation_abort(&kdf);
	psa_destroy_key(shared);
	memset(keys, 0, sizeof(keys));

	if (!ok) {
		LOG_ERR("Transport key derivation failed");
	}

	return ok;
}

static size_t status_reply(uint8_t *out, uint8_t code)
{
	out[0] = MSG_STATUS;
	out[1] = code;
	return STATUS_LEN;
}

static bool handle_start(const uint8_t *in, size_t in_len, uint8_t *out, size_t *out_len)
{
	const uint8_t *verifier;
	psa_status_t status;

	if (in_len != START_LEN || in[1] != WIRE_VERSION ||
	    (in[2] != TIER_LOW && in[2] != TIER_HIGH)) {
		LOG_WRN("Malformed Start (%u bytes)", (unsigned)in_len);
		*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
		return true;
	}

	if (rate_limited()) {
		*out_len = status_reply(out, STATUS_RATE_LIMITED);
		return true;
	}

	/* A fresh Start supersedes whatever session existed (per-command
	 * sessions over a link the phone stack kept alive) — same rule as
	 * the Noise responder.
	 */
	if (session.phase != PK_IDLE) {
		LOG_INF("Handshake restart; superseding the current session");
		link_reset();
	}

	session.tier = in[2] == TIER_HIGH ? LINK_PRIV_HIGH : LINK_PRIV_LOW;
	verifier = pake_verifier(session.tier);
	if (!verifier) {
		LOG_WRN("No verifier provisioned for the %s tier",
			session.tier == LINK_PRIV_HIGH ? "high" : "low");
		link_reset();
		*out_len = status_reply(out, STATUS_NO_VERIFIER);
		return true;
	}

	/* Count this Start as a guess. The 3rd unconfirmed attempt arms
	 * lockout so the next Start is refused until the window expires;
	 * this Start is still answered.
	 */
	note_attempt();

	status = pake_begin(verifier, in + 3, out, out_len);
	if (status != PSA_SUCCESS) {
		link_reset();
		*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
		return true;
	}

	session.phase = PK_RESPONDED;
	return true;
}

static bool handle_confirm(const uint8_t *in, size_t in_len, uint8_t *out, size_t *out_len)
{
	psa_status_t status;

	if (session.phase != PK_RESPONDED || in_len != CONFIRM_LEN) {
		LOG_WRN("Confirm out of sequence or malformed");
		link_reset();
		*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
		return true;
	}

	status = psa_pake_input(&session.op, PSA_PAKE_STEP_CONFIRM, in + 1, HASH_LEN);
	if (status != PSA_SUCCESS) {
		LOG_WRN("confirmP failed (%d); the codes do not match", (int)status);
		/* Already counted at Start; a failed confirm just isn't forgiven. */
		link_reset();
		*out_len = status_reply(out, STATUS_AUTH_FAILED);
		return true;
	}

	if (!derive_transport_keys()) {
		link_reset();
		*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
		return true;
	}

	session.phase = PK_ESTABLISHED;
	note_success();
	LOG_INF("PAKE session established (%s privilege)",
		session.tier == LINK_PRIV_HIGH ? "HIGH" : "low");
	*out_len = status_reply(out, STATUS_OK);
	return true;
}

bool link_handshake(uint8_t frame_type, const uint8_t *in, size_t in_len,
		    uint8_t *out, size_t out_cap, size_t *out_len)
{
	*out_len = 0;

	if (frame_type != LINK_TYPE_PAKE) {
		LOG_WRN("Frame type 0x%02x is not this link's handshake dialect", frame_type);
		link_reset();
		return false;
	}

	if (in_len < 1 || out_cap < RESPOND_LEN) {
		link_reset();
		return false;
	}

	switch (in[0]) {
	case MSG_START:
		return handle_start(in, in_len, out, out_len);
	case MSG_CONFIRM:
		return handle_confirm(in, in_len, out, out_len);
	default:
		LOG_WRN("Unknown PAKE message kind 0x%02x", in[0]);
		link_reset();
		*out_len = status_reply(out, STATUS_PROTOCOL_ERROR);
		return true;
	}
}

/* ---- Transport ---- */

bool link_encrypt(const uint8_t *in, size_t in_len,
		  uint8_t *out, size_t out_cap, size_t *out_len)
{
	if (session.phase != PK_ESTABLISHED || out_cap < in_len + LINK_TAG_LEN) {
		return false;
	}

	return cs_encrypt(&session.send, in, in_len, out, out_len);
}

bool link_decrypt(const uint8_t *in, size_t in_len,
		  uint8_t *out, size_t out_cap, size_t *out_len)
{
	if (session.phase != PK_ESTABLISHED || in_len < LINK_TAG_LEN ||
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

/* ---- Pairing (there is no in-band ceremony: provisioning is over USB) ---- */

enum link_pair_result link_pair(enum link_privilege privilege)
{
	ARG_UNUSED(privilege);
	return LINK_PAIR_UNSUPPORTED;
}

bool link_unpair_slot(enum link_privilege privilege)
{
	/* Verifiers are factory credentials: erasing one over BLE would be a
	 * remote lockout of provisioned access. Rotation is `pk clear` on the
	 * USB shell (possession proof), same as provisioning.
	 */
	ARG_UNUSED(privilege);
	LOG_INF("Unpair refused; verifiers are managed over USB");
	return false;
}

/* ---- Verifier slots ---- */

bool pake_has_verifier(enum link_privilege privilege)
{
	if (privilege == LINK_PRIV_HIGH) {
		return have_ver_hi;
	}
	if (privilege == LINK_PRIV_LOW) {
		return have_ver_lo;
	}
	return false;
}

const uint8_t *pake_verifier(enum link_privilege privilege)
{
	if (privilege == LINK_PRIV_HIGH && have_ver_hi) {
		return ver_hi;
	}
	if (privilege == LINK_PRIV_LOW && have_ver_lo) {
		return ver_lo;
	}
	return NULL;
}

static bool blob_is_zero(const uint8_t blob[PAKE_VERIFIER_LEN])
{
	uint8_t acc = 0;

	for (size_t i = 0; i < PAKE_VERIFIER_LEN; i++) {
		acc |= blob[i];
	}

	return acc == 0;
}

bool pake_set_verifier(enum link_privilege privilege, const uint8_t blob[PAKE_VERIFIER_LEN])
{
	bool high = privilege == LINK_PRIV_HIGH;
	uint8_t *slot = high ? ver_hi : ver_lo;
	bool *have_slot = high ? &have_ver_hi : &have_ver_lo;

	if (privilege != LINK_PRIV_LOW && privilege != LINK_PRIV_HIGH) {
		return false;
	}

	if (blob_is_zero(blob) || blob[32] != 0x04) {
		LOG_ERR("Rejected verifier: not w0 || uncompressed L");
		return false;
	}

	memcpy(slot, blob, PAKE_VERIFIER_LEN);
	*have_slot = true;
	store_save(high ? "pk/ver_hi" : "pk/ver_lo", slot, PAKE_VERIFIER_LEN);
	verdigris_set_secured(secured_flags());
	LOG_INF("Provisioned the %s verifier", high ? "high" : "low");
	return true;
}

void pake_clear_verifier(enum link_privilege privilege)
{
	if (privilege == LINK_PRIV_HIGH && have_ver_hi) {
		have_ver_hi = false;
		memset(ver_hi, 0, sizeof(ver_hi));
		store_delete("pk/ver_hi");
	} else if (privilege == LINK_PRIV_LOW && have_ver_lo) {
		have_ver_lo = false;
		memset(ver_lo, 0, sizeof(ver_lo));
		store_delete("pk/ver_lo");
	} else {
		return;
	}

	verdigris_set_secured(secured_flags());
	LOG_INF("Cleared the %s verifier", privilege == LINK_PRIV_HIGH ? "high" : "low");
}

void pake_clear_verifiers(void)
{
	pake_clear_verifier(LINK_PRIV_LOW);
	pake_clear_verifier(LINK_PRIV_HIGH);
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
	if (store_leaf(name, "ver_lo") && len == PAKE_VERIFIER_LEN) {
		if (read_cb(cb_arg, ver_lo, PAKE_VERIFIER_LEN) == PAKE_VERIFIER_LEN &&
		    !blob_is_zero(ver_lo)) {
			have_ver_lo = true;
			LOG_INF("Restored low-privilege verifier");
		}
		return 0;
	}

	if (store_leaf(name, "ver_hi") && len == PAKE_VERIFIER_LEN) {
		if (read_cb(cb_arg, ver_hi, PAKE_VERIFIER_LEN) == PAKE_VERIFIER_LEN &&
		    !blob_is_zero(ver_hi)) {
			have_ver_hi = true;
			LOG_INF("Restored high-privilege verifier");
		}
		return 0;
	}

	if (store_leaf(name, "fails") && len == sizeof(consecutive_failures)) {
		if (read_cb(cb_arg, &consecutive_failures,
			    sizeof(consecutive_failures)) == sizeof(consecutive_failures)) {
			LOG_INF("Restored PAKE failure count %u", consecutive_failures);
		}
		return 0;
	}

	return 0;
}

static int store_export(int (*cb)(const char *name, const void *val, size_t val_len))
{
	int rc;

	if (have_ver_lo) {
		rc = cb("pk/ver_lo", ver_lo, PAKE_VERIFIER_LEN);
		if (rc) {
			return rc;
		}
	}
	if (have_ver_hi) {
		rc = cb("pk/ver_hi", ver_hi, PAKE_VERIFIER_LEN);
		if (rc) {
			return rc;
		}
	}
	if (consecutive_failures != 0) {
		return cb("pk/fails", &consecutive_failures, sizeof(consecutive_failures));
	}
	return 0;
}

static int store_commit(void)
{
	LOG_INF("PAKE settings committed; lo=%s hi=%s",
		have_ver_lo ? "yes" : "no", have_ver_hi ? "yes" : "no");
	return 0;
}

SETTINGS_STATIC_HANDLER_DEFINE(pake_settings, "pk", NULL, store_set, store_commit,
			       store_export);

/* Called from main() after settings_load(). */
void pake_init(void)
{
	psa_status_t status = psa_crypto_init();

	if (status != PSA_SUCCESS) {
		LOG_ERR("psa_crypto_init failed (%d)", (int)status);
		return;
	}

	session.op = psa_pake_operation_init();
	if (consecutive_failures >= RATE_LIMIT_FREE_ATTEMPTS) {
		arm_lockout();
	}
	verdigris_set_secured(secured_flags());
	LOG_INF("PAKE link ready; verifiers lo=%s hi=%s fails=%u",
		have_ver_lo ? "yes" : "no", have_ver_hi ? "yes" : "no",
		consecutive_failures);
}
