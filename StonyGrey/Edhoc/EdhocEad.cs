// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Edhoc;

/// <summary>
/// Helpers for External Authorization Data items (RFC 9528 §3.8): encode a
/// bare non-critical label, and test a received EAD sequence for one.
/// </summary>
public static class EdhocEad
{
    /// <summary>Encodes a single value-less, non-critical EAD item.</summary>
    public static byte[] EncodeLabel(int label)
    {
        if (label <= 0)
        {
            throw new EdhocException("Only positive (non-critical) EAD labels are supported.");
        }

        return EdhocCbor.EncodeInt(label);
    }

    /// <summary>True when the EAD sequence contains an item with the label.</summary>
    public static bool ContainsLabel(ReadOnlyMemory<byte>? ead, int label)
    {
        if (ead is null || ead.Value.Length == 0)
        {
            return false;
        }

        var reader = EdhocCbor.Reader(ead.Value);
        return EdhocCbor.Parse("EAD", () =>
        {
            while (reader.PeekState() != System.Formats.Cbor.CborReaderState.Finished)
            {
                var found = reader.ReadInt64();
                if (reader.PeekState() == System.Formats.Cbor.CborReaderState.ByteString)
                {
                    reader.ReadByteString();
                }

                if (found == label)
                {
                    return true;
                }
            }

            return false;
        });
    }
}
