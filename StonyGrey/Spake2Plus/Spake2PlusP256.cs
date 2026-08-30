// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Security.Cryptography;

using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Math;

using ECCurve = Org.BouncyCastle.Math.EC.ECCurve;
using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;

namespace StonyGrey.Spake2Plus;

/// <summary>
/// The one ciphersuite this library speaks: SPAKE2+ P256-SHA256-HKDF-SHA256-
/// HMAC-SHA256 per RFC 9383. The firmware side is the same suite via PSA
/// (PSA_ALG_SPAKE2P_HMAC(PSA_ALG_SHA_256), secp256r1).
/// </summary>
internal static class Spake2PlusP256
{
    /// <summary>Uncompressed SEC1 point length.</summary>
    public const int PointLength = 65;

    /// <summary>Scalar / hash / MAC length.</summary>
    public const int ScalarLength = 32;

    /// <summary>PBKDF output half-width per RFC 9383 §3.2 for P-256.</summary>
    public const int ScalarSeedLength = 40;

    private static readonly X9ECParameters CurveParameters = SecNamedCurves.GetByName("secp256r1");

    public static ECCurve Curve => CurveParameters.Curve;

    public static ECPoint G => CurveParameters.G;

    /// <summary>The group order (RFC 9383 reduces w0s/w1s mod this).</summary>
    public static BigInteger N => CurveParameters.N;

    /// <summary>RFC 9383 fixed point M for P-256.</summary>
    public static readonly ECPoint M = Curve.DecodePoint(
        Convert.FromHexString("02886e2f97ace46e55ba9dd7242579f2993b64e16ef3dcab95afd497333d8fa12f"));

    /// <summary>RFC 9383 fixed point N for P-256 (named NPoint; N is the order).</summary>
    public static readonly ECPoint NPoint = Curve.DecodePoint(
        Convert.FromHexString("03d8bbd6c639c62937b04d997f38c3770719c629d7014d49a24b4f98baa1292b49"));

    public static byte[] Encode(ECPoint point) => point.Normalize().GetEncoded(compressed: false);

    /// <summary>
    /// Decodes and validates a received public share: correct length,
    /// on-curve (DecodePoint enforces the curve equation), and not the
    /// identity. P-256 has cofactor 1, so no subgroup check is needed.
    /// </summary>
    public static ECPoint DecodeShare(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length != PointLength || encoded[0] != 0x04)
        {
            throw new Spake2PlusException("Peer share is not a 65-byte uncompressed P-256 point.");
        }

        ECPoint point;
        try
        {
            point = Curve.DecodePoint(encoded.ToArray());
        }
        catch (Exception e)
        {
            throw new Spake2PlusException("Peer share is not on the curve.", e);
        }

        if (point.IsInfinity)
        {
            throw new Spake2PlusException("Peer share is the identity point.");
        }

        return point;
    }

    public static BigInteger ToScalar(ReadOnlySpan<byte> bytes) => new(1, bytes.ToArray());

    public static byte[] EncodeScalar(BigInteger value)
    {
        var bytes = value.ToByteArrayUnsigned();
        if (bytes.Length == ScalarLength)
        {
            return bytes;
        }

        var padded = new byte[ScalarLength];
        bytes.CopyTo(padded, ScalarLength - bytes.Length);
        return padded;
    }

    public static byte[] RandomScalar()
    {
        // Rejection-sample below the group order; zero is rejected.
        Span<byte> candidate = stackalloc byte[ScalarLength];
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

    /// <summary>
    /// The protocol transcript TT per RFC 9383 §3.3: each field prefixed by
    /// its length as an eight-byte little-endian integer. Zero-length fields
    /// (empty identities) still contribute their length prefix.
    /// </summary>
    public static byte[] Transcript(
        byte[] context, byte[] idProver, byte[] idVerifier,
        byte[] shareP, byte[] shareV, byte[] z, byte[] v, byte[] w0)
    {
        var m = Encode(M);
        var n = Encode(NPoint);
        byte[][] fields = [context, idProver, idVerifier, m, n, shareP, shareV, z, v, w0];

        var length = 0;
        foreach (var field in fields)
        {
            length += 8 + field.Length;
        }

        var tt = new byte[length];
        var offset = 0;
        foreach (var field in fields)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(tt.AsSpan(offset), (ulong)field.Length);
            offset += 8;
            field.CopyTo(tt, offset);
            offset += field.Length;
        }

        return tt;
    }

    /// <summary>
    /// RFC 9383 §3.4 key schedule from the transcript hash: confirmation
    /// keys, the peer confirmation MACs, and the shared secret.
    /// </summary>
    public static Spake2PlusKeys DeriveKeys(byte[] transcript, byte[] shareP, byte[] shareV)
    {
        var kMain = SHA256.HashData(transcript);

        var confirmationKeys = HKDF.DeriveKey(
            HashAlgorithmName.SHA256, kMain, 2 * ScalarLength,
            salt: null, info: "ConfirmationKeys"u8.ToArray());
        var kConfirmP = confirmationKeys[..ScalarLength];
        var kConfirmV = confirmationKeys[ScalarLength..];

        return new Spake2PlusKeys(
            ConfirmP: HMACSHA256.HashData(kConfirmP, shareV),
            ConfirmV: HMACSHA256.HashData(kConfirmV, shareP),
            SharedKey: HKDF.DeriveKey(
                HashAlgorithmName.SHA256, kMain, ScalarLength,
                salt: null, info: "SharedKey"u8.ToArray()));
    }
}

internal sealed record Spake2PlusKeys(byte[] ConfirmP, byte[] ConfirmV, byte[] SharedKey);
