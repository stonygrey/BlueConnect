/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* Provisioning UI for the EDHOC firmware: the `ek` serial shell (USB access
 * is the possession proof), slot LEDs, the clear-all button, and the
 * pairing-window LED/button ceremony for enrolled candidates.
 */
#ifndef CRED_UI_H_
#define CRED_UI_H_

#include "link_iface.h"

void cred_ui_init(void);

/* Pairing-window display, driven from link_pair's wait loop: begin blanks
 * the slot LEDs, tick blinks the armed tier's LED, end restores the slot
 * display.
 */
void cred_ui_window_begin(enum link_privilege privilege);
void cred_ui_window_tick(int slice, bool high);
void cred_ui_window_end(void);

#endif /* CRED_UI_H_ */
