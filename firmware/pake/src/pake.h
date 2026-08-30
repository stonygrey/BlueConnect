/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* BlueConnect PAKE link: SPAKE2+ (RFC 9383, P256-SHA256) verifier over the
 * PSA PAKE API, verifier slots, privilege. The dispatcher-facing contract is
 * link_iface.h; this header adds the PAKE app's provisioning API. The device
 * stores only w0‖L per tier — never the setup code. Framing and the
 * Verdigris command set live in firmware/common; LEDs/shell are prov_ui.
 */
#ifndef PAKE_H_
#define PAKE_H_

#include "link_iface.h"

/* w0 (32) ‖ L (65, uncompressed SEC1): the PSA SPAKE2P public-key format. */
#define PAKE_VERIFIER_LEN 97

void pake_init(void);

bool pake_session_active(void);

bool pake_has_verifier(enum link_privilege privilege);
const uint8_t *pake_verifier(enum link_privilege privilege);

/* Provisioning (USB shell; possession proof). Persists to settings. */
bool pake_set_verifier(enum link_privilege privilege, const uint8_t blob[PAKE_VERIFIER_LEN]);
void pake_clear_verifier(enum link_privilege privilege);
void pake_clear_verifiers(void);

/* Factory-wipe support: zero the persisted rate-limit counter (pk/fails). */
void pake_forget_failures(void);

#endif /* PAKE_H_ */
