// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.BlueConnect.Abstractions;

public class BlueConnectOptions
{
    public static readonly BlueConnectOptions Empty = new();

    public int ConnectTimeoutSeconds { get; set; } = 15;

    /// <summary>Timeout for a command write and, when enabled, its echoed response.</summary>
    public int CommandTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// How long one <c>Connect</c> call keeps retrying after a failed attempt.
    /// Each attempt is bounded by <see cref="ConnectTimeoutSeconds"/>; attempts
    /// repeat, a second apart, until one succeeds or this window (measured
    /// from the first attempt) runs out, at which point the last failure is
    /// reported. 0 retries until the caller cancels. Covers a device that is
    /// briefly out of range as well as the platform stack's own sporadic
    /// failures (Android's GATT status 133, or a connect that arrives on an ACL
    /// the stack is tearing down and then finds an empty attribute cache).
    /// Every retry is logged as a warning.
    /// </summary>
    public int ConnectRetryTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// When true, connect retries also stop as soon as the scanner has not
    /// seen an advertisement from the device within its expiry window
    /// (<see cref="BlueConnectConstants.ScannerExpirationSeconds"/>): a device
    /// that has gone out of range or off is given up on without waiting for
    /// the retry window, and the last failure is reported. Requires a running
    /// scan; with no scan the device always looks silent, so leave this off
    /// for clients that connect without scanning.
    /// </summary>
    public bool RetryOnlyWhileAdvertising { get; set; }

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
    /// slots). Must match the firmware. Default 0xFFFF
    /// (Bluetooth SIG reserved / testing).
    /// </summary>
    public ushort ManufacturerCompanyId { get; set; } = BlueConnectConstants.ManufacturerCompanyId;
}
