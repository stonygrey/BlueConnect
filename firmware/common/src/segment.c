/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

#include "segment.h"

#include <string.h>

#include <zephyr/kernel.h>
#include <zephyr/logging/log.h>
#include <zephyr/bluetooth/gatt.h>
#include <zephyr/random/random.h>

LOG_MODULE_REGISTER(segment);

#define SEG_TIMEOUT_MS 5000
#define SEGQ_CAP 16

static segment_notify_fn notify_fn;

static uint8_t segq[SEGQ_CAP][SEG_ATT_MAX];
static uint16_t segq_len[SEGQ_CAP];
static uint8_t segq_r, segq_w, segq_n;

static uint8_t rx_acc[SEG_MAX_MESSAGE];
static size_t rx_acc_len;
static bool rx_assembling;
static uint8_t rx_seq;
static int64_t rx_last_ms;

static struct bt_conn *active_conn;
static K_MUTEX_DEFINE(acc_mutex);
static K_SEM_DEFINE(ready, 0, 1);

static uint32_t payload_mtu(struct bt_conn *conn)
{
	uint32_t att = bt_gatt_get_mtu(conn);

	return att > 3 ? att - 3 : 20;
}

static void send_abort(struct bt_conn *conn, uint8_t seq)
{
	uint8_t hdr = SEG_PACK(seq, SEG_ABORT);

	if (notify_fn) {
		notify_fn(conn, &hdr, 1);
	}
}

static void reset_rx(void)
{
	rx_assembling = false;
	rx_acc_len = 0;
}

void segment_init(segment_notify_fn notify)
{
	notify_fn = notify;
}

void segment_rx(struct bt_conn *conn, const uint8_t *data, uint16_t len)
{
	k_mutex_lock(&acc_mutex, K_FOREVER);

	if (active_conn != conn) {
		if (active_conn) {
			bt_conn_unref(active_conn);
		}
		active_conn = bt_conn_ref(conn);
		segq_r = segq_w = segq_n = 0;
		reset_rx();
	}

	if (len == 0 || len > SEG_ATT_MAX) {
		LOG_WRN("Rejecting ATT value of %u bytes", len);
	} else if (segq_n == SEGQ_CAP) {
		LOG_WRN("Segment queue full; dropping");
	} else {
		memcpy(segq[segq_w], data, len);
		segq_len[segq_w] = len;
		segq_w = (uint8_t)((segq_w + 1) % SEGQ_CAP);
		segq_n++;
	}

	k_mutex_unlock(&acc_mutex);
	k_sem_give(&ready);
}

void segment_wait(void)
{
	k_sem_take(&ready, K_FOREVER);
}

void segment_reset(void)
{
	k_mutex_lock(&acc_mutex, K_FOREVER);
	segq_r = segq_w = segq_n = 0;
	reset_rx();
	if (active_conn) {
		bt_conn_unref(active_conn);
		active_conn = NULL;
	}
	k_mutex_unlock(&acc_mutex);
}

bool segment_take(uint8_t *out, size_t *out_len, struct bt_conn **conn)
{
	bool got = false;

	k_mutex_lock(&acc_mutex, K_FOREVER);

	if (segq_n > 0) {
		*out_len = segq_len[segq_r];
		memcpy(out, segq[segq_r], *out_len);
		segq_r = (uint8_t)((segq_r + 1) % SEGQ_CAP);
		segq_n--;
		*conn = active_conn ? bt_conn_ref(active_conn) : NULL;
		got = true;
	}

	k_mutex_unlock(&acc_mutex);
	return got;
}

bool segment_feed(struct bt_conn *conn, const uint8_t *pdu, size_t pdu_len,
		  uint8_t *out, size_t *out_len)
{
	uint8_t header, seq, flags;
	const uint8_t *data;
	size_t dlen;
	int64_t now = k_uptime_get();

	if (pdu_len < 1) {
		send_abort(conn, 0);
		reset_rx();
		return false;
	}

	header = pdu[0];
	if (header & SEG_RESERVED) {
		LOG_WRN("Reserved segment bit set");
		send_abort(conn, 0);
		reset_rx();
		return false;
	}

	if (header & SEG_ABORT) {
		reset_rx();
		return false;
	}

	if (rx_assembling && (now - rx_last_ms) > SEG_TIMEOUT_MS) {
		LOG_WRN("Incomplete message timed out");
		reset_rx();
	}
	rx_last_ms = now;

	seq = SEG_SEQ(header);
	flags = (uint8_t)(header & 0x07);
	data = pdu + 1;
	dlen = pdu_len - 1;

	if (flags & SEG_START) {
		if (dlen > SEG_MAX_MESSAGE) {
			send_abort(conn, 0);
			reset_rx();
			return false;
		}
		rx_assembling = true;
		rx_seq = seq;
		memcpy(rx_acc, data, dlen);
		rx_acc_len = dlen;
	} else {
		if (!rx_assembling) {
			LOG_WRN("Orphan continuation");
			send_abort(conn, 0);
			return false;
		}
		if (seq != (uint8_t)((rx_seq + 1) & 0x0F)) {
			LOG_WRN("Segment sequence gap");
			send_abort(conn, 0);
			reset_rx();
			return false;
		}
		if (rx_acc_len + dlen > SEG_MAX_MESSAGE) {
			send_abort(conn, 0);
			reset_rx();
			return false;
		}
		rx_seq = seq;
		memcpy(rx_acc + rx_acc_len, data, dlen);
		rx_acc_len += dlen;
	}

	if (flags & SEG_END) {
		memcpy(out, rx_acc, rx_acc_len);
		*out_len = rx_acc_len;
		reset_rx();
		return true;
	}

	return false;
}

void segment_send(struct bt_conn *conn, const uint8_t *msg, size_t len)
{
	static uint8_t buf[SEG_ATT_MAX];
	uint32_t p = payload_mtu(conn);
	size_t cap = p > 1 ? p - 1 : 0;
	size_t off = 0;
	uint8_t seq = (uint8_t)(sys_rand32_get() & 0x0F);
	bool first = true;

	if (len > SEG_MAX_MESSAGE || cap == 0) {
		LOG_ERR("Cannot send %u-byte message (cap %u)", (unsigned)len, (unsigned)cap);
		return;
	}

	if (!notify_fn) {
		return;
	}

	do {
		size_t n = MIN(cap, len - off);
		uint8_t flags = 0;

		if (first) {
			flags |= SEG_START;
		}
		if (off + n == len) {
			flags |= SEG_END;
		}

		buf[0] = SEG_PACK(seq, flags);
		if (n > 0) {
			memcpy(buf + 1, msg + off, n);
		}
		notify_fn(conn, buf, 1 + n);

		off += n;
		first = false;
		seq = (uint8_t)((seq + 1) & 0x0F);
	} while (off < len);
}
