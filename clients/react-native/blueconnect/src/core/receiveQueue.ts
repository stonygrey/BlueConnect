// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { BleTransportError, InvalidStateError } from '../abstractions/errors';

interface Waiter {
  resolve(value: Uint8Array | null): void;
  reject(error: unknown): void;
}

/**
 * The inbound ATT value queue behind `BlePeripheral.read`: notifications are
 * pushed from the platform callback, and one reader pops them in order. A
 * `pop` with a timeout withdraws its waiter when the timeout elapses, so a late
 * value is kept for the next reader instead of being lost to a promise nobody
 * awaits.
 */
export class ReceiveQueue {
  private readonly items: Uint8Array[] = [];
  private waiter: Waiter | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private closed = false;

  push(value: Uint8Array): void {
    const waiter = this.waiter;
    if (waiter) {
      this.waiter = null;
      this.clearTimer();
      waiter.resolve(value);
      return;
    }
    if (!this.closed) {
      this.items.push(value);
    }
  }

  /** Fails the pending and every future `pop` with `error`. */
  close(error: unknown): void {
    this.closed = true;
    const waiter = this.waiter;
    this.waiter = null;
    this.clearTimer();
    waiter?.reject(error);
  }

  /** The next value, or null when `timeoutMs` passes first. */
  pop(timeoutMs?: number): Promise<Uint8Array | null> {
    const next = this.items.shift();
    if (next !== undefined) {
      return Promise.resolve(next);
    }
    if (this.closed) {
      return Promise.reject(new BleTransportError('The peripheral has been closed.'));
    }
    if (this.waiter) {
      return Promise.reject(new InvalidStateError('ReceiveQueue has one reader at a time.'));
    }
    return new Promise((resolve, reject) => {
      const waiter: Waiter = { resolve, reject };
      this.waiter = waiter;
      if (timeoutMs !== undefined) {
        this.timer = setTimeout(() => {
          if (this.waiter === waiter) {
            this.waiter = null;
            this.timer = null;
            waiter.resolve(null);
          }
        }, timeoutMs);
      }
    });
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }
}
