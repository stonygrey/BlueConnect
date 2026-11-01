// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.Shared.Services.Abstractions;

namespace Zaffre.Services.Abstractions;

/// <summary>
/// One-shot Verdigris commands against a scanned device, each issued under a
/// pairing role's credentials: connect, invoke, disconnect. The device
/// enforces the role's privilege tier — a low-privilege session attempting a
/// high-privilege command gets STATUS_PERMISSION_DENIED. Calls are serialized
/// (the board is single-central).
/// </summary>
public interface ICommandService : IService
{
    Task<byte[]> Echo(IAdvertisement advertisement, PairingRole role, byte[] payload, CancellationToken cancellationToken = default);

    Task<double> SquareRoot(IAdvertisement advertisement, PairingRole role, double value, CancellationToken cancellationToken = default);

    Task<ulong> Fibonacci(IAdvertisement advertisement, PairingRole role, uint n, CancellationToken cancellationToken = default);

    Task<DateTimeOffset> GetTime(IAdvertisement advertisement, PairingRole role, CancellationToken cancellationToken = default);

    Task SetTime(IAdvertisement advertisement, PairingRole role, DateTimeOffset time, CancellationToken cancellationToken = default);

    Task<bool> GetLed(IAdvertisement advertisement, PairingRole role, CancellationToken cancellationToken = default);

    Task<bool> SetLed(IAdvertisement advertisement, PairingRole role, bool on, CancellationToken cancellationToken = default);

    Task<string> GetSerialNumber(IAdvertisement advertisement, PairingRole role, CancellationToken cancellationToken = default);

    Task<string> SetSerialNumber(IAdvertisement advertisement, PairingRole role, ushort serialNumber, CancellationToken cancellationToken = default);
}
