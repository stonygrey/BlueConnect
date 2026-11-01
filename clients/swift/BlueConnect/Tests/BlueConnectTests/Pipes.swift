// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import Foundation

let pipeAdvertisement = Advertisement(
    id: "pipe", localName: "pipe", isConnectable: true, isSecured: false,
    rssi: Rssi(-50), timestamp: Date())

/// Test double: inbound ATT values are queued by the test, outbound writes
/// are recorded and can be scripted to fail.
final class ScriptedPipe: BlePeripheral, @unchecked Sendable {
    private let inbound = ReceiveQueue()
    private let lock = NSLock()
    private var writtenItems: [Data] = []
    private var failAt: Int?

    var mtu: Int = 20
    let advertisement = pipeAdvertisement
    var isConnected: Bool { true }

    var written: [Data] {
        lock.lock()
        defer { lock.unlock() }
        return writtenItems
    }

    func enqueue(_ bytes: UInt8...) {
        inbound.push(Data(bytes))
    }

    func failOnWrite(_ index: Int) {
        lock.lock()
        failAt = index
        lock.unlock()
    }

    func connect() async throws {}

    func disconnect() async throws {}

    func read() async throws -> Data {
        try await inbound.pop()
    }

    func write(_ bytes: Data) async throws {
        try record(bytes)
    }

    private func record(_ bytes: Data) throws {
        lock.lock()
        defer { lock.unlock() }
        if failAt == writtenItems.count {
            failAt = nil
            throw BlueConnectError.transport("scripted write failure")
        }
        writtenItems.append(bytes)
    }

    func states() -> AsyncStream<ConnectionState> {
        AsyncStream { $0.finish() }
    }

    func close() async {
        inbound.close(BlueConnectError.transport("closed"))
    }
}

/// Two cross-wired in-memory peripherals. Each `write` is one ATT value.
final class LoopbackPipe: BlePeripheral, @unchecked Sendable {
    private let inbound = ReceiveQueue()
    private var peer: LoopbackPipe?

    var mtu: Int = 20
    let advertisement = pipeAdvertisement
    var isConnected: Bool { true }

    static func pair(mtu: Int = 20) -> (LoopbackPipe, LoopbackPipe) {
        let a = LoopbackPipe()
        let b = LoopbackPipe()
        a.mtu = mtu
        b.mtu = mtu
        a.peer = b
        b.peer = a
        return (a, b)
    }

    func connect() async throws {}

    func disconnect() async throws {}

    func read() async throws -> Data {
        try await inbound.pop()
    }

    func write(_ bytes: Data) async throws {
        peer!.inbound.push(bytes)
    }

    func states() -> AsyncStream<ConnectionState> {
        AsyncStream { $0.finish() }
    }

    func close() async {
        inbound.close(BlueConnectError.transport("closed"))
    }
}

func payload(_ length: Int) -> Data {
    Data((0..<length).map { UInt8(truncatingIfNeeded: $0) })
}
