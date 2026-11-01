// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation

/// The failures the library reports. The messages are written for the app's
/// toasts. Task cancellation is reported as Swift's own `CancellationError`,
/// never as one of these.
public enum BlueConnectError: Error, Sendable, CustomStringConvertible {
    /// The GATT layer or the segment layer failed.
    case transport(String)
    /// The handshake or an AEAD check failed.
    case linkSecurity(String)
    /// A call that the current state does not allow.
    case invalidOperation(String)
    /// A connect attempt, retry window, or command ran out of time.
    case timeout(String)

    public var description: String {
        switch self {
        case .transport(let m), .linkSecurity(let m), .invalidOperation(let m), .timeout(let m):
            m
        }
    }

    public var localizedDescription: String { description }
}
