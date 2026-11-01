// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import type { Advertisement } from './advertisement';

export type ConnectionState = 'disconnected' | 'connected';

/**
 * One GATT connection to a Verdigris device. `read` returns one ATT value
 * (one notification); `write` sends raw bytes, split at `mtu`. Segmentation
 * is above this in `SegmentLink`.
 */
export interface BlePeripheral {
  readonly advertisement: Advertisement;
  readonly isConnected: boolean;
  /** The ATT payload size, already `ATT_MTU - 3`. Zero when not connected. */
  readonly mtu: number;
  /** Connects with the retry policy in `BlueConnectOptions`. */
  connect(): Promise<void>;
  disconnect(): Promise<void>;
  /**
   * The next received ATT value, or null when `timeoutMs` passes first. A
   * timed-out read consumes nothing: the next call still gets the next value.
   */
  read(timeoutMs?: number): Promise<Uint8Array | null>;
  write(bytes: Uint8Array): Promise<void>;
  /** Connection state changes. Returns the unsubscribe function. */
  onStateChange(listener: (state: ConnectionState) => void): () => void;
  /** Disconnects if connected and releases the platform object. Idempotent. */
  close(): Promise<void>;
}
