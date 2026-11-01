// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { BleTransportError, InvalidStateError } from '../abstractions/errors';
import { IoLog } from '../core/ioLog';
import { ReceiveQueue } from '../core/receiveQueue';
import { SegmentHeader } from '../core/segmentHeader';
import { SegmentLink } from '../core/segmentLink';
import { LoopbackPipe, ScriptedPipe, payload } from './pipes';

/**
 * JavaScript has no task cancellation, so there is no caller-cancellation
 * case; the timed `read` of `ReceiveQueue` covers abandoning a wait instead.
 */
const cap = 2304;

beforeAll(() => {
  IoLog.sink = () => {};
});

async function sendAndReceive(send: SegmentLink, recv: SegmentLink, message: Uint8Array): Promise<Uint8Array> {
  const receive = recv.read();
  await send.write(message);
  return receive;
}

function echo(message: Uint8Array, mtu: number): Promise<Uint8Array> {
  const [a, b] = LoopbackPipe.pair(mtu);
  return sendAndReceive(new SegmentLink(a, cap), new SegmentLink(b, cap), message);
}

async function transportFailure(work: Promise<unknown>, fragment: string): Promise<void> {
  await expect(work).rejects.toBeInstanceOf(BleTransportError);
  await expect(work).rejects.toThrow(new RegExp(fragment, 'i'));
}

test('round trip at default mtu', async () => {
  for (const length of [0, 1, 19, 20, 38, 323]) {
    expect(await echo(payload(length), 20)).toEqual(payload(length));
  }
});

test('round trip exactly one segment', async () => {
  expect(await echo(payload(19), 20)).toEqual(payload(19));
});

test('round trip one byte into a second segment', async () => {
  expect(await echo(payload(20), 20)).toEqual(payload(20));
});

test('round trip wraps seq modulo 16', async () => {
  // 17 segments at P=20 (cap 19) forces seq to wrap.
  expect(await echo(payload(17 * 19), 20)).toEqual(payload(17 * 19));
});

test('two messages on the same link', async () => {
  const [a, b] = LoopbackPipe.pair(20);
  const send = new SegmentLink(a, cap);
  const recv = new SegmentLink(b, cap);
  expect(await sendAndReceive(send, recv, payload(40))).toEqual(payload(40));
  expect(await sendAndReceive(send, recv, payload(7))).toEqual(payload(7));
});

test('write emits start, end, and consecutive seq', async () => {
  const pipe = new ScriptedPipe();
  await new SegmentLink(pipe, cap).write(payload(40));
  expect(pipe.written).toHaveLength(3);
  expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.start)).toBe(true);
  expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.end)).toBe(false);
  expect(SegmentHeader.has(pipe.written[1][0], SegmentHeader.start)).toBe(false);
  expect(SegmentHeader.has(pipe.written[1][0], SegmentHeader.end)).toBe(false);
  expect(SegmentHeader.has(pipe.written[2][0], SegmentHeader.end)).toBe(true);
  expect(SegmentHeader.has(pipe.written[2][0], SegmentHeader.start)).toBe(false);
  const seq0 = SegmentHeader.seq(pipe.written[0][0]);
  expect(SegmentHeader.seq(pipe.written[1][0])).toBe((seq0 + 1) % 16);
  expect(SegmentHeader.seq(pipe.written[2][0])).toBe((seq0 + 2) % 16);
  expect(pipe.written[0].length - 1).toBe(19);
  expect(pipe.written[1].length - 1).toBe(19);
  expect(pipe.written[2].length - 1).toBe(2);
});

test('single segment sets start and end', async () => {
  const pipe = new ScriptedPipe();
  await new SegmentLink(pipe, cap).write(payload(3));
  expect(pipe.written).toHaveLength(1);
  expect(SegmentHeader.flags(pipe.written[0][0])).toBe(SegmentHeader.start | SegmentHeader.end);
});

test('empty message is start+end with no data', async () => {
  const pipe = new ScriptedPipe();
  await new SegmentLink(pipe, cap).write(new Uint8Array(0));
  expect(pipe.written).toHaveLength(1);
  expect(pipe.written[0]).toHaveLength(1);
  expect(SegmentHeader.flags(pipe.written[0][0])).toBe(SegmentHeader.start | SegmentHeader.end);
});

test('write rejects an oversize message', async () => {
  const pipe = new ScriptedPipe();
  await expect(new SegmentLink(pipe, 8).write(payload(9))).rejects.toBeInstanceOf(InvalidStateError);
  expect(pipe.written).toHaveLength(0);
});

test('write rejects a non-empty message when the mtu is 1', async () => {
  const pipe = new ScriptedPipe();
  pipe.mtu = 1;
  await expect(new SegmentLink(pipe, cap).write(payload(1))).rejects.toBeInstanceOf(BleTransportError);
});

test('write sends abort if a later segment fails', async () => {
  const pipe = new ScriptedPipe();
  pipe.failOnWrite(1);
  await expect(new SegmentLink(pipe, cap).write(payload(40))).rejects.toBeInstanceOf(BleTransportError);
  expect(pipe.written).toHaveLength(2);
  expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.start)).toBe(true);
  expect(SegmentHeader.has(pipe.written[1][0], SegmentHeader.abort)).toBe(true);
  const startSeq = SegmentHeader.seq(pipe.written[0][0]);
  // The failed segment used startSeq+1; the abort occupies the next slot.
  expect(SegmentHeader.seq(pipe.written[1][0])).toBe((startSeq + 2) % SegmentHeader.seqModulus);
});

test('read of an empty pdu is malformed', async () => {
  const pipe = new ScriptedPipe();
  pipe.enqueue([]);
  await transportFailure(new SegmentLink(pipe, cap).read(), 'Malformed');
  expect(pipe.written).toHaveLength(1);
  expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort)).toBe(true);
});

test('read with the reserved bit is malformed', async () => {
  const pipe = new ScriptedPipe();
  pipe.enqueue([SegmentHeader.pack(0, SegmentHeader.start | SegmentHeader.end) | SegmentHeader.reserved, 0xaa]);
  await transportFailure(new SegmentLink(pipe, cap).read(), 'Malformed');
  expect(pipe.written).toHaveLength(1);
  expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort)).toBe(true);
});

test('read of an orphan continuation fails', async () => {
  const pipe = new ScriptedPipe();
  pipe.enqueue([SegmentHeader.pack(1, SegmentHeader.end), 0xaa]);
  await transportFailure(new SegmentLink(pipe, cap).read(), 'Orphan');
  expect(pipe.written).toHaveLength(1);
  expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort)).toBe(true);
});

test('read with a sequence gap fails', async () => {
  const pipe = new ScriptedPipe();
  pipe.enqueue([SegmentHeader.pack(3, SegmentHeader.start), 0xaa]);
  pipe.enqueue([SegmentHeader.pack(5, SegmentHeader.end), 0xbb]);
  await transportFailure(new SegmentLink(pipe, cap).read(), 'gap');
  expect(pipe.written).toHaveLength(1);
  expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort)).toBe(true);
});

test('read of an abort while assembling fails', async () => {
  const pipe = new ScriptedPipe();
  pipe.enqueue([SegmentHeader.pack(0, SegmentHeader.start), 0xaa]);
  pipe.enqueue([SegmentHeader.pack(1, SegmentHeader.abort)]);
  await transportFailure(new SegmentLink(pipe, cap).read(), 'aborted');
});

test('read of an abort while idle is ignored', async () => {
  const pipe = new ScriptedPipe();
  pipe.enqueue([SegmentHeader.pack(0, SegmentHeader.abort)]);
  pipe.enqueue([SegmentHeader.pack(2, SegmentHeader.start | SegmentHeader.end), 0x42]);
  expect(await new SegmentLink(pipe, cap).read()).toEqual(Uint8Array.of(0x42));
  expect(pipe.written).toHaveLength(0);
});

test('read of a start while assembling discards the partial', async () => {
  const pipe = new ScriptedPipe();
  pipe.enqueue([SegmentHeader.pack(0, SegmentHeader.start), 0xaa, 0xbb]);
  pipe.enqueue([SegmentHeader.pack(9, SegmentHeader.start | SegmentHeader.end), 0xcc]);
  expect(await new SegmentLink(pipe, cap).read()).toEqual(Uint8Array.of(0xcc));
});

test('read rejects a message over the cap', async () => {
  const pipe = new ScriptedPipe();
  pipe.enqueue([SegmentHeader.pack(0, SegmentHeader.start | SegmentHeader.end), 0x01, 0x02, 0x03]);
  await transportFailure(new SegmentLink(pipe, 2).read(), 'exceeds');
  expect(pipe.written).toHaveLength(1);
  expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort)).toBe(true);
});

test('read times out an incomplete message', async () => {
  const pipe = new ScriptedPipe();
  pipe.enqueue([SegmentHeader.pack(0, SegmentHeader.start), 0xaa]);
  await transportFailure(new SegmentLink(pipe, cap, 40).read(), 'timed out');
  expect(pipe.written).toHaveLength(1);
  expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort)).toBe(true);
});

test('after a gap the next read accepts a fresh start', async () => {
  const pipe = new ScriptedPipe();
  pipe.enqueue([SegmentHeader.pack(0, SegmentHeader.start), 0xaa]);
  pipe.enqueue([SegmentHeader.pack(2, SegmentHeader.end), 0xbb]);
  const link = new SegmentLink(pipe, cap);
  await expect(link.read()).rejects.toBeInstanceOf(BleTransportError);
  pipe.enqueue([SegmentHeader.pack(7, SegmentHeader.start | SegmentHeader.end), 0xcc]);
  expect(await link.read()).toEqual(Uint8Array.of(0xcc));
});

test('a timed-out read consumes nothing', async () => {
  const queue = new ReceiveQueue();
  expect(await queue.pop(20)).toBeNull();
  queue.push(Uint8Array.of(0x42));
  expect(await queue.pop()).toEqual(Uint8Array.of(0x42));
});
