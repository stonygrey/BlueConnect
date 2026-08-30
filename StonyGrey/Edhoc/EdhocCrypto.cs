// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

using ECCurve = Org.BouncyCastle.Math.EC.ECCurve;
using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;

namespace StonyGrey.Edhoc;

/// <summary>
/// The cipher suite 2 primitives (RFC 9528 §3.6): P-256 ECDH, SHA-256, and
/// AES-CCM-16-64-128 (13-byte nonce, 8-byte tag). Public keys travel as the
/// 32-byte 'x'-coordinate; ECDH shared secrets are the 'x'-coordinate of the
/// shared point (RFC 9528 §9.5), so the sign of 'y' never matters.
/// </summary>
internal static class EdhocCrypto
{
    public const int CoordinateLength = 32;
    public const int HashLength = 32;
    public const int MacLength = 8;
    public const int AeadKeyLength = 16;
    public const int AeadNonceLength = 13;
    public const int AeadTagLength = 8;

    private static readonly X9ECParameters CurveParameters = SecNamedCurves.GetByName("secp256r1");

    public static ECCurve Curve => CurveParameters.Curve;

    public static ECPoint G => CurveParameters.G;

    public static BigInteger N => CurveParameters.N;

    public static byte[] RandomScalar()
    {
        // Rejection-sample below the group order; zero is rejected.
        Span<byte> candidate = stackalloc byte[CoordinateLength];
        for (; ; )
        {
            RandomNumberGenerator.Fill(candidate);
            var value = new BigInteger(1, candidate.ToArray());
            if (value.SignValue > 0 && value.CompareTo(N) < 0)
            {
                return value.ToByteArrayUnsigned();
            }
        }
    }

    public static ECPoint PublicPoint(BigInteger scalar) => G.Multiply(scalar).Normalize();

    public static byte[] XCoordinate(ECPoint point) =>
        point.Normalize().AffineXCoord.GetEncoded();

    public static byte[] YCoordinate(ECPoint point) =>
        point.Normalize().AffineYCoord.GetEncoded();

    /// <summary>
    /// Recovers a curve point from a bare 'x'-coordinate. The choice of 'y'
    /// is arbitrary (compressed prefix 0x02): both candidate points share
    /// every ECDH 'x'-coordinate. Rejects x-values not on the curve.
    /// </summary>
    public static ECPoint DecodeXOnly(ReadOnlySpan<byte> x)
    {
        if (x.Length != CoordinateLength)
        {
            throw new EdhocException("Public key is not a 32-byte P-256 x-coordinate.");
        }

        var encoded = new byte[1 + CoordinateLength];
        encoded[0] = 0x02;
        x.CopyTo(encoded.AsSpan(1));
        try
        {
            return Curve.DecodePoint(encoded);
        }
        catch (Exception e)
        {
            throw new EdhocException("Public key x-coordinate is not on the curve.", e);
        }
    }

    /// <summary>Decodes and validates an (x, y) public key, e.g. from a CCS.</summary>
    public static ECPoint DecodePoint(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y)
    {
        if (x.Length != CoordinateLength || y.Length != CoordinateLength)
        {
            throw new EdhocException("Public key coordinates are not 32 bytes each.");
        }

        var encoded = new byte[1 + 2 * CoordinateLength];
        encoded[0] = 0x04;
        x.CopyTo(encoded.AsSpan(1));
        y.CopyTo(encoded.AsSpan(1 + CoordinateLength));
        try
        {
            return Curve.DecodePoint(encoded);
        }
        catch (Exception e)
        {
            throw new EdhocException("Public key is not on the curve.", e);
        }
    }

    /// <summary>ECDH: the 'x'-coordinate of scalar·point (RFC 9528 §9.5).</summary>
    public static byte[] Agree(BigInteger scalar, ECPoint point)
    {
        var shared = point.Multiply(scalar).Normalize();
        if (shared.IsInfinity)
        {
            throw new EdhocException("ECDH produced the identity point.");
        }

        return XCoordinate(shared);
    }

    /// <summary>AES-CCM-16-64-128 seal: returns ciphertext ‖ 8-byte tag.</summary>
    public static byte[] CcmEncrypt(byte[] key, byte[] nonce, byte[] aad, byte[] plaintext) =>
        RunCcm(forEncryption: true, key, nonce, aad, plaintext,
               failure: "AEAD encryption failed.");

    /// <summary>AES-CCM-16-64-128 open: throws when the tag does not verify.</summary>
    public static byte[] CcmDecrypt(byte[] key, byte[] nonce, byte[] aad, byte[] ciphertext) =>
        RunCcm(forEncryption: false, key, nonce, aad, ciphertext,
               failure: "AEAD tag verification failed.");

    private static byte[] RunCcm(
        bool forEncryption, byte[] key, byte[] nonce, byte[] aad, byte[] input, string failure)
    {
        var cipher = new CcmBlockCipher(AesUtilities.CreateEngine());
        cipher.Init(forEncryption, new AeadParameters(
            new KeyParameter(key), AeadTagLength * 8, nonce, aad));

        var output = new byte[cipher.GetOutputSize(input.Length)];
        try
        {
            var written = cipher.ProcessBytes(input, 0, input.Length, output, 0);
            cipher.DoFinal(output, written);
        }
        catch (InvalidCipherTextException e)
        {
            throw new EdhocException(failure, e);
        }

        return output;
    }
}
