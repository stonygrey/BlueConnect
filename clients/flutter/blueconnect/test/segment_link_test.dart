// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:typed_data';

import 'package:blueconnect/blueconnect.dart';
import 'package:flutter_test/flutter_test.dart';

import 'pipes.dart';

/// Dart has no task cancellation, so there is no caller-cancellation case;
/// the timed `read` of `ReceiveQueue` covers abandoning a wait instead.
void main() {
  const cap = 2304;
  IoLog.sink = (_) {};

  Future<Uint8List> sendAndReceive(SegmentLink send, SegmentLink recv, Uint8List message) async {
    final receive = recv.read();
    await send.write(message);
    return receive;
  }

  Future<Uint8List> echo(Uint8List message, int mtu) {
    final (a, b) = LoopbackPipe.pair(mtu: mtu);
    return sendAndReceive(SegmentLink(a, cap), SegmentLink(b, cap), message);
  }

  Matcher transportError(String fragment) => isA<BleTransportException>().having((e) => e.message.toLowerCase(), 'message', contains(fragment.toLowerCase()));

  test('round trip at default mtu', () async {
    for (final length in [0, 1, 19, 20, 38, 323]) {
      expect(await echo(payload(length), 20), payload(length), reason: 'length $length');
    }
  });

  test('round trip exactly one segment', () async => expect(await echo(payload(19), 20), payload(19)));

  test('round trip one byte into a second segment', () async => expect(await echo(payload(20), 20), payload(20)));

  test('round trip wraps seq modulo 16', () async {
    // 17 segments at P=20 (cap 19) forces seq to wrap.
    expect(await echo(payload(17 * 19), 20), payload(17 * 19));
  });

  test('two messages on the same link', () async {
    final (a, b) = LoopbackPipe.pair(mtu: 20);
    final send = SegmentLink(a, cap);
    final recv = SegmentLink(b, cap);
    expect(await sendAndReceive(send, recv, payload(40)), payload(40));
    expect(await sendAndReceive(send, recv, payload(7)), payload(7));
  });

  test('write emits start, end, and consecutive seq', () async {
    final pipe = ScriptedPipe();
    await SegmentLink(pipe, cap).write(payload(40));
    expect(pipe.written.length, 3);
    expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.start), isTrue);
    expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.end), isFalse);
    expect(SegmentHeader.has(pipe.written[1][0], SegmentHeader.start), isFalse);
    expect(SegmentHeader.has(pipe.written[1][0], SegmentHeader.end), isFalse);
    expect(SegmentHeader.has(pipe.written[2][0], SegmentHeader.end), isTrue);
    expect(SegmentHeader.has(pipe.written[2][0], SegmentHeader.start), isFalse);
    final seq0 = SegmentHeader.seq(pipe.written[0][0]);
    expect(SegmentHeader.seq(pipe.written[1][0]), (seq0 + 1) % 16);
    expect(SegmentHeader.seq(pipe.written[2][0]), (seq0 + 2) % 16);
    expect(pipe.written[0].length - 1, 19);
    expect(pipe.written[1].length - 1, 19);
    expect(pipe.written[2].length - 1, 2);
  });

  test('single segment sets start and end', () async {
    final pipe = ScriptedPipe();
    await SegmentLink(pipe, cap).write(payload(3));
    expect(pipe.written.length, 1);
    expect(SegmentHeader.flags(pipe.written[0][0]), SegmentHeader.start | SegmentHeader.end);
  });

  test('empty message is start+end with no data', () async {
    final pipe = ScriptedPipe();
    await SegmentLink(pipe, cap).write(Uint8List(0));
    expect(pipe.written.length, 1);
    expect(pipe.written[0].length, 1);
    expect(SegmentHeader.flags(pipe.written[0][0]), SegmentHeader.start | SegmentHeader.end);
  });

  test('write rejects an oversize message', () async {
    final pipe = ScriptedPipe();
    await expectLater(SegmentLink(pipe, 8).write(payload(9)), throwsStateError);
    expect(pipe.written, isEmpty);
  });

  test('write rejects a non-empty message when the mtu is 1', () async {
    final pipe = ScriptedPipe()..mtu = 1;
    await expectLater(SegmentLink(pipe, cap).write(payload(1)), throwsA(isA<BleTransportException>()));
  });

  test('write sends abort if a later segment fails', () async {
    final pipe = ScriptedPipe()..failOnWrite(1);
    await expectLater(SegmentLink(pipe, cap).write(payload(40)), throwsA(isA<BleTransportException>()));
    expect(pipe.written.length, 2);
    expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.start), isTrue);
    expect(SegmentHeader.has(pipe.written[1][0], SegmentHeader.abort), isTrue);
    final startSeq = SegmentHeader.seq(pipe.written[0][0]);
    // The failed segment used startSeq+1; the abort occupies the next slot.
    expect(SegmentHeader.seq(pipe.written[1][0]), (startSeq + 2) % SegmentHeader.seqModulus);
  });

  test('read of an empty pdu is malformed', () async {
    final pipe = ScriptedPipe()..enqueue([]);
    await expectLater(SegmentLink(pipe, cap).read(), throwsA(transportError('Malformed')));
    expect(pipe.written.length, 1);
    expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort), isTrue);
  });

  test('read with the reserved bit is malformed', () async {
    final pipe = ScriptedPipe()..enqueue([SegmentHeader.pack(0, SegmentHeader.start | SegmentHeader.end) | SegmentHeader.reserved, 0xAA]);
    await expectLater(SegmentLink(pipe, cap).read(), throwsA(transportError('Malformed')));
    expect(pipe.written.length, 1);
    expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort), isTrue);
  });

  test('read of an orphan continuation fails', () async {
    final pipe = ScriptedPipe()..enqueue([SegmentHeader.pack(1, SegmentHeader.end), 0xAA]);
    await expectLater(SegmentLink(pipe, cap).read(), throwsA(transportError('Orphan')));
    expect(pipe.written.length, 1);
    expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort), isTrue);
  });

  test('read with a sequence gap fails', () async {
    final pipe = ScriptedPipe()
      ..enqueue([SegmentHeader.pack(3, SegmentHeader.start), 0xAA])
      ..enqueue([SegmentHeader.pack(5, SegmentHeader.end), 0xBB]);
    await expectLater(SegmentLink(pipe, cap).read(), throwsA(transportError('gap')));
    expect(pipe.written.length, 1);
    expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort), isTrue);
  });

  test('read of an abort while assembling fails', () async {
    final pipe = ScriptedPipe()
      ..enqueue([SegmentHeader.pack(0, SegmentHeader.start), 0xAA])
      ..enqueue([SegmentHeader.pack(1, SegmentHeader.abort)]);
    await expectLater(SegmentLink(pipe, cap).read(), throwsA(transportError('aborted')));
  });

  test('read of an abort while idle is ignored', () async {
    final pipe = ScriptedPipe()
      ..enqueue([SegmentHeader.pack(0, SegmentHeader.abort)])
      ..enqueue([SegmentHeader.pack(2, SegmentHeader.start | SegmentHeader.end), 0x42]);
    expect(await SegmentLink(pipe, cap).read(), [0x42]);
    expect(pipe.written, isEmpty);
  });

  test('read of a start while assembling discards the partial', () async {
    final pipe = ScriptedPipe()
      ..enqueue([SegmentHeader.pack(0, SegmentHeader.start), 0xAA, 0xBB])
      ..enqueue([SegmentHeader.pack(9, SegmentHeader.start | SegmentHeader.end), 0xCC]);
    expect(await SegmentLink(pipe, cap).read(), [0xCC]);
  });

  test('read rejects a message over the cap', () async {
    final pipe = ScriptedPipe()..enqueue([SegmentHeader.pack(0, SegmentHeader.start | SegmentHeader.end), 0x01, 0x02, 0x03]);
    await expectLater(SegmentLink(pipe, 2).read(), throwsA(transportError('exceeds')));
    expect(pipe.written.length, 1);
    expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort), isTrue);
  });

  test('read times out an incomplete message', () async {
    final pipe = ScriptedPipe()..enqueue([SegmentHeader.pack(0, SegmentHeader.start), 0xAA]);
    await expectLater(SegmentLink(pipe, cap, incompleteTimeout: const Duration(milliseconds: 40)).read(), throwsA(transportError('timed out')));
    expect(pipe.written.length, 1);
    expect(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort), isTrue);
  });

  test('after a gap the next read accepts a fresh start', () async {
    final pipe = ScriptedPipe()
      ..enqueue([SegmentHeader.pack(0, SegmentHeader.start), 0xAA])
      ..enqueue([SegmentHeader.pack(2, SegmentHeader.end), 0xBB]);
    final link = SegmentLink(pipe, cap);
    await expectLater(link.read(), throwsA(isA<BleTransportException>()));
    pipe.enqueue([SegmentHeader.pack(7, SegmentHeader.start | SegmentHeader.end), 0xCC]);
    expect(await link.read(), [0xCC]);
  });

  test('a timed-out read consumes nothing', () async {
    final queue = ReceiveQueue();
    expect(await queue.pop(timeout: const Duration(milliseconds: 20)), isNull);
    queue.push(Uint8List.fromList([0x42]));
    expect(await queue.pop(), [0x42]);
  });
}
