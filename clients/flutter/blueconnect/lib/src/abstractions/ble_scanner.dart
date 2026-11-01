// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'advertisement.dart';
import 'ble_peripheral.dart';

enum ScanEventType { added, updated, expired }

final class ScanEvent {
  const ScanEvent(this.type, this.advertisement);

  final ScanEventType type;
  final Advertisement advertisement;
}

/// A BLE central that scans for Verdigris devices and creates peripherals for
/// them. Backends implement it over the shared `AdvertisementRegistry`.
abstract interface class BleScanner {
  /// Every device seen within the last [BlueConnectConstants.scannerExpiration].
  List<Advertisement> get peripherals;

  bool get isScanning;

  /// Starts scanning. Completes once the radio is on and the scan is running.
  Future<void> start();

  /// Scan events, broadcast; a late listener sees only what happens after it subscribes.
  Stream<ScanEvent> get events;

  /// When [id] was last seen, or null once it has expired from the cache.
  DateTime? lastSeen(String id);

  /// A peripheral for a device still in the cache. Throws [StateError] once
  /// the entry has expired.
  BlePeripheral createPeripheral(Advertisement advertisement);
}
