// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Spake2Plus.Tests;

/// <summary>
/// End-to-end handshakes through the BlueConnect profile with random
/// ephemerals, plus the failure modes: mismatched codes, tampering, and
/// malformed points.
/// </summary>
public class ProtocolTests
{
    private const string Code = "AB3D-EF6H-JK8M-NP2R";

    [Theory]
    [InlineData(PakeTier.Low)]
    [InlineData(PakeTier.High)]
    public void HandshakeAgreesOnSharedKey(PakeTier tier)
    {
        var registration = BlueConnectPake.Register(Code, tier);
        var prover = BlueConnectPake.CreateProver(registration);
        var verifier = BlueConnectPake.CreateVerifier(registration);

        var shareP = prover.GenerateShare();
        var (shareV, confirmV) = verifier.Respond(shareP);
        var (confirmP, proverKey) = prover.Finish(shareV, confirmV);
        var verifierKey = verifier.VerifyConfirm(confirmP);

        Assert.Equal(proverKey, verifierKey);
        Assert.Equal(32, proverKey.Length);
    }

    [Fact]
    public void WrongCodeFailsOnBothSides()
    {
        var deviceRegistration = BlueConnectPake.Register(Code, PakeTier.Low);
        var phoneRegistration = BlueConnectPake.Register("2222-3333-4444-5555", PakeTier.Low);

        var prover = BlueConnectPake.CreateProver(phoneRegistration);
        var verifier = BlueConnectPake.CreateVerifier(deviceRegistration);

        var shareP = prover.GenerateShare();
        var (shareV, confirmV) = verifier.Respond(shareP);

        // The prover detects the mismatch in confirmV...
        Assert.Throws<Spake2PlusException>(() => prover.Finish(shareV, confirmV));

        // ...and even a prover that ignored it cannot forge confirmP.
        var oblivious = BlueConnectPake.CreateProver(phoneRegistration);
        var obliviousShare = oblivious.GenerateShare();
        var freshVerifier = BlueConnectPake.CreateVerifier(deviceRegistration);
        var (freshShareV, _) = freshVerifier.Respond(obliviousShare);

        byte[] forgedConfirmP;
        try
        {
            (forgedConfirmP, _) = oblivious.Finish(freshShareV, new byte[32]);
        }
        catch (Spake2PlusException)
        {
            // Expected path: it cannot even get that far. Send garbage.
            forgedConfirmP = new byte[32];
        }

        Assert.Throws<Spake2PlusException>(() => freshVerifier.VerifyConfirm(forgedConfirmP));
    }

    [Fact]
    public void SameCodeDifferentTierDoesNotInterop()
    {
        var low = BlueConnectPake.Register(Code, PakeTier.Low);
        var high = BlueConnectPake.Register(Code, PakeTier.High);

        var prover = BlueConnectPake.CreateProver(low);
        var verifier = BlueConnectPake.CreateVerifier(high);

        var shareP = prover.GenerateShare();
        var (shareV, confirmV) = verifier.Respond(shareP);

        Assert.Throws<Spake2PlusException>(() => prover.Finish(shareV, confirmV));
    }

    [Fact]
    public void TamperedConfirmVIsRejected()
    {
        var registration = BlueConnectPake.Register(Code, PakeTier.Low);
        var prover = BlueConnectPake.CreateProver(registration);
        var verifier = BlueConnectPake.CreateVerifier(registration);

        var shareP = prover.GenerateShare();
        var (shareV, confirmV) = verifier.Respond(shareP);
        confirmV[0] ^= 0x01;

        Assert.Throws<Spake2PlusException>(() => prover.Finish(shareV, confirmV));
    }

    [Fact]
    public void TamperedConfirmPIsRejected()
    {
        var registration = BlueConnectPake.Register(Code, PakeTier.Low);
        var prover = BlueConnectPake.CreateProver(registration);
        var verifier = BlueConnectPake.CreateVerifier(registration);

        var shareP = prover.GenerateShare();
        var (shareV, confirmV) = verifier.Respond(shareP);
        var (confirmP, _) = prover.Finish(shareV, confirmV);
        confirmP[31] ^= 0x80;

        Assert.Throws<Spake2PlusException>(() => verifier.VerifyConfirm(confirmP));
    }

    [Theory]
    [InlineData(0)]   // empty
    [InlineData(64)]  // truncated
    [InlineData(66)]  // oversize
    public void MalformedShareLengthIsRejected(int length)
    {
        var registration = BlueConnectPake.Register(Code, PakeTier.Low);
        var verifier = BlueConnectPake.CreateVerifier(registration);

        Assert.Throws<Spake2PlusException>(() => verifier.Respond(new byte[length]));
    }

    [Fact]
    public void OffCurveShareIsRejected()
    {
        var registration = BlueConnectPake.Register(Code, PakeTier.Low);
        var verifier = BlueConnectPake.CreateVerifier(registration);

        var bogus = new byte[65];
        bogus[0] = 0x04;
        bogus[64] = 0x01; // x = 0, y = 1 is not on P-256

        Assert.Throws<Spake2PlusException>(() => verifier.Respond(bogus));
    }

    [Fact]
    public void HandshakeObjectsAreSingleUse()
    {
        var registration = BlueConnectPake.Register(Code, PakeTier.Low);
        var prover = BlueConnectPake.CreateProver(registration);
        var verifier = BlueConnectPake.CreateVerifier(registration);

        var shareP = prover.GenerateShare();
        Assert.Throws<Spake2PlusException>(prover.GenerateShare);

        var (shareV, confirmV) = verifier.Respond(shareP);
        Assert.Throws<Spake2PlusException>(() => verifier.Respond(shareP));

        var (confirmP, _) = prover.Finish(shareV, confirmV);
        Assert.Throws<Spake2PlusException>(() => prover.Finish(shareV, confirmV));

        verifier.VerifyConfirm(confirmP);
        Assert.Throws<Spake2PlusException>(() => verifier.VerifyConfirm(confirmP));
    }
}
