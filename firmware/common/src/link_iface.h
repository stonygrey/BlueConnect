/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* The link contract between the shared Verdigris dispatcher and the Noise
 * link implementation, firmware/noise/src/link.c. The link's own API
 * (ceremony, provisioning) is in link.h alongside its implementation.
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

#define LINK_KEY_LEN 32
#define LINK_TAG_LEN 16

/* Bits of the advertised manufacturer-data status byte (an unauthenticated
 * UX hint, twin of host BlueConnectConstants): bits 0-1 say which slots are
 * provisioned. The other bits are reserved and sent as zero.
 */
#define LINK_STATUS_LOW_SLOT 0x01
#define LINK_STATUS_HIGH_SLOT 0x02

/* Capacity of the dispatcher's handshake reply buffer (a static buffer in
 * verdigris.c, not stack). Noise message 2 is 113 bytes.
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
	/* The slot was provisioned over USB; no ceremony may touch it. */
	LINK_PAIR_LOCKED,
};

/* Called by the shared chassis (app_main.c) after settings_load(): the app's
 * src/main.c shim initializes the link implementation and the pairing UI.
 */
void link_app_init(void);

void link_reset(void);

enum link_privilege link_privilege(void);

/* Handles one handshake message (a LINK_TYPE_HANDSHAKE frame body) and
 * writes the reply, if any, to out. Returns false when the message is
 * rejected, which drops the frame unanswered.
 */
bool link_handshake(const uint8_t *in, size_t in_len,
		    uint8_t *out, size_t out_cap, size_t *out_len);
bool link_encrypt(const uint8_t *in, size_t in_len,
		  uint8_t *out, size_t out_cap, size_t *out_len);
bool link_decrypt(const uint8_t *in, size_t in_len,
		  uint8_t *out, size_t out_cap, size_t *out_len);

enum link_pair_result link_pair(enum link_privilege privilege);

/* Clears the caller's own slot. Returns false when the slot was provisioned
 * over USB (only USB possession clears it) or the session is the enrollment
 * session (the enrollment key is factory material, never cleared over BLE).
 */
bool link_unpair_slot(enum link_privilege privilege);

/* Enrollment: a LINK_PRIV_ENROLL session pins `key` into the low slot,
 * replacing any earlier occupant (one enrolled client at a time). Returns
 * false when the session is not an enrollment session or the low slot is
 * USB-provisioned.
 */
bool link_enroll(const uint8_t key[32]);

#endif /* LINK_IFACE_H_ */
