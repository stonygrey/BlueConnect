// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Spake2Plus.Tests;

public class RegistrationAndCodeTests
{
    private const string Code = "AB3D-EF6H-JK8M-NP2R";

    [Fact]
    public void RegistrationIsDeterministicPerCodeAndTier()
    {
        var first = BlueConnectPake.Register(Code, PakeTier.Low);
        var again = BlueConnectPake.Register(Code, PakeTier.Low);

        Assert.Equal(first.VerifierBlob, again.VerifierBlob);
        Assert.Equal(32, first.W0.Length);
        Assert.Equal(32, first.W1.Length);
        Assert.Equal(65, first.L.Length);
        Assert.Equal(0x04, first.L[0]);
    }

    [Fact]
    public void TiersDeriveDifferentVerifiers()
    {
        var low = BlueConnectPake.Register(Code, PakeTier.Low);
        var high = BlueConnectPake.Register(Code, PakeTier.High);

        Assert.NotEqual(low.VerifierBlob, high.VerifierBlob);
    }

    [Theory]
    [InlineData("ab3d-ef6h-jk8m-np2r")]          // case
    [InlineData("AB3DEF6HJK8MNP2R")]             // no dashes
    [InlineData("AB3D EF6H JK8M NP2R")]          // spaces
    [InlineData("AB3D-EF6H-JK8M-NP2R")]          // canonical
    public void NormalizationMakesEquivalentFormsAgree(string variant)
    {
        var canonical = BlueConnectPake.Register(Code, PakeTier.Low);
        var derived = BlueConnectPake.Register(variant, PakeTier.Low);

        Assert.Equal(canonical.VerifierBlob, derived.VerifierBlob);
    }

    [Fact]
    public void CrockfordLookalikesFold()
    {
        // I/L fold to 1, O folds to 0.
        Assert.Equal(SetupCode.Normalize("O111-2222-3333-4444"), SetupCode.Normalize("0ILl-2222-3333-4444"));
    }

    [Fact]
    public void GeneratedCodesAreWellFormedAndDistinct()
    {
        var first = SetupCode.Generate();
        var second = SetupCode.Generate();

        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){3}$", first);
        Assert.NotEqual(first, second);
        Assert.True(SetupCode.IsValid(first));
    }

    [Theory]
    [InlineData("")]
    [InlineData("AB3D-EF6H-JK8M")]              // too short
    [InlineData("AB3D-EF6H-JK8M-NP2R-XXXX")]    // too long
    [InlineData("AB3U-EF6H-JK8M-NP2R")]         // U is not in the alphabet
    public void InvalidCodesAreRejected(string code)
    {
        Assert.False(SetupCode.IsValid(code));
        Assert.Throws<Spake2PlusException>(() => BlueConnectPake.Register(code, PakeTier.Low));
    }

    [Theory]
    [InlineData("0000-0000-0000-0000")]         // one symbol
    [InlineData("AAAA-AAAA-AAAA-AAAA")]
    [InlineData("0123-0123-0123-0123")]         // repeated group
    [InlineData("0123-4567-89AB-CDEF")]         // sequential Crockford
    [InlineData("ABCD-EFGH-JKMN-PQRS")]         // sequential letters
    public void LowEntropyCodesAreDetected(string code)
    {
        Assert.True(SetupCode.IsValid(code));
        Assert.True(SetupCode.IsLowEntropy(code));
    }

    [Fact]
    public void TypicalAndGeneratedCodesAreNotLowEntropy()
    {
        Assert.False(SetupCode.IsLowEntropy(Code));
        Assert.False(SetupCode.IsLowEntropy(SetupCode.Generate()));
    }
}

public class QrPayloadTests
{
    [Fact]
    public void RoundTripsWithName()
    {
        var payload = new PakeQrPayload(PakeTier.High, "AB3D-EF6H-JK8M-NP2R", "Verdigris 1234");
        var encoded = payload.Encode();

        Assert.Equal("SGPK1:H:AB3D-EF6H-JK8M-NP2R:Verdigris 1234", encoded);
        Assert.True(PakeQrPayload.TryParse(encoded, out var parsed));
        Assert.Equal(payload, parsed);
    }

    [Fact]
    public void RoundTripsWithoutName()
    {
        var payload = new PakeQrPayload(PakeTier.Low, "AB3D-EF6H-JK8M-NP2R", null);
        var encoded = payload.Encode();

        Assert.Equal("SGPK1:L:AB3D-EF6H-JK8M-NP2R", encoded);
        Assert.True(PakeQrPayload.TryParse(encoded, out var parsed));
        Assert.Equal(payload, parsed);
    }

    [Theory]
    [InlineData("SGPK2:L:AB3D-EF6H-JK8M-NP2R")]  // unknown version
    [InlineData("SGPK1:X:AB3D-EF6H-JK8M-NP2R")]  // bad tier
    [InlineData("SGPK1:L:NOT-A-CODE")]           // bad code
    [InlineData("SGPK1:L")]                      // missing code
    [InlineData("hello")]
    [InlineData("")]
    public void RejectsMalformedPayloads(string text)
    {
        Assert.False(PakeQrPayload.TryParse(text, out var payload));
        Assert.Null(payload);
    }
}
