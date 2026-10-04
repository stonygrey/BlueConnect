// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Secure;

namespace StonyGrey.BlueConnect.Core.Tests;

public class NoiseEnrollmentPayloadTests
{
    private static byte[] Key(byte fill) => Enumerable.Repeat(fill, 32).ToArray();

    [Fact]
    public void RoundTripsWithAndWithoutAName()
    {
        var named = new NoiseEnrollmentPayload(Key(1), Key(2), "Verdigris 0001");
        Assert.True(NoiseEnrollmentPayload.TryParse(named.Encode(), out var parsed));
        Assert.Equal(named.EnrollmentPrivateKey, parsed!.EnrollmentPrivateKey);
        Assert.Equal(named.DevicePublicKey, parsed.DevicePublicKey);
        Assert.Equal(named.DeviceName, parsed.DeviceName);
        Assert.StartsWith("SGNE1:", named.Encode());

        var anonymous = new NoiseEnrollmentPayload(Key(3), Key(4), null);
        Assert.True(NoiseEnrollmentPayload.TryParse(anonymous.Encode(), out var parsed2));
        Assert.Null(parsed2!.DeviceName);
        Assert.Equal(Key(3), parsed2.EnrollmentPrivateKey);
        Assert.Equal(Key(4), parsed2.DevicePublicKey);
    }

    [Fact]
    public void RejectsCredentialPayloadsAndBadBlobs()
    {
        var credential = new NoiseQrPayload(StonyGrey.Spake2Plus.PakeTier.Low, Key(1), Key(2), null).Encode();
        Assert.False(NoiseEnrollmentPayload.TryParse(credential, out _));
        Assert.False(NoiseQrPayload.TryParse(new NoiseEnrollmentPayload(Key(1), Key(2), null).Encode(), out _));
        Assert.False(NoiseEnrollmentPayload.TryParse("SGNE1:not-base64!", out _));
        Assert.False(NoiseEnrollmentPayload.TryParse("SGNE1:" + System.Buffers.Text.Base64Url.EncodeToString(new byte[63]), out _));
        Assert.Throws<LinkSecurityException>(() => new NoiseEnrollmentPayload(new byte[32], Key(2), null).Encode());
    }
}
