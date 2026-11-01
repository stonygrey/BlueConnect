// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:convert';
import 'dart:typed_data';

import '../abstractions/exceptions.dart';
import '../verdigris/verdigris_peripheral.dart';
import 'link_protocol.dart';

/// A USB-provisioned Noise credential as the QR carries it:
/// `SGNK1:<L|H>:<base64url 64 bytes>[:<NAME>]`, the blob being the app's
/// X25519 private key followed by the device's X25519 public key. The QR is
/// the credential itself.
final class NoiseQrPayload {
  const NoiseQrPayload({required this.tier, required this.appPrivateKey, required this.devicePublicKey, this.deviceName});

  static const prefix = 'SGNK1';

  final PrivilegeLevel tier;
  final Uint8List appPrivateKey;
  final Uint8List devicePublicKey;
  final String? deviceName;

  String encode() {
    if (appPrivateKey.length != LinkProtocol.keyLength ||
        devicePublicKey.length != LinkProtocol.keyLength ||
        _isZero(appPrivateKey) ||
        _isZero(devicePublicKey)) {
      throw LinkSecurityException('Noise QR payload fields are out of range.');
    }
    final encoded = _encodeBase64Url(Uint8List.fromList([...appPrivateKey, ...devicePublicKey]));
    final letter = tier == PrivilegeLevel.high ? 'H' : 'L';
    final name = deviceName;
    return name == null || name.isEmpty ? '$prefix:$letter:$encoded' : '$prefix:$letter:$encoded:$name';
  }

  /// Null for anything that is not a well-formed SGNK1 payload with non-zero keys.
  static NoiseQrPayload? parse(String text) {
    final parts = _split(text.trim(), 4);
    if (parts.length < 3 || parts[0] != prefix) return null;
    final PrivilegeLevel tier;
    switch (parts[1]) {
      case 'L':
        tier = PrivilegeLevel.low;
      case 'H':
        tier = PrivilegeLevel.high;
      default:
        return null;
    }
    final blob = _decodeBase64Url(parts[2]);
    if (blob == null || blob.length != 2 * LinkProtocol.keyLength) return null;
    final appPrivate = blob.sublist(0, LinkProtocol.keyLength);
    final devicePublic = blob.sublist(LinkProtocol.keyLength);
    if (_isZero(appPrivate) || _isZero(devicePublic)) return null;
    return NoiseQrPayload(
      tier: tier,
      appPrivateKey: appPrivate,
      devicePublicKey: devicePublic,
      deviceName: parts.length == 4 && parts[3].isNotEmpty ? parts[3] : null,
    );
  }
}

/// An enrollment card: `SGNE1:<base64url 64 bytes>[:<NAME>]`, the blob being
/// the device's enrollment private key followed by the device's public key.
/// Used once, to enroll the phone's own low-tier key, then discarded.
final class NoiseEnrollmentPayload {
  const NoiseEnrollmentPayload({required this.enrollmentPrivateKey, required this.devicePublicKey, this.deviceName});

  static const prefix = 'SGNE1';

  final Uint8List enrollmentPrivateKey;
  final Uint8List devicePublicKey;
  final String? deviceName;

  String encode() {
    if (enrollmentPrivateKey.length != LinkProtocol.keyLength ||
        devicePublicKey.length != LinkProtocol.keyLength ||
        _isZero(enrollmentPrivateKey) ||
        _isZero(devicePublicKey)) {
      throw LinkSecurityException('Noise enrollment payload fields are out of range.');
    }
    final encoded = _encodeBase64Url(Uint8List.fromList([...enrollmentPrivateKey, ...devicePublicKey]));
    final name = deviceName;
    return name == null || name.isEmpty ? '$prefix:$encoded' : '$prefix:$encoded:$name';
  }

  static NoiseEnrollmentPayload? parse(String text) {
    final parts = _split(text.trim(), 3);
    if (parts.length < 2 || parts[0] != prefix) return null;
    final blob = _decodeBase64Url(parts[1]);
    if (blob == null || blob.length != 2 * LinkProtocol.keyLength) return null;
    final enrollmentPrivate = blob.sublist(0, LinkProtocol.keyLength);
    final devicePublic = blob.sublist(LinkProtocol.keyLength);
    if (_isZero(enrollmentPrivate) || _isZero(devicePublic)) return null;
    return NoiseEnrollmentPayload(
      enrollmentPrivateKey: enrollmentPrivate,
      devicePublicKey: devicePublic,
      deviceName: parts.length == 3 && parts[2].isNotEmpty ? parts[2] : null,
    );
  }
}

/// `String.split` on `:` with a limit: at most [limit] parts, the last one
/// keeping any further separators.
List<String> _split(String text, int limit) {
  final parts = <String>[];
  var rest = text;
  while (parts.length < limit - 1) {
    final i = rest.indexOf(':');
    if (i < 0) break;
    parts.add(rest.substring(0, i));
    rest = rest.substring(i + 1);
  }
  parts.add(rest);
  return parts;
}

/// Base64url without padding, as the QR payloads carry it; the decoder also
/// accepts padded input.
String _encodeBase64Url(Uint8List bytes) => base64Url.encode(bytes).replaceAll('=', '');

Uint8List? _decodeBase64Url(String text) {
  try {
    return base64Url.decode(base64Url.normalize(text));
  } on FormatException {
    return null;
  }
}

bool _isZero(List<int> key) => key.every((b) => b == 0);
