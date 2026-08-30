// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Formats.Cbor;

namespace StonyGrey.Edhoc;

/// <summary>
/// Encoders and parsers for the EDHOC message shapes (RFC 9528 §5), the
/// COSE Enc_structure, and the MAC contexts. Messages are CBOR sequences;
/// each function that parses a peer message first checks for an EDHOC error
/// message in its place and throws <see cref="EdhocErrorReceivedException"/>.
/// </summary>
internal static class EdhocMessageCodec
{
    public sealed record Message1(int Method, int[] Suites, byte[] GX, byte[] CI, byte[]? Ead1);

    /// <summary>
    /// An ID_CRED as received: a kid reference, or a whole credential sent
    /// by value (kccs, RFC 9528 §3.5.3). Exactly one field is set.
    /// </summary>
    public sealed record IdCred(byte[]? Kid, byte[]? Ccs);

    /// <summary>message_1 = ( METHOD, SUITES_I, G_X, C_I, ? EAD_1 ). RFC 9528 §5.2.1.</summary>
    public static byte[] EncodeMessage1(int method, int[] suites, byte[] gX, byte[] cI, byte[]? ead1)
    {
        var writer = EdhocCbor.Writer();
        writer.WriteInt32(method);
        EdhocErrorMessage.WriteSuites(writer, suites);
        writer.WriteByteString(gX);
        return EdhocKeySchedule.Concat(
            writer.Encode(), EdhocCbor.EncodeIdentifier(cI), ead1 ?? []);
    }

    public static Message1 ParseMessage1(ReadOnlyMemory<byte> data) =>
        EdhocCbor.Parse("message_1", () =>
        {
            var reader = EdhocCbor.Reader(data);
            var method = checked((int)reader.ReadInt64());
            var suites = EdhocErrorMessage.ReadSuites(reader);
            var gX = reader.ReadByteString();
            if (gX.Length != EdhocCrypto.CoordinateLength)
            {
                throw new EdhocException("G_X is not a 32-byte x-coordinate.");
            }

            var cI = EdhocCbor.ReadIdentifier(reader);
            var ead1 = EdhocCbor.ReadExternalAuthorizationData(reader);
            return new Message1(method, suites, gX, cI, ead1);
        });

    /// <summary>message_2 = ( G_Y_CIPHERTEXT_2 ): one bstr of G_Y ‖ CIPHERTEXT_2. RFC 9528 §5.3.1.</summary>
    public static byte[] EncodeMessage2(byte[] gY, byte[] ciphertext2) =>
        EdhocCbor.EncodeByteString(EdhocKeySchedule.Concat(gY, ciphertext2));

    public static (byte[] GY, byte[] Ciphertext2) ParseMessage2(ReadOnlyMemory<byte> data) =>
        EdhocCbor.Parse("message_2", () =>
        {
            var reader = EdhocCbor.Reader(data);
            ThrowIfErrorMessage(reader, data);
            var combined = reader.ReadByteString();
            RequireEnd(reader, "message_2");
            if (combined.Length <= EdhocCrypto.CoordinateLength)
            {
                throw new EdhocException("message_2 is too short to hold G_Y and CIPHERTEXT_2.");
            }

            return (combined[..EdhocCrypto.CoordinateLength], combined[EdhocCrypto.CoordinateLength..]);
        });

    /// <summary>PLAINTEXT_2 = ( C_R, ID_CRED_R, Signature_or_MAC_2, ? EAD_2 ). RFC 9528 §5.3.2.
    /// <paramref name="idCred2"/> is pre-encoded: the compact kid form or the kccs map.</summary>
    public static byte[] EncodePlaintext2(byte[] cR, byte[] idCred2, byte[] mac2, byte[]? ead2) =>
        EdhocKeySchedule.Concat(
            EdhocCbor.EncodeIdentifier(cR),
            idCred2,
            EdhocCbor.EncodeByteString(mac2),
            ead2 ?? []);

    public static (byte[] CR, IdCred IdCredR, byte[] Mac2, byte[]? Ead2) ParsePlaintext2(ReadOnlyMemory<byte> data) =>
        EdhocCbor.Parse("PLAINTEXT_2", () =>
        {
            var reader = EdhocCbor.Reader(data);
            var cR = EdhocCbor.ReadIdentifier(reader);
            var idCred = ReadIdCred(reader);
            var mac2 = ReadMac(reader, "MAC_2");
            var ead2 = EdhocCbor.ReadExternalAuthorizationData(reader);
            return (cR, idCred, mac2, ead2);
        });

    /// <summary>message_3 = ( CIPHERTEXT_3 ) and message_4 = ( CIPHERTEXT_4 ): one bstr.</summary>
    public static byte[] EncodeCiphertextOnly(byte[] ciphertext) =>
        EdhocCbor.EncodeByteString(ciphertext);

    public static byte[] ParseCiphertextOnly(ReadOnlyMemory<byte> data, string what) =>
        EdhocCbor.Parse(what, () =>
        {
            var reader = EdhocCbor.Reader(data);
            ThrowIfErrorMessage(reader, data);
            var ciphertext = reader.ReadByteString();
            RequireEnd(reader, what);
            if (ciphertext.Length < EdhocCrypto.AeadTagLength)
            {
                throw new EdhocException($"{what} is shorter than the AEAD tag.");
            }

            return ciphertext;
        });

    /// <summary>PLAINTEXT_3 = ( ID_CRED_I, Signature_or_MAC_3, ? EAD_3 ). RFC 9528 §5.4.2.
    /// <paramref name="idCred3"/> is pre-encoded: the compact kid form or the kccs map.</summary>
    public static byte[] EncodePlaintext3(byte[] idCred3, byte[] mac3, byte[]? ead3) =>
        EdhocKeySchedule.Concat(
            idCred3,
            EdhocCbor.EncodeByteString(mac3),
            ead3 ?? []);

    public static (IdCred IdCredI, byte[] Mac3, byte[]? Ead3) ParsePlaintext3(ReadOnlyMemory<byte> data) =>
        EdhocCbor.Parse("PLAINTEXT_3", () =>
        {
            var reader = EdhocCbor.Reader(data);
            var idCred = ReadIdCred(reader);
            var mac3 = ReadMac(reader, "MAC_3");
            var ead3 = EdhocCbor.ReadExternalAuthorizationData(reader);
            return (idCred, mac3, ead3);
        });

    /// <summary>PLAINTEXT_4 = ( ? EAD_4 ). RFC 9528 §5.5.2.</summary>
    public static byte[]? ParsePlaintext4(ReadOnlyMemory<byte> data) =>
        EdhocCbor.Parse("PLAINTEXT_4", () =>
            EdhocCbor.ReadExternalAuthorizationData(EdhocCbor.Reader(data)));

    /// <summary>COSE Enc_structure [ "Encrypt0", h'', TH_x ] — the AEAD external AAD.</summary>
    public static byte[] EncStructure(byte[] transcriptHash)
    {
        var writer = EdhocCbor.Writer();
        writer.WriteStartArray(3);
        writer.WriteTextString("Encrypt0");
        writer.WriteByteString([]);
        writer.WriteByteString(transcriptHash);
        writer.WriteEndArray();
        return writer.Encode();
    }

    /// <summary>The full ID_CRED map { 4 : kid } as used in the MAC contexts (not compact).</summary>
    public static byte[] EncodeIdCredMap(ReadOnlyMemory<byte> kid)
    {
        var writer = EdhocCbor.Writer();
        writer.WriteStartMap(1);
        writer.WriteInt32(4);
        writer.WriteByteString(kid.Span);
        writer.WriteEndMap();
        return writer.Encode();
    }

    /// <summary>The compact ID_CRED form for a kid, as sent in a plaintext.</summary>
    public static byte[] EncodeIdCredCompact(ReadOnlyMemory<byte> kid) =>
        EdhocCbor.EncodeIdentifier(kid.Span);

    /// <summary>
    /// ID_CRED as { 14 : CCS } — the credential sent by value (kccs). The
    /// same bytes serve the plaintext and the MAC context.
    /// </summary>
    public static byte[] EncodeIdCredKccs(ReadOnlyMemory<byte> ccs)
    {
        var writer = EdhocCbor.Writer();
        writer.WriteStartMap(1);
        writer.WriteInt32(14);
        writer.WriteEncodedValue(ccs.Span);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static IdCred ReadIdCred(System.Formats.Cbor.CborReader reader)
    {
        if (reader.PeekState() != System.Formats.Cbor.CborReaderState.StartMap)
        {
            return new IdCred(EdhocCbor.ReadIdentifier(reader), null);
        }

        var count = reader.ReadStartMap();
        if (count != 1)
        {
            throw new EdhocException("ID_CRED map must have exactly one entry.");
        }

        var label = reader.ReadInt64();
        if (label != 14)
        {
            // A kid must arrive in its compact form; { 4 : kid } in a
            // plaintext is a surplus encoding and label 4 never carries a
            // whole credential. Only kccs ({ 14 : CCS }) is a legal map.
            throw new EdhocException($"ID_CRED map label {label} is not supported (kccs only).");
        }

        var result = new IdCred(null, reader.ReadEncodedValue().ToArray());
        reader.ReadEndMap();
        return result;
    }

    /// <summary>context_2 = &lt;&lt; C_R, ID_CRED_R, TH_2, CRED_R, ? EAD_2 &gt;&gt;. RFC 9528 §5.3.2.
    /// <paramref name="idCredFull2"/> is the full-form ID_CRED bytes ({4: kid} or {14: CCS}).</summary>
    public static byte[] Context2(
        byte[] cR, byte[] idCredFull2, byte[] th2, ReadOnlyMemory<byte> credR, byte[]? ead2) =>
        EdhocKeySchedule.Concat(
            EdhocCbor.EncodeIdentifier(cR),
            idCredFull2,
            EdhocCbor.EncodeByteString(th2),
            credR.ToArray(),
            ead2 ?? []);

    /// <summary>context_3 = &lt;&lt; ID_CRED_I, TH_3, CRED_I, ? EAD_3 &gt;&gt;. RFC 9528 §5.4.2.
    /// <paramref name="idCredFull3"/> is the full-form ID_CRED bytes ({4: kid} or {14: CCS}).</summary>
    public static byte[] Context3(
        byte[] idCredFull3, byte[] th3, ReadOnlyMemory<byte> credI, byte[]? ead3) =>
        EdhocKeySchedule.Concat(
            idCredFull3,
            EdhocCbor.EncodeByteString(th3),
            credI.ToArray(),
            ead3 ?? []);

    private static byte[] ReadMac(CborReader reader, string what)
    {
        var mac = reader.ReadByteString();
        if (mac.Length != EdhocCrypto.MacLength)
        {
            throw new EdhocException($"{what} is not {EdhocCrypto.MacLength} bytes.");
        }

        return mac;
    }

    private static void ThrowIfErrorMessage(CborReader reader, ReadOnlyMemory<byte> data)
    {
        if (reader.PeekState() is not (CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger))
        {
            return;
        }

        if (EdhocErrorMessage.TryParse(data.Span, out var code, out var diagnostic, out var suites))
        {
            throw new EdhocErrorReceivedException(code, diagnostic, suites);
        }

        throw new EdhocException("Peer sent an int where a byte string was expected.");
    }

    private static void RequireEnd(CborReader reader, string what)
    {
        if (reader.PeekState() != CborReaderState.Finished)
        {
            throw new EdhocException($"{what} has trailing data.");
        }
    }
}
