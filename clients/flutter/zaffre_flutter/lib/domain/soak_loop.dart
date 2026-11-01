// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:async';
import 'dart:math';

import 'package:blueconnect/blueconnect.dart';
import 'package:flutter/foundation.dart';

import '../data/pairing_repository.dart';

/// The Loop tab: connect, echo, disconnect, over and over, with the
/// high-tier pairing. Dart has no task cancellation, so [stop] raises a
/// flag that the loop checks between steps and waits for the cycle in
/// flight to end.
final class SoakLoop extends ChangeNotifier {
  SoakLoop(this._scanner, this._options, this._pairings);

  final BleScanner _scanner;
  final BlueConnectOptions _options;
  final PairingRepository _pairings;
  final _random = Random();

  bool isRunning = false;
  String deviceName = '';
  int tests = 0;
  int connectionFailures = 0;
  int commandFailures = 0;
  double? minConnectMs;
  double? avgConnectMs;
  double? maxConnectMs;
  DateTime? started;
  Duration elapsed = Duration.zero;
  bool _stopRequested = false;
  Future<void>? _run;
  Timer? _ticker;

  Future<void> start(Advertisement device) async {
    await stop();
    tests = 0;
    connectionFailures = 0;
    commandFailures = 0;
    minConnectMs = null;
    avgConnectMs = null;
    maxConnectMs = null;
    deviceName = device.localName;
    started = DateTime.now();
    elapsed = Duration.zero;
    isRunning = true;
    _stopRequested = false;
    _ticker = Timer.periodic(const Duration(seconds: 1), (_) => _tick());
    notifyListeners();
    _run = _loop(device);
  }

  Future<void> stop() async {
    _stopRequested = true;
    _ticker?.cancel();
    _ticker = null;
    await _run;
    _run = null;
    isRunning = false;
    notifyListeners();
  }

  void _tick() {
    final started = this.started;
    if (started != null) {
      elapsed = DateTime.now().difference(started);
      notifyListeners();
    }
  }

  Future<void> _loop(Advertisement device) async {
    while (!_stopRequested) {
      final security = _pairings.security(Tier.high);
      try {
        final peripheral = _scanner.createPeripheral(device);
        final verdigris = VerdigrisPeripheral(peripheral, _options, security);
        try {
          final stopwatch = Stopwatch()..start();
          await peripheral.connect();
          _recordConnect(stopwatch.elapsedMicroseconds / 1000);
          try {
            final payload = _randomPayload(_options.isEchoVerificationEnabled ? 1536 : 16);
            final reply = await verdigris.echo(payload);
            if (!listEquals(reply, payload)) {
              throw BleTransportException('Echoed payload does not match the command.');
            }
          } catch (_) {
            commandFailures++;
          }
          try {
            await peripheral.disconnect();
          } catch (_) {
            // The cycle still counts.
          }
        } catch (_) {
          connectionFailures++;
        } finally {
          await verdigris.close();
        }
      } catch (_) {
        connectionFailures++;
      }
      tests++;
      _tick();
      if (_stopRequested) break;
      final delaySeconds = _options.minCycleDelay.inSeconds + _random.nextInt(_options.maxCycleDelay.inSeconds - _options.minCycleDelay.inSeconds + 1);
      await Future<void>.delayed(Duration(seconds: delaySeconds));
    }
    isRunning = false;
    notifyListeners();
  }

  void _recordConnect(double ms) {
    minConnectMs = minConnectMs == null ? ms : min(minConnectMs!, ms);
    maxConnectMs = maxConnectMs == null ? ms : max(maxConnectMs!, ms);
    avgConnectMs = ((avgConnectMs ?? 0) * tests + ms) / (tests + 1);
  }

  Uint8List _randomPayload(int count) => Uint8List.fromList(List.generate(count, (_) => _random.nextInt(256)));
}
