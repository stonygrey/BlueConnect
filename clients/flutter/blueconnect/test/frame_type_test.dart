// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:typed_data';

import 'package:blueconnect/blueconnect.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('bundle and split round trip', () {
    final bundled = FrameType.bundle(FrameType.encrypted, Uint8List.fromList([1, 2, 3]));
    expect(bundled, [0x02, 1, 2, 3]);
    final (type, payload) = FrameType.split(bundled);
    expect(type, FrameType.encrypted);
    expect(payload, [1, 2, 3]);
  });

  test('split rejects an empty message', () {
    expect(() => FrameType.split(Uint8List(0)), throwsA(isA<BleTransportException>()));
  });

  test('handshake lengths match the wire format', () {
    expect(SecureLink.handshakeMessage1Length, 32);
    expect(SecureLink.handshakeMessage2Length, 97);
    expect(SecureLink.handshakeMessage3Length, 65);
    expect(SecureLink.ikHandshakeMessage1Length, 97);
    expect(SecureLink.ikHandshakeMessage2Length, 49);
    expect(VerdigrisPeripheral.maxEncodedRequest, 2287);
  });

  test('io log lines match the format lib/smalt parses', () {
    expect(IoLog.message('write', 2, Uint8List.fromList([0x02, 0xAB])), '_BLE_IO message write type=02 len=2 hex=02AB');
    expect(IoLog.segment('read', 3, SegmentHeader.start | SegmentHeader.end, Uint8List.fromList([0x33, 0x01])), '_BLE_IO segment read seq=3 flags=start+end len=2 hex=3301');
    expect(IoLog.formatFlags(0), 'none');
  });
}
