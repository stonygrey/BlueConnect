// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.Shared.Services.Abstractions;

namespace Zaffre.Services.Abstractions;

/// <summary>
/// Which transport security a session runs under: Noise XX with the role's
/// pinned keys (against the noise firmware), SPAKE2+ from the role's setup
/// code (against the pake firmware), or EDHOC with the role's provisioned
/// credential (against the edhoc firmware). The command layer is identical
/// either way — that independence is the point of the firmware apps.
/// </summary>
public enum LinkKind
{
    Noise,
    Pake,
    Edhoc,
}

/// <summary>
/// One-shot Verdigris commands against a scanned device, each issued under a
/// pairing role's credentials: connect, invoke, disconnect. The device
/// enforces the role's privilege tier — a low-privilege session attempting a
/// high-privilege command gets STATUS_PERMISSION_DENIED. Calls are serialized
/// (the board is single-central).
/// </summary>
public interface ICommandService : IService
{
    Task<byte[]> Echo(IAdvertisement advertisement, PairingRole role, byte[] payload, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default);

    Task<double> SquareRoot(IAdvertisement advertisement, PairingRole role, double value, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default);

    Task<ulong> Fibonacci(IAdvertisement advertisement, PairingRole role, uint n, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default);

    Task<DateTimeOffset> GetTime(IAdvertisement advertisement, PairingRole role, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default);

    Task SetTime(IAdvertisement advertisement, PairingRole role, DateTimeOffset time, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default);

    Task<bool> GetLed(IAdvertisement advertisement, PairingRole role, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default);

    Task<bool> SetLed(IAdvertisement advertisement, PairingRole role, bool on, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default);

    Task<string> GetSerialNumber(IAdvertisement advertisement, PairingRole role, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default);

    Task<string> SetSerialNumber(IAdvertisement advertisement, PairingRole role, ushort serialNumber, LinkKind kind = LinkKind.Noise, CancellationToken cancellationToken = default);
}
