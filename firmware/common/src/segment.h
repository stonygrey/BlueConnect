/* Copyright © 2026 Stony Grey LLC.
 * SPDX-License-Identifier: MIT
 */

/* GATT segmentation: one ATT value is one self-describing segment
 * (documents/ble-framing.md). Independent of Noise and of Verdigris commands.
 */
#ifndef SEGMENT_H_
#define SEGMENT_H_

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include <zephyr/bluetooth/conn.h>

/* Keep in sync with VerdigrisPeripheral.MaxFrame on the host. */
#define SEG_MAX_MESSAGE 2304
#define SEG_ATT_MAX 256

#define SEG_START    0x01
#define SEG_END      0x02
#define SEG_ABORT    0x04
#define SEG_RESERVED 0x08
#define SEG_SEQ(h)   ((uint8_t)((h) >> 4))
#define SEG_PACK(seq, flags) ((uint8_t)((((seq) & 0x0F) << 4) | ((flags) & 0x07)))

typedef void (*segment_notify_fn)(struct bt_conn *conn, const uint8_t *data, size_t len);

void segment_init(segment_notify_fn notify);

/* Queue one inbound ATT value (GATT write callback) and wake the consumer. */
void segment_rx(struct bt_conn *conn, const uint8_t *data, uint16_t len);

/* Block until segment_rx has queued at least one ATT value. */
void segment_wait(void);

/* Drop the queue, any partial message, and the connection reference. */
void segment_reset(void);

/* Pop one queued ATT value. On success *conn is a new ref the caller must unref. */
bool segment_take(uint8_t *out, size_t *out_len, struct bt_conn **conn);

/* Feed one ATT value into reassembly. On END, copies the message into out. */
bool segment_feed(struct bt_conn *conn, const uint8_t *pdu, size_t pdu_len,
		  uint8_t *out, size_t *out_len);

/* Slice one buffer into ATT notifications. */
void segment_send(struct bt_conn *conn, const uint8_t *msg, size_t len);

#endif /* SEGMENT_H_ */
