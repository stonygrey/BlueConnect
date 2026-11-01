// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/// The current RSSI sample and the extremes seen so far, in dBm.
final class Rssi {
  const Rssi(this.valueDbm, this.minDbm, this.maxDbm);

  const Rssi.single(int dbm) : this(dbm, dbm, dbm);

  final int valueDbm;
  final int minDbm;
  final int maxDbm;
}

/// One scanned device as last seen. A value snapshot: the scanner publishes a
/// new one on every packet, and the app keeps whichever it selected. [id] is
/// the platform identifier, the MAC address on Android and a UUID on Apple.
final class Advertisement {
  const Advertisement({
    required this.id,
    required this.localName,
    required this.isConnectable,
    required this.isSecured,
    required this.rssi,
    required this.timestamp,
  });

  final String id;
  final String localName;
  final bool isConnectable;

  /// Bit 0 or 1 of the status byte: at least one tier is pinned.
  final bool isSecured;
  final Rssi rssi;
  final DateTime timestamp;
}
