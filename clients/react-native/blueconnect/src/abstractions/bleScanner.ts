// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import type { Advertisement } from './advertisement';
import type { BlePeripheral } from './blePeripheral';

export type ScanEventType = 'added' | 'updated' | 'expired';

export interface ScanEvent {
  readonly type: ScanEventType;
  readonly advertisement: Advertisement;
}

/**
 * A BLE central that scans for Verdigris devices and creates peripherals for
 * them. Backends implement it over the shared `AdvertisementRegistry`.
 */
export interface BleScanner {
  /** Every device seen within the last `BlueConnectConstants.scannerExpirationMs`. */
  readonly peripherals: Advertisement[];
  readonly isScanning: boolean;
  /** Starts scanning. Resolves once the radio is on and the scan is running. */
  start(): Promise<void>;
  /** Scan events; a late listener sees only what happens after it subscribes. Returns the unsubscribe function. */
  onEvent(listener: (event: ScanEvent) => void): () => void;
  /** When `id` was last seen, in milliseconds since the epoch, or null once it has expired from the cache. */
  lastSeen(id: string): number | null;
  /** A peripheral for a device still in the cache. Throws `InvalidStateError` once the entry has expired. */
  createPeripheral(advertisement: Advertisement): BlePeripheral;
}
