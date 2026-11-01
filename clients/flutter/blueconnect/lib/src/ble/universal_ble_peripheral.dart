// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:async';
import 'dart:typed_data';

import 'package:universal_ble/universal_ble.dart';

import '../abstractions/advertisement.dart';
import '../abstractions/ble_peripheral.dart';
import '../abstractions/blueconnect_options.dart';
import '../abstractions/exceptions.dart';
import '../core/connect_retry.dart';
import '../core/receive_queue.dart';
import 'universal_ble_scanner.dart';

/// One GATT connection through universal_ble. The plugin queues GATT
/// operations itself and returns futures, so this class only orders them:
/// connect, request the MTU, discover, subscribe, then one write at a time.
final class BackendPeripheral implements BlePeripheral {
  BackendPeripheral(this._scanner, this.advertisement, this._options);

  final BackendScanner _scanner;
  final BlueConnectOptions _options;
  final _received = ReceiveQueue();
  final _states = StreamController<ConnectionState>.broadcast();
  StreamSubscription<Uint8List>? _values;
  StreamSubscription<bool>? _connection;
  bool _connected = false;
  bool _closed = false;
  int _mtu = 0;

  @override
  final Advertisement advertisement;

  @override
  bool get isConnected => _connected;

  @override
  int get mtu => _connected ? _mtu : 0;

  @override
  Stream<ConnectionState> get states => _states.stream;

  @override
  Future<void> connect() async {
    if (_closed) {
      throw StateError('The peripheral has been closed.');
    }
    await ConnectRetry.run(
      options: _options,
      name: advertisement.localName,
      isPresent: () {
        final seen = _scanner.lastSeen(advertisement.id);
        return seen != null && DateTime.now().difference(seen) <= BlueConnectConstants.scannerExpiration;
      },
      attempt: _connectOnce,
    );
  }

  Future<void> _connectOnce(Duration timeout) async {
    _scanner.register(advertisement.id);
    try {
      await _connectCore().timeout(timeout);
      _connected = true;
      _states.add(ConnectionState.connected);
    } on TimeoutException {
      await _abortConnect();
      throw BleTimeoutException("Connecting to '${advertisement.localName}' timed out after ${timeout.inSeconds}s.");
    } catch (_) {
      await _abortConnect();
      rethrow;
    }
  }

  Future<void> _connectCore() async {
    final id = advertisement.id;
    _connection ??= UniversalBle.connectionStream(id).listen((connected) {
      if (!connected && _connected) {
        _connected = false;
        _states.add(ConnectionState.disconnected);
        _received.close(BleTransportException('Peripheral disconnected.'));
      }
    });
    await UniversalBle.connect(id);
    // The plugin reports the negotiated ATT MTU; the usable payload is 3 less.
    int att;
    try {
      att = await UniversalBle.requestMtu(id, 247);
    } catch (_) {
      att = 23;
    }
    _mtu = att - 3 < 20 ? 20 : att - 3;
    final services = await UniversalBle.discoverServices(id);
    final service = services.where((s) => _sameUuid(s.uuid, _options.serviceUuid)).firstOrNull;
    if (service == null) {
      throw BleTransportException('Peripheral does not expose GATT service ${_options.serviceUuid}.');
    }
    if (!service.characteristics.any((c) => _sameUuid(c.uuid, _options.rxCharacteristicUuid))) {
      throw BleTransportException('RX characteristic ${_options.rxCharacteristicUuid} not found.');
    }
    if (!service.characteristics.any((c) => _sameUuid(c.uuid, _options.txCharacteristicUuid))) {
      throw BleTransportException('TX characteristic ${_options.txCharacteristicUuid} not found.');
    }
    _values ??= UniversalBle.characteristicValueStream(id, _options.txCharacteristicUuid.toLowerCase()).listen(_received.push);
    await UniversalBle.subscribeNotifications(id, _options.serviceUuid, _options.txCharacteristicUuid);
  }

  @override
  Future<void> disconnect() async {
    if (!_connected) return;
    await UniversalBle.disconnect(advertisement.id);
    _connected = false;
    _states.add(ConnectionState.disconnected);
  }

  @override
  Future<Uint8List?> read({Duration? timeout}) => _received.pop(timeout: timeout);

  @override
  Future<void> write(Uint8List bytes) async {
    if (!_connected) {
      throw StateError('Peripheral is not connected.');
    }
    final chunk = _mtu < 1 ? 1 : _mtu;
    var offset = 0;
    while (offset < bytes.length) {
      final end = offset + chunk < bytes.length ? offset + chunk : bytes.length;
      await UniversalBle.write(advertisement.id, _options.serviceUuid, _options.rxCharacteristicUuid, bytes.sublist(offset, end));
      offset = end;
    }
  }

  @override
  Future<void> close() async {
    if (_closed) return;
    _closed = true;
    if (_connected) {
      try {
        await disconnect().timeout(const Duration(seconds: 5));
      } catch (_) {
        // Teardown must not throw; the cleanup below still runs.
      }
    } else {
      await _abortConnect();
    }
    await _values?.cancel();
    await _connection?.cancel();
    _scanner.unregister(advertisement.id);
    _received.close(BleTransportException('The peripheral has been closed.'));
    await _states.close();
  }

  Future<void> _abortConnect() async {
    try {
      await UniversalBle.disconnect(advertisement.id).timeout(const Duration(seconds: 5));
    } catch (_) {
      // Best effort; the attempt already failed.
    }
  }

  static bool _sameUuid(String a, String b) => a.toLowerCase() == b.toLowerCase();
}
