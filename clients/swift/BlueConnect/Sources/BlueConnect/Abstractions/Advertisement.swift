// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation

/// The current RSSI sample and the extremes seen so far, in dBm.
public struct Rssi: Sendable, Equatable {
    public let valueDbm: Int
    public let minDbm: Int
    public let maxDbm: Int

    public init(valueDbm: Int, minDbm: Int, maxDbm: Int) {
        self.valueDbm = valueDbm
        self.minDbm = minDbm
        self.maxDbm = maxDbm
    }

    public init(_ dbm: Int) {
        self.init(valueDbm: dbm, minDbm: dbm, maxDbm: dbm)
    }
}

/// One scanned device as last seen. A value snapshot: the scanner publishes a
/// new one on every packet, and the app keeps whichever it selected.
public struct Advertisement: Sendable, Equatable, Identifiable {
    /// The platform identifier: the `CBPeripheral` UUID on Apple platforms.
    public let id: String
    public let localName: String
    public let isConnectable: Bool
    /// Bit 0 or 1 of the status byte: at least one tier is pinned.
    public let isSecured: Bool
    public let rssi: Rssi
    public let timestamp: Date

    public init(
        id: String, localName: String, isConnectable: Bool, isSecured: Bool, rssi: Rssi, timestamp: Date
    ) {
        self.id = id
        self.localName = localName
        self.isConnectable = isConnectable
        self.isSecured = isSecured
        self.rssi = rssi
        self.timestamp = timestamp
    }
}
