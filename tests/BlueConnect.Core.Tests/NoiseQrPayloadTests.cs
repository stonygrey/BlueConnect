// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Buffers.Text;
using System.Security.Cryptography;

using StonyGrey.BlueConnect.Secure;
using StonyGrey.Spake2Plus;

namespace StonyGrey.BlueConnect.Core.Tests;

public class NoiseQrPayloadTests
{
    [Fact]
    public void RoundTripPreservesKeysAndTier()
    {
        var app = RandomNumberGenerator.GetBytes(32);
        app[0] |= 1;
        var device = RandomNumberGenerator.GetBytes(32);
        device[0] |= 1;

        var original = new NoiseQrPayload(PakeTier.High, app, device, "Verdigris 1");
        Assert.True(NoiseQrPayload.TryParse(original.Encode(), out var parsed));
        Assert.Equal(PakeTier.High, parsed!.Tier);
        Assert.Equal(app, parsed.AppPrivateKey);
        Assert.Equal(device, parsed.DevicePublicKey);
        Assert.Equal("Verdigris 1", parsed.DeviceName);
    }

    [Fact]
    public void TryParseRejectsAllZeroKeys()
    {
        var blob = new byte[64];
        var text = $"SGNK1:L:{Base64Url.EncodeToString(blob)}";
        Assert.False(NoiseQrPayload.TryParse(text, out _));
    }
}
