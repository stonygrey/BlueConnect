// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Where identities and pins are stored.
abstract interface class CredentialStore {
  Future<Uint8List?> get(String key);

  /// Stores [value], or removes the entry when [value] is null.
  Future<void> set(String key, Uint8List? value);
}

/// flutter_secure_storage: the Keychain on iOS and EncryptedSharedPreferences
/// on Android. On macOS it needs the keychain-access-groups entitlement,
/// which the local build does not carry, so the macOS target is not a
/// supported configuration yet.
final class SecureCredentialStore implements CredentialStore {
  final _storage = const FlutterSecureStorage();

  @override
  Future<Uint8List?> get(String key) async {
    final value = await _storage.read(key: key);
    return value == null ? null : base64Decode(value);
  }

  @override
  Future<void> set(String key, Uint8List? value) async {
    if (value == null) {
      await _storage.delete(key: key);
    } else {
      await _storage.write(key: key, value: base64Encode(value));
    }
  }
}
