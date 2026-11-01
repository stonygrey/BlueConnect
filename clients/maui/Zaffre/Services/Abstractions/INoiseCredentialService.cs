// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Secure;
using StonyGrey.Shared.Services.Abstractions;

namespace Zaffre.Services.Abstractions;

/// <summary>A role's pairing state; <see cref="Origin"/> is meaningful only when paired.</summary>
public sealed record NoiseCredentialState(bool IsPaired, string? DeviceName, CredentialOrigin Origin = CredentialOrigin.Paired);

/// <summary>Both roles' pairing states, published as one snapshot.</summary>
public sealed record NoiseCredentials(NoiseCredentialState Low, NoiseCredentialState High)
{
    public NoiseCredentialState For(PairingRole role) => role == PairingRole.High ? High : Low;
}

/// <summary>
/// Owns the app's link identities (an X25519 key pair per privilege role,
/// generated on first use and kept in platform secure storage) and their
/// pairings (one pinned device key per role). Every session is encrypted;
/// once a role is paired, its sessions also mutually authenticate against
/// the pinned key, and the device grants that role's privilege tier.
/// </summary>
public interface INoiseCredentialService : IObservableService<NoiseCredentials>
{
    /// <summary>Security for a session under the given role: always encrypted; pinned when paired.</summary>
    Task<LinkSecurity> GetLinkSecurity(PairingRole role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the pairing ceremony for a role; completes when the device's
    /// button is pressed. On success both sides have pinned each other and
    /// the device grants the role's privilege tier to this identity. A
    /// The matching button (3 = low, 4 = high) replaces any previous occupant.
    /// </summary>
    Task Pair(IAdvertisement advertisement, PairingRole role, CancellationToken cancellationToken = default);

    /// <summary>Clears the role's pairing on the device and locally.</summary>
    Task Unpair(IAdvertisement advertisement, PairingRole role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs a USB-provisioned credential: the payload's private key
    /// becomes the role's identity and its device key the role's pin,
    /// replacing any generated identity. The matching device slot was locked
    /// by <c>lib/device noise-provision</c>, so no ceremony can displace it.
    /// </summary>
    Task StorePayload(PairingRole role, NoiseQrPayload payload);

    /// <summary>
    /// Enrolls this app's low-tier identity with a device from a factory
    /// enrollment QR: opens a session with the payload's enrollment key,
    /// sends <c>Enroll</c> with the low identity's public key, and on success
    /// pins the payload's device key for the low role (<see cref="CredentialOrigin.Enrolled"/>).
    /// The enrollment key is never stored. The device replaces any earlier
    /// occupant of its low slot, so one enrolled phone holds it at a time.
    /// </summary>
    Task Enroll(IAdvertisement advertisement, NoiseEnrollmentPayload payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs a compact endorsement of this role's existing
    /// Noise static. Stored next to the identity in the same secure store.
    /// Does not rotate the key and does not grant privilege.
    /// </summary>
    Task InstallEndorsement(PairingRole role, byte[] blob);

    /// <summary>This role's Noise static public key (created on first use).</summary>
    Task<byte[]> GetStaticPublicKey(PairingRole role, CancellationToken cancellationToken = default);
}
