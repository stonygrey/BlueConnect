/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* Button 3 / LED 3 = low, Button 4 / LED 4 = high. Hold Button 4 5 s unpairs.
 * Serial `vg` is the same possession proof.
 */
#ifndef PAIRING_UI_H_
#define PAIRING_UI_H_

#include "link.h"

void pairing_ui_init(void);
void pairing_ui_window_begin(enum link_privilege privilege);
void pairing_ui_window_tick(int slice, bool high);
void pairing_ui_window_end(void);
void pairing_ui_unpair_ack(void);

#endif /* PAIRING_UI_H_ */
