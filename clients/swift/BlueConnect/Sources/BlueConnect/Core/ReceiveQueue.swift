// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation

/// The inbound ATT value queue behind `BlePeripheral.read`: notifications are
/// pushed from the platform callback thread, and one reader pops them in
/// order. `pop` honors task cancellation, which the segment layer relies on
/// to tell a caller's cancel from an incomplete-message timeout.
public final class ReceiveQueue: @unchecked Sendable {
    private let lock = NSLock()
    private var items: [Data] = []
    private var waiter: CheckedContinuation<Data, any Error>?
    private var closed = false

    public init() {}

    public func push(_ value: Data) {
        lock.lock()
        if let waiter {
            self.waiter = nil
            lock.unlock()
            waiter.resume(returning: value)
            return
        }
        if !closed {
            items.append(value)
        }
        lock.unlock()
    }

    /// Fails the pending and every future `pop` with `error`.
    public func close(_ error: any Error) {
        lock.lock()
        closed = true
        let waiter = self.waiter
        self.waiter = nil
        lock.unlock()
        waiter?.resume(throwing: error)
    }

    public func pop() async throws -> Data {
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                lock.lock()
                if !items.isEmpty {
                    let value = items.removeFirst()
                    lock.unlock()
                    continuation.resume(returning: value)
                    return
                }
                if closed {
                    lock.unlock()
                    continuation.resume(throwing: BlueConnectError.transport("The peripheral has been closed."))
                    return
                }
                if Task.isCancelled {
                    lock.unlock()
                    continuation.resume(throwing: CancellationError())
                    return
                }
                waiter = continuation
                lock.unlock()
            }
        } onCancel: {
            lock.lock()
            let waiter = self.waiter
            self.waiter = nil
            lock.unlock()
            waiter?.resume(throwing: CancellationError())
        }
    }
}
