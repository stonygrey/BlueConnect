// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Formats.Cbor;

namespace StonyGrey.Edhoc;

/// <summary>
/// Deterministic-CBOR helpers for the EDHOC message dialect. EDHOC messages
/// are CBOR <em>sequences</em> (RFC 8742), not single items, so messages are
/// built by concatenating individually encoded items and parsed with a reader
/// allowing multiple root-level values. Encoding follows the RFC 8949
/// deterministic rules required by RFC 9528 §3.1.
/// </summary>
internal static class EdhocCbor
{
    /// <summary>Reader over a message or CBOR sequence fragment. Canonical mode
    /// rejects indefinite lengths and non-shortest integer forms.</summary>
    public static CborReader Reader(ReadOnlyMemory<byte> data) =>
        new(data, CborConformanceMode.Canonical, allowMultipleRootLevelValues: true);

    public static CborWriter Writer() =>
        new(CborConformanceMode.Ctap2Canonical, allowMultipleRootLevelValues: true);

    public static byte[] EncodeInt(long value)
    {
        var writer = Writer();
        writer.WriteInt64(value);
        return writer.Encode();
    }

    public static byte[] EncodeByteString(ReadOnlySpan<byte> value)
    {
        var writer = Writer();
        writer.WriteByteString(value);
        return writer.Encode();
    }

    /// <summary>
    /// Encodes a connection identifier or kid per RFC 9528 §3.3.2: a one-byte
    /// byte string whose byte is also a valid one-byte CBOR int encoding
    /// (0x00–0x17 for 0..23, 0x20–0x37 for -1..-24) MUST be sent as that int —
    /// the raw byte is its own encoding. Anything else is sent as a bstr.
    /// </summary>
    public static byte[] EncodeIdentifier(ReadOnlySpan<byte> raw) =>
        IsIdentityEncodable(raw) ? [raw[0]] : EncodeByteString(raw);

    public static bool IsIdentityEncodable(ReadOnlySpan<byte> raw) =>
        raw.Length == 1 && (raw[0] <= 0x17 || (raw[0] >= 0x20 && raw[0] <= 0x37));

    /// <summary>
    /// Reads a connection identifier or compact kid: a CBOR int in -24..23
    /// yields its own encoding byte as the raw value; a bstr yields its
    /// contents. A bstr that should have been int-encoded is rejected
    /// (RFC 9529 §4.1.2 lists it as an invalid encoding).
    /// </summary>
    public static byte[] ReadIdentifier(CborReader reader)
    {
        switch (reader.PeekState())
        {
            case CborReaderState.UnsignedInteger:
            case CborReaderState.NegativeInteger:
                var value = reader.ReadInt64();
                if (value is < -24 or > 23)
                {
                    throw new EdhocException("Identifier int is outside -24..23.");
                }

                return [value >= 0 ? (byte)value : (byte)(0x20 + (-value - 1))];

            case CborReaderState.ByteString:
                var bytes = reader.ReadByteString();
                if (IsIdentityEncodable(bytes))
                {
                    throw new EdhocException("Identifier uses a surplus bstr encoding.");
                }

                return bytes;

            default:
                throw new EdhocException("Identifier is neither an int nor a byte string.");
        }
    }

    /// <summary>
    /// Reads the trailing EAD items of a message or plaintext as one raw CBOR
    /// sequence. Critical items (negative ead_label, RFC 9528 §3.8) are
    /// unsupported and rejected; padding (label 0) and non-critical items are
    /// passed through opaquely. Returns null when there are none.
    /// </summary>
    public static byte[]? ReadExternalAuthorizationData(CborReader reader)
    {
        if (reader.PeekState() == CborReaderState.Finished)
        {
            return null;
        }

        var writer = Writer();
        while (reader.PeekState() != CborReaderState.Finished)
        {
            var state = reader.PeekState();
            if (state is not (CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger))
            {
                throw new EdhocException("EAD item does not start with an int label.");
            }

            var label = reader.ReadInt64();
            if (label < 0)
            {
                throw new EdhocException($"EAD item with critical label {label} is not supported.");
            }

            writer.WriteInt64(label);
            if (reader.PeekState() == CborReaderState.ByteString)
            {
                writer.WriteByteString(reader.ReadByteString());
            }
        }

        return writer.Encode();
    }

    /// <summary>Validates caller-supplied EAD bytes by round-tripping the parse.</summary>
    public static byte[]? ValidateExternalAuthorizationData(byte[]? ead)
    {
        if (ead is null || ead.Length == 0)
        {
            return null;
        }

        try
        {
            return ReadExternalAuthorizationData(Reader(ead));
        }
        catch (Exception e) when (e is not EdhocException)
        {
            throw new EdhocException("EAD is not a well-formed CBOR sequence.", e);
        }
    }

    /// <summary>Runs a parse action, normalizing CBOR library failures.</summary>
    public static T Parse<T>(string what, Func<T> parse)
    {
        try
        {
            return parse();
        }
        catch (Exception e) when (
            e is CborContentException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new EdhocException($"{what} is not well-formed deterministic CBOR.", e);
        }
    }
}
