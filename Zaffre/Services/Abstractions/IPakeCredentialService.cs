// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Secure;
using StonyGrey.Shared.Services.Abstractions;

namespace Zaffre.Services.Abstractions;

public sealed record PakeCredentialState(bool HasCode, string? DeviceName);

/// <summary>Both roles' PAKE credential states, published as one snapshot.</summary>
public sealed record PakeCredentials(PakeCredentialState Low, PakeCredentialState High)
{
    public PakeCredentialState For(PairingRole role) => role == PairingRole.High ? High : Low;
}

/// <summary>
/// Owns the app's SPAKE2+ setup codes (one per privilege role, scanned from
/// the device's QR or typed, kept in platform secure storage). The stored
/// code is the source of truth; the derived scalars are cached in memory
/// because the PBKDF2 derivation is deliberately slow. There is no pairing
/// ceremony: knowing the tier's code is the pairing.
/// </summary>
public interface IPakeCredentialService : IObservableService<PakeCredentials>
{
    /// <summary>
    /// Security for a session under the role's stored code. Throws
    /// <see cref="InvalidOperationException"/> when no code is stored.
    /// </summary>
    Task<PakeSecurity> GetLinkSecurity(PairingRole role, CancellationToken cancellationToken = default);

    /// <summary>Validates, stores, and derives from a scanned or typed code.</summary>
    Task StoreCode(PairingRole role, string code, string? deviceName, CancellationToken cancellationToken = default);

    /// <summary>Forgets the role's code locally (the device is untouched).</summary>
    Task Clear(PairingRole role);
}
