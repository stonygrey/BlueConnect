// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.BlueConnect.Secure;

/// <summary>
/// The secure link's type dialect — the first byte of a reassembled message.
/// Segment framing does not interpret these.
/// </summary>
public static class FrameType
{
    /// <summary>Reserved. Application payloads must be sealed; 0x00 is rejected.</summary>
    public const byte Plaintext = 0x00;

    /// <summary>Noise XX handshake message.</summary>
    public const byte Handshake = 0x01;

    /// <summary>AEAD-sealed application payload.</summary>
    public const byte Encrypted = 0x02;

    /// <summary>SPAKE2+ handshake message (the PAKE firmware's dialect).</summary>
    public const byte PakeHandshake = 0x03;

    /// <summary>EDHOC handshake message (the EDHOC firmware's dialect).</summary>
    public const byte EdhocHandshake = 0x04;

    public static byte[] Bundle(byte type, ReadOnlySpan<byte> payload)
    {
        var message = new byte[1 + payload.Length];
        message[0] = type;
        payload.CopyTo(message.AsSpan(1));
        return message;
    }

    public static (byte Type, byte[] Payload) Split(ReadOnlySpan<byte> message)
    {
        if (message.Length < 1)
        {
            throw new Abstractions.BleTransportException("Empty message; missing type byte.");
        }

        return (message[0], message[1..].ToArray());
    }
}
