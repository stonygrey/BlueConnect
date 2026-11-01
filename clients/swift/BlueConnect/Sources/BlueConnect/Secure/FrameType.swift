// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation

/// The one-byte frame type in front of every reassembled message
/// (`documents/noise-security.md`, "Wire framing").
public enum FrameType {
    public static let plaintext: UInt8 = 0x00
    public static let handshake: UInt8 = 0x01
    public static let encrypted: UInt8 = 0x02

    public static func bundle(_ type: UInt8, _ payload: Data) -> Data {
        var message = Data(capacity: 1 + payload.count)
        message.append(type)
        message.append(payload)
        return message
    }

    public static func split(_ message: Data) throws -> (type: UInt8, payload: Data) {
        guard let type = message.first else {
            throw BlueConnectError.transport("Empty message; missing type byte.")
        }
        return (type, Data(message.dropFirst()))
    }
}
