// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:typed_data';

import '../abstractions/exceptions.dart';

/// The one-byte frame type in front of every reassembled message
/// (`documents/noise-security.md`, "Wire framing").
abstract final class FrameType {
  static const plaintext = 0x00;
  static const handshake = 0x01;
  static const encrypted = 0x02;

  static Uint8List bundle(int type, Uint8List payload) {
    final message = Uint8List(1 + payload.length);
    message[0] = type;
    message.setRange(1, message.length, payload);
    return message;
  }

  static (int, Uint8List) split(Uint8List message) {
    if (message.isEmpty) {
      throw BleTransportException('Empty message; missing type byte.');
    }
    return (message[0], message.sublist(1));
  }
}
