// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Text;

using Org.BouncyCastle.Math;

namespace StonyGrey.Spake2Plus.Tests;

/// <summary>
/// Known-answer tests against the RFC 9383 Appendix C vectors for
/// P256-SHA256-HKDF-SHA256-HMAC-SHA256. Ephemerals are injected through the
/// internal constructors; every on-wire and derived value must match the RFC
/// byte-exactly. These vectors are also what the firmware's PSA
/// implementation conforms to, so passing here is the interop baseline.
/// </summary>
public class Rfc9383VectorTests
{
    private static readonly byte[] Context =
        Encoding.UTF8.GetBytes("SPAKE2+-P256-SHA256-HKDF-SHA256-HMAC-SHA256 Test Vectors");

    private static readonly byte[] IdProver = Encoding.UTF8.GetBytes("client");
    private static readonly byte[] IdVerifier = Encoding.UTF8.GetBytes("server");

    private static readonly byte[] W0 =
        Convert.FromHexString("bb8e1bbcf3c48f62c08db243652ae55d3e5586053fca77102994f23ad95491b3");

    private static readonly byte[] W1 =
        Convert.FromHexString("7e945f34d78785b8a3ef44d0df5a1a97d6b3b460409a345ca7830387a74b1dba");

    private static readonly byte[] L = Convert.FromHexString(
        "04eb7c9db3d9a9eb1f8adab81b5794c1f13ae3e225efbe91ea487425854c7fc00f" +
        "00bfedcbd09b2400142d40a14f2064ef31dfaa903b91d1faea7093d835966efd");

    private static readonly BigInteger X = new(
        "d1232c8e8693d02368976c174e2088851b8365d0d79a9eee709c6a05a2fad539", 16);

    private static readonly BigInteger Y = new(
        "717a72348a182085109c8d3917d6c43d59b224dc6a7fc4f0483232fa6516d8b3", 16);

    private static readonly byte[] ShareP = Convert.FromHexString(
        "04ef3bd051bf78a2234ec0df197f7828060fe9856503579bb1733009042c15c0c1" +
        "de127727f418b5966afadfdd95a6e4591d171056b333dab97a79c7193e341727");

    private static readonly byte[] ShareV = Convert.FromHexString(
        "04c0f65da0d11927bdf5d560c69e1d7d939a05b0e88291887d679fcadea75810fb" +
        "5cc1ca7494db39e82ff2f50665255d76173e09986ab46742c798a9a68437b048");

    private static readonly byte[] ConfirmP =
        Convert.FromHexString("926cc713504b9b4d76c9162ded04b5493e89109f6d89462cd33adc46fda27527");

    private static readonly byte[] ConfirmV =
        Convert.FromHexString("9747bcc4f8fe9f63defee53ac9b07876d907d55047e6ff2def2e7529089d3e68");

    private static readonly byte[] SharedKey =
        Convert.FromHexString("0c5f8ccd1413423a54f6c1fb26ff01534a87f893779c6e68666d772bfd91f3e7");

    [Fact]
    public void ProverMatchesVectors()
    {
        var prover = new Spake2PlusProver(W0, W1, Context, IdProver, IdVerifier, X);

        Assert.Equal(ShareP, prover.GenerateShare());

        var (confirmP, sharedKey) = prover.Finish(ShareV, ConfirmV);

        Assert.Equal(ConfirmP, confirmP);
        Assert.Equal(SharedKey, sharedKey);
    }

    [Fact]
    public void VerifierMatchesVectors()
    {
        var verifier = new Spake2PlusVerifier(W0, L, Context, IdProver, IdVerifier, Y);

        var (shareV, confirmV) = verifier.Respond(ShareP);

        Assert.Equal(ShareV, shareV);
        Assert.Equal(ConfirmV, confirmV);
        Assert.Equal(SharedKey, verifier.VerifyConfirm(ConfirmP));
    }

    [Fact]
    public void RegistrationRecoversLFromW1()
    {
        var registration = Spake2PlusRegistration.FromScalars(W0, W1);

        Assert.Equal(L, registration.L);
        Assert.Equal(97, registration.VerifierBlob.Length);
        Assert.Equal(W0, registration.VerifierBlob[..32]);
        Assert.Equal(L, registration.VerifierBlob[32..]);
    }
}
