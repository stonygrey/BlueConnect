// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/** Values shared with the firmware. */
export const BlueConnectConstants = {
  scannerExpirationMs: 60_000,
  verdigrisServiceUuid: '2B200001-465F-4625-94D8-37EC5C99BFB3',
  verdigrisRxCharacteristicUuid: '2B200002-465F-4625-94D8-37EC5C99BFB3',
  verdigrisTxCharacteristicUuid: '2B200003-465F-4625-94D8-37EC5C99BFB3',
  manufacturerCompanyId: 0xffff,
  lowPrivilegePairedFlag: 0x01,
  highPrivilegePairedFlag: 0x02,
  securedStatusMask: 0x03,
} as const;

/**
 * Connection and command policy. The app uses the defaults as they stand;
 * there is no configuration file. Durations are milliseconds.
 */
export interface BlueConnectOptions {
  readonly connectTimeoutMs: number;
  readonly commandTimeoutMs: number;
  /** The whole retry window. Zero retries until the caller stops. */
  readonly connectRetryTimeoutMs: number;
  readonly retryOnlyWhileAdvertising: boolean;
  readonly minCycleDelayMs: number;
  readonly maxCycleDelayMs: number;
  readonly isEchoVerificationEnabled: boolean;
  readonly serviceUuid: string;
  readonly rxCharacteristicUuid: string;
  readonly txCharacteristicUuid: string;
  readonly manufacturerCompanyId: number;
}

export const defaultOptions: BlueConnectOptions = {
  connectTimeoutMs: 15_000,
  commandTimeoutMs: 10_000,
  connectRetryTimeoutMs: 30_000,
  retryOnlyWhileAdvertising: true,
  minCycleDelayMs: 1_000,
  maxCycleDelayMs: 5_000,
  isEchoVerificationEnabled: true,
  serviceUuid: BlueConnectConstants.verdigrisServiceUuid,
  rxCharacteristicUuid: BlueConnectConstants.verdigrisRxCharacteristicUuid,
  txCharacteristicUuid: BlueConnectConstants.verdigrisTxCharacteristicUuid,
  manufacturerCompanyId: BlueConnectConstants.manufacturerCompanyId,
};
