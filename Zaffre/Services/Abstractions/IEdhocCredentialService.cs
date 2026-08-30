// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Secure;
using StonyGrey.Shared.Services.Abstractions;

namespace Zaffre.Services.Abstractions;

/// <summary>Origin is "paired" (ceremony) or "provisioned" (QR); null when
/// stored before origins were recorded (treated as paired).</summary>
public sealed record EdhocCredentialState(bool HasCredential, string? DeviceName, string? Origin = null);

/// <summary>Both roles' EDHOC credential states, published as one snapshot.</summary>
public sealed record EdhocCredentials(EdhocCredentialState Low, EdhocCredentialState High)
{
    public EdhocCredentialState For(PairingRole role) => role == PairingRole.High ? High : Low;
}

/// <summary>
/// Owns the app's EDHOC credentials (one per privilege role, kept in
/// platform secure storage as the SGEK1 payload: the role's static DH
/// private key plus the device's pinned credential). A credential arrives
/// two ways: scan/paste the QR minted at USB provisioning, or
/// <see cref="Pair"/> — an enrolling handshake that generates keys on both
/// ends and pins them via the device's button ceremony.
/// </summary>
public interface IEdhocCredentialService : IObservableService<EdhocCredentials>
{
    /// <summary>
    /// Enrolls with the device: runs an enrolling EDHOC handshake (fresh
    /// keys on first use), asks the device to open its pairing ceremony
    /// window, and — once the matching button confirms — stores the
    /// credential pair exactly as if its QR had been scanned. The device
    /// refuses when the slot was provisioned over USB.
    /// </summary>
    Task Pair(IAdvertisement advertisement, PairingRole role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Security for a session under the role's stored credential. Throws
    /// <see cref="InvalidOperationException"/> when none is stored.
    /// </summary>
    Task<EdhocSecurity> GetLinkSecurity(PairingRole role, CancellationToken cancellationToken = default);

    /// <summary>Validates and stores a scanned or pasted SGEK1 payload.</summary>
    Task StorePayload(PairingRole role, EdhocQrPayload payload, string origin = "provisioned", CancellationToken cancellationToken = default);

    /// <summary>Forgets the role's credential locally (the device is untouched).</summary>
    Task Clear(PairingRole role);
}
