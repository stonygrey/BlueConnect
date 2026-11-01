// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/// A FIFO mutual-exclusion gate for async code. Actors alone do not give
/// this: an actor method that awaits lets another call of the same method
/// interleave, so two concurrent `SegmentLink.write` calls would interleave
/// their segments on the air.
public actor AsyncGate {
    private var locked = false
    private var waiters: [CheckedContinuation<Void, Never>] = []

    public init() {}

    public func acquire() async {
        if !locked {
            locked = true
            return
        }
        await withCheckedContinuation { waiters.append($0) }
    }

    public func release() {
        if waiters.isEmpty {
            locked = false
        } else {
            waiters.removeFirst().resume()
        }
    }

    public func withLock<T: Sendable>(_ body: @Sendable () async throws -> T) async rethrows -> T {
        await acquire()
        defer { release() }
        return try await body()
    }
}
