// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/// Thrown by `withTimeout` when the deadline passes first. Callers translate
/// it into a `BlueConnectError` carrying the message for the app, so it never
/// reaches the app.
public struct DeadlineExceeded: Error, Sendable {}

/// Races `operation` against `duration`. Cancellation of the calling task
/// propagates to `operation` as a `CancellationError`, which lets callers
/// tell a caller's cancel from a timeout.
public func withTimeout<T: Sendable>(
    _ duration: Duration,
    _ operation: @Sendable @escaping () async throws -> T
) async throws -> T {
    try await withThrowingTaskGroup(of: T.self) { group in
        group.addTask { try await operation() }
        group.addTask {
            try await Task.sleep(for: duration)
            throw DeadlineExceeded()
        }
        let result = try await group.next()!
        group.cancelAll()
        return result
    }
}
