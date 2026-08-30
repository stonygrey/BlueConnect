// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.Droid;

/// <summary>
/// Android advertisement. A plain managed record: everything is copied out of
/// the Java <c>ScanResult</c> (which is then disposed) at discovery time, so
/// clients never hold — or need to dispose — Java peer objects. <see cref="Id"/>
/// is the device MAC, which is all that's needed to reconnect later via
/// <c>BluetoothAdapter.GetRemoteDevice</c>.
/// </summary>
internal sealed record Advertisement(
    string Id,
    string LocalName,
    bool IsConnectable,
    bool IsSecured,
    AuthenticationScheme AuthenticationScheme,
    Rssi Rssi,
    DateTimeOffset Timestamp) : IAdvertisement
{
    public bool Equals(IAdvertisement? other) => other is not null && Id == other.Id;
}
