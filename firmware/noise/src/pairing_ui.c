/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

#include "pairing_ui.h"
#include "verdigris.h"

#include <string.h>

#include <zephyr/kernel.h>
#include <zephyr/bluetooth/bluetooth.h>
#include <zephyr/logging/log.h>
#include <zephyr/shell/shell.h>

#include <dk_buttons_and_leds.h>

LOG_MODULE_REGISTER(pairing_ui);

#define UNPAIR_HOLD_MS 5000
#define WIPE_HOLD_MS 5000
#define PAIR_LED_LO DK_LED3
#define PAIR_LED_HI DK_LED4
#define PAIR_BTN_LO_MSK DK_BTN3_MSK
#define PAIR_BTN_HI_MSK DK_BTN4_MSK

static int64_t button_down_at;
static bool chord_armed;

static void pair_led(int led, bool on)
{
	dk_set_led(led, on);
}

static void pair_leds_off(void)
{
	dk_set_led(PAIR_LED_LO, 0);
	dk_set_led(PAIR_LED_HI, 0);
}

static void unpair_led_off(struct k_timer *timer)
{
	ARG_UNUSED(timer);
	pair_led(PAIR_LED_HI, false);
}

static K_TIMER_DEFINE(unpair_led_timer, unpair_led_off, NULL);

/* The wipe indication: LEDs 2 and 3 (dk indices 3/4) flash together three times so a factory
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

void pairing_ui_window_begin(enum link_privilege privilege)
{
	ARG_UNUSED(privilege);
	pair_leds_off();
}

void pairing_ui_window_tick(int slice, bool high)
{
	int led = high ? PAIR_LED_HI : PAIR_LED_LO;

	pair_led(led, high ? (slice % 2) == 0 : (slice % 4) < 2);
}

void pairing_ui_window_end(void)
{
	pair_leds_off();
}

void pairing_ui_unpair_ack(void)
{
	pair_led(PAIR_LED_HI, true);
	k_timer_start(&unpair_led_timer, K_MSEC(500), K_NO_WAIT);
}

/* Factory wipe: pins, locks, and the device identity all fall; a fresh
 * identity is generated immediately so the link keeps working. Every
 * printed QR is invalidated and every pinned phone fails its pin check; that is the
 * point of the gesture.
 */
static void factory_wipe(void)
{
	link_unpair();
	link_clear_ca_root();
	(void)link_keygen(true);
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
	enum link_privilege needed = link_armed();
	uint32_t chord = PAIR_BTN_LO_MSK | PAIR_BTN_HI_MSK;

	/* Buttons 2+3 (dk indices 3/4) held together (no window armed) = factory wipe, triggered
	 * automatically at the 5 s mark while still held — the LED flashes starting
	 * are the cue to let go; no counting. Releasing early cancels. The
	 * chord claims both buttons and ignores their release.
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
		return; /* ignore the chord's release */
	}

	if (has_changed & PAIR_BTN_LO_MSK) {
		if (button_state & PAIR_BTN_LO_MSK) {
			link_confirm(LINK_PRIV_LOW);
		}
	}

	if (has_changed & PAIR_BTN_HI_MSK) {
		if (button_state & PAIR_BTN_HI_MSK) {
			if (needed != LINK_PRIV_NONE) {
				/* A pairing window owns this press; do not
				 * treat a later release as an unpair hold.
				 */
				button_down_at = 0;
				link_confirm(LINK_PRIV_HIGH);
			} else {
				button_down_at = k_uptime_get();
			}
		} else if (needed == LINK_PRIV_NONE && button_down_at &&
			   (k_uptime_get() - button_down_at) >= UNPAIR_HOLD_MS) {
			link_unpair();
			pairing_ui_unpair_ack();
			button_down_at = 0;
		} else {
			button_down_at = 0;
		}
	}
}

void pairing_ui_init(void)
{
	dk_buttons_init(button_changed);
}

static void hex_line(const struct shell *sh, const char *label, const uint8_t *data, size_t len)
{
	static char hex[2 * LINK_KEY_LEN + 1];

	for (size_t i = 0; i < len && i < LINK_KEY_LEN; i++) {
		snprintk(&hex[2 * i], 3, "%02X", data[i]);
	}
	shell_print(sh, "%s%s", label, hex);
}

static const char *provenance_label(enum link_privilege tier)
{
	switch (link_slot_provenance(tier)) {
	case LINK_PROV_USB:
		return " (usb)";
	case LINK_PROV_ENROLLED:
		return " (enrolled)";
	default:
		return "";
	}
}

static int cmd_vg_info(const struct shell *sh, size_t argc, char **argv)
{
	ARG_UNUSED(argc);
	ARG_UNUSED(argv);

	const uint8_t *pub = link_static_public();
	const uint8_t *lo = link_pin(LINK_PRIV_LOW);
	const uint8_t *hi = link_pin(LINK_PRIV_HIGH);
	const uint8_t *en = link_pin(LINK_PRIV_ENROLL);

	shell_print(sh, "name: %s", bt_get_name());
	shell_print(sh, "identity: %s", link_has_identity() ? "present" : "missing");
	if (pub) {
		hex_line(sh, "pub: ", pub, LINK_KEY_LEN);
	}
	shell_print(sh, "paired: %s", link_is_paired() ? "yes" : "no");
	shell_print(sh, "low slot: %s%s", lo ? "pinned" : "empty",
		    provenance_label(LINK_PRIV_LOW));
	if (lo) {
		hex_line(sh, "  pin: ", lo, LINK_KEY_LEN);
	}
	shell_print(sh, "high slot: %s%s", hi ? "pinned" : "empty",
		    provenance_label(LINK_PRIV_HIGH));
	if (hi) {
		hex_line(sh, "  pin: ", hi, LINK_KEY_LEN);
	}
	shell_print(sh, "enrollment key: %s", en ? "pinned (usb)" : "empty");
	if (en) {
		hex_line(sh, "  key: ", en, LINK_KEY_LEN);
	}
	shell_print(sh, "ca root: %s", link_has_ca_root() ? "loaded" : "empty");
	if (link_has_ca_root()) {
		hex_line(sh, "  root: ", link_ca_root(), LINK_KEY_LEN);
	}
	if (link_session_active()) {
		shell_print(sh, "session: secure%s",
			    link_session_endorsed() ? " (endorsed)" : "");
	} else {
		shell_print(sh, "session: none");
	}
	return 0;
}

static enum link_privilege parse_tier(const char *arg)
{
	if (strcmp(arg, "lo") == 0 || strcmp(arg, "low") == 0) {
		return LINK_PRIV_LOW;
	}
	if (strcmp(arg, "hi") == 0 || strcmp(arg, "high") == 0) {
		return LINK_PRIV_HIGH;
	}
	if (strcmp(arg, "en") == 0 || strcmp(arg, "enroll") == 0) {
		return LINK_PRIV_ENROLL;
	}
	return LINK_PRIV_NONE;
}

static int cmd_vg_clear(const struct shell *sh, size_t argc, char **argv)
{
	if (argc == 2 && strcmp(argv[1], "all") != 0) {
		enum link_privilege tier = parse_tier(argv[1]);

		if (tier == LINK_PRIV_NONE) {
			shell_error(sh, "usage: vg clear [lo|hi|en|all]");
			return -EINVAL;
		}

		link_clear_slot(tier);
	} else {
		link_unpair();
	}

	shell_print(sh, "cleared");
	return 0;
}

static int cmd_vg_keygen(const struct shell *sh, size_t argc, char **argv)
{
	bool force = argc == 2 && strcmp(argv[1], "force") == 0;

	if (!link_keygen(force)) {
		shell_error(sh, link_has_identity()
			    ? "an identity exists; `vg keygen force` rotates it "
			      "(every pinned phone will fail its pin check)"
			    : "identity generation failed");
		return -EINVAL;
	}

	shell_print(sh, "rotated; read the public key back with `vg cred`");
	return 0;
}

static int cmd_vg_cred(const struct shell *sh, size_t argc, char **argv)
{
	ARG_UNUSED(argc);
	ARG_UNUSED(argv);

	const uint8_t *pub = link_static_public();

	if (!pub) {
		shell_error(sh, "no device identity; run `vg keygen`");
		return -ENOENT;
	}

	hex_line(sh, "", pub, LINK_KEY_LEN);
	return 0;
}

static int cmd_vg_confirm(const struct shell *sh, size_t argc, char **argv)
{
	ARG_UNUSED(argc);
	ARG_UNUSED(argv);

	enum link_privilege armed = link_armed();

	if (armed == LINK_PRIV_NONE) {
		shell_print(sh, "no pairing in progress");
		return 0;
	}

	link_confirm(armed);
	shell_print(sh, "confirmed");
	return 0;
}

static int cmd_vg_set(const struct shell *sh, size_t argc, char **argv)
{
	uint8_t key[LINK_KEY_LEN];
	enum link_privilege tier;
	size_t len = 0;

	if (argc != 3) {
		shell_error(sh, "usage: vg set <lo|hi|en> <hex32>");
		return -EINVAL;
	}

	tier = parse_tier(argv[1]);
	if (tier == LINK_PRIV_NONE) {
		shell_error(sh, "tier must be lo, hi, or en");
		return -EINVAL;
	}

	for (const char *c = argv[2]; *c != '\0'; c++, len++) {
		int hi_n, lo_n;

		if (len >= LINK_KEY_LEN) {
			break;
		}
		hi_n = *c;
		hi_n = hi_n >= '0' && hi_n <= '9' ? hi_n - '0'
		     : hi_n >= 'a' && hi_n <= 'f' ? hi_n - 'a' + 10
		     : hi_n >= 'A' && hi_n <= 'F' ? hi_n - 'A' + 10 : -1;
		c++;
		lo_n = *c;
		lo_n = lo_n >= '0' && lo_n <= '9' ? lo_n - '0'
		     : lo_n >= 'a' && lo_n <= 'f' ? lo_n - 'a' + 10
		     : lo_n >= 'A' && lo_n <= 'F' ? lo_n - 'A' + 10 : -1;
		if (hi_n < 0 || lo_n < 0) {
			shell_error(sh, "expected %u hex characters", 2 * LINK_KEY_LEN);
			return -EINVAL;
		}
		key[len] = (uint8_t)((hi_n << 4) | lo_n);
	}
	if (len != LINK_KEY_LEN || argv[2][2 * LINK_KEY_LEN] != '\0') {
		shell_error(sh, "expected %u hex characters", 2 * LINK_KEY_LEN);
		return -EINVAL;
	}

	if (!link_set_pin(tier, key)) {
		shell_error(sh, "pin rejected");
		return -EINVAL;
	}

	shell_print(sh, "pinned (%s, usb)", argv[1]);
	return 0;
}

static int parse_hex32(const struct shell *sh, const char *hex, uint8_t key[LINK_KEY_LEN])
{
	size_t len = 0;

	for (const char *c = hex; *c != '\0'; c++, len++) {
		int hi_n, lo_n;

		if (len >= LINK_KEY_LEN) {
			break;
		}
		hi_n = *c;
		hi_n = hi_n >= '0' && hi_n <= '9' ? hi_n - '0'
		     : hi_n >= 'a' && hi_n <= 'f' ? hi_n - 'a' + 10
		     : hi_n >= 'A' && hi_n <= 'F' ? hi_n - 'A' + 10 : -1;
		c++;
		lo_n = *c;
		lo_n = lo_n >= '0' && lo_n <= '9' ? lo_n - '0'
		     : lo_n >= 'a' && lo_n <= 'f' ? lo_n - 'a' + 10
		     : lo_n >= 'A' && lo_n <= 'F' ? lo_n - 'A' + 10 : -1;
		if (hi_n < 0 || lo_n < 0) {
			shell_error(sh, "expected %u hex characters", 2 * LINK_KEY_LEN);
			return -EINVAL;
		}
		key[len] = (uint8_t)((hi_n << 4) | lo_n);
	}
	if (len != LINK_KEY_LEN || hex[2 * LINK_KEY_LEN] != '\0') {
		shell_error(sh, "expected %u hex characters", 2 * LINK_KEY_LEN);
		return -EINVAL;
	}

	return 0;
}

static int cmd_vg_ca_root(const struct shell *sh, size_t argc, char **argv)
{
	uint8_t root[LINK_KEY_LEN];

	if (argc == 1) {
		if (!link_has_ca_root()) {
			shell_print(sh, "ca root: empty");
			return 0;
		}

		shell_print(sh, "ca root: loaded");
		hex_line(sh, "", link_ca_root(), LINK_KEY_LEN);
		return 0;
	}

	if (argc == 2 && strcmp(argv[1], "clear") == 0) {
		link_clear_ca_root();
		shell_print(sh, "cleared");
		return 0;
	}

	if (argc != 2) {
		shell_error(sh, "usage: vg ca-root [<hex32>|clear]");
		return -EINVAL;
	}

	if (parse_hex32(sh, argv[1], root) != 0) {
		return -EINVAL;
	}

	if (!link_set_ca_root(root)) {
		shell_error(sh, "root rejected");
		return -EINVAL;
	}

	shell_print(sh, "ca root loaded");
	return 0;
}

static int cmd_vg_wipe(const struct shell *sh, size_t argc, char **argv)
{
	ARG_UNUSED(argc);
	ARG_UNUSED(argv);

	factory_wipe();
	shell_print(sh, "wiped the stored keys; a fresh identity was generated");
	return 0;
}

SHELL_STATIC_SUBCMD_SET_CREATE(vg_cmds,
	SHELL_CMD(info, NULL, "Show link identity and pairing state", cmd_vg_info),
	SHELL_CMD(keygen, NULL, "Rotate the device identity: vg keygen [force]", cmd_vg_keygen),
	SHELL_CMD(cred, NULL, "Print the device public key as hex", cmd_vg_cred),
	SHELL_CMD(set, NULL, "Pin a client key over USB (locks the slot): vg set <lo|hi|en> <hex32>",
		  cmd_vg_set),
	SHELL_CMD(confirm, NULL, "Confirm the armed pairing (matching button)", cmd_vg_confirm),
	SHELL_CMD(clear, NULL, "Clear pin slot(s) and locks: vg clear [lo|hi|en|all]", cmd_vg_clear),
	SHELL_CMD(ca-root, NULL, "Show/set endorsement CA root: vg ca-root [<hex32>|clear]",
		  cmd_vg_ca_root),
	SHELL_CMD(wipe, NULL, "Factory wipe of the stored keys "
		  "(same as holding Buttons 2+3 for 5 s)", cmd_vg_wipe),
	SHELL_SUBCMD_SET_END);

SHELL_CMD_REGISTER(vg, &vg_cmds, "Verdigris link security", NULL);
