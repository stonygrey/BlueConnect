// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import type { Advertisement, Rssi } from '../abstractions/advertisement';
import { singleRssi } from '../abstractions/advertisement';
import type { ScanEvent } from '../abstractions/bleScanner';
import { BlueConnectConstants } from '../abstractions/blueConnectOptions';

export interface Observation {
  readonly id: string;
  readonly localName: string;
  readonly isConnectable: boolean;
  /** Null for packets without the Stony Grey manufacturer data, which keep the previous value. */
  readonly secured: boolean | null;
  readonly dbm: number;
}

/**
 * The scan cache that every backend shares: the last advertisement per
 * device, the event listeners, the set of devices with a connection in
 * progress (which never expire), and the 5 s expiry sweep.
 */
export class AdvertisementRegistry {
  private readonly cache = new Map<string, Advertisement>();
  private readonly connections = new Set<string>();
  private readonly listeners = new Set<(event: ScanEvent) => void>();
  private readonly expiry: ReturnType<typeof setInterval>;

  /** `minDbm` and `maxDbm` are exclusive bounds; samples outside them are ignored. */
  constructor(
    private readonly minDbm: number,
    private readonly maxDbm: number
  ) {
    this.expiry = setInterval(() => this.expireStaleEntries(), 5_000);
  }

  get peripherals(): Advertisement[] {
    return Array.from(this.cache.values());
  }

  cached(id: string): Advertisement | null {
    return this.cache.get(id) ?? null;
  }

  lastSeen(id: string): number | null {
    return this.cache.get(id)?.timestamp ?? null;
  }

  onEvent(listener: (event: ScanEvent) => void): () => void {
    this.listeners.add(listener);
    return () => {
      this.listeners.delete(listener);
    };
  }

  /** Merges one packet. */
  observe(observation: Observation): void {
    const now = Date.now();
    const previous = this.cache.get(observation.id);
    const advertisement: Advertisement = previous
      ? {
          id: observation.id,
          localName: observation.localName,
          isConnectable: observation.isConnectable,
          isSecured: observation.secured ?? previous.isSecured,
          rssi: this.mergeRssi(previous.rssi, observation.dbm),
          timestamp: now,
        }
      : {
          id: observation.id,
          localName: observation.localName,
          isConnectable: observation.isConnectable,
          isSecured: observation.secured ?? false,
          rssi: singleRssi(observation.dbm),
          timestamp: now,
        };
    this.cache.set(observation.id, advertisement);
    this.emit({ type: previous ? 'updated' : 'added', advertisement });
  }

  /** Marks a device as connecting or connected so that it is not expired while it has stopped advertising. */
  register(id: string): void {
    this.connections.add(id);
  }

  unregister(id: string): void {
    this.connections.delete(id);
  }

  close(): void {
    clearInterval(this.expiry);
    this.listeners.clear();
  }

  private mergeRssi(previous: Rssi, dbm: number): Rssi {
    if (dbm > this.minDbm && dbm < this.maxDbm) {
      return { valueDbm: dbm, minDbm: Math.min(previous.minDbm, dbm), maxDbm: Math.max(previous.maxDbm, dbm) };
    }
    return previous;
  }

  private expireStaleEntries(): void {
    const cutoff = Date.now() - BlueConnectConstants.scannerExpirationMs;
    for (const advertisement of Array.from(this.cache.values())) {
      if (advertisement.timestamp < cutoff && !this.connections.has(advertisement.id)) {
        this.cache.delete(advertisement.id);
        this.emit({ type: 'expired', advertisement });
      }
    }
  }

  private emit(event: ScanEvent): void {
    for (const listener of Array.from(this.listeners)) {
      listener(event);
    }
  }
}
