// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.WinRT;

/// <summary>
/// Windows advertisement. <see cref="Address"/> carries the raw 48-bit
/// Bluetooth address needed by <see cref="Peripheral"/> to connect;
/// <see cref="Id"/> is the same address formatted as a MAC string.
/// </summary>
internal sealed record Advertisement(
    string Id,
    string LocalName,
    bool IsConnectable,
    bool IsSecured,
    Rssi Rssi,
    DateTimeOffset Timestamp,
    ulong Address) : IAdvertisement
{
    public bool Equals(IAdvertisement? other) => other is not null && Id == other.Id;
}
