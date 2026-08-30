// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

using Microsoft.Extensions.Logging;

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.Secure;

/// <summary>
/// Credentials plus link construction: what a session authenticates with
/// decides which <see cref="ILink"/> speaks for it — Noise XX for pinned
/// static keys (<see cref="LinkSecurity"/>), SPAKE2+ for a setup code
/// (<see cref="PakeSecurity"/>).
/// </summary>
public interface ILinkSecurity
{
    ILink CreateLink(IPeripheral peripheral, int maxFrame, ILogger logger);
}

/// <summary>
/// A party's long-term link identity: an X25519 static key pair. Identity is
/// the key itself. Trust is established by the pairing ceremony (mutual key
/// pinning gated by a button press on the device). An optional client
/// endorsement cert binds this key to a CA; it does not grant privilege.
/// </summary>
public sealed class LinkIdentity
{
    public byte[] StaticPrivateKey { get; }

    public byte[] StaticPublicKey { get; }

    public LinkIdentity(byte[] staticPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(staticPrivateKey);
        ArgumentOutOfRangeException.ThrowIfNotEqual(staticPrivateKey.Length, LinkProtocol.KeyLength);

        StaticPrivateKey = staticPrivateKey;
        using var keyPair = LinkProtocol.Protocol.CreateKeyPair(staticPrivateKey);
        StaticPublicKey = (byte[])keyPair.PublicKey.Clone();
    }

    public static LinkIdentity Generate()
    {
        var privateKey = new byte[LinkProtocol.KeyLength];
        RandomNumberGenerator.Fill(privateKey);
        return new LinkIdentity(privateKey);
    }
}

/// <summary>
/// Link-security settings for one session. A null <see cref="PinnedPeerKey"/>
/// accepts any peer (used only during the pairing ceremony, where trust is
/// established by the device-side button press); otherwise the handshake fails
/// unless the peer proves possession of exactly the pinned key.
/// <see cref="Endorsement"/> is an optional compact endorsement of
/// <see cref="Identity"/>'s static public key; when present it rides in
/// Noise XX message 3. It does not grant privilege.
/// </summary>
public sealed record LinkSecurity(LinkIdentity Identity, byte[]? PinnedPeerKey, byte[]? Endorsement = null) : ILinkSecurity
{
    public ILink CreateLink(IPeripheral peripheral, int maxFrame, ILogger logger)
        => new SecureLink(peripheral, maxFrame, this, initiator: true, logger);
}
