// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.BlueConnect.Abstractions;

public static class BlueConnectConstants
{
    public const int ScannerExpirationSeconds = 60;

    /// <summary>
    /// The Verdigris GATT service (Stony Grey vendor UUID family), advertised
    /// by the Verdigris firmware in its scan response.
    /// </summary>
    public const string VerdigrisServiceUuid = "2B200001-465F-4625-94D8-37EC5C99BFB3";

    /// <summary>Verdigris RX characteristic: the central writes framed requests here.</summary>
    public const string VerdigrisRxCharacteristicUuid = "2B200002-465F-4625-94D8-37EC5C99BFB3";

    /// <summary>Verdigris TX characteristic: the peripheral notifies framed responses here.</summary>
    public const string VerdigrisTxCharacteristicUuid = "2B200003-465F-4625-94D8-37EC5C99BFB3";

    /// <summary>
    /// Company id of the advertised manufacturer data (0xFFFF is the
    /// Bluetooth SIG value reserved for internal use / testing).
    /// </summary>
    public const ushort ManufacturerCompanyId = 0xFFFF;

    /// <summary>Manufacturer-data status: a low-privilege client is pinned.</summary>
    public const byte LowPrivilegePairedFlag = 0x01;

    /// <summary>Manufacturer-data status: a high-privilege client is pinned.</summary>
    public const byte HighPrivilegePairedFlag = 0x02;

    /// <summary>
    /// Any pairing slot pinned (drives the scan-list padlock). The other bits
    /// of the status byte are reserved; the firmware sends them as zero.
    /// </summary>
    public const byte SecuredStatusMask = LowPrivilegePairedFlag | HighPrivilegePairedFlag;
}
