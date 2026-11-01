// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:async';
import 'dart:convert';
import 'dart:math';

import 'package:blueconnect/blueconnect.dart';
import 'package:flutter/foundation.dart';

import '../data/device_selection.dart';
import '../data/pairing_repository.dart';
import '../domain/command_runner.dart';
import '../domain/soak_loop.dart';
import '../domain/toasts.dart';

/// The scan list, sampled from the scanner every 500 ms.
final class ScanViewModel extends ChangeNotifier {
  ScanViewModel(this._scanner, this._selection);

  final BleScanner _scanner;
  final DeviceSelection _selection;
  List<Advertisement> devices = const [];
  String? scanFailure;
  Timer? _sampling;

  Future<void> start() async {
    try {
      await _scanner.start();
    } catch (e) {
      scanFailure = describe(e);
      notifyListeners();
      return;
    }
    _sampling = Timer.periodic(const Duration(milliseconds: 500), (_) {
      devices = _scanner.peripherals.toList()..sort((a, b) => b.rssi.valueDbm.compareTo(a.rssi.valueDbm));
      _selection.refresh(devices);
      notifyListeners();
    });
  }

  void select(Advertisement device) => _selection.value = device;

  @override
  void dispose() {
    _sampling?.cancel();
    super.dispose();
  }
}

/// The Noise tab and the Low and High tabs: pairing state and the command
/// actions, per tier.
final class NoiseViewModel extends ChangeNotifier {
  NoiseViewModel(this.selection, this.pairings, this.commands, this.loop, this._toasts) {
    for (final tier in Tier.values) {
      serialInput[tier] = _randomSerial();
      echoInput[tier] = 'Hello, Verdigris!';
    }
    pairings.load();
  }

  final DeviceSelection selection;
  final PairingRepository pairings;
  final CommandRunner commands;
  final SoakLoop loop;
  final Toasts _toasts;
  final Map<Tier, String> serialInput = {};
  final Map<Tier, String> echoInput = {};
  final _random = Random();

  /// Commands and pairing are allowed when a device is selected, the
  /// credentials are loaded, nothing else is running, and the loop is stopped.
  bool get canRun => selection.value != null && pairings.isLoaded && !commands.isBusy && !loop.isRunning;

  Future<void> pair(Tier tier) => commands.pair(selection.value, tier);

  Future<void> unpair(Tier tier) => commands.unpair(selection.value, tier);

  /// A scanned SGNK1 or SGNE1 payload for the tier's card.
  Future<void> provision(Tier tier, String text) => commands.provision(selection.value, tier, text);

  Future<void> readSerial(Tier tier) async {
    final name = await commands.run(selection.value, tier, (v) => v.getSerialNumber());
    if (name != null) _toasts.info("Advertising as '$name'");
  }

  Future<void> writeSerial(Tier tier) async {
    final serial = int.tryParse(serialInput[tier]!.trim());
    if (serial == null || serial < 1 || serial > 65535) {
      _toasts.error('Serial number must be a whole number from 1 to 65535.');
      return;
    }
    final name = await commands.run(selection.value, tier, (v) => v.setSerialNumber(serial));
    if (name != null) {
      _toasts.info("Now advertising as '$name'");
      serialInput[tier] = _randomSerial();
      notifyListeners();
    }
  }

  Future<void> readLed(Tier tier) => commands.readLed(selection.value, tier);

  Future<void> tapLed(Tier tier) => commands.tapLed(selection.value, tier);

  Future<void> echo(Tier tier) async {
    final payload = Uint8List.fromList(utf8.encode(echoInput[tier]!));
    final reply = await commands.run(selection.value, tier, (v) => v.echo(payload));
    if (reply != null) {
      final text = utf8.decode(reply, allowMalformed: true);
      if (text.isNotEmpty) _toasts.info(text);
    }
  }

  String _randomSerial() => (1 + _random.nextInt(65535)).toString();
}

/// The Loop tab.
final class LoopViewModel extends ChangeNotifier {
  LoopViewModel(this.selection, this.loop);

  final DeviceSelection selection;
  final SoakLoop loop;

  Future<void> start() async {
    final device = selection.value;
    if (device != null) await loop.start(device);
  }

  Future<void> stop() => loop.stop();
}
