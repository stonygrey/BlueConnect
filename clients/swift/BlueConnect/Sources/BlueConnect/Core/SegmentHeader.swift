// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/// One-byte GATT segment header: `seq` in bits 7–4, reserved bit 3, and
/// `ABORT` / `END` / `START` in bits 2–0. See `documents/ble-framing.md`.
public enum SegmentHeader {
    public static let start: UInt8 = 0x01
    public static let end: UInt8 = 0x02
    public static let abort: UInt8 = 0x04
    public static let reserved: UInt8 = 0x08
    public static let seqModulus = 16

    public static func pack(seq: Int, flags: UInt8) -> UInt8 {
        UInt8((seq & 0x0F) << 4) | (flags & 0x07)
    }

    public static func seq(_ header: UInt8) -> Int { Int(header >> 4) }

    public static func flags(_ header: UInt8) -> UInt8 { header & 0x0F }

    public static func has(_ header: UInt8, _ flag: UInt8) -> Bool { header & flag != 0 }
}
