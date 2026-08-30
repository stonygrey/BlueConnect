// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.BlueConnect.Abstractions;

public class BlueConnectOptions
{
    public static readonly BlueConnectOptions Empty = new();

    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>Timeout for a command write and, when enabled, its echoed response.</summary>
    public int CommandTimeoutSeconds { get; set; } = 10;

    /// <summary>Lower bound (inclusive) of the random wait between test cycles.</summary>
    public int MinCycleDelaySeconds { get; set; } = 1;

    /// <summary>Upper bound (inclusive) of the random wait between test cycles.</summary>
    public int MaxCycleDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Controls the payload of the per-cycle Verdigris Echo command: when true a
    /// large random payload spanning multiple MTUs is used (stressing framing and
    /// reassembly in both directions); when false a small 16-byte ping is used.
    /// The echoed payload is always verified byte-for-byte.
    /// </summary>
    public bool IsEchoVerificationEnabled { get; set; }

    /// <summary>
    /// GATT service the scanner filters on and the peripheral binds at connect.
    /// Defaults to the Verdigris service.
    /// </summary>
    public Guid ServiceUuid { get; set; } = Guid.Parse(BlueConnectConstants.VerdigrisServiceUuid);

    /// <summary>
    /// Characteristic the central writes (Write Request). Defaults to Verdigris RX.
    /// </summary>
    public Guid RxCharacteristicUuid { get; set; } = Guid.Parse(BlueConnectConstants.VerdigrisRxCharacteristicUuid);

    /// <summary>
    /// Characteristic the peripheral notifies on. Defaults to Verdigris TX.
    /// </summary>
    public Guid TxCharacteristicUuid { get; set; } = Guid.Parse(BlueConnectConstants.VerdigrisTxCharacteristicUuid);

    /// <summary>
    /// Company id in advertised manufacturer data (status byte: pairing
    /// slots and Noise vs PAKE). Must match the firmware. Default 0xFFFF
    /// (Bluetooth SIG reserved / testing).
    /// </summary>
    public ushort ManufacturerCompanyId { get; set; } = BlueConnectConstants.ManufacturerCompanyId;
}
