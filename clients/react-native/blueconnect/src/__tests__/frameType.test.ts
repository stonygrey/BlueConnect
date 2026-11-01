// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { BleTransportError } from '../abstractions/errors';
import { base64Decode, base64Encode, base64UrlDecode, base64UrlEncode } from '../core/base64';
import { utf8Decode, utf8Encode } from '../core/bytes';
import { IoLog } from '../core/ioLog';
import { SegmentHeader } from '../core/segmentHeader';
import { FrameType } from '../secure/frameType';
import { SecureLink } from '../secure/secureLink';
import { VerdigrisPeripheral } from '../verdigris/verdigrisPeripheral';

jest.mock('@stonygrey/torann', () => ({ NoiseProtocol: { parse: jest.fn() }, TorannError: class extends Error {} }));

test('bundle and split round trip', () => {
  const bundled = FrameType.bundle(FrameType.encrypted, Uint8Array.of(1, 2, 3));
  expect(bundled).toEqual(Uint8Array.of(0x02, 1, 2, 3));
  const [type, payload] = FrameType.split(bundled);
  expect(type).toBe(FrameType.encrypted);
  expect(payload).toEqual(Uint8Array.of(1, 2, 3));
});

test('split rejects an empty message', () => {
  expect(() => FrameType.split(new Uint8Array(0))).toThrow(BleTransportError);
});

test('handshake lengths match the wire format', () => {
  expect(SecureLink.handshakeMessage1Length).toBe(32);
  expect(SecureLink.handshakeMessage2Length).toBe(97);
  expect(SecureLink.handshakeMessage3Length).toBe(65);
  expect(SecureLink.ikHandshakeMessage1Length).toBe(97);
  expect(SecureLink.ikHandshakeMessage2Length).toBe(49);
  expect(VerdigrisPeripheral.maxEncodedRequest).toBe(2287);
});

test('io log lines match the format lib/smalt parses', () => {
  expect(IoLog.message('write', 2, Uint8Array.of(0x02, 0xab))).toBe('_BLE_IO message write type=02 len=2 hex=02AB');
  expect(IoLog.segment('read', 3, SegmentHeader.start | SegmentHeader.end, Uint8Array.of(0x33, 0x01))).toBe('_BLE_IO segment read seq=3 flags=start+end len=2 hex=3301');
  expect(IoLog.formatFlags(0)).toBe('none');
});

test('base64 round trips every length and rejects bad input', () => {
  for (let length = 0; length < 70; length++) {
    const bytes = Uint8Array.from({ length }, (_, i) => (i * 37 + 11) & 0xff);
    expect(base64Decode(base64Encode(bytes))).toEqual(bytes);
    expect(base64UrlDecode(base64UrlEncode(bytes))).toEqual(bytes);
  }
  expect(base64Encode(Uint8Array.of(0xfb, 0xff))).toBe('+/8=');
  expect(base64UrlEncode(Uint8Array.of(0xfb, 0xff))).toBe('-_8');
  expect(base64UrlDecode('-_8=')).toEqual(Uint8Array.of(0xfb, 0xff));
  expect(base64Decode('not base64!')).toBeNull();
  expect(base64UrlDecode('AAAAA')).toBeNull();
});

test('utf8 round trips text and tolerates malformed bytes', () => {
  for (const text of ['', 'Hello, Verdigris!', 'Verdigris 0001', 'ünïcödé', '日本語', '🙂']) {
    expect(utf8Decode(utf8Encode(text))).toBe(text);
  }
  expect(utf8Decode(Uint8Array.of(0x48, 0xff, 0x69))).toBe('H�i');
});
