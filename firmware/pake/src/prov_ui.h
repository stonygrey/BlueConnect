/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* Provisioning presentation: LED3/LED4 mirror the low/high verifier slots,
 * holding Button 4 for 5 s clears both, and the serial `pk` shell
 * (info/set-verifier/clear) is the factory write path — USB access is the
 * possession proof. There is no pairing ceremony: the SPAKE2+ handshake
 * itself is the pairing.
 */
#ifndef PROV_UI_H_
#define PROV_UI_H_

void prov_ui_init(void);

#endif /* PROV_UI_H_ */
