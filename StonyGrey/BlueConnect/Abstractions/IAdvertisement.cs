// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.BlueConnect.Abstractions;

public record struct Rssi(int ValueDbm, int MinDbm, int MaxDbm)
{
    public static readonly Rssi Empty = new(0, 0, 0);
}

/// <summary>
/// Which transport-security scheme the device's firmware speaks, advertised
/// in the manufacturer-data status byte. Unknown until a packet carrying the
/// Stony Grey manufacturer data has been seen.
/// </summary>
public enum AuthenticationScheme
{
    Unknown,
    Noise,
    Pake,
    Edhoc,
}

public interface IAdvertisement : IEquatable<IAdvertisement>
{
    /// <summary>
    /// 48-bit MAC address on Android/ Windows, 128 GUID on iOS.
    /// Uniquely identifies the peripheral.
    /// </summary>
    string Id { get; }

    string LocalName { get; }

    bool IsConnectable { get; }

    /// <summary>
    /// True when the device advertises that it is paired and will only accept
    /// the pinned client. A pre-connection hint for UI and state-mismatch
    /// detection only — advertisements are unauthenticated, so enforcement
    /// always happens at the link layer, never from this flag. Sessions are
    /// encrypted whether or not this flag is set.
    /// </summary>
    bool IsSecured { get; }

    /// <summary>
    /// The transport-security scheme the firmware advertises (Noise, PAKE, or
    /// EDHOC).
    /// Like <see cref="IsSecured"/>, an unauthenticated pre-connection hint
    /// for UI routing only — the wrong link simply fails to establish.
    /// </summary>
    AuthenticationScheme AuthenticationScheme { get; }

    Rssi Rssi { get; }

    DateTimeOffset Timestamp { get; }
}
