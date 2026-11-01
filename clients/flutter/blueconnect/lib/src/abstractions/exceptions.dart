// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/// The failures the library reports. The messages are written for the app's
/// toasts. A call the current state does not allow throws Dart's own
/// [StateError].
class BlueConnectException implements Exception {
  BlueConnectException(this.message, [this.cause]);

  final String message;
  final Object? cause;

  @override
  String toString() => message;
}

/// The GATT layer or the segment layer failed.
final class BleTransportException extends BlueConnectException {
  BleTransportException(super.message, [super.cause]);
}

/// The handshake or an AEAD check failed.
final class LinkSecurityException extends BlueConnectException {
  LinkSecurityException(super.message, [super.cause]);
}

/// A connect attempt, retry window, or command ran out of time.
final class BleTimeoutException extends BlueConnectException {
  BleTimeoutException(super.message, [super.cause]);
}
