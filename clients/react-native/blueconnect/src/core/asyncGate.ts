// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/**
 * A FIFO mutual-exclusion gate for async code: two concurrent
 * `SegmentLink.write` calls must not interleave their segments on the air.
 */
export class AsyncGate {
  private locked = false;
  private readonly waiters: (() => void)[] = [];

  acquire(): Promise<void> {
    if (!this.locked) {
      this.locked = true;
      return Promise.resolve();
    }
    return new Promise((resolve) => this.waiters.push(resolve));
  }

  /** Takes the gate when it is free; false otherwise, without waiting. */
  tryAcquire(): boolean {
    if (this.locked) return false;
    this.locked = true;
    return true;
  }

  release(): void {
    const next = this.waiters.shift();
    if (next) {
      next();
    } else {
      this.locked = false;
    }
  }

  async withLock<T>(body: () => Promise<T>): Promise<T> {
    await this.acquire();
    try {
      return await body();
    } finally {
      this.release();
    }
  }
}
