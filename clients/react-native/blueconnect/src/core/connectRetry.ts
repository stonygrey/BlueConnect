// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import type { BlueConnectOptions } from '../abstractions/blueConnectOptions';
import { BleTimeoutError, BleTransportError } from '../abstractions/errors';
import { IoLog } from './ioLog';
import { delay } from './timeout';

export const retryDelayMs = 1_000;

/**
 * The connect policy: attempts bounded by `connectTimeoutMs`, repeated after
 * a 1 s delay until `connectRetryTimeoutMs` runs out, and abandoned early
 * when the device has stopped advertising and `retryOnlyWhileAdvertising` is
 * set. Backends call it from `connect()` with their single-attempt connect
 * as `attempt`, which receives the time it may take; `isPresent` reports
 * whether the scanner has seen the device within the expiry window.
 */
export async function runConnectRetry(
  options: BlueConnectOptions,
  name: string,
  isPresent: () => boolean,
  attempt: (timeoutMs: number) => Promise<void>
): Promise<void> {
  const unbounded = options.connectRetryTimeoutMs <= 0;
  const window = options.connectRetryTimeoutMs;
  const attemptTimeout = options.connectTimeoutMs;
  const started = Date.now();
  let attemptNumber = 1;
  for (;;) {
    const remaining = unbounded ? attemptTimeout : window - (Date.now() - started);
    try {
      await attempt(Math.min(attemptTimeout, remaining));
      return;
    } catch (e) {
      const elapsed = Date.now() - started;
      if (!unbounded && elapsed + retryDelayMs >= window) {
        throw new BleTimeoutError(
          `Connecting to '${name}' did not succeed within ${Math.round(window / 1000)}s (${attemptNumber} attempt(s); last failure: ${String(e)})`,
          e
        );
      }
      IoLog.warn(`Connect attempt ${attemptNumber} to '${name}' failed after ${(elapsed / 1000).toFixed(1)}s (${String(e)}); retrying.`);
      await delay(retryDelayMs);
      if (options.retryOnlyWhileAdvertising && !isPresent()) {
        throw new BleTransportError(
          `'${name}' is no longer advertising; giving up after ${attemptNumber} attempt(s) (last failure: ${String(e)})`,
          e
        );
      }
    }
    attemptNumber++;
  }
}
