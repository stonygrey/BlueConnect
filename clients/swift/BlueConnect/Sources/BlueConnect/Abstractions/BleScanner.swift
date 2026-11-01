// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation

public enum ScanEventType: Sendable {
    case added
    case updated
    case expired
}

public struct ScanEvent: Sendable {
    public let type: ScanEventType
    public let advertisement: Advertisement

    public init(type: ScanEventType, advertisement: Advertisement) {
        self.type = type
        self.advertisement = advertisement
    }
}

/// A BLE central that scans for Verdigris devices and creates peripherals for
/// them. Platform backends implement it over the shared
/// `AdvertisementRegistry`.
public protocol BleScanner: AnyObject, Sendable {
    /// Every device seen within the last `BlueConnectConstants.scannerExpiration`.
    var peripherals: [Advertisement] { get }
    var isScanning: Bool { get }
    /// Starts scanning. Returns once the radio is on and the scan is running.
    func start() async throws
    /// A fresh stream of scan events for one subscriber.
    func events() -> AsyncStream<ScanEvent>
    /// When `id` was last seen, or nil when it has expired from the cache.
    func lastSeen(_ id: String) -> Date?
    /// A peripheral for a device still in the cache. Throws
    /// `BlueConnectError.invalidOperation` once the entry has expired.
    func createPeripheral(_ advertisement: Advertisement) throws -> any BlePeripheral
}
