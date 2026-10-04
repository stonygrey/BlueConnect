/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* The link contract between the shared Verdigris dispatcher and a firmware
 * app's transport-security implementation. Each image links exactly one:
 * firmware/noise (Noise XX responder) or firmware/pake (SPAKE2+ verifier).
 * App-specific link API (ceremony, provisioning) lives in the app's own
 * header alongside its implementation.
 */
#ifndef LINK_IFACE_H_
#define LINK_IFACE_H_

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

/* First byte of a reassembled message. Twin of host FrameType. */
#define LINK_TYPE_PLAINTEXT 0x00
#define LINK_TYPE_HANDSHAKE 0x01
#define LINK_TYPE_ENCRYPTED 0x02
#define LINK_TYPE_PAKE 0x03
#define LINK_TYPE_EDHOC 0x04

#define LINK_KEY_LEN 32
#define LINK_TAG_LEN 16

/* Bits of the advertised manufacturer-data status byte (an unauthenticated
 * UX hint, twin of host BlueConnectConstants): bits 0-1 say which slots are
 * provisioned; bits 2-3 are a two-bit scheme field naming the transport
 * scheme this image speaks (00 = Noise XX, 01 = SPAKE2+, 10 = EDHOC,
 * 11 = reserved, decoded as unknown).
 */
#define LINK_STATUS_LOW_SLOT 0x01
#define LINK_STATUS_HIGH_SLOT 0x02
#define LINK_STATUS_PAKE 0x04
#define LINK_STATUS_EDHOC 0x08
#define LINK_STATUS_SCHEME_MASK 0x0C

/* Capacity of the dispatcher's handshake reply buffer (a static buffer in
 * verdigris.c, not stack). Noise message 2 is 113 bytes and the PAKE Respond
 * is 98; EDHOC message_2 is variable-length CBOR (~50 bytes with kid
 * credentials), so the cap leaves headroom for EAD and larger identifiers.
 */
#define LINK_HS_OUT_CAP 512

enum link_privilege {
	LINK_PRIV_NONE = 0,
	LINK_PRIV_LOW = 1,
	LINK_PRIV_HIGH = 2,
	/* A session opened with the factory enrollment key. It is not a rank:
	 * the dispatcher serves it exactly one command, Enroll, and never
	 * compares it against the tiers.
	 */
	LINK_PRIV_ENROLL = 3,
};

enum link_pair_result {
	LINK_PAIR_OK,
	LINK_PAIR_UNCONFIRMED,
	LINK_PAIR_TIER_MISMATCH,
	LINK_PAIR_NO_SESSION,
	/* This link has no in-band pairing ceremony (PAKE: verifiers are
	 * provisioned over USB, and the handshake itself is the pairing).
	 */
	/* The slot was provisioned over USB; no ceremony may touch it. */
	LINK_PAIR_LOCKED,
	LINK_PAIR_UNSUPPORTED,
};

/* Called by the shared chassis (app_main.c) after settings_load(): the app's
 * src/main.c shim initializes its link implementation and provisioning UI.
 */
void link_app_init(void);

void link_reset(void);

enum link_privilege link_privilege(void);

/* frame_type is the message's type byte; an implementation accepts only its
 * own handshake dialect (noise: LINK_TYPE_HANDSHAKE, pake: LINK_TYPE_PAKE)
 * and returns false for the other, which drops the frame unanswered.
 */
bool link_handshake(uint8_t frame_type, const uint8_t *in, size_t in_len,
		    uint8_t *out, size_t out_cap, size_t *out_len);
bool link_encrypt(const uint8_t *in, size_t in_len,
		  uint8_t *out, size_t out_cap, size_t *out_len);
bool link_decrypt(const uint8_t *in, size_t in_len,
		  uint8_t *out, size_t out_cap, size_t *out_len);

enum link_pair_result link_pair(enum link_privilege privilege);

/* Clears the caller's own slot. Returns false when this link has no in-band
 * unpair (PAKE: verifiers are factory credentials, managed only over USB —
 * erasing one over BLE would be a remote lockout of provisioned access).
 */
bool link_unpair_slot(enum link_privilege privilege);

/* Enrollment: a LINK_PRIV_ENROLL session pins `key` into the low slot,
 * replacing any earlier occupant (one enrolled client at a time). Returns
 * false when the session is not an enrollment session, the low slot is
 * USB-provisioned, or this link has no enrollment (PAKE, EDHOC).
 */
bool link_enroll(const uint8_t key[32]);

#endif /* LINK_IFACE_H_ */
