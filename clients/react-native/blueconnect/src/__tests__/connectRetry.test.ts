// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { defaultOptions } from '../abstractions/blueConnectOptions';
import { BleTimeoutError, BleTransportError } from '../abstractions/errors';
import { runConnectRetry } from '../core/connectRetry';
import { IoLog } from '../core/ioLog';

/** The connect policy: retries inside the window, early abandonment, and the final timeout. */
beforeAll(() => {
  IoLog.sink = () => {};
});

const options = { ...defaultOptions, connectTimeoutMs: 50, connectRetryTimeoutMs: 2_500 };

test('a failed attempt is retried and a later success returns', async () => {
  let attempts = 0;
  await runConnectRetry(options, 'dev', () => true, async () => {
    attempts++;
    if (attempts < 3) throw new Error('busy');
  });
  expect(attempts).toBe(3);
}, 10_000);

test('retries stop once the device stops advertising', async () => {
  let attempts = 0;
  await expect(
    runConnectRetry(options, 'dev', () => false, async () => {
      attempts++;
      throw new Error('busy');
    })
  ).rejects.toBeInstanceOf(BleTransportError);
  expect(attempts).toBe(1);
}, 10_000);

test('the window ends in a timeout error', async () => {
  const short = { ...options, connectRetryTimeoutMs: 1_500 };
  await expect(
    runConnectRetry(short, 'dev', () => true, async () => {
      throw new Error('busy');
    })
  ).rejects.toBeInstanceOf(BleTimeoutError);
}, 10_000);
