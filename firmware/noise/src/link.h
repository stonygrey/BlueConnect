/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* BlueConnect link: Noise_XX_25519_AESGCM_SHA256 responder, pins, privilege.
 * The dispatcher-facing contract is link_iface.h; this header adds the
 * Noise app's ceremony and identity API. Framing and the Verdigris command
 * set live elsewhere. Button/LED/shell are pairing_ui.
 */
#ifndef LINK_H_
#define LINK_H_

#include "link_iface.h"

void link_init(void);

bool link_session_active(void);
bool link_is_paired(void);
const uint8_t *link_remote_key(void);

bool link_has_identity(void);
const uint8_t *link_static_public(void);
bool link_has_pin(enum link_privilege privilege);
const uint8_t *link_pin(enum link_privilege privilege);

/* Possession-gated clears (USB shell / button hold): the lock falls first
 * so the slot clear goes through. link_unpair clears both tiers.
 */
void link_unpair(void);
void link_clear_slot(enum link_privilege privilege);

/* USB provisioning: pin a client key into a tier slot and lock it — no
 * ceremony may replace or remotely clear a locked slot; `vg clear` and the
 * button hold clear pins and locks together (possession).
 */
bool link_set_pin(enum link_privilege privilege, const uint8_t key[32]);
bool link_slot_locked(enum link_privilege privilege);

/* Rotates the device identity (vg/sk). Refuses when one exists unless
 * force — rotation makes every phone's pinned device key alarm on its next
 * session, which is the TOFU model working as intended.
 */
bool link_keygen(bool force);

/* Optional endorsement CA root (Ed25519 public). Empty = skip cert checks
 * (today's 0x5A handshake). Loaded = 0x5A still allowed; a cert blob is
 * verified fail-closed. Endorsement does not grant privilege.
 */
bool link_has_ca_root(void);
const uint8_t *link_ca_root(void);
bool link_set_ca_root(const uint8_t root[32]);
void link_clear_ca_root(void);
bool link_session_endorsed(void);

/* Pairing UI / shell: which window is armed (NONE if idle), and the
 * confirmation that was pressed (LOW or HIGH). USB confirm passes the
 * armed privilege so it always matches.
 */
enum link_privilege link_armed(void);
void link_confirm(enum link_privilege pressed);

#endif /* LINK_H_ */
