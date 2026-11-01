/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* The peripheral_uart chassis is shared across every Verdigris app and is
 * in firmware/common/src/app_main.c; this shim supplies the app-specific
 * link initialization it calls after settings_load().
 */

#ifdef CONFIG_VERDIGRIS
#include "link_iface.h"
#include "link.h"
#include "pairing_ui.h"

void link_app_init(void)
{
	link_init();
	pairing_ui_init();
}
#endif
