// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { BleTransportError } from '../abstractions/errors';

/**
 * The one-byte frame type in front of every reassembled message
 * (`documents/noise-security.md`, "Wire framing").
 */
export const FrameType = {
  plaintext: 0x00,
  handshake: 0x01,
  encrypted: 0x02,

  bundle(type: number, payload: Uint8Array): Uint8Array {
    const message = new Uint8Array(1 + payload.length);
    message[0] = type;
    message.set(payload, 1);
    return message;
  },

  split(message: Uint8Array): [number, Uint8Array] {
    if (message.length === 0) {
      throw new BleTransportError('Empty message; missing type byte.');
    }
    return [message[0], message.subarray(1)];
  },
} as const;
