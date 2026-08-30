/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

#include "prov_ui.h"
#include "verdigris.h"
#include "pake.h"

#include <stdlib.h>
#include <string.h>

#include <zephyr/kernel.h>
#include <zephyr/bluetooth/bluetooth.h>
#include <zephyr/logging/log.h>
#include <zephyr/shell/shell.h>

#include <dk_buttons_and_leds.h>

LOG_MODULE_REGISTER(prov_ui);

#define CLEAR_HOLD_MS 5000
#define WIPE_HOLD_MS 5000
#define SLOT_LED_LO DK_LED3
#define SLOT_LED_HI DK_LED4
#define WIPE_BTN_MSK DK_BTN3_MSK
#define CLEAR_BTN_MSK DK_BTN4_MSK

static int64_t button_down_at;
static bool chord_armed;

static void show_slots(void)
{
	dk_set_led(SLOT_LED_LO, pake_has_verifier(LINK_PRIV_LOW));
	dk_set_led(SLOT_LED_HI, pake_has_verifier(LINK_PRIV_HIGH));
}

/* The wipe salute: LEDs 2 and 3 (dk indices 3/4) flash together three times so a factory
 * wipe is unmistakable (LED1/LED2 belong to the app state and the
 * connection; they stay out of it).
 */
#define SALUTE_TOGGLES 6
#define SALUTE_STEP_MS 150

static int salute_step;

static void salute_set(bool on)
{
	dk_set_led(DK_LED3, on);
	dk_set_led(DK_LED4, on);
}

static void salute_tick(struct k_timer *timer)
{
	if (salute_step >= SALUTE_TOGGLES) {
		k_timer_stop(timer);
		salute_set(false);
		show_slots();
		return;
	}

	salute_set((salute_step % 2) == 0);
	salute_step++;
}

static K_TIMER_DEFINE(salute_timer, salute_tick, NULL);

static void salute_start(void)
{
	salute_step = 0;
	k_timer_start(&salute_timer, K_NO_WAIT, K_MSEC(SALUTE_STEP_MS));
}

/* Factory wipe: verifiers and the rate-limit counter. PAKE has no device
 * key, so this and the clear differ only in the counter — kept as its own
 * gesture so Buttons 2+3 mean the same thing on every image.
 */
static void factory_wipe(void)
{
	link_reset();
	pake_clear_verifiers();
	pake_forget_failures();
	verdigris_wipe_foreign_credentials("pk/");
	salute_start();
}

static void wipe_fire(struct k_work *work)
{
	ARG_UNUSED(work);
	factory_wipe();
}

static K_WORK_DELAYABLE_DEFINE(wipe_work, wipe_fire);

static void button_changed(uint32_t button_state, uint32_t has_changed)
{
	uint32_t chord = WIPE_BTN_MSK | CLEAR_BTN_MSK;

	/* Buttons 2+3 (dk indices 3/4) held together = factory wipe, fired automatically at
	 * the 5 s mark while still held — the salute starting is the cue to
	 * let go. Releasing early cancels; the chord swallows its own release.
	 */
	if ((button_state & chord) == chord) {
		if (!chord_armed) {
			chord_armed = true;
			k_work_schedule(&wipe_work, K_MSEC(WIPE_HOLD_MS));
		}
		button_down_at = 0;
		return;
	}
	if (chord_armed) {
		chord_armed = false;
		k_work_cancel_delayable(&wipe_work);
		return; /* swallow the chord's release */
	}

	if (!(has_changed & CLEAR_BTN_MSK)) {
		return;
	}

	if (button_state & CLEAR_BTN_MSK) {
		button_down_at = k_uptime_get();
	} else if (button_down_at &&
		   (k_uptime_get() - button_down_at) >= CLEAR_HOLD_MS) {
		pake_clear_verifiers();
		show_slots();
		button_down_at = 0;
	} else {
		button_down_at = 0;
	}
}

void prov_ui_init(void)
{
	dk_buttons_init(button_changed);
	show_slots();
}

static enum link_privilege parse_tier(const char *arg)
{
	if (strcmp(arg, "lo") == 0 || strcmp(arg, "low") == 0) {
		return LINK_PRIV_LOW;
	}
	if (strcmp(arg, "hi") == 0 || strcmp(arg, "high") == 0) {
		return LINK_PRIV_HIGH;
	}
	return LINK_PRIV_NONE;
}

static int hex_nibble(char c)
{
	if (c >= '0' && c <= '9') {
		return c - '0';
	}
	if (c >= 'a' && c <= 'f') {
		return c - 'a' + 10;
	}
	if (c >= 'A' && c <= 'F') {
		return c - 'A' + 10;
	}
	return -1;
}

static bool parse_hex(const char *hex, uint8_t *out, size_t out_len)
{
	if (strlen(hex) != 2 * out_len) {
		return false;
	}

	for (size_t i = 0; i < out_len; i++) {
		int hi = hex_nibble(hex[2 * i]);
		int lo = hex_nibble(hex[2 * i + 1]);

		if (hi < 0 || lo < 0) {
			return false;
		}

		out[i] = (uint8_t)((hi << 4) | lo);
	}

	return true;
}

static int cmd_pk_info(const struct shell *sh, size_t argc, char **argv)
{
	ARG_UNUSED(argc);
	ARG_UNUSED(argv);

	const uint8_t *lo = pake_verifier(LINK_PRIV_LOW);
	const uint8_t *hi = pake_verifier(LINK_PRIV_HIGH);

	shell_print(sh, "name: %s", bt_get_name());
	shell_print(sh, "low slot: %s", lo ? "provisioned" : "empty");
	if (lo) {
		shell_print(sh, "  w0: %02X%02X%02X%02X...", lo[0], lo[1], lo[2], lo[3]);
	}
	shell_print(sh, "high slot: %s", hi ? "provisioned" : "empty");
	if (hi) {
		shell_print(sh, "  w0: %02X%02X%02X%02X...", hi[0], hi[1], hi[2], hi[3]);
	}
	shell_print(sh, "session: %s", pake_session_active() ? "secure" : "none");
	return 0;
}

static int cmd_pk_set_verifier(const struct shell *sh, size_t argc, char **argv)
{
	static uint8_t blob[PAKE_VERIFIER_LEN];
	enum link_privilege tier;

	if (argc != 3) {
		shell_error(sh, "usage: vg set <lo|hi> <hex w0||L (%u chars)>",
			    2 * PAKE_VERIFIER_LEN);
		return -EINVAL;
	}

	tier = parse_tier(argv[1]);
	if (tier == LINK_PRIV_NONE) {
		shell_error(sh, "tier must be lo or hi");
		return -EINVAL;
	}

	if (!parse_hex(argv[2], blob, sizeof(blob))) {
		shell_error(sh, "expected %u hex characters", 2 * PAKE_VERIFIER_LEN);
		return -EINVAL;
	}

	if (!pake_set_verifier(tier, blob)) {
		shell_error(sh, "verifier rejected");
		return -EINVAL;
	}

	show_slots();
	shell_print(sh, "provisioned %s", argv[1]);
	return 0;
}

static int cmd_pk_clear(const struct shell *sh, size_t argc, char **argv)
{
	if (argc == 2 && strcmp(argv[1], "all") != 0) {
		enum link_privilege tier = parse_tier(argv[1]);

		if (tier == LINK_PRIV_NONE) {
			shell_error(sh, "usage: vg clear [lo|hi|all]");
			return -EINVAL;
		}

		pake_clear_verifier(tier);
	} else {
		pake_clear_verifiers();
	}

	show_slots();
	shell_print(sh, "cleared");
	return 0;
}

static int cmd_pk_wipe(const struct shell *sh, size_t argc, char **argv)
{
	ARG_UNUSED(argc);
	ARG_UNUSED(argv);

	factory_wipe();
	shell_print(sh, "wiped every scheme's stored keys");
	return 0;
}

SHELL_STATIC_SUBCMD_SET_CREATE(vg_cmds,
	SHELL_CMD(info, NULL, "Show verifier slots and session state", cmd_pk_info),
	SHELL_CMD(set, NULL, "Provision a verifier over USB: vg set <lo|hi> <hex>",
		  cmd_pk_set_verifier),
	SHELL_CMD(clear, NULL, "Clear verifier slot(s): vg clear [lo|hi|all]", cmd_pk_clear),
	SHELL_CMD(wipe, NULL, "Factory wipe of every scheme's stored keys "
		  "(same as holding Buttons 2+3 for 5 s)", cmd_pk_wipe),
	SHELL_SUBCMD_SET_END);

SHELL_CMD_REGISTER(vg, &vg_cmds, "Verdigris link security", NULL);
