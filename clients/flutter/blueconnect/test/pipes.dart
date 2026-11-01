// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:typed_data';

import 'package:blueconnect/blueconnect.dart';

final pipeAdvertisement = Advertisement(
  id: 'pipe',
  localName: 'pipe',
  isConnectable: true,
  isSecured: false,
  rssi: const Rssi.single(-50),
  timestamp: DateTime.now(),
);

Uint8List payload(int length) => Uint8List.fromList(List.generate(length, (i) => i & 0xFF));

/// Test double: inbound ATT values are queued by the test, outbound writes
/// are recorded and can be scripted to fail.
final class ScriptedPipe implements BlePeripheral {
  final _inbound = ReceiveQueue();
  final List<Uint8List> written = [];
  int? _failAt;

  @override
  int mtu = 20;

  @override
  Advertisement get advertisement => pipeAdvertisement;

  @override
  bool get isConnected => true;

  @override
  Stream<ConnectionState> get states => const Stream.empty();

  void enqueue(List<int> bytes) => _inbound.push(Uint8List.fromList(bytes));

  void failOnWrite(int index) => _failAt = index;

  @override
  Future<void> connect() async {}

  @override
  Future<void> disconnect() async {}

  @override
  Future<Uint8List?> read({Duration? timeout}) => _inbound.pop(timeout: timeout);

  @override
  Future<void> write(Uint8List bytes) async {
    if (_failAt == written.length) {
      _failAt = null;
      throw BleTransportException('scripted write failure');
    }
    written.add(bytes);
  }

  @override
  Future<void> close() async => _inbound.close(BleTransportException('closed'));
}

/// Two cross-wired in-memory peripherals. Each [write] is one ATT value.
final class LoopbackPipe implements BlePeripheral {
  LoopbackPipe._(this.mtu);

  final _inbound = ReceiveQueue();
  late LoopbackPipe _peer;

  @override
  int mtu;

  @override
  Advertisement get advertisement => pipeAdvertisement;

  @override
  bool get isConnected => true;

  @override
  Stream<ConnectionState> get states => const Stream.empty();

  static (LoopbackPipe, LoopbackPipe) pair({int mtu = 20}) {
    final a = LoopbackPipe._(mtu);
    final b = LoopbackPipe._(mtu);
    a._peer = b;
    b._peer = a;
    return (a, b);
  }

  @override
  Future<void> connect() async {}

  @override
  Future<void> disconnect() async {}

  @override
  Future<Uint8List?> read({Duration? timeout}) => _inbound.pop(timeout: timeout);

  @override
  Future<void> write(Uint8List bytes) async => _peer._inbound.push(bytes);

  @override
  Future<void> close() async => _inbound.close(BleTransportException('closed'));
}
