// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/**
 * One-byte GATT segment header: `seq` in bits 7–4, reserved bit 3, and
 * `ABORT` / `END` / `START` in bits 2–0. See `documents/ble-framing.md`.
 */
export const SegmentHeader = {
  start: 0x01,
  end: 0x02,
  abort: 0x04,
  reserved: 0x08,
  seqModulus: 16,

  pack(seq: number, flags: number): number {
    return ((seq & 0x0f) << 4) | (flags & 0x07);
  },

  seq(header: number): number {
    return (header & 0xff) >> 4;
  },

  flags(header: number): number {
    return header & 0x0f;
  },

  has(header: number, flag: number): boolean {
    return (header & flag) !== 0;
  },
} as const;
