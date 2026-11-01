// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:typed_data';

import 'advertisement.dart';

enum ConnectionState { disconnected, connected }

/// One GATT connection to a Verdigris device. [read] returns one ATT value
/// (one notification); [write] sends raw bytes, split at [mtu]. Segmentation
/// is above this in `SegmentLink`.
abstract interface class BlePeripheral {
  Advertisement get advertisement;

  bool get isConnected;

  /// The ATT payload size, already `ATT_MTU - 3`. Zero when not connected.
  int get mtu;

  /// Connects with the retry policy in `BlueConnectOptions`.
  Future<void> connect();

  Future<void> disconnect();

  /// The next received ATT value, or null when [timeout] passes first. A
  /// timed-out read consumes nothing: the next call still gets the next value.
  /// Dart has no task cancellation, so the timeout is part of the call.
  Future<Uint8List?> read({Duration? timeout});

  Future<void> write(Uint8List bytes);

  /// Connection state changes, broadcast.
  Stream<ConnectionState> get states;

  /// Disconnects if connected and releases the platform object. Idempotent.
  Future<void> close();
}
