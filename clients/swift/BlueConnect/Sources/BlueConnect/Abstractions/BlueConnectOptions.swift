// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation

/// Values shared with the firmware.
public enum BlueConnectConstants {
    public static let scannerExpiration: Duration = .seconds(60)
    public static let verdigrisServiceUuid = "2B200001-465F-4625-94D8-37EC5C99BFB3"
    public static let verdigrisRxCharacteristicUuid = "2B200002-465F-4625-94D8-37EC5C99BFB3"
    public static let verdigrisTxCharacteristicUuid = "2B200003-465F-4625-94D8-37EC5C99BFB3"
    public static let manufacturerCompanyId: UInt16 = 0xFFFF
    public static let lowPrivilegePairedFlag: UInt8 = 0x01
    public static let highPrivilegePairedFlag: UInt8 = 0x02
    public static let securedStatusMask: UInt8 = lowPrivilegePairedFlag | highPrivilegePairedFlag
}

/// Connection and command policy. The app uses the defaults as they stand;
/// there is no configuration file.
public struct BlueConnectOptions: Sendable {
    public var connectTimeout: Duration = .seconds(15)
    public var commandTimeout: Duration = .seconds(10)
    /// The whole retry window. Zero retries until cancelled.
    public var connectRetryTimeout: Duration = .seconds(30)
    public var retryOnlyWhileAdvertising = true
    public var minCycleDelay: Duration = .seconds(1)
    public var maxCycleDelay: Duration = .seconds(5)
    public var isEchoVerificationEnabled = true
    public var serviceUuid = BlueConnectConstants.verdigrisServiceUuid
    public var rxCharacteristicUuid = BlueConnectConstants.verdigrisRxCharacteristicUuid
    public var txCharacteristicUuid = BlueConnectConstants.verdigrisTxCharacteristicUuid
    public var manufacturerCompanyId = BlueConnectConstants.manufacturerCompanyId

    public init() {}
}
