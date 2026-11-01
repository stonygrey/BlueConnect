// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/** The current RSSI sample and the extremes seen so far, in dBm. */
export interface Rssi {
  readonly valueDbm: number;
  readonly minDbm: number;
  readonly maxDbm: number;
}

export function singleRssi(dbm: number): Rssi {
  return { valueDbm: dbm, minDbm: dbm, maxDbm: dbm };
}

/**
 * One scanned device as last seen. A value snapshot: the scanner publishes a
 * new one on every packet, and the app keeps whichever it selected. `id` is
 * the platform identifier, a UUID on Apple and the MAC address on Android.
 * `timestamp` is milliseconds since the epoch.
 */
export interface Advertisement {
  readonly id: string;
  readonly localName: string;
  readonly isConnectable: boolean;
  /** Bit 0 or 1 of the status byte: at least one tier is pinned. */
  readonly isSecured: boolean;
  readonly rssi: Rssi;
  readonly timestamp: number;
}
