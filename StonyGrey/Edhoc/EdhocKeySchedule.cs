// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

namespace StonyGrey.Edhoc;

/// <summary>
/// The RFC 9528 §4 key schedule for cipher suite 2, shared by both roles.
/// EDHOC_Extract is HMAC-SHA-256; EDHOC_KDF is HKDF-Expand with the CBOR
/// info sequence ( label, context, length ).
/// </summary>
internal static class EdhocKeySchedule
{
    // EDHOC_KDF info labels, RFC 9528 §4.1.2 Table 1.
    public const int LabelKeystream2 = 0;
    public const int LabelSalt3e2m = 1;
    public const int LabelMac2 = 2;
    public const int LabelK3 = 3;
    public const int LabelIv3 = 4;
    public const int LabelSalt4e3m = 5;
    public const int LabelMac3 = 6;
    public const int LabelPrkOut = 7;
    public const int LabelK4 = 8;
    public const int LabelIv4 = 9;
    public const int LabelPrkExporter = 10;

    /// <summary>EDHOC_Extract( salt, IKM ) = HMAC-SHA-256( salt, IKM ). RFC 9528 §4.1.1.</summary>
    public static byte[] Extract(byte[] salt, byte[] ikm) => HMACSHA256.HashData(salt, ikm);

    /// <summary>
    /// EDHOC_KDF( PRK, label, context, length ) = HKDF-Expand( PRK, info, length )
    /// with info = ( label, bstr context, length ) as a CBOR sequence. RFC 9528 §4.1.2.
    /// </summary>
    public static byte[] Kdf(byte[] prk, int label, ReadOnlySpan<byte> context, int length)
    {
        var info = Concat(
            EdhocCbor.EncodeInt(label),
            EdhocCbor.EncodeByteString(context),
            EdhocCbor.EncodeInt(length));

        var output = new byte[length];
        HKDF.Expand(HashAlgorithmName.SHA256, prk, output, info);
        return output;
    }

    /// <summary>TH_2 = H( G_Y, H(message_1) ), both as CBOR bstrs. RFC 9528 §5.3.2.</summary>
    public static byte[] TranscriptHash2(byte[] gY, byte[] message1) =>
        SHA256.HashData(Concat(
            EdhocCbor.EncodeByteString(gY),
            EdhocCbor.EncodeByteString(SHA256.HashData(message1))));

    /// <summary>TH_3 = H( TH_2, PLAINTEXT_2, CRED_R ). RFC 9528 §5.4.2.</summary>
    public static byte[] TranscriptHash3(byte[] th2, byte[] plaintext2, ReadOnlyMemory<byte> credR) =>
        SHA256.HashData(Concat(EdhocCbor.EncodeByteString(th2), plaintext2, credR.ToArray()));

    /// <summary>TH_4 = H( TH_3, PLAINTEXT_3, CRED_I ). RFC 9528 §5.5.2.</summary>
    public static byte[] TranscriptHash4(byte[] th3, byte[] plaintext3, ReadOnlyMemory<byte> credI) =>
        SHA256.HashData(Concat(EdhocCbor.EncodeByteString(th3), plaintext3, credI.ToArray()));

    public static byte[] Concat(params byte[][] parts)
    {
        var length = 0;
        foreach (var part in parts)
        {
            length += part.Length;
        }

        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
