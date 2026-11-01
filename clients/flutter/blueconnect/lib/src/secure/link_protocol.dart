// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:typed_data';

import 'package:torann/torann.dart';

/// The Noise suites the link runs. Every Torann call crosses to the host over
/// Pigeon, so the parsed protocols are futures, cached after the first use.
abstract final class LinkProtocol {
  static const name = 'Noise_XX_25519_AESGCM_SHA256';
  static const pinnedName = 'Noise_IK_25519_AESGCM_SHA256';
  static const keyLength = 32;
  static const tagLength = 16;

  static Future<NoiseProtocol>? _xx;
  static Future<NoiseProtocol>? _ik;

  static Future<NoiseProtocol> xx() => _xx ??= NoiseProtocol.parse(name);

  static Future<NoiseProtocol> ik() => _ik ??= NoiseProtocol.parse(pinnedName);
}

/// The app's long-term X25519 identity for one tier.
final class LinkIdentity {
  LinkIdentity._(this.privateKey, this.publicKey);

  final Uint8List privateKey;
  final Uint8List publicKey;

  /// Derives the public key with Torann. Throws [ArgumentError] for a key
  /// that is not 32 bytes.
  static Future<LinkIdentity> fromPrivateKey(Uint8List privateKey) async {
    if (privateKey.length != LinkProtocol.keyLength) {
      throw ArgumentError('A link identity private key is ${LinkProtocol.keyLength} bytes.');
    }
    final keyPair = await (await LinkProtocol.xx()).createKeyPair(privateKey);
    try {
      return LinkIdentity._(Uint8List.fromList(privateKey), Uint8List.fromList(keyPair.publicKey));
    } finally {
      await keyPair.close();
    }
  }

  static Future<LinkIdentity> generate() async {
    final keyPair = await (await LinkProtocol.xx()).generateKeyPair();
    try {
      return LinkIdentity._(Uint8List.fromList(await keyPair.privateKey()), Uint8List.fromList(keyPair.publicKey));
    } finally {
      await keyPair.close();
    }
  }
}

/// What a session is opened with: this side's identity and, once paired, the
/// device key it must present. No endorsement: this library does not carry
/// client certificates.
final class LinkSecurity {
  const LinkSecurity(this.identity, this.pinnedPeerKey);

  final LinkIdentity identity;
  final Uint8List? pinnedPeerKey;
}
