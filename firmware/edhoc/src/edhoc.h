/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* BlueConnect EDHOC link: RFC 9528 responder (method 3 static-static DH,
 * cipher suite 2) over PSA crypto, per-slot pinned initiator credentials,
 * privilege by kid. The dispatcher-facing contract is link_iface.h; this
 * header adds the EDHOC app's provisioning API. Credentials are CWT Claims
 * Sets (CCS) stored verbatim — they participate byte-exactly in the
 * transcript MACs, so they are never re-encoded. Framing and the Verdigris
 * command set live in firmware/common; LEDs/shell are cred_ui.
 */
#ifndef EDHOC_H_
#define EDHOC_H_

#include "link_iface.h"

/* Largest CCS blob a slot accepts (the standard provisioning credential is
 * 83 bytes: cnf/COSE_Key with kid, x, y and no subject).
 */
#define EDHOC_CRED_MAX 192

/* Initiator kids name the privilege slots; the device's own kid is fixed.
 * Twin of host BlueConnectEdhoc.
 */
#define EDHOC_KID_LOW 0x01
#define EDHOC_KID_HIGH 0x02
#define EDHOC_KID_DEVICE 0x0A

void edhoc_init(void);

bool edhoc_session_active(void);

/* The device's own credential: private key auto-generated at first boot,
 * CCS persisted verbatim (ek/key, ek/cred). keygen(force) rotates the key —
 * which orphans every previously printed QR.
 */
bool edhoc_have_key(void);
bool edhoc_keygen(bool force);
const uint8_t *edhoc_device_cred(size_t *len);

/* Per-slot pinned initiator credentials (ek/peer_lo, ek/peer_hi), each with
 * a provenance flag (ek/lock_lo, ek/lock_hi). `vg set` over the USB
 * shell locks the slot: no pairing ceremony may replace it and no remote
 * unpair may clear it — only `vg clear` (possession proof). A ceremony-made
 * pin is unlocked: a later matching ceremony may replace it, and the phone
 * may unpair it remotely.
 */
bool edhoc_has_peer(enum link_privilege privilege);
bool edhoc_slot_locked(enum link_privilege privilege);
bool edhoc_set_peer(enum link_privilege privilege, const uint8_t *ccs, size_t len);
void edhoc_clear_peer(enum link_privilege privilege);
void edhoc_clear_peers(void);

/* Factory-wipe support: zero the persisted rate-limit counter (ek/fails). */
void edhoc_forget_failures(void);

/* Pairing-ceremony confirmation, driven by cred_ui (buttons and the
 * `vg confirm` shell command). armed() reports the privilege of the open
 * window (NONE = no window); confirm(pressed) resolves it.
 */
enum link_privilege edhoc_armed(void);
void edhoc_confirm(enum link_privilege pressed);

#endif /* EDHOC_H_ */
