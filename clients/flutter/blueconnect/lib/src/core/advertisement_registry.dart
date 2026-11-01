// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:async';

import '../abstractions/advertisement.dart';
import '../abstractions/ble_scanner.dart';
import '../abstractions/blueconnect_options.dart';

/// The scan cache that every backend shares: the last advertisement per
/// device, the broadcast event stream, the set of devices with a connection
/// in progress (which never expire), and the 5 s expiry sweep.
final class AdvertisementRegistry {
  AdvertisementRegistry({required this.minDbm, required this.maxDbm}) {
    _expiry = Timer.periodic(const Duration(seconds: 5), (_) => _expireStaleEntries());
  }

  /// Exclusive bounds; samples outside them are ignored.
  final int minDbm;
  final int maxDbm;
  final _cache = <String, Advertisement>{};
  final _connections = <String>{};
  final _events = StreamController<ScanEvent>.broadcast();
  late final Timer _expiry;

  Stream<ScanEvent> get events => _events.stream;

  List<Advertisement> get peripherals => _cache.values.toList();

  Advertisement? cached(String id) => _cache[id];

  DateTime? lastSeen(String id) => _cache[id]?.timestamp;

  /// Merges one packet. [secured] is null for packets without the Stony Grey
  /// manufacturer data, which keep the previous value.
  void observe({
    required String id,
    required String localName,
    required bool isConnectable,
    required bool? secured,
    required int dbm,
  }) {
    final now = DateTime.now();
    final previous = _cache[id];
    final advertisement = previous != null
        ? Advertisement(
            id: id,
            localName: localName,
            isConnectable: isConnectable,
            isSecured: secured ?? previous.isSecured,
            rssi: _mergeRssi(previous.rssi, dbm),
            timestamp: now,
          )
        : Advertisement(
            id: id,
            localName: localName,
            isConnectable: isConnectable,
            isSecured: secured ?? false,
            rssi: Rssi.single(dbm),
            timestamp: now,
          );
    _cache[id] = advertisement;
    _events.add(ScanEvent(previous == null ? ScanEventType.added : ScanEventType.updated, advertisement));
  }

  /// Marks a device as connecting or connected so that it is not expired
  /// while it has stopped advertising.
  void register(String id) => _connections.add(id);

  void unregister(String id) => _connections.remove(id);

  void close() {
    _expiry.cancel();
    _events.close();
  }

  Rssi _mergeRssi(Rssi previous, int dbm) => dbm > minDbm && dbm < maxDbm
      ? Rssi(dbm, previous.minDbm < dbm ? previous.minDbm : dbm, previous.maxDbm > dbm ? previous.maxDbm : dbm)
      : previous;

  void _expireStaleEntries() {
    final cutoff = DateTime.now().subtract(BlueConnectConstants.scannerExpiration);
    final expired = _cache.values.where((a) => a.timestamp.isBefore(cutoff) && !_connections.contains(a.id)).toList();
    for (final advertisement in expired) {
      _cache.remove(advertisement.id);
      _events.add(ScanEvent(ScanEventType.expired, advertisement));
    }
  }
}
