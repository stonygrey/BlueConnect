// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/// One-byte GATT segment header: `seq` in bits 7–4, reserved bit 3, and
/// `ABORT` / `END` / `START` in bits 2–0. See `documents/ble-framing.md`.
abstract final class SegmentHeader {
  static const start = 0x01;
  static const end = 0x02;
  static const abort = 0x04;
  static const reserved = 0x08;
  static const seqModulus = 16;

  static int pack(int seq, int flags) => ((seq & 0x0F) << 4) | (flags & 0x07);

  static int seq(int header) => (header & 0xFF) >> 4;

  static int flags(int header) => header & 0x0F;

  static bool has(int header, int flag) => header & flag != 0;
}
