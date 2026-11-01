// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:async';

import 'package:universal_ble/universal_ble.dart';

import '../abstractions/advertisement.dart';
import '../abstractions/ble_peripheral.dart';
import '../abstractions/ble_scanner.dart';
import '../abstractions/blueconnect_options.dart';
import '../abstractions/exceptions.dart';
import '../core/advertisement_registry.dart';
import 'universal_ble_peripheral.dart';

/// The scanner over the universal_ble plugin: a scan filtered on the
/// Verdigris service UUID, merged into the shared registry. universal_ble
/// strips the company id from manufacturer data on every platform, so the
/// first payload byte is the status byte, as on Android. The plugin does not
/// expose the connectable flag, so every device is reported as connectable.
final class BackendScanner implements BleScanner {
  BackendScanner({this.options = const BlueConnectOptions()});

  final BlueConnectOptions options;
  final _registry = AdvertisementRegistry(minDbm: -127, maxDbm: 20);
  StreamSubscription<BleDevice>? _results;
  bool _scanning = false;

  @override
  List<Advertisement> get peripherals => _registry.peripherals;

  @override
  bool get isScanning => _scanning;

  @override
  Stream<ScanEvent> get events => _registry.events;

  @override
  DateTime? lastSeen(String id) => _registry.lastSeen(id);

  @override
  Future<void> start() async {
    if (_scanning) return;
    // Android 12 and later need BLUETOOTH_SCAN and BLUETOOTH_CONNECT at
    // runtime. Scanning is not used to derive location, so no location permission.
    if (!await UniversalBle.hasPermissions(withAndroidFineLocation: false)) {
      await UniversalBle.requestPermissions(withAndroidFineLocation: false);
    }
    final state = await UniversalBle.getBluetoothAvailabilityState();
    if (state == AvailabilityState.unsupported || state == AvailabilityState.unauthorized) {
      throw BleTransportException('Bluetooth is unavailable: ${state.name}.');
    }
    if (state == AvailabilityState.poweredOff) {
      throw BleTransportException('Bluetooth is disabled.');
    }
    _results ??= UniversalBle.scanStream.listen(_handle);
    await UniversalBle.startScan(scanFilter: ScanFilter(withServices: [options.serviceUuid]));
    _scanning = true;
  }

  @override
  BlePeripheral createPeripheral(Advertisement advertisement) {
    final cached = _registry.cached(advertisement.id);
    if (cached == null) {
      throw StateError("'${advertisement.localName}' (${advertisement.id}) is no longer in the scan cache.");
    }
    return BackendPeripheral(this, cached, options);
  }

  /// Marks a device as connecting so that the registry does not expire it.
  void register(String id) => _registry.register(id);

  void unregister(String id) => _registry.unregister(id);

  void _handle(BleDevice device) {
    final name = (device.name ?? '').isNotEmpty ? device.name! : (_registry.cached(device.deviceId)?.localName ?? '(unnamed)');
    bool? secured;
    for (final data in device.manufacturerDataList) {
      if (data.companyId == options.manufacturerCompanyId && data.payload.isNotEmpty) {
        final status = data.payload[0];
        secured = status & BlueConnectConstants.securedStatusMask != 0;
      }
    }
    _registry.observe(
      id: device.deviceId,
      localName: name,
      isConnectable: true,
      secured: secured,
      dbm: device.rssi ?? 0,
    );
  }
}
