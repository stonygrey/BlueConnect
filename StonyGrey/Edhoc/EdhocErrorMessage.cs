// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Formats.Cbor;

namespace StonyGrey.Edhoc;

/// <summary>
/// EDHOC error messages (RFC 9528 §6): the CBOR sequence ( ERR_CODE,
/// ERR_INFO ). A transport can emit these independently of a session, and the
/// state machines throw <see cref="EdhocErrorReceivedException"/> when one
/// arrives in place of a protocol message.
/// </summary>
public static class EdhocErrorMessage
{
    /// <summary>An unspecified error (ERR_CODE 1) with a diagnostic text. RFC 9528 §6.2.</summary>
    public static byte[] Create(string diagnostic)
    {
        var writer = EdhocCbor.Writer();
        writer.WriteInt32(1);
        writer.WriteTextString(diagnostic);
        return writer.Encode();
    }

    /// <summary>
    /// A wrong-cipher-suite error (ERR_CODE 2) carrying SUITES_R in the
    /// responder's order of preference. RFC 9528 §6.3.
    /// </summary>
    public static byte[] CreateWrongSuites(params int[] suitesR)
    {
        if (suitesR.Length == 0)
        {
            throw new EdhocException("SUITES_R must name at least one cipher suite.");
        }

        var writer = EdhocCbor.Writer();
        writer.WriteInt32(2);
        WriteSuites(writer, suitesR);
        return writer.Encode();
    }

    /// <summary>
    /// Attempts to parse an error message. Returns false when the bytes do not
    /// form one; unknown ERR_CODEs parse with null info fields.
    /// </summary>
    public static bool TryParse(
        ReadOnlySpan<byte> data, out int errorCode, out string? diagnostic, out int[]? supportedSuites)
    {
        errorCode = 0;
        diagnostic = null;
        supportedSuites = null;

        try
        {
            var reader = EdhocCbor.Reader(data.ToArray());
            if (reader.PeekState() is not (CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger))
            {
                return false;
            }

            errorCode = checked((int)reader.ReadInt64());
            switch (errorCode)
            {
                case 1:
                    diagnostic = reader.ReadTextString();
                    break;
                case 2:
                    supportedSuites = ReadSuites(reader);
                    break;
                default:
                    while (reader.PeekState() != CborReaderState.Finished)
                    {
                        reader.SkipValue();
                    }

                    break;
            }

            return reader.PeekState() == CborReaderState.Finished;
        }
        catch (Exception e) when (e is CborContentException or InvalidOperationException or OverflowException)
        {
            return false;
        }
    }

    internal static void WriteSuites(CborWriter writer, int[] suites)
    {
        if (suites.Length == 1)
        {
            writer.WriteInt32(suites[0]);
            return;
        }

        writer.WriteStartArray(suites.Length);
        foreach (var suite in suites)
        {
            writer.WriteInt32(suite);
        }

        writer.WriteEndArray();
    }

    internal static int[] ReadSuites(CborReader reader)
    {
        if (reader.PeekState() is CborReaderState.UnsignedInteger or CborReaderState.NegativeInteger)
        {
            return [checked((int)reader.ReadInt64())];
        }

        if (reader.PeekState() != CborReaderState.StartArray)
        {
            throw new EdhocException("Cipher suites are neither an int nor an array.");
        }

        reader.ReadStartArray();
        var suites = new List<int>();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            suites.Add(checked((int)reader.ReadInt64()));
        }

        reader.ReadEndArray();
        if (suites.Count < 2)
        {
            // RFC 9529 §4.1.3: a one-element array is a surplus encoding.
            throw new EdhocException("Cipher suite array must hold at least two entries.");
        }

        return [.. suites];
    }
}
