// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/// Values shared with the firmware.
abstract final class BlueConnectConstants {
  static const scannerExpiration = Duration(seconds: 60);
  static const verdigrisServiceUuid = '2B200001-465F-4625-94D8-37EC5C99BFB3';
  static const verdigrisRxCharacteristicUuid = '2B200002-465F-4625-94D8-37EC5C99BFB3';
  static const verdigrisTxCharacteristicUuid = '2B200003-465F-4625-94D8-37EC5C99BFB3';
  static const manufacturerCompanyId = 0xFFFF;
  static const lowPrivilegePairedFlag = 0x01;
  static const highPrivilegePairedFlag = 0x02;
  static const securedStatusMask = lowPrivilegePairedFlag | highPrivilegePairedFlag;
}

/// Connection and command policy. The app uses the defaults as they stand;
/// there is no configuration file.
final class BlueConnectOptions {
  const BlueConnectOptions({
    this.connectTimeout = const Duration(seconds: 15),
    this.commandTimeout = const Duration(seconds: 10),
    this.connectRetryTimeout = const Duration(seconds: 30),
    this.retryOnlyWhileAdvertising = true,
    this.minCycleDelay = const Duration(seconds: 1),
    this.maxCycleDelay = const Duration(seconds: 5),
    this.isEchoVerificationEnabled = true,
    this.serviceUuid = BlueConnectConstants.verdigrisServiceUuid,
    this.rxCharacteristicUuid = BlueConnectConstants.verdigrisRxCharacteristicUuid,
    this.txCharacteristicUuid = BlueConnectConstants.verdigrisTxCharacteristicUuid,
    this.manufacturerCompanyId = BlueConnectConstants.manufacturerCompanyId,
  });

  final Duration connectTimeout;
  final Duration commandTimeout;

  /// The whole retry window. Zero retries until the caller stops.
  final Duration connectRetryTimeout;
  final bool retryOnlyWhileAdvertising;
  final Duration minCycleDelay;
  final Duration maxCycleDelay;
  final bool isEchoVerificationEnabled;
  final String serviceUuid;
  final String rxCharacteristicUuid;
  final String txCharacteristicUuid;
  final int manufacturerCompanyId;
}
