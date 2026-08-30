// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.Core.Tests;

/// <summary>
/// The two-bit scheme field in the advertised status byte: every combination
/// of the low four bits, including the legacy bytes deployed firmware sends
/// (bit 3 clear), must decode as documented.
/// </summary>
public class SchemeFromStatusTests
{
    [Theory]
    // Legacy Noise images: scheme bits 00 with any slot bits.
    [InlineData(0x00, AuthenticationScheme.Noise)]
    [InlineData(0x01, AuthenticationScheme.Noise)]
    [InlineData(0x02, AuthenticationScheme.Noise)]
    [InlineData(0x03, AuthenticationScheme.Noise)]
    // Deployed PAKE images: scheme bits 01.
    [InlineData(0x04, AuthenticationScheme.Pake)]
    [InlineData(0x05, AuthenticationScheme.Pake)]
    [InlineData(0x06, AuthenticationScheme.Pake)]
    [InlineData(0x07, AuthenticationScheme.Pake)]
    // EDHOC images: scheme bits 10.
    [InlineData(0x08, AuthenticationScheme.Edhoc)]
    [InlineData(0x09, AuthenticationScheme.Edhoc)]
    [InlineData(0x0A, AuthenticationScheme.Edhoc)]
    [InlineData(0x0B, AuthenticationScheme.Edhoc)]
    // Reserved scheme bits 11: unknown, never a wrong route.
    [InlineData(0x0C, AuthenticationScheme.Unknown)]
    [InlineData(0x0D, AuthenticationScheme.Unknown)]
    [InlineData(0x0E, AuthenticationScheme.Unknown)]
    [InlineData(0x0F, AuthenticationScheme.Unknown)]
    public void DecodesEveryLowNibble(byte status, AuthenticationScheme expected)
    {
        Assert.Equal(expected, BlueConnectConstants.SchemeFromStatus(status));
    }

    [Fact]
    public void HighBitsDoNotDisturbTheSchemeField()
    {
        Assert.Equal(
            AuthenticationScheme.Edhoc, BlueConnectConstants.SchemeFromStatus(0xF8));
        Assert.Equal(
            AuthenticationScheme.Pake, BlueConnectConstants.SchemeFromStatus(0xF4));
        Assert.Equal(
            AuthenticationScheme.Noise, BlueConnectConstants.SchemeFromStatus(0xF0));
    }
}
