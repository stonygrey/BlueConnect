// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

using StonyGrey.BlueConnect.Secure;

namespace StonyGrey.BlueConnect.Core.Tests;

public class ClientEndorsementTests
{
    [Fact]
    public void ValidMatchingKeyPasses()
    {
        var (caPrivate, caPublic) = ClientEndorsement.GenerateCa();
        var subject = NonZeroKey();
        var blob = ClientEndorsement.Sign(subject, caPrivate);

        Assert.Equal(ClientEndorsement.BlobLength, blob.Length);
        Assert.True(ClientEndorsement.IsCertBlob(blob));
        Assert.True(ClientEndorsement.TryVerify(blob, subject, caPublic));
    }

    [Fact]
    public void WrongSubjectKeyFails()
    {
        var (caPrivate, caPublic) = ClientEndorsement.GenerateCa();
        var subject = NonZeroKey();
        var other = NonZeroKey();
        var blob = ClientEndorsement.Sign(subject, caPrivate);

        Assert.False(ClientEndorsement.TryVerify(blob, other, caPublic));
    }

    [Fact]
    public void BadSignatureFails()
    {
        var (caPrivate, caPublic) = ClientEndorsement.GenerateCa();
        var subject = NonZeroKey();
        var blob = ClientEndorsement.Sign(subject, caPrivate);
        blob[^1] ^= 0x01;

        Assert.True(ClientEndorsement.IsCertBlob(blob));
        Assert.False(ClientEndorsement.TryVerify(blob, subject, caPublic));
    }

    [Fact]
    public void DummyHandshakePayloadIsNotACert()
    {
        Assert.False(ClientEndorsement.IsCertBlob(SecureLink.HandshakePayload));
        Assert.False(ClientEndorsement.TryVerify(SecureLink.HandshakePayload, NonZeroKey(), NonZeroKey()));
    }

    private static byte[] NonZeroKey()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        key[0] |= 1;
        return key;
    }
}
