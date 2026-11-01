// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.CoreBluetooth;

internal sealed record Advertisement(
    string Id,
    string LocalName,
    bool IsConnectable,
    bool IsSecured,
    Rssi Rssi,
    DateTimeOffset Timestamp) : IAdvertisement
{
    public bool Equals(IAdvertisement? other) => other is not null && Id == other.Id;
}
