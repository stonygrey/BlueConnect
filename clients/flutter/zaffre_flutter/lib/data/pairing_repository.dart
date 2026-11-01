// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:convert';

import 'package:blueconnect/blueconnect.dart';
import 'package:flutter/foundation.dart';

import 'credential_store.dart';

/// The two privilege tiers, each with its own app identity and pin.
enum Tier {
  low(PrivilegeLevel.low, 'Low privilege', 'Button 2', 'Low'),
  high(PrivilegeLevel.high, 'High privilege', 'Button 3', 'High');

  const Tier(this.privilege, this.title, this.button, this.logName);

  final PrivilegeLevel privilege;
  final String title;

  /// The nRF54L15-DK silkscreen label of the confirming button.
  final String button;
  final String logName;

  String get key => name;
}

/// The source of a pin: the button ceremony, a USB-provisioned QR, or
/// enrollment.
enum CredentialOrigin {
  paired('Paired'),
  provisioned('Provisioned'),
  enrolled('Enrolled');

  const CredentialOrigin(this.label);

  final String label;

  static CredentialOrigin? parse(String? value) {
    for (final origin in values) {
      if (origin.name == value) return origin;
    }
    return null;
  }
}

final class TierCredential {
  const TierCredential({required this.identity, this.deviceKey, this.deviceName, this.origin});

  final LinkIdentity identity;
  final Uint8List? deviceKey;
  final String? deviceName;
  final CredentialOrigin? origin;

  bool get isPaired => deviceKey != null;

  TierCredential copyWith({Uint8List? deviceKey, String? deviceName, CredentialOrigin? origin, bool clear = false}) => TierCredential(
        identity: identity,
        deviceKey: clear ? null : deviceKey ?? this.deviceKey,
        deviceName: clear ? null : deviceName ?? this.deviceName,
        origin: clear ? null : origin ?? this.origin,
      );
}

/// Identity and pin storage per tier. Identities derive their public key
/// through Torann, which is asynchronous on Flutter, so the repository loads
/// after construction and reports [isLoaded].
final class PairingRepository extends ChangeNotifier {
  PairingRepository(this._store);

  final CredentialStore _store;
  final Map<Tier, TierCredential> _credentials = {};

  bool get isLoaded => _credentials.length == Tier.values.length;

  Map<Tier, TierCredential> get credentials => Map.unmodifiable(_credentials);

  TierCredential? credential(Tier tier) => _credentials[tier];

  Future<void> load() async {
    for (final tier in Tier.values) {
      final identity = await _loadOrGenerateIdentity(tier);
      final deviceKey = await _store.get('zaffre.pair.device.key.${tier.key}');
      final deviceName = await _store.get('zaffre.pair.device.name.${tier.key}');
      final origin = CredentialOrigin.parse(_text(await _store.get('zaffre.pair.origin.${tier.key}')));
      _credentials[tier] = TierCredential(
        identity: identity,
        deviceKey: deviceKey,
        deviceName: _text(deviceName),
        origin: deviceKey == null ? null : origin ?? CredentialOrigin.paired,
      );
      debugPrint('ZAFFRE-NOISE-PUB: ${tier.logName} ${IoLog.hex(identity.publicKey)} cert=none.');
    }
    notifyListeners();
  }

  /// The security for a normal session: IK against the pin when paired.
  LinkSecurity security(Tier tier) {
    final credential = _credentials[tier]!;
    return LinkSecurity(credential.identity, credential.deviceKey);
  }

  /// The security for the pairing ceremony: XX, any device key accepted.
  LinkSecurity pairingSecurity(Tier tier) => LinkSecurity(_credentials[tier]!.identity, null);

  /// Pins the device key for the tier: after the button ceremony
  /// (`paired`) or after `Enroll` (`enrolled`).
  Future<void> record(Tier tier, Uint8List deviceKey, String? deviceName, {CredentialOrigin origin = CredentialOrigin.paired}) async {
    await _store.set('zaffre.pair.device.key.${tier.key}', deviceKey);
    await _store.set('zaffre.pair.device.name.${tier.key}', deviceName == null ? null : Uint8List.fromList(utf8.encode(deviceName)));
    await _store.set('zaffre.pair.origin.${tier.key}', Uint8List.fromList(utf8.encode(origin.name)));
    _credentials[tier] = TierCredential(identity: _credentials[tier]!.identity, deviceKey: deviceKey, deviceName: deviceName, origin: origin);
    debugPrint("${origin.label} (${tier.logName}) with '${deviceName ?? ''}' (${IoLog.hex(deviceKey)}).");
    notifyListeners();
  }

  /// Installs a USB-provisioned credential: the identity from the QR
  /// replaces the tier's own, and the device key is pinned. No BLE traffic.
  Future<void> storeProvisioned(Tier tier, LinkIdentity identity, Uint8List deviceKey, String? deviceName) async {
    await _store.set('zaffre.link.identity.${tier.key}', identity.privateKey);
    _credentials[tier] = TierCredential(identity: identity);
    await record(tier, deviceKey, deviceName, origin: CredentialOrigin.provisioned);
  }

  /// Deletes the pin. A provisioned tier also gets a fresh identity, because
  /// the one from the QR stays locked into the device's slot until USB
  /// possession clears it.
  Future<void> clear(Tier tier) async {
    final wasProvisioned = _credentials[tier]!.origin == CredentialOrigin.provisioned;
    await _store.set('zaffre.pair.device.key.${tier.key}', null);
    await _store.set('zaffre.pair.device.name.${tier.key}', null);
    await _store.set('zaffre.pair.origin.${tier.key}', null);
    _credentials[tier] = _credentials[tier]!.copyWith(clear: true);
    if (wasProvisioned) {
      await _store.set('zaffre.link.identity.${tier.key}', null);
      _credentials[tier] = TierCredential(identity: await _loadOrGenerateIdentity(tier));
    }
    notifyListeners();
  }

  Future<LinkIdentity> _loadOrGenerateIdentity(Tier tier) async {
    final key = 'zaffre.link.identity.${tier.key}';
    final stored = await _store.get(key);
    if (stored != null) {
      try {
        return await LinkIdentity.fromPrivateKey(stored);
      } catch (_) {
        // A corrupt entry is replaced below.
      }
    }
    final identity = await LinkIdentity.generate();
    await _store.set(key, identity.privateKey);
    debugPrint('Generated ${tier.logName} app link identity ${IoLog.hex(identity.publicKey)}.');
    return identity;
  }

  static String? _text(Uint8List? bytes) => bytes == null ? null : utf8.decode(bytes);
}
