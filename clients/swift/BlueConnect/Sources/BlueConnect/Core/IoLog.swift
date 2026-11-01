// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation
import os

/// The device I/O log lines, in the exact format `lib/smalt` parses. Lines go
/// to the unified log under the `com.stonygrey.blueconnect` subsystem;
/// `log show --predicate 'subsystem == "com.stonygrey.blueconnect"'`
/// recovers them.
public enum IoLog {
    public static let prefix = "_BLE_IO"
    public static let logger = Logger(subsystem: "com.stonygrey.blueconnect", category: "io")

    public static func message(_ direction: String, type: UInt8, bytes: Data) -> String {
        "\(prefix) message \(direction) type=\(hex2(type)) len=\(bytes.count) hex=\(hex(bytes))"
    }

    public static func segment(_ direction: String, seq: Int, flags: UInt8, bytes: Data) -> String {
        "\(prefix) segment \(direction) seq=\(seq) flags=\(formatFlags(flags)) len=\(bytes.count) hex=\(hex(bytes))"
    }

    public static func formatFlags(_ flags: UInt8) -> String {
        var parts: [String] = []
        if flags & SegmentHeader.start != 0 { parts.append("start") }
        if flags & SegmentHeader.end != 0 { parts.append("end") }
        if flags & SegmentHeader.abort != 0 { parts.append("abort") }
        return parts.isEmpty ? "none" : parts.joined(separator: "+")
    }

    /// Uppercase hex with no separators, as `Convert.ToHexString` produces.
    public static func hex(_ bytes: Data) -> String {
        bytes.map { hex2($0) }.joined()
    }

    private static let digits = Array("0123456789ABCDEF")

    private static func hex2(_ byte: UInt8) -> String {
        String([digits[Int(byte >> 4)], digits[Int(byte & 0x0F)]])
    }
}
