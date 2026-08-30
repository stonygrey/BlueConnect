// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Formats.Cbor;

using Org.BouncyCastle.Math;

using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;

namespace StonyGrey.Edhoc;

/// <summary>
/// An EDHOC authentication credential: a raw P-256 public key wrapped in a
/// CWT Claims Set (CCS, RFC 9528 §3.5.2) and identified by the COSE 'kid'
/// header parameter. The CCS bytes are kept verbatim — CRED_I/CRED_R
/// participate byte-exactly in the transcript hashes and MAC contexts, so a
/// credential must never be re-encoded once issued.
/// </summary>
public sealed class EdhocCredential
{
    private EdhocCredential(byte[] credentialBytes, byte[] kid, ECPoint publicPoint, BigInteger? privateScalar)
    {
        CredentialBytes = credentialBytes;
        Kid = kid;
        PublicPoint = publicPoint;
        PrivateScalar = privateScalar;
    }

    /// <summary>The verbatim CCS bytes (CRED_x).</summary>
    public ReadOnlyMemory<byte> CredentialBytes { get; }

    /// <summary>The COSE 'kid' value that identifies this credential (ID_CRED_x).</summary>
    public ReadOnlyMemory<byte> Kid { get; }

    public bool HasPrivateKey => PrivateScalar is not null;

    /// <summary>The static DH private key, when held (own credentials only).</summary>
    public ReadOnlyMemory<byte>? PrivateKey =>
        PrivateScalar is null ? null : Pad32(PrivateScalar.ToByteArrayUnsigned());

    internal ECPoint PublicPoint { get; }

    internal BigInteger? PrivateScalar { get; }

    /// <summary>
    /// Parses a CCS (CBOR map with a 'cnf' claim carrying an EC2 P-256
    /// COSE_Key) and keeps the input bytes verbatim. RFC 9528 §3.5.2.
    /// </summary>
    public static EdhocCredential FromCcs(ReadOnlySpan<byte> ccsBytes)
    {
        var credential = ccsBytes.ToArray();
        return EdhocCbor.Parse("CCS", () => ParseCcs(credential));
    }

    /// <summary>
    /// Builds a deterministically encoded CCS for a public key. Meant for
    /// issuing credentials at provisioning time; the output bytes are what
    /// both sides must then use verbatim.
    /// </summary>
    public static EdhocCredential CreateCcs(
        ReadOnlySpan<byte> kid, string? subject,
        ReadOnlySpan<byte> publicKeyX, ReadOnlySpan<byte> publicKeyY)
    {
        if (kid.IsEmpty)
        {
            throw new EdhocException("A credential requires a non-empty kid.");
        }

        // Validates the coordinates before anything is encoded.
        _ = EdhocCrypto.DecodePoint(publicKeyX, publicKeyY);

        var writer = EdhocCbor.Writer();
        writer.WriteStartMap(subject is null ? 1 : 2);
        if (subject is not null)
        {
            writer.WriteInt32(2);                    // sub
            writer.WriteTextString(subject);
        }

        writer.WriteInt32(8);                        // cnf
        writer.WriteStartMap(1);
        writer.WriteInt32(1);                        // COSE_Key
        writer.WriteStartMap(5);
        writer.WriteInt32(1);                        // kty
        writer.WriteInt32(2);                        // EC2
        writer.WriteInt32(2);                        // kid
        writer.WriteByteString(kid);
        writer.WriteInt32(-1);                       // crv
        writer.WriteInt32(1);                        // P-256
        writer.WriteInt32(-2);                       // x
        writer.WriteByteString(publicKeyX);
        writer.WriteInt32(-3);                       // y
        writer.WriteByteString(publicKeyY);
        writer.WriteEndMap();
        writer.WriteEndMap();
        writer.WriteEndMap();

        return FromCcs(writer.Encode());
    }

    /// <summary>
    /// Generates a fresh static DH key pair and issues its CCS. The returned
    /// credential carries the private key.
    /// </summary>
    public static EdhocCredential Generate(ReadOnlySpan<byte> kid, string? subject) =>
        FromPrivateKey(kid, subject, EdhocCrypto.RandomScalar());

    /// <summary>
    /// Rebuilds the deterministic CCS for an existing private key. Both sides
    /// of a provisioning relationship can derive the identical CRED_x bytes
    /// from the key alone, as long as kid and subject match.
    /// </summary>
    public static EdhocCredential FromPrivateKey(
        ReadOnlySpan<byte> kid, string? subject, ReadOnlySpan<byte> privateKey)
    {
        var scalar = new BigInteger(1, privateKey.ToArray());
        if (scalar.SignValue <= 0 || scalar.CompareTo(EdhocCrypto.N) >= 0)
        {
            throw new EdhocException("Private key is not a valid P-256 scalar.");
        }

        var point = EdhocCrypto.PublicPoint(scalar);
        return CreateCcs(
                kid, subject, EdhocCrypto.XCoordinate(point), EdhocCrypto.YCoordinate(point))
            .WithScalar(scalar);
    }

    /// <summary>
    /// Returns a copy of this credential carrying the static DH private key.
    /// Throws when the key does not match the credential's public key.
    /// </summary>
    public EdhocCredential WithPrivateKey(ReadOnlySpan<byte> privateKey)
    {
        var scalar = new BigInteger(1, privateKey.ToArray());
        if (scalar.SignValue <= 0 || scalar.CompareTo(EdhocCrypto.N) >= 0)
        {
            throw new EdhocException("Private key is not a valid P-256 scalar.");
        }

        if (!EdhocCrypto.PublicPoint(scalar).Equals(PublicPoint))
        {
            throw new EdhocException("Private key does not match the credential's public key.");
        }

        return WithScalar(scalar);
    }

    private EdhocCredential WithScalar(BigInteger scalar) =>
        new(CredentialBytes.ToArray(), Kid.ToArray(), PublicPoint, scalar);

    private static EdhocCredential ParseCcs(byte[] credential)
    {
        var reader = EdhocCbor.Reader(credential);

        byte[]? kid = null;
        byte[]? x = null;
        byte[]? y = null;
        var sawConfirmationKey = false;

        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            if (reader.ReadInt64() != 8)                 // cnf
            {
                reader.SkipValue();
                continue;
            }

            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                if (reader.ReadInt64() != 1)             // COSE_Key
                {
                    reader.SkipValue();
                    continue;
                }

                sawConfirmationKey = true;
                reader.ReadStartMap();
                while (reader.PeekState() != CborReaderState.EndMap)
                {
                    switch (reader.ReadInt64())
                    {
                        case 1:                          // kty
                            if (reader.ReadInt64() != 2)
                            {
                                throw new EdhocException("CCS key type is not EC2.");
                            }

                            break;
                        case 2:
                            kid = reader.ReadByteString();
                            break;
                        case -1:                         // crv
                            if (reader.ReadInt64() != 1)
                            {
                                throw new EdhocException("CCS curve is not P-256.");
                            }

                            break;
                        case -2:
                            x = reader.ReadByteString();
                            break;
                        case -3:
                            y = reader.ReadByteString();
                            break;
                        default:
                            reader.SkipValue();
                            break;
                    }
                }

                reader.ReadEndMap();
            }

            reader.ReadEndMap();
        }

        reader.ReadEndMap();
        if (reader.PeekState() != CborReaderState.Finished)
        {
            throw new EdhocException("CCS has trailing data.");
        }

        if (!sawConfirmationKey || kid is null || x is null || y is null)
        {
            throw new EdhocException("CCS lacks a cnf COSE_Key with kid, x, and y.");
        }

        if (kid.Length == 0)
        {
            throw new EdhocException("A credential requires a non-empty kid.");
        }

        return new EdhocCredential(credential, kid, EdhocCrypto.DecodePoint(x, y), null);
    }

    private static byte[] Pad32(byte[] bytes)
    {
        if (bytes.Length == 32)
        {
            return bytes;
        }

        var padded = new byte[32];
        bytes.CopyTo(padded, 32 - bytes.Length);
        return padded;
    }
}
