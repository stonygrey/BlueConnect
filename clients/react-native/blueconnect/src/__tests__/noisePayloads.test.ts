// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { LinkSecurityError } from '../abstractions/errors';
import { base64UrlEncode } from '../core/base64';
import { NoiseEnrollmentPayload, NoiseQrPayload } from '../secure/noisePayloads';

jest.mock('@stonygrey/torann', () => ({ NoiseProtocol: { parse: jest.fn() }, TorannError: class extends Error {} }));

function key(fill: number): Uint8Array {
  return new Uint8Array(32).fill(fill);
}

function randomKey(): Uint8Array {
  const bytes = Uint8Array.from({ length: 32 }, () => Math.floor(Math.random() * 256));
  bytes[0] |= 1;
  return bytes;
}

test('credential round trip preserves keys and tier', () => {
  const app = randomKey();
  const device = randomKey();
  const original = new NoiseQrPayload('high', app, device, 'Verdigris 1');
  const parsed = NoiseQrPayload.parse(original.encode())!;
  expect(parsed.tier).toBe('high');
  expect(parsed.appPrivateKey).toEqual(app);
  expect(parsed.devicePublicKey).toEqual(device);
  expect(parsed.deviceName).toBe('Verdigris 1');
});

test('credential parse rejects all-zero keys', () => {
  expect(NoiseQrPayload.parse(`SGNK1:L:${base64UrlEncode(new Uint8Array(64))}`)).toBeNull();
});

test('enrollment round trips with and without a name', () => {
  const named = new NoiseEnrollmentPayload(key(1), key(2), 'Verdigris 0001');
  const parsed = NoiseEnrollmentPayload.parse(named.encode())!;
  expect(parsed.enrollmentPrivateKey).toEqual(named.enrollmentPrivateKey);
  expect(parsed.devicePublicKey).toEqual(named.devicePublicKey);
  expect(parsed.deviceName).toBe(named.deviceName);
  expect(named.encode().startsWith('SGNE1:')).toBe(true);
  const anonymous = new NoiseEnrollmentPayload(key(3), key(4));
  const parsed2 = NoiseEnrollmentPayload.parse(anonymous.encode())!;
  expect(parsed2.deviceName).toBeNull();
  expect(parsed2.enrollmentPrivateKey).toEqual(key(3));
  expect(parsed2.devicePublicKey).toEqual(key(4));
});

test('each parser rejects the other payload and bad blobs', () => {
  const credential = new NoiseQrPayload('low', key(1), key(2)).encode();
  expect(NoiseEnrollmentPayload.parse(credential)).toBeNull();
  expect(NoiseQrPayload.parse(new NoiseEnrollmentPayload(key(1), key(2)).encode())).toBeNull();
  expect(NoiseEnrollmentPayload.parse('SGNE1:not-base64!')).toBeNull();
  expect(NoiseEnrollmentPayload.parse(`SGNE1:${base64UrlEncode(new Uint8Array(63))}`)).toBeNull();
  expect(() => new NoiseEnrollmentPayload(new Uint8Array(32), key(2)).encode()).toThrow(LinkSecurityError);
});

test('a name containing colons survives the split', () => {
  const payload = new NoiseEnrollmentPayload(key(1), key(2), 'a:b');
  expect(NoiseEnrollmentPayload.parse(payload.encode())!.deviceName).toBe('a:b');
});
