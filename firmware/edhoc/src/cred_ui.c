/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

#include "cred_ui.h"
#include "verdigris.h"
#include "edhoc.h"

#include <stdlib.h>
#include <string.h>

#include <zephyr/kernel.h>
#include <zephyr/bluetooth/bluetooth.h>
#include <zephyr/logging/log.h>
#include <zephyr/shell/shell.h>

#include <dk_buttons_and_leds.h>

LOG_MODULE_REGISTER(cred_ui);

#define CLEAR_HOLD_MS 5000
#define WIPE_HOLD_MS 5000
#define SLOT_LED_LO DK_LED3
#define SLOT_LED_HI DK_LED4
#define PAIR_BTN_LO_MSK DK_BTN3_MSK
#define PAIR_BTN_HI_MSK DK_BTN4_MSK
#define CLEAR_BTN_MSK DK_BTN4_MSK

static int64_t button_down_at;
static bool chord_armed;

static void show_slots(void)
{
	dk_set_led(SLOT_LED_LO, edhoc_has_peer(LINK_PRIV_LOW));
	dk_set_led(SLOT_LED_HI, edhoc_has_peer(LINK_PRIV_HIGH));
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

/* ---- Pairing window (same LED/button grammar as the Noise app) ---- */

void cred_ui_window_begin(enum link_privilege privilege)
{
	ARG_UNUSED(privilege);
	dk_set_led(SLOT_LED_LO, false);
	dk_set_led(SLOT_LED_HI, false);
}

void cred_ui_window_tick(int slice, bool high)
{
	int led = high ? SLOT_LED_HI : SLOT_LED_LO;

	dk_set_led(led, high ? (slice % 2) == 0 : (slice % 4) < 2);
}

void cred_ui_window_end(void)
{
	show_slots();
}

/* Factory wipe: slots, locks, the device key, and the rate-limit counter
 * all fall; a fresh key is generated immediately so the link keeps
 * working. Every printed QR is orphaned and every stored phone credential
 * stops verifying — that is the point of the gesture.
 */
static void factory_wipe(void)
{
	link_reset();
	edhoc_clear_peers();
	(void)edhoc_keygen(true);
	edhoc_forget_failures();
	verdigris_wipe_foreign_credentials("ek/");
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
	enum link_privilege needed = edhoc_armed();
	uint32_t chord = PAIR_BTN_LO_MSK | PAIR_BTN_HI_MSK;

	/* Buttons 2+3 (dk indices 3/4) held together (no window armed) = factory wipe, fired
	 * automatically at the 5 s mark while still held — the salute starting
	 * is the cue to let go; no counting. Releasing early cancels. The
	 * chord owns both buttons and swallows its own release.
	 */
	if ((button_state & chord) == chord) {
		if (needed == LINK_PRIV_NONE && !chord_armed) {
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

	if (has_changed & PAIR_BTN_LO_MSK) {
		if (button_state & PAIR_BTN_LO_MSK) {
			edhoc_confirm(LINK_PRIV_LOW);
		}
	}

	if (has_changed & PAIR_BTN_HI_MSK) {
		if (button_state & PAIR_BTN_HI_MSK) {
			if (needed != LINK_PRIV_NONE) {
				/* A pairing window owns this press; do not
				 * treat a later release as a clear hold.
				 */
				button_down_at = 0;
				edhoc_confirm(LINK_PRIV_HIGH);
			} else {
				button_down_at = k_uptime_get();
			}
		} else if (needed == LINK_PRIV_NONE && button_down_at &&
			   (k_uptime_get() - button_down_at) >= CLEAR_HOLD_MS) {
			edhoc_clear_peers();
			show_slots();
			button_down_at = 0;
		} else {
			button_down_at = 0;
		}
	}
}

void cred_ui_init(void)
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

static bool parse_hex(const char *hex, uint8_t *out, size_t out_cap, size_t *out_len)
{
	size_t chars = strlen(hex);

	if (chars == 0 || chars % 2 != 0 || chars / 2 > out_cap) {
		return false;
	}

	for (size_t i = 0; i < chars / 2; i++) {
		int hi = hex_nibble(hex[2 * i]);
		int lo = hex_nibble(hex[2 * i + 1]);

		if (hi < 0 || lo < 0) {
			return false;
		}

		out[i] = (uint8_t)((hi << 4) | lo);
	}

	*out_len = chars / 2;
	return true;
}

static void print_hex(const struct shell *sh, const uint8_t *data, size_t len)
{
	static char line[2 * EDHOC_CRED_MAX + 1];

	for (size_t i = 0; i < len && i < EDHOC_CRED_MAX; i++) {
		line[2 * i] = "0123456789abcdef"[data[i] >> 4];
		line[2 * i + 1] = "0123456789abcdef"[data[i] & 0x0f];
	}
	line[2 * MIN(len, (size_t)EDHOC_CRED_MAX)] = '\0';
	shell_print(sh, "%s", line);
}

static int cmd_ek_info(const struct shell *sh, size_t argc, char **argv)
{
	ARG_UNUSED(argc);
	ARG_UNUSED(argv);

	size_t cred_len = 0;
	const uint8_t *cred = edhoc_device_cred(&cred_len);

	shell_print(sh, "name: %s", bt_get_name());
	shell_print(sh, "device key: %s", edhoc_have_key() ? "present" : "missing");
	if (cred) {
		shell_print(sh, "device cred: %u-byte CCS, kid 0x%02x",
			    (unsigned)cred_len, EDHOC_KID_DEVICE);
	}
	shell_print(sh, "low slot: %s",
		    !edhoc_has_peer(LINK_PRIV_LOW) ? "empty"
		    : edhoc_slot_locked(LINK_PRIV_LOW) ? "provisioned (usb)"
						       : "pinned (ceremony)");
	shell_print(sh, "high slot: %s",
		    !edhoc_has_peer(LINK_PRIV_HIGH) ? "empty"
		    : edhoc_slot_locked(LINK_PRIV_HIGH) ? "provisioned (usb)"
							: "pinned (ceremony)");
	shell_print(sh, "session: %s", edhoc_session_active() ? "secure" : "none");
	return 0;
}

static int cmd_ek_keygen(const struct shell *sh, size_t argc, char **argv)
{
	bool force = argc == 2 && strcmp(argv[1], "force") == 0;

	if (!edhoc_keygen(force)) {
		shell_error(sh, edhoc_have_key()
			    ? "a key exists; `vg keygen force` rotates it "
			      "(orphans every printed QR)"
			    : "key generation failed");
		return -EINVAL;
	}

	shell_print(sh, "generated; read the credential back with `vg cred`");
	return 0;
}

static int cmd_ek_cred(const struct shell *sh, size_t argc, char **argv)
{
	ARG_UNUSED(argc);
	ARG_UNUSED(argv);

	size_t len = 0;
	const uint8_t *cred = edhoc_device_cred(&len);

	if (!cred) {
		shell_error(sh, "no device credential; run `vg keygen`");
		return -ENOENT;
	}

	print_hex(sh, cred, len);
	return 0;
}

static int cmd_ek_set_peer(const struct shell *sh, size_t argc, char **argv)
{
	static uint8_t ccs[EDHOC_CRED_MAX];
	enum link_privilege tier;
	size_t len;

	if (argc != 3) {
		shell_error(sh, "usage: vg set <lo|hi> <hex CCS>");
		return -EINVAL;
	}

	tier = parse_tier(argv[1]);
	if (tier == LINK_PRIV_NONE) {
		shell_error(sh, "tier must be lo or hi");
		return -EINVAL;
	}

	if (!parse_hex(argv[2], ccs, sizeof(ccs), &len)) {
		shell_error(sh, "expected up to %u hex characters, an even count",
			    2 * EDHOC_CRED_MAX);
		return -EINVAL;
	}

	if (!edhoc_set_peer(tier, ccs, len)) {
		shell_error(sh, "credential rejected");
		return -EINVAL;
	}

	show_slots();
	shell_print(sh, "provisioned %s", argv[1]);
	return 0;
}

static int cmd_ek_confirm(const struct shell *sh, size_t argc, char **argv)
{
	ARG_UNUSED(argc);
	ARG_UNUSED(argv);

	enum link_privilege armed = edhoc_armed();

	if (armed == LINK_PRIV_NONE) {
		shell_print(sh, "no pairing in progress");
		return 0;
	}

	edhoc_confirm(armed);
	shell_print(sh, "confirmed");
	return 0;
}

static int cmd_ek_clear(const struct shell *sh, size_t argc, char **argv)
{
	if (argc == 2 && strcmp(argv[1], "all") != 0) {
		enum link_privilege tier = parse_tier(argv[1]);

		if (tier == LINK_PRIV_NONE) {
			shell_error(sh, "usage: vg clear [lo|hi|all]");
			return -EINVAL;
		}

		edhoc_clear_peer(tier);
	} else {
		edhoc_clear_peers();
	}

	show_slots();
	shell_print(sh, "cleared");
	return 0;
}

static int cmd_ek_wipe(const struct shell *sh, size_t argc, char **argv)
{
	ARG_UNUSED(argc);
	ARG_UNUSED(argv);

	factory_wipe();
	shell_print(sh, "wiped every scheme's stored keys; a fresh key was generated");
	return 0;
}

SHELL_STATIC_SUBCMD_SET_CREATE(vg_cmds,
	SHELL_CMD(info, NULL, "Show credential slots and session state", cmd_ek_info),
	SHELL_CMD(keygen, NULL, "Generate the device key: vg keygen [force]", cmd_ek_keygen),
	SHELL_CMD(cred, NULL, "Print the device CCS as hex", cmd_ek_cred),
	SHELL_CMD(set, NULL, "Provision a CCS over USB (locks the slot): vg set <lo|hi> <hex>",
		  cmd_ek_set_peer),
	SHELL_CMD(confirm, NULL, "Confirm an in-progress pairing ceremony", cmd_ek_confirm),
	SHELL_CMD(clear, NULL, "Clear credential slot(s) and locks: vg clear [lo|hi|all]",
		  cmd_ek_clear),
	SHELL_CMD(wipe, NULL, "Factory wipe of every scheme's stored keys "
		  "(same as holding Buttons 2+3 for 5 s)", cmd_ek_wipe),
	SHELL_SUBCMD_SET_END);

SHELL_CMD_REGISTER(vg, &vg_cmds, "Verdigris link security", NULL);
