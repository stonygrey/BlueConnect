/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* Verdigris app API: GATT service + nanopb commands (echo, time, serial,
 * LED, sqrt, fib). Pair/Unpair are one-liners into the link. Segmentation lives
 * in segment.c; the link implementation (Noise or PAKE) is supplied by the
 * app image behind link_iface.h.
 */
#include "verdigris.h"
#include "segment.h"
#include "link_iface.h"

#include <math.h>
#include <stdio.h>
#include <string.h>

#include <zephyr/kernel.h>
#include <zephyr/logging/log.h>
#include <zephyr/settings/settings.h>

#include <zephyr/bluetooth/bluetooth.h>
#include <zephyr/bluetooth/gatt.h>
#include <zephyr/bluetooth/uuid.h>

#include <pb_decode.h>
#include <pb_encode.h>

#include "src/verdigris.pb.h"

LOG_MODULE_REGISTER(verdigris);

static const struct bt_uuid_128 vg_svc_uuid = BT_UUID_INIT_128(VERDIGRIS_SVC_UUID_VAL);
static const struct bt_uuid_128 vg_rx_uuid = BT_UUID_INIT_128(VERDIGRIS_RX_UUID_VAL);
static const struct bt_uuid_128 vg_tx_uuid = BT_UUID_INIT_128(VERDIGRIS_TX_UUID_VAL);

static ssize_t vg_rx_written(struct bt_conn *conn, const struct bt_gatt_attr *attr,
			     const void *buf, uint16_t len, uint16_t offset, uint8_t flags)
{
	ARG_UNUSED(attr);
	ARG_UNUSED(offset);
	ARG_UNUSED(flags);

	verdigris_rx(conn, buf, len);
	return len;
}

static void vg_ccc_changed(const struct bt_gatt_attr *attr, uint16_t value)
{
	ARG_UNUSED(attr);
	LOG_DBG("TX notifications %s", value == BT_GATT_CCC_NOTIFY ? "enabled" : "disabled");
}

BT_GATT_SERVICE_DEFINE(vg_svc,
	BT_GATT_PRIMARY_SERVICE((void *)&vg_svc_uuid),
	BT_GATT_CHARACTERISTIC(&vg_rx_uuid.uuid,
			       BT_GATT_CHRC_WRITE | BT_GATT_CHRC_WRITE_WITHOUT_RESP,
			       BT_GATT_PERM_WRITE, NULL, vg_rx_written, NULL),
	BT_GATT_CHARACTERISTIC(&vg_tx_uuid.uuid, BT_GATT_CHRC_NOTIFY,
			       BT_GATT_PERM_NONE, NULL, NULL, NULL),
	BT_GATT_CCC(vg_ccc_changed, BT_GATT_PERM_READ | BT_GATT_PERM_WRITE),
);

#define FIB_MAX_N 93

static int64_t time_offset_ms;

/* App-controlled LED 1 state; main.c drives the pin from this. */
static bool app_led;

bool verdigris_led_state(void)
{
	return app_led;
}

static void notify_value(struct bt_conn *conn, const uint8_t *data, size_t len)
{
	for (;;) {
		int err = bt_gatt_notify(conn, &vg_svc.attrs[3], data, len);

		if (err == -EAGAIN || err == -ENOMEM) {
			k_sleep(K_MSEC(5));
			continue;
		}

		if (err) {
			LOG_WRN("Notify failed (err %d); dropping segment", err);
		}

		return;
	}
}

void verdigris_init(void)
{
	segment_init(notify_value);
}

void verdigris_rx(struct bt_conn *conn, const uint8_t *data, uint16_t len)
{
	segment_rx(conn, data, len);
}

void verdigris_reset(void)
{
	segment_reset();
	link_reset();
}

static void send_typed(struct bt_conn *conn, uint8_t type, const uint8_t *payload, size_t len)
{
	static uint8_t msg[SEG_MAX_MESSAGE + 1];

	if (len + 1 > SEG_MAX_MESSAGE) {
		LOG_ERR("Frame payload %u too large", (unsigned)len);
		return;
	}

	msg[0] = type;
	memcpy(msg + 1, payload, len);
	segment_send(conn, msg, len + 1);
}

static void send_response(struct bt_conn *conn, const verdigris_Response *rsp)
{
	static uint8_t body_buf[SEG_MAX_MESSAGE];
	static uint8_t sealed_buf[SEG_MAX_MESSAGE];
	pb_ostream_t os = pb_ostream_from_buffer(body_buf, sizeof(body_buf));
	size_t sealed_len;

	if (!pb_encode(&os, verdigris_Response_fields, rsp)) {
		LOG_ERR("Response encode failed: %s", PB_GET_ERROR(&os));
		return;
	}

	if (!link_encrypt(body_buf, os.bytes_written,
			  sealed_buf, sizeof(sealed_buf), &sealed_len)) {
		LOG_WRN("Seal failed; dropping response %u", rsp->id);
		return;
	}

	send_typed(conn, LINK_TYPE_ENCRYPTED, sealed_buf, sealed_len);
}

static void handle_request(struct bt_conn *conn, const uint8_t *frame, size_t len)
{
	static verdigris_Request req;
	static verdigris_Response rsp;

	req = (verdigris_Request)verdigris_Request_init_zero;
	rsp = (verdigris_Response)verdigris_Response_init_zero;

	pb_istream_t is = pb_istream_from_buffer(frame, len);

	if (!pb_decode(&is, verdigris_Request_fields, &req)) {
		LOG_WRN("Undecodable request: %s", PB_GET_ERROR(&is));
		return;
	}

	rsp.id = req.id;
	rsp.status = verdigris_Status_STATUS_OK;

	enum link_privilege privilege = link_privilege();
	enum link_privilege required;

	/* Enrollment sessions sit outside the tier ladder: exactly one command
	 * is served to them, and that command is served to nobody else.
	 */
	if (privilege == LINK_PRIV_ENROLL && req.which_op != verdigris_Request_enroll_tag) {
		rsp.status = verdigris_Status_STATUS_PERMISSION_DENIED;
		strncpy(rsp.detail, "an enrollment session may only Enroll",
			sizeof(rsp.detail) - 1);
		send_response(conn, &rsp);
		return;
	}
	if (privilege != LINK_PRIV_ENROLL && req.which_op == verdigris_Request_enroll_tag) {
		rsp.status = privilege == LINK_PRIV_NONE
			     ? verdigris_Status_STATUS_UNAUTHENTICATED
			     : verdigris_Status_STATUS_PERMISSION_DENIED;
		strncpy(rsp.detail, "Enroll needs a session opened with the enrollment key",
			sizeof(rsp.detail) - 1);
		send_response(conn, &rsp);
		return;
	}

	switch (req.which_op) {
	case verdigris_Request_pair_tag:
	case verdigris_Request_enroll_tag:
		required = LINK_PRIV_NONE;
		break;
	case verdigris_Request_get_time_tag:
	case verdigris_Request_get_led_tag:
	case verdigris_Request_get_serial_number_tag:
	case verdigris_Request_sqrt_tag:
	case verdigris_Request_fib_tag:
	case verdigris_Request_unpair_tag:
		required = LINK_PRIV_LOW;
		break;
	default:
		required = LINK_PRIV_HIGH;
		break;
	}

	if (privilege < required) {
		if (privilege == LINK_PRIV_NONE) {
			rsp.status = verdigris_Status_STATUS_UNAUTHENTICATED;
			strncpy(rsp.detail, "pair with the device first",
				sizeof(rsp.detail) - 1);
		} else {
			rsp.status = verdigris_Status_STATUS_PERMISSION_DENIED;
			strncpy(rsp.detail, "high privilege required",
				sizeof(rsp.detail) - 1);
		}
		send_response(conn, &rsp);
		return;
	}

	switch (req.which_op) {
	case verdigris_Request_echo_tag:
		rsp.which_result = verdigris_Response_echo_tag;
		rsp.result.echo.payload.size = req.op.echo.payload.size;
		memcpy(rsp.result.echo.payload.bytes, req.op.echo.payload.bytes,
		       req.op.echo.payload.size);
		break;

	case verdigris_Request_sqrt_tag:
		if (req.op.sqrt.value < 0) {
			rsp.status = verdigris_Status_STATUS_INVALID_ARGUMENT;
			strncpy(rsp.detail, "sqrt of a negative value", sizeof(rsp.detail) - 1);
		} else {
			rsp.which_result = verdigris_Response_sqrt_tag;
			rsp.result.sqrt.value = sqrt(req.op.sqrt.value);
		}
		break;

	case verdigris_Request_fib_tag:
		if (req.op.fib.n > FIB_MAX_N) {
			rsp.status = verdigris_Status_STATUS_OUT_OF_RANGE;
			strncpy(rsp.detail, "fib(n) overflows uint64 for n > 93",
				sizeof(rsp.detail) - 1);
		} else {
			uint64_t a = 0, b = 1;

			for (uint32_t i = 0; i < req.op.fib.n; i++) {
				uint64_t t = a + b;

				a = b;
				b = t;
			}

			rsp.which_result = verdigris_Response_fib_tag;
			rsp.result.fib.value = a;
		}
		break;

	case verdigris_Request_get_time_tag:
		rsp.which_result = verdigris_Response_time_tag;
		rsp.result.time.unix_millis =
			time_offset_ms ? time_offset_ms + k_uptime_get() : 0;
		break;

	case verdigris_Request_set_serial_number_tag:
		if (req.op.set_serial_number.serial_number > 0xFFFF) {
			rsp.status = verdigris_Status_STATUS_INVALID_ARGUMENT;
			strncpy(rsp.detail, "serial number must fit in 16 bits",
				sizeof(rsp.detail) - 1);
		} else {
			char name[20];

			snprintf(name, sizeof(name), "Verdigris %u",
				 req.op.set_serial_number.serial_number);
			verdigris_apply_name(name);
			rsp.which_result = verdigris_Response_name_tag;
			strncpy(rsp.result.name.name, name,
				sizeof(rsp.result.name.name) - 1);
		}
		break;

	case verdigris_Request_set_time_tag:
		time_offset_ms = req.op.set_time.unix_millis - k_uptime_get();
		rsp.which_result = verdigris_Response_time_tag;
		rsp.result.time.unix_millis = req.op.set_time.unix_millis;
		break;

	case verdigris_Request_pair_tag:
		switch (link_pair(req.op.pair.privilege == verdigris_Privilege_PRIVILEGE_HIGH
					  ? LINK_PRIV_HIGH
					  : LINK_PRIV_LOW)) {
		case LINK_PAIR_OK:
			rsp.which_result = verdigris_Response_ack_tag;
			break;
		case LINK_PAIR_TIER_MISMATCH:
			rsp.status = verdigris_Status_STATUS_UNAUTHENTICATED;
			strncpy(rsp.detail,
				"privilege confirmation mismatch (Button 2=low, Button 3=high)",
				sizeof(rsp.detail) - 1);
			break;
		case LINK_PAIR_LOCKED:
			rsp.status = verdigris_Status_STATUS_PERMISSION_DENIED;
			strncpy(rsp.detail, "slot is locked (USB-provisioned or enrolled)",
				sizeof(rsp.detail) - 1);
			break;
		case LINK_PAIR_UNSUPPORTED:
			rsp.status = verdigris_Status_STATUS_UNKNOWN_OP;
			strncpy(rsp.detail, "provision verifiers over USB",
				sizeof(rsp.detail) - 1);
			break;
		default:
			rsp.status = verdigris_Status_STATUS_UNAUTHENTICATED;
			strncpy(rsp.detail, "pairing was not confirmed on the device",
				sizeof(rsp.detail) - 1);
			break;
		}
		break;

	case verdigris_Request_enroll_tag:
		if (req.op.enroll.client_public_key.size != 32) {
			rsp.status = verdigris_Status_STATUS_INVALID_ARGUMENT;
			strncpy(rsp.detail, "client_public_key must be 32 bytes",
				sizeof(rsp.detail) - 1);
		} else if (link_enroll(req.op.enroll.client_public_key.bytes)) {
			rsp.which_result = verdigris_Response_ack_tag;
		} else {
			rsp.status = verdigris_Status_STATUS_PERMISSION_DENIED;
			strncpy(rsp.detail, "enrollment refused (low slot is provisioned over USB)",
				sizeof(rsp.detail) - 1);
		}
		break;

	case verdigris_Request_unpair_tag:
		if (link_unpair_slot(privilege)) {
			rsp.which_result = verdigris_Response_ack_tag;
		} else {
			rsp.status = verdigris_Status_STATUS_PERMISSION_DENIED;
			strncpy(rsp.detail, "slot is provisioned over USB - clear it there",
				sizeof(rsp.detail) - 1);
		}
		break;

	case verdigris_Request_get_serial_number_tag:
		rsp.which_result = verdigris_Response_name_tag;
		strncpy(rsp.result.name.name, bt_get_name(),
			sizeof(rsp.result.name.name) - 1);
		break;

	case verdigris_Request_get_led_tag:
		rsp.which_result = verdigris_Response_led_tag;
		rsp.result.led.on = app_led;
		break;

	case verdigris_Request_set_led_tag:
		app_led = req.op.set_led.on;
		rsp.which_result = verdigris_Response_led_tag;
		rsp.result.led.on = app_led;
		break;

	default:
		rsp.status = verdigris_Status_STATUS_UNKNOWN_OP;
		strncpy(rsp.detail, "unknown op", sizeof(rsp.detail) - 1);
		break;
	}

	send_response(conn, &rsp);
}

static void verdigris_thread(void)
{
	static uint8_t pdu[SEG_ATT_MAX];
	static uint8_t message[SEG_MAX_MESSAGE];
	static uint8_t plain[SEG_MAX_MESSAGE];
	static uint8_t hs_out[LINK_HS_OUT_CAP];
	size_t pdu_len, msg_len, out_len;
	struct bt_conn *conn;

	for (;;) {
		segment_wait();

		while (segment_take(pdu, &pdu_len, &conn)) {
			if (!conn) {
				continue;
			}

			if (segment_feed(conn, pdu, pdu_len, message, &msg_len) &&
			    msg_len > 0) {
				uint8_t type = message[0];
				const uint8_t *body = message + 1;
				size_t body_len = msg_len - 1;

				switch (type) {
				case LINK_TYPE_PLAINTEXT:
					LOG_WRN("Plaintext frame rejected");
					break;

				case LINK_TYPE_HANDSHAKE:
				case LINK_TYPE_PAKE:
				case LINK_TYPE_EDHOC:
					if (link_handshake(type, body, body_len, hs_out,
							   sizeof(hs_out), &out_len) &&
					    out_len > 0) {
						send_typed(conn, type, hs_out, out_len);
					}
					break;

				case LINK_TYPE_ENCRYPTED:
					if (link_decrypt(body, body_len, plain,
							 sizeof(plain), &out_len)) {
						handle_request(conn, plain, out_len);
					}
					break;

				default:
					LOG_WRN("Unknown frame type 0x%02x", type);
					break;
				}
			}

			bt_conn_unref(conn);
		}
	}
}

K_THREAD_DEFINE(verdigris_thread_id, 4096, verdigris_thread, NULL, NULL, NULL, 7, 0, 0);

void verdigris_wipe_foreign_credentials(const char *own_prefix)
{
	/* Every credential key any image persists. Keep in step with the
	 * store_save/store_delete calls in noise/link.c, edhoc/edhoc.c, and
	 * pake/pake.c.
	 */
	static const char *const keys[] = {
		"vg/sk", "vg/pin_lo", "vg/pin_hi", "vg/pin_en", "vg/lock_lo", "vg/lock_hi", "vg/ca_root",
		"ek/key", "ek/cred", "ek/peer_lo", "ek/peer_hi",
		"ek/lock_lo", "ek/lock_hi", "ek/fails",
		"pk/ver_lo", "pk/ver_hi", "pk/fails",
	};

	for (size_t i = 0; i < ARRAY_SIZE(keys); i++) {
		if (strncmp(keys[i], own_prefix, strlen(own_prefix)) == 0) {
			continue;
		}
		(void)settings_delete(keys[i]);
	}
	LOG_INF("Wiped the other schemes' stored credentials");
}
