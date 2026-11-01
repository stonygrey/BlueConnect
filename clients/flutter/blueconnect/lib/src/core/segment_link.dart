// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:math';
import 'dart:typed_data';

import '../abstractions/ble_peripheral.dart';
import '../abstractions/exceptions.dart';
import 'async_gate.dart';
import 'io_log.dart';
import 'segment_header.dart';

/// Message pipe over a peripheral: one [write] is one buffer, one [read]
/// returns one complete buffer. Each ATT value is a self-describing segment
/// (`documents/ble-framing.md`). The caller owns the peripheral and reads
/// from one place at a time.
final class SegmentLink {
  SegmentLink(this._peripheral, this._maxMessage, {Duration? incompleteTimeout})
      : _incompleteTimeout = incompleteTimeout ?? defaultIncompleteTimeout {
    if (_maxMessage <= 0) {
      throw ArgumentError.value(_maxMessage, 'maxMessage', 'must be positive');
    }
  }

  static const defaultIncompleteTimeout = Duration(seconds: 5);

  /// Bound on the best-effort `ABORT` after a failed [write].
  static const abortWriteTimeout = Duration(seconds: 2);

  final BlePeripheral _peripheral;
  final int _maxMessage;
  final Duration _incompleteTimeout;
  final _send = AsyncGate();
  final _random = Random();
  final _acc = BytesBuilder(copy: false);
  bool _assembling = false;
  int _seqIn = 0;

  Future<void> write(Uint8List message) async {
    if (message.length > _maxMessage) {
      throw StateError('Message is ${message.length} bytes; the maximum is $_maxMessage.');
    }
    await _send.withLock(() async {
      final payloadMtu = max(1, _peripheral.mtu);
      final cap = max(0, payloadMtu - 1);
      if (cap < 1 && message.isNotEmpty) {
        throw BleTransportException('ATT payload is too small to carry a segment.');
      }
      var seq = _random.nextInt(SegmentHeader.seqModulus);
      var offset = 0;
      var first = true;
      try {
        do {
          final n = min(cap, message.length - offset);
          final last = offset + n == message.length;
          var flags = 0;
          if (first) flags |= SegmentHeader.start;
          if (last) flags |= SegmentHeader.end;
          final segment = Uint8List(1 + n);
          segment[0] = SegmentHeader.pack(seq, flags);
          segment.setRange(1, 1 + n, message, offset);
          IoLog.info(IoLog.segment('write', seq, flags, segment));
          await _peripheral.write(segment);
          offset += n;
          first = false;
          seq = (seq + 1) % SegmentHeader.seqModulus;
        } while (offset < message.length);
      } catch (_) {
        await _sendAbort((seq + 1) % SegmentHeader.seqModulus).timeout(abortWriteTimeout, onTimeout: () {}).catchError((_) {});
        rethrow;
      }
    });
  }

  Future<Uint8List> read() async {
    _resetRx();
    while (true) {
      Uint8List pdu;
      if (_assembling) {
        final next = await _peripheral.read(timeout: _incompleteTimeout);
        if (next == null) {
          _resetRx();
          await _trySendAbort(0);
          throw BleTransportException('Incomplete message: timed out waiting for the next segment.');
        }
        pdu = next;
      } else {
        pdu = (await _peripheral.read())!;
      }
      _logInbound(pdu);
      if (pdu.isEmpty || SegmentHeader.has(pdu[0], SegmentHeader.reserved)) {
        _resetRx();
        await _trySendAbort(0);
        throw BleTransportException('Malformed segment header.');
      }
      final header = pdu[0];
      if (SegmentHeader.has(header, SegmentHeader.abort)) {
        final wasAssembling = _assembling;
        _resetRx();
        if (wasAssembling) {
          throw BleTransportException('Peer aborted the message.');
        }
        continue;
      }
      final seq = SegmentHeader.seq(header);
      if (SegmentHeader.has(header, SegmentHeader.start)) {
        _assembling = true;
        _seqIn = seq;
        _acc.clear();
        _acc.add(pdu.sublist(1));
      } else {
        if (!_assembling) {
          await _trySendAbort(0);
          throw BleTransportException('Orphan continuation segment.');
        }
        if (seq != (_seqIn + 1) % SegmentHeader.seqModulus) {
          _resetRx();
          await _trySendAbort(0);
          throw BleTransportException('Segment sequence gap.');
        }
        _seqIn = seq;
        _acc.add(pdu.sublist(1));
      }
      if (_acc.length > _maxMessage) {
        _resetRx();
        await _trySendAbort(0);
        throw BleTransportException('Message exceeds the $_maxMessage-byte cap.');
      }
      if (SegmentHeader.has(header, SegmentHeader.end)) {
        final complete = _acc.toBytes();
        _resetRx();
        return complete;
      }
    }
  }

  void _logInbound(Uint8List pdu) {
    if (pdu.isEmpty) {
      IoLog.info(IoLog.segment('read', 0, 0, pdu));
      return;
    }
    IoLog.info(IoLog.segment('read', SegmentHeader.seq(pdu[0]), SegmentHeader.flags(pdu[0]), pdu));
  }

  void _resetRx() {
    _assembling = false;
    _acc.clear();
  }

  /// Best effort: the read that triggered this already failed.
  Future<void> _trySendAbort(int seq) async {
    try {
      await _send.withLock(() => _sendAbort(seq));
    } catch (_) {
      // The read already failed.
    }
  }

  Future<void> _sendAbort(int seq) async {
    final abort = Uint8List.fromList([SegmentHeader.pack(seq, SegmentHeader.abort)]);
    IoLog.info(IoLog.segment('write', seq, SegmentHeader.abort, abort));
    await _peripheral.write(abort);
  }
}
