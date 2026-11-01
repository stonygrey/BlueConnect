// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation

public enum ConnectionState: Sendable, Equatable {
    case disconnected
    case connected
}

/// One GATT connection to a Verdigris device. `read` returns one ATT value
/// (one notification); `write` sends raw bytes, split at `mtu`. Segmentation
/// is above this in `SegmentLink`.
public protocol BlePeripheral: AnyObject, Sendable {
    var advertisement: Advertisement { get }
    var isConnected: Bool { get }
    /// The ATT payload size, already `ATT_MTU - 3`. Zero when not connected.
    var mtu: Int { get }
    /// Connects with the retry policy in `BlueConnectOptions`.
    func connect() async throws
    func disconnect() async throws
    /// The next received ATT value. Honors task cancellation.
    func read() async throws -> Data
    func write(_ bytes: Data) async throws
    /// A fresh stream of connection state changes for one subscriber.
    func states() -> AsyncStream<ConnectionState>
    /// Disconnects if connected and releases the platform object. Idempotent.
    func close() async
}
