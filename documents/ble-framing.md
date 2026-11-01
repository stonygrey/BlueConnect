<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# GATT message framing

This document describes how BlueConnect carries one buffer of any agreed
length over ATT writes and notifications, and reassembles it on the
other side. The host implementation is
`clients/maui/StonyGrey/BlueConnect/Core/SegmentLink.cs`. The firmware implementation
is `firmware/common/src/segment.c`. Both deliver an opaque buffer. They
do not interpret the bytes.

An ATT write, notification, or indication is one PDU. Its value is at
most `ATT_MTU − 3` bytes (20 at the default MTU). The controller may
fragment that PDU across radio packets, but it does not turn a 32-byte
GATT write into two ATT writes. The Prepare Write procedure on iOS and
Android is a different procedure; it is not available for notifications
and it is not a stream. It is for this reason that the only portable
atomic unit is one characteristic value of at most `ATT_MTU − 3` bytes,
and anything larger is carried by this protocol.

`ATT_MTU` is whatever the two stacks negotiated. The host reads it from
`IPeripheral.MTU`, which is already the payload size. The firmware uses
`bt_gatt_get_mtu(conn) − 3`.

Every ATT value is a self-describing segment. A message is one `START`
segment, zero or more continuations, and an `END` segment; a message
that fits in one segment sets both flags. The header is one byte: three
flags and a 4-bit sequence number. The packet length is not encoded,
because the ATT value length already gives the size of each segment.

The host sends with GATT Write Request. The device sends with GATT
Notification. A missing, extra, or oversized segment is a gap. The
message it belongs to is discarded and the `Read` or `Write` fails.

```
  caller  (opaque buffer)
        │
        ▼
  SegmentLink / segment.c
  message ↔ GATT segments
        │
        ▼
  ATT write-with-response  (central → peripheral)
  ATT notification         (peripheral → central)
  value length ≤ ATT_MTU − 3
```

Notifications can be dropped by the stack. A dropped notification fails
the message in progress. What this layer prevents is a dropped
notification being taken for the middle of the next message.

## Segment format

Every ATT value is exactly one segment. Bit 0 is the least-significant
bit. The figures show transmission order, left to right.

### Header

```
  7   6   5   4   3   2   1   0
+---+---+---+---+---+---+---+---+
|      seq      | 0 | A | E | S |
+---+---+---+---+---+---+---+---+
                              S = START
                          E     = END
                      A         = ABORT
                  bit 3 MUST be 0; reject otherwise
                  seq is bits 7–4, increments by 1 mod 16
```

| Bits | Mask | Name | Meaning |
|---|---|---|---|
| 0 | `0x01` | `START` | First segment of a message. |
| 1 | `0x02` | `END` | Last segment of a message. |
| 2 | `0x04` | `ABORT` | Discard any partial message in this direction. `data` is ignored. |
| 3 | `0x08` | reserved | Send 0; reject if set. |
| 7–4 | `0xF0` | `seq` | Per-message sequence. The first segment's `seq` may be anything. Each following segment is `+1 (mod 16)`. |

A single-segment message sets `START|END`. Continuations set neither.
`ABORT` may be sent alone, and `START` and `END` on an `ABORT` are
ignored. Receivers ignore `seq` on `ABORT`. A send-path abort (same
direction, after a failed outgoing segment) uses the next `seq` in that
message. A reply `ABORT` (opposite direction, after a bad inbound
segment) is a standalone one-byte message and its `seq` may be
anything.

These flag constants are `SegmentHeader` on the host.

### Data segment (`ABORT` clear)

```
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|    header     |                                               |
+-+-+-+-+-+-+-+-+                                               +
|                            data …                             |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

`P = ATT_MTU − 3` at send time. There is no length field. The ATT value
length is `1 + data.Length`. The receiver appends `data` until it sees
`END`.

### Abort

```
 0
 0 1 2 3 4 5 6 7
+-+-+-+-+-+-+-+-+
|    header     |
+-+-+-+-+-+-+-+-+
```

`ABORT` is set. `data` is absent, or ignored if present.

### Budget at the default 20-byte payload

```
 P = 20

 every data segment     1-byte header,  up to 19 data bytes
 ABORT                  1-byte header,  no data
```

Any legal ATT MTU (`ATT_MTU ≥ 23`, so `P ≥ 20`) can carry a `START`.

A 32-byte message at `P = 20` is two segments (19 + 13 data bytes). A
2 KiB write is 19 + 19×107 bytes of data, 108 segments. At the 182-byte
payload iOS usually negotiates, the same 2 KiB is twelve segments.
Segmentation is the normal case for Verdigris messages at every MTU.

`seq` is four bits, so it wraps every 16 segments. The check is that
this `seq` is the previous one plus 1, not that `seq` is unique for the
whole connection. Losing exactly 16 consecutive segments and landing on
the expected nibble is therefore not detected as a gap; the
incomplete-message timeout (5 s on both sides) and the caller's own
checks cover that case.

## Worked example

Message `M` is 32 bytes. `P = 20`. The sender picks first `seq = 3`.

Segment 0 carries `START` and 19 data bytes. The header is `seq 3` with
`START`, `0x31`:

```
     0        1        2        3        4        5        6        7
+--------+--------+--------+--------+--------+--------+--------+--------+
|  0x31  |  M[0]  |  M[1]  |  M[2]  |  M[3]  |  M[4]  |  M[5]  |  M[6]  |
| S seq=3|                                                              |
+--------+--------+--------+--------+--------+--------+--------+--------+
|  M[7]  |  M[8]  |  M[9]  |  M[10] |  M[11] |  M[12] |  M[13] |  M[14] |
+--------+--------+--------+--------+--------+--------+--------+--------+
|  M[15] |  M[16] |  M[17] |  M[18] |
+--------+--------+--------+--------+
                                              20-byte ATT value
```

Segment 1 carries `END` and the remaining 13 data bytes. The header is
`seq 4` with `END`, `0x42`:

```
     0        1        2        3        4        5        6        7
+--------+--------+--------+--------+--------+--------+--------+--------+
|  0x42  |  M[19] |  M[20] |  M[21] |  M[22] |  M[23] |  M[24] |  M[25] |
| E seq=4|                                                              |
+--------+--------+--------+--------+--------+--------+--------+--------+
|  M[26] |  M[27] |  M[28] |  M[29] |  M[30] |  M[31] |
+--------+--------+--------+--------+--------+--------+
                                              14-byte ATT value
```

The receiver delivers `M` unchanged. It has not looked at `M[0]`.

If segment 0 arrives and segment 1 does not, the receiver keeps the
partial message for 5 s and then sends `ABORT`. It does not read the
next notification's first bytes as a length prefix.

## Sender

Let `P = ATT_MTU − 3` at send time. Let `M` be the message. Let
`cap = P − 1`, the data bytes per segment.

1. If `M.Length` is greater than the peer's cap, fail locally. Do not send.
2. Choose a starting `seq`, anything in `0…15`.
3. Emit segment 0: `START` (and `END` if `M` fits in `cap`), that `seq`,
   `data = M[0 .. min(cap, M.Length)]`.
4. While bytes remain: `seq = (seq + 1) mod 16`, emit a continuation of at
   most `cap` bytes, `END` on the last.
5. Central to peripheral: Write Request for every segment. Wait for the
   ATT acknowledgement before the next segment.
6. Peripheral to central: Notification for every segment. Do not wait.
   Do not indicate.

If the stack returns an error, or the send is cancelled, emit one
`ABORT` if the link is still up, with the next `seq`, then fail the
`Write`. The host bounds that abort at 2 s
(`SegmentLink.AbortWriteTimeout`), because the caller's token may
already be cancelled when the abort is sent.

Two messages are never interleaved in one direction. One `Write`
finishes, or aborts, before the next starts. The two directions are
independent; half-duplex request and response is a policy of the layer
above.

## Receiver

Per direction the receiver keeps `seq_in`, the expected `seq` while
assembling and unset when idle, and `acc`, the partial message.

On each ATT value:

```
                    ┌─────────────┐
                    │    idle     │
                    └──────┬──────┘
                           │ START
                           ▼
                    ┌─────────────┐
           more     │ assembling  │  END
         ◄──────────┤             ├──────────► deliver, idle
           seq +1   └──────┬──────┘
                           │ gap / bad bits / overflow / orphan
                           ▼
                    ┌─────────────┐
                    │ send ABORT  │──────────► idle, fail Read
                    └─────────────┘
```

1. Empty value, or reserved bit set: send `ABORT`, go idle, error.
2. `ABORT`: discard `acc`, go idle. If a message was in progress, that
   `Read` fails.
3. `START`: if already assembling, discard the partial, because the
   sender has restarted. Set `seq_in = header.seq` and append `data`.
4. Otherwise, a continuation: if idle, send `ABORT` and fail (an orphan).
   If `header.seq != (seq_in + 1) mod 16`, send `ABORT`, go idle, and
   fail (a gap). Else set `seq_in = header.seq` and append `data`.
5. After appending, if `acc.Length` exceeds the local cap: `ABORT`, idle,
   fail.
6. `END`: deliver `acc`. Go idle.
7. Incomplete-message timeout (5 s, `SEG_TIMEOUT_MS` on the firmware and
   `SegmentLink.DefaultIncompleteTimeout` on the host): `ABORT`, fail the
   `Read`. Without it a lost last notification would leave the receiver
   assembling until disconnect.

A failed `Read` or `Write` is reported to the caller. This layer does
not decide whether the connection, or anything above it, must be closed.

## Cap

There is no length on the wire. Each side stops accumulating at a local
cap. Host and firmware both use 2304 bytes
(`VerdigrisPeripheral.MaxFrame`, `SEG_MAX_MESSAGE`). The value is
arbitrary and specific to this implementation; it is not derived from
the ATT MTU or from any Verdigris message size. Peers do not negotiate
the cap, so the two constants should be kept in sync. A message within
the sender's cap but over the receiver's is aborted by the receiver at
step 5.

## Connection lifecycle

| Event | Action |
|---|---|
| Connect, MTU exchange, CCC configured for notifications | idle in both directions |
| Disconnect | Discard `acc`, fail any in-flight `Read` or `Write` |
| MTU update mid-connection | Use the new `P` for the segments that follow. Do not resize a segment already handed to ATT. |
| `ABORT` received | Receiver step 2 |

There is no version field. If the segment header ever changes
incompatibly, the change is a new GATT characteristic UUID, not a flag
bit.

## What this layer does not do

This layer is not reliability. Notifications can still be dropped. A gap
is a clean failure of one message; the messages after it are unaffected.

This layer is not an application protocol. No type byte, channel,
transaction id, or checksum is here. Those are in the buffer the caller
supplies (e.g. the type byte, `0x01` handshake or `0x02` sealed, in
`SecureLink`).

This layer is not a UART sentinel codec. BLE has already framed the ATT
value. A delimiter-byte scheme concatenates two messages when the byte
that was lost was the delimiter.

This layer is not L2CAP CoC. Callers write a buffer and read a buffer,
so CoC could replace this layer without changing them, once every
target OS exposes it. Until then this is the GATT path.
