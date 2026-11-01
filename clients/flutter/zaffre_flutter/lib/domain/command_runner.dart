// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'package:blueconnect/blueconnect.dart';
import 'package:flutter/foundation.dart';

import '../data/pairing_repository.dart';
import 'toasts.dart';

/// Runs one Verdigris command at a time: each command opens its own
/// connection, runs, disconnects, and releases the peripheral.
final class CommandRunner extends ChangeNotifier {
  CommandRunner(this._scanner, this._options, this._pairings, this._toasts);

  final BleScanner _scanner;
  final BlueConnectOptions _options;
  final PairingRepository _pairings;
  final Toasts _toasts;
  bool _busy = false;
  final Map<Tier, bool> _ledOn = {};
  final Map<Tier, String> _pairResult = {};

  bool get isBusy => _busy;

  /// LED 1 as last reported by the device, per tier page; absent until read.
  bool? ledOn(Tier tier) => _ledOn[tier];

  /// The pairing card's result line.
  String pairResult(Tier tier) => _pairResult[tier] ?? '';

  /// Connect, run [body], disconnect, close. The connection is never kept
  /// between commands.
  Future<T> _session<T>(Advertisement device, LinkSecurity security, Future<T> Function(VerdigrisPeripheral) body) async {
    final peripheral = _scanner.createPeripheral(device);
    final verdigris = VerdigrisPeripheral(peripheral, _options, security);
    try {
      await peripheral.connect();
      final result = await body(verdigris);
      await peripheral.disconnect();
      return result;
    } finally {
      await verdigris.close();
    }
  }

  /// A command on the tier's pinned session. A failure becomes an error
  /// toast, and null is returned.
  Future<T?> run<T>(Advertisement? device, Tier tier, Future<T> Function(VerdigrisPeripheral) body) async {
    if (device == null || _busy) return null;
    _setBusy(true);
    try {
      return await _session(device, _pairings.security(tier), body);
    } catch (e) {
      debugPrint("Verdigris command failed against '${device.localName}': $e");
      _toasts.error(describe(e));
      return null;
    } finally {
      _setBusy(false);
    }
  }

  Future<void> readLed(Advertisement? device, Tier tier) async {
    final on = await run(device, tier, (v) => v.getLed());
    if (on != null) {
      _ledOn[tier] = on;
      notifyListeners();
    }
  }

  /// Tap on the LED: read it when unknown, otherwise switch it.
  Future<void> tapLed(Advertisement? device, Tier tier) async {
    final current = _ledOn[tier];
    if (current == null) {
      await readLed(device, tier);
      return;
    }
    final on = await run(device, tier, (v) => v.setLed(!current));
    if (on != null) {
      _ledOn[tier] = on;
      notifyListeners();
    }
  }

  /// The ceremony: an XX session that accepts any device key, `Pair`, and
  /// the button within 30 s. On Ack the proven device key is pinned.
  Future<void> pair(Advertisement? device, Tier tier) async {
    if (device == null || _busy) return;
    _setBusy(true);
    _pairResult[tier] = 'Press ${tier.button} on the device to confirm…';
    notifyListeners();
    try {
      final key = await _session(device, _pairings.pairingSecurity(tier), (v) {
        // The marker a hardware test watches for to confirm over USB.
        debugPrint('ZAFFRE-PAIR: armed ${tier.key}');
        return v.pair(tier.privilege);
      });
      await _pairings.record(tier, key, device.localName);
      _pairResult[tier] = '';
    } on VerdigrisException catch (e) {
      if (e.status == Status.STATUS_UNAUTHENTICATED) {
        _pairResult[tier] = e.detail != null
            ? 'Pairing not confirmed: ${e.detail}'
            : 'Pairing not confirmed. Press ${tier.button} on the device within 30 seconds and try again.';
      } else {
        _pairResult[tier] = '';
        _toasts.error(describe(e));
      }
    } catch (e) {
      _pairResult[tier] = '';
      _toasts.error(describe(e));
    } finally {
      _setBusy(false);
    }
  }

  /// A pasted or scanned credential for the tier's card: an `SGNE1` card
  /// enrolls the low tier over the air, an `SGNK1` credential is installed
  /// without any traffic, and anything else is refused.
  Future<void> provision(Advertisement? device, Tier tier, String text) async {
    final trimmed = text.trim();
    if (trimmed.isEmpty) {
      _toasts.error('Paste the SGNK1 payload first.');
      return;
    }
    final card = NoiseEnrollmentPayload.parse(trimmed);
    if (card != null) {
      if (tier != Tier.low) {
        _toasts.warning('An enrollment QR enrolls the low tier — scan it on the low card.');
        return;
      }
      if (device == null) {
        _toasts.error('Select the device first.');
        return;
      }
      await _enroll(device, card);
      return;
    }
    final credential = NoiseQrPayload.parse(trimmed);
    if (credential == null) {
      _toasts.error('That is not a BlueConnect Noise credential (SGNK1) or enrollment (SGNE1) payload.');
      return;
    }
    final payloadTier = credential.tier == PrivilegeLevel.high ? Tier.high : Tier.low;
    if (payloadTier != tier) {
      _toasts.warning('That QR is the ${payloadTier.key} Noise credential — scan it on the ${payloadTier.key} card.');
      return;
    }
    try {
      final identity = await LinkIdentity.fromPrivateKey(credential.appPrivateKey);
      await _pairings.storeProvisioned(tier, identity, credential.devicePublicKey, credential.deviceName);
    } catch (e) {
      _toasts.error(describe(e));
    }
  }

  /// One IK session as the enrollment key, with the device key from the
  /// card pinned, carrying `Enroll` with the low identity's public key. The
  /// enrollment key is used for this call and never stored.
  Future<void> _enroll(Advertisement device, NoiseEnrollmentPayload card) async {
    if (_busy) return;
    _setBusy(true);
    try {
      final enrollmentIdentity = await LinkIdentity.fromPrivateKey(card.enrollmentPrivateKey);
      final security = LinkSecurity(enrollmentIdentity, card.devicePublicKey);
      final lowPublicKey = _pairings.credential(Tier.low)!.identity.publicKey;
      await _session(device, security, (v) => v.enroll(lowPublicKey));
      await _pairings.record(Tier.low, card.devicePublicKey, card.deviceName ?? device.localName, origin: CredentialOrigin.enrolled);
    } catch (e) {
      debugPrint("Enrollment failed against '${device.localName}': $e");
      _toasts.error(describe(e));
    } finally {
      _setBusy(false);
    }
  }

  /// `Unpair` on the pinned session, then the local pin is cleared even if
  /// the device call failed.
  Future<void> unpair(Advertisement? device, Tier tier) async {
    if (device == null || _busy) return;
    if (!(_pairings.credential(tier)?.isPaired ?? false)) {
      _toasts.error('Not paired.');
      return;
    }
    _setBusy(true);
    try {
      await _session(device, _pairings.security(tier), (v) => v.unpair());
    } catch (e) {
      debugPrint('Device-side unpair (${tier.logName}) failed; clearing the local pairing anyway: $e');
    } finally {
      _setBusy(false);
    }
    await _pairings.clear(tier);
    _pairResult[tier] = '';
    notifyListeners();
  }

  void _setBusy(bool value) {
    _busy = value;
    notifyListeners();
  }
}
