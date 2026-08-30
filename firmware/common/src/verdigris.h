/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* The Verdigris GATT service: these UUIDs are the product surface.
 * The same RX write / TX notify pipe carries segmented messages whose
 * type byte is handshake or sealed app payload (see link.h).
 */
#ifndef VERDIGRIS_H_
#define VERDIGRIS_H_

#include <zephyr/types.h>
#include <zephyr/bluetooth/conn.h>
#include <zephyr/bluetooth/uuid.h>

/* Stony Grey vendor UUID family (base 465F-4625-94D8-37EC5C99BFB3):
 * service ...0001, RX (central writes) ...0002, TX (notify) ...0003.
 * Keep in sync with BlueConnectConstants on the host.
 */
#define VERDIGRIS_SVC_UUID_VAL \
	BT_UUID_128_ENCODE(0x2b200001, 0x465f, 0x4625, 0x94d8, 0x37ec5c99bfb3)
#define VERDIGRIS_RX_UUID_VAL \
	BT_UUID_128_ENCODE(0x2b200002, 0x465f, 0x4625, 0x94d8, 0x37ec5c99bfb3)
#define VERDIGRIS_TX_UUID_VAL \
	BT_UUID_128_ENCODE(0x2b200003, 0x465f, 0x4625, 0x94d8, 0x37ec5c99bfb3)

void verdigris_init(void);

/* GATT write callback → segment queue. */
void verdigris_rx(struct bt_conn *conn, const uint8_t *data, uint16_t len);

/* Drop partial segments, the connection ref, and the link session. */
void verdigris_reset(void);

/* Set + persist the advertised name (implemented in main.c). */
void verdigris_apply_name(const char *name);

/* Pairing bits in advertised manufacturer data (implemented in main.c). */
void verdigris_set_secured(uint8_t flags);

/* App-controlled LED 1 state (SetLed command); main.c drives the pin. */
bool verdigris_led_state(void);

/* Factory-wipe support. Settings survive a `west flash` sector erase, so
 * every scheme's credentials sit in the same partition regardless of which
 * image is running — a wipe that only cleared its own store would leave
 * another image's trust (USB locks included) to resurrect on reflash.
 * Deletes every known credential key EXCEPT those under own_prefix
 * ("vg/", "ek/", or "pk/"), which the caller's own clear/keygen functions
 * manage coherently with their RAM state.
 */
void verdigris_wipe_foreign_credentials(const char *own_prefix);

#endif /* VERDIGRIS_H_ */
