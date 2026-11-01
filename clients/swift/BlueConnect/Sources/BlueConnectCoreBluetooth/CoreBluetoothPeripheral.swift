// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import CoreBluetooth
import Foundation
import os

/// One GATT connection through CoreBluetooth. Each asynchronous step parks a
/// continuation that the matching delegate callback resumes. Every
/// continuation is cancellation-aware, so a timeout in `withTimeout` unwinds
/// at once and the late delegate callback finds no continuation to resume.
public final class CoreBluetoothPeripheral: NSObject, BlePeripheral, CBPeripheralDelegate, @unchecked Sendable {
    private enum Slot: Hashable {
        case connect, disconnect, services, characteristics, write
    }

    public let advertisement: Advertisement
    private let scanner: CoreBluetoothScanner
    private let central: CBCentralManager
    private let cb: CBPeripheral
    private let options: BlueConnectOptions
    private let serviceUuid: CBUUID
    private let rxUuid: CBUUID
    private let txUuid: CBUUID
    private let lock = NSLock()
    private var waiters: [Slot: CheckedContinuation<Void, any Error>] = [:]
    private var rx: CBCharacteristic?
    private var tx: CBCharacteristic?
    private let received = ReceiveQueue()
    private var stateSubscribers: [UUID: AsyncStream<ConnectionState>.Continuation] = [:]
    private var connected = false
    private var closed = false
    private static let logger = Logger(subsystem: "com.stonygrey.blueconnect", category: "corebluetooth")

    init(scanner: CoreBluetoothScanner, central: CBCentralManager, cb: CBPeripheral, advertisement: Advertisement, options: BlueConnectOptions) {
        self.scanner = scanner
        self.central = central
        self.cb = cb
        self.advertisement = advertisement
        self.options = options
        serviceUuid = CBUUID(string: options.serviceUuid)
        rxUuid = CBUUID(string: options.rxCharacteristicUuid)
        txUuid = CBUUID(string: options.txCharacteristicUuid)
        super.init()
    }

    public var isConnected: Bool {
        lock.lock()
        defer { lock.unlock() }
        return connected
    }

    /// `.withoutResponse` reports the true ATT payload (MTU − 3). `.withResponse`
    /// would report 512 because CoreBluetooth converts larger writes
    /// into ATT long writes, which Zephyr rejects by default.
    public var mtu: Int {
        isConnected ? cb.maximumWriteValueLength(for: .withoutResponse) : 0
    }

    public func connect() async throws {
        if isClosed {
            throw BlueConnectError.invalidOperation("The peripheral has been closed.")
        }
        let name = advertisement.localName
        let id = advertisement.id
        let scanner = self.scanner
        try await ConnectRetry.run(
            options: options, name: name,
            isPresent: {
                guard let seen = scanner.lastSeen(id) else { return false }
                return Date().timeIntervalSince(seen) <= BlueConnectConstants.scannerExpiration.seconds
            },
            attempt: { timeout in try await self.connectOnce(timeout) })
    }

    private func connectOnce(_ timeout: Duration) async throws {
        scanner.register(self)
        do {
            try await withTimeout(timeout) { try await self.connectCore() }
            setConnected(true)
            publish(.connected)
        } catch is DeadlineExceeded {
            abortConnect()
            throw BlueConnectError.timeout("Connecting to '\(advertisement.localName)' timed out after \(Int(timeout.seconds))s.")
        } catch {
            abortConnect()
            throw error
        }
    }

    private func connectCore() async throws {
        cb.delegate = self
        try await wait(.connect) { central.connect(cb, options: nil) }
        try await wait(.services) { cb.discoverServices([serviceUuid]) }
        guard let service = cb.services?.first(where: { $0.uuid == serviceUuid }) else {
            throw BlueConnectError.transport("Peripheral does not expose GATT service \(options.serviceUuid).")
        }
        try await wait(.characteristics) { cb.discoverCharacteristics([rxUuid, txUuid], for: service) }
        guard let rx = service.characteristics?.first(where: { $0.uuid == rxUuid }) else {
            throw BlueConnectError.transport("RX characteristic \(options.rxCharacteristicUuid) not found.")
        }
        guard let tx = service.characteristics?.first(where: { $0.uuid == txUuid }) else {
            throw BlueConnectError.transport("TX characteristic \(options.txCharacteristicUuid) not found.")
        }
        setCharacteristics(rx: rx, tx: tx)
        cb.setNotifyValue(true, for: tx)
    }

    public func disconnect() async throws {
        guard isConnected else { return }
        try await wait(.disconnect) { central.cancelPeripheralConnection(cb) }
        setConnected(false)
        publish(.disconnected)
    }

    public func read() async throws -> Data {
        try await received.pop()
    }

    public func write(_ bytes: Data) async throws {
        guard isConnected else {
            throw BlueConnectError.invalidOperation("Peripheral is not connected.")
        }
        guard let rx = currentRx() else {
            throw BlueConnectError.invalidOperation("Peripheral is not connected.")
        }
        let mtu = max(1, self.mtu)
        var offset = bytes.startIndex
        while offset < bytes.endIndex {
            let end = min(offset + mtu, bytes.endIndex)
            let chunk = bytes.subdata(in: offset..<end)
            try await wait(.write) { cb.writeValue(chunk, for: rx, type: .withResponse) }
            offset = end
        }
    }

    public func states() -> AsyncStream<ConnectionState> {
        let id = UUID()
        let (stream, continuation) = AsyncStream<ConnectionState>.makeStream()
        continuation.onTermination = { [weak self] _ in
            guard let self else { return }
            self.lock.lock()
            self.stateSubscribers[id] = nil
            self.lock.unlock()
        }
        lock.lock()
        stateSubscribers[id] = continuation
        lock.unlock()
        return stream
    }

    public func close() async {
        guard let connected = markClosed() else { return }
        if connected {
            do {
                try await withTimeout(.seconds(5)) { try await self.disconnect() }
            } catch {
                Self.logger.warning("Teardown of '\(self.advertisement.localName, privacy: .public)' failed during close; releasing anyway.")
            }
        } else {
            abortConnect()
        }
        cb.delegate = nil
        scanner.unregister(self)
        received.close(BlueConnectError.transport("The peripheral has been closed."))
        for subscriber in takeStateSubscribers() {
            subscriber.finish()
        }
    }

    // MARK: Delegate routing

    func onConnected() {
        resume(.connect, nil)
    }

    func onConnectFailed(_ error: (any Error)?) {
        resume(.connect, BlueConnectError.transport("Connect failed: \(error?.localizedDescription ?? "unknown error")"))
    }

    func onDisconnected(_ error: (any Error)?) {
        lock.lock()
        let expected = waiters[.disconnect]
        waiters[.disconnect] = nil
        lock.unlock()
        if let expected {
            expected.resume()
            return
        }
        // Unexpected drop: fail whatever is in flight.
        let failure = BlueConnectError.transport("Peripheral disconnected: \(error?.localizedDescription ?? "no error")")
        for slot in [Slot.connect, .services, .characteristics, .write] {
            resume(slot, failure)
        }
        if isConnected {
            setConnected(false)
            publish(.disconnected)
        }
    }

    public func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: (any Error)?) {
        resume(.services, error.map { BlueConnectError.transport($0.localizedDescription) })
    }

    public func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: (any Error)?) {
        resume(.characteristics, error.map { BlueConnectError.transport($0.localizedDescription) })
    }

    public func peripheral(_ peripheral: CBPeripheral, didWriteValueFor characteristic: CBCharacteristic, error: (any Error)?) {
        resume(.write, error.map { BlueConnectError.transport($0.localizedDescription) })
    }

    public func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: (any Error)?) {
        guard error == nil, let value = characteristic.value else { return }
        received.push(value)
    }

    // MARK: Locked state

    private func setCharacteristics(rx: CBCharacteristic, tx: CBCharacteristic) {
        lock.lock()
        self.rx = rx
        self.tx = tx
        lock.unlock()
    }

    private func currentRx() -> CBCharacteristic? {
        lock.lock()
        defer { lock.unlock() }
        return rx
    }

    /// Marks the peripheral closed. Returns whether it was connected, or nil
    /// when it was already closed.
    private func markClosed() -> Bool? {
        lock.lock()
        defer { lock.unlock() }
        if closed {
            return nil
        }
        closed = true
        return connected
    }

    private func takeStateSubscribers() -> [AsyncStream<ConnectionState>.Continuation] {
        lock.lock()
        defer { lock.unlock() }
        let subscribers = Array(stateSubscribers.values)
        stateSubscribers.removeAll()
        return subscribers
    }

    // MARK: Continuations

    private var isClosed: Bool {
        lock.lock()
        defer { lock.unlock() }
        return closed
    }

    private func setConnected(_ value: Bool) {
        lock.lock()
        connected = value
        lock.unlock()
    }

    private func publish(_ state: ConnectionState) {
        lock.lock()
        let subscribers = Array(stateSubscribers.values)
        lock.unlock()
        for subscriber in subscribers {
            subscriber.yield(state)
        }
    }

    private func abortConnect() {
        central.cancelPeripheralConnection(cb)
    }

    private func wait(_ slot: Slot, _ start: () -> Void) async throws {
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, any Error>) in
                lock.lock()
                if Task.isCancelled {
                    lock.unlock()
                    continuation.resume(throwing: CancellationError())
                    return
                }
                waiters[slot] = continuation
                lock.unlock()
                start()
            }
        } onCancel: {
            resume(slot, CancellationError())
        }
    }

    private func resume(_ slot: Slot, _ error: (any Error)?) {
        lock.lock()
        let continuation = waiters[slot]
        waiters[slot] = nil
        lock.unlock()
        guard let continuation else { return }
        if let error {
            continuation.resume(throwing: error)
        } else {
            continuation.resume()
        }
    }
}
