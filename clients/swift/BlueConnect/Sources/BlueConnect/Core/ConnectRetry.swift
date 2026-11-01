// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation
import os

/// The connect policy: attempts bounded by `connectTimeout`, repeated after
/// a 1 s delay until `connectRetryTimeout` runs out, and abandoned early when
/// the device has stopped advertising and `retryOnlyWhileAdvertising` is set.
/// Backends call it from `connect()` with their single-attempt connect as
/// `attempt`.
public enum ConnectRetry {
    public static let retryDelay: Duration = .seconds(1)
    static let logger = Logger(subsystem: "com.stonygrey.blueconnect", category: "connect")

    /// `attempt` receives the time it may take. `isPresent` reports whether the
    /// scanner has seen the device within the expiry window.
    public static func run(
        options: BlueConnectOptions,
        name: String,
        isPresent: @Sendable () -> Bool,
        attempt: @Sendable (Duration) async throws -> Void
    ) async throws {
        let unbounded = options.connectRetryTimeout <= .zero
        let window = options.connectRetryTimeout
        let attemptTimeout = options.connectTimeout
        let clock = ContinuousClock()
        let started = clock.now
        var attemptNumber = 1
        while true {
            let elapsedBefore = clock.now - started
            let remaining = unbounded ? attemptTimeout : window - elapsedBefore
            do {
                try await attempt(min(attemptTimeout, remaining))
                return
            } catch is CancellationError {
                throw CancellationError()
            } catch {
                let elapsed = clock.now - started
                if !unbounded && elapsed + retryDelay >= window {
                    throw BlueConnectError.timeout(
                        "Connecting to '\(name)' did not succeed within \(Int(window.seconds))s "
                            + "(\(attemptNumber) attempt(s); last failure: \(describe(error)))")
                }
                logger.warning(
                    "Connect attempt \(attemptNumber) to '\(name, privacy: .public)' failed after \(elapsed.seconds, format: .fixed(precision: 1))s (\(describe(error), privacy: .public)); retrying.")
                try await Task.sleep(for: retryDelay)
                if options.retryOnlyWhileAdvertising && !isPresent() {
                    throw BlueConnectError.transport(
                        "'\(name)' is no longer advertising; giving up after \(attemptNumber) attempt(s) "
                            + "(last failure: \(describe(error)))")
                }
            }
            attemptNumber += 1
        }
    }

    static func describe(_ error: any Error) -> String {
        if let error = error as? BlueConnectError {
            return error.description
        }
        return String(describing: error)
    }
}
