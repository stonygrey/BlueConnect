// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Core;
using StonyGrey.BlueConnect.Secure;

namespace StonyGrey.BlueConnect.Smalt;

internal enum ByteRole
{
    Header,
    TypePlain,
    TypeHandshake,
    TypeEncrypted,
    TypeOther,
    Ephemeral,
    EncryptedStatic,
    HandshakePayload,
    Ciphertext,
    Tag,
    Continuation,
}

internal static class ByteRoles
{
    public static readonly (ByteRole Role, string Label, string Color, string Description)[] Legend =
    [
        (ByteRole.Header, "segment header", "#6d28d9", "GATT header: seq in the high nibble, START/END/ABORT in the low bits."),
        (ByteRole.TypePlain, "type 0x00", "#64748b", "Reserved plaintext type — rejected on a live link."),
        (ByteRole.TypeHandshake, "type 0x01", "#0f766e", "Noise XX handshake message."),
        (ByteRole.TypeEncrypted, "type 0x02", "#1d4ed8", "AEAD-sealed application payload."),
        (ByteRole.TypeOther, "type other", "#c026d3", "A type byte that is not 0x00 / 0x01 / 0x02."),
        (ByteRole.Ephemeral, "ephemeral e", "#15803d", "X25519 public ephemeral (32 bytes)."),
        (ByteRole.EncryptedStatic, "encrypted static s", "#c2410c", "Static public key under the handshake AEAD (32 + 16-byte tag)."),
        (ByteRole.HandshakePayload, "handshake payload", "#a16207", "Handshake payload 0x5A plus its 16-byte tag."),
        (ByteRole.Ciphertext, "ciphertext", "#0369a1", "AES-GCM ciphertext (application bytes)."),
        (ByteRole.Tag, "AEAD tag", "#be123c", "16-byte authentication tag."),
        (ByteRole.Continuation, "continuation", "#78716c", "Payload on a segment that is not START — the type byte is on the first segment — or a body that is not a known handshake/sealed layout."),
    ];

    public static ByteRole[] Classify(IoLine io)
    {
        var roles = new ByteRole[io.Bytes.Length];
        if (roles.Length == 0)
        {
            return roles;
        }

        if (io.Kind == IoKind.Segment)
        {
            roles[0] = ByteRole.Header;
            if (roles.Length == 1)
            {
                return roles;
            }

            var flags = SegmentHeader.Flags(io.Bytes[0]);
            if (SegmentHeader.Has(flags, SegmentHeader.Start) && io.Bytes.Length > 1)
            {
                ClassifyMessage(io.Bytes.AsSpan(1), io.Bytes[1]).CopyTo(roles.AsSpan(1));
            }
            else
            {
                roles.AsSpan(1).Fill(ByteRole.Continuation);
            }

            return roles;
        }

        return ClassifyMessage(io.Bytes, io.Type ?? (io.Bytes.Length > 0 ? io.Bytes[0] : null));
    }

    private static ByteRole[] ClassifyMessage(ReadOnlySpan<byte> bytes, byte? typeHint)
    {
        var roles = new ByteRole[bytes.Length];
        roles.AsSpan().Fill(ByteRole.Continuation);
        if (bytes.Length == 0)
        {
            return roles;
        }

        var type = typeHint ?? bytes[0];
        roles[0] = type switch
        {
            FrameType.Plaintext => ByteRole.TypePlain,
            FrameType.Handshake => ByteRole.TypeHandshake,
            FrameType.Encrypted => ByteRole.TypeEncrypted,
            _ => ByteRole.TypeOther,
        };

        var body = bytes[1..];
        var bodyRoles = type switch
        {
            FrameType.Handshake => ClassifyHandshake(body),
            FrameType.Encrypted => ClassifySealed(body),
            _ => Enumerable.Repeat(ByteRole.Continuation, body.Length).ToArray(),
        };
        bodyRoles.CopyTo(roles.AsSpan(1));
        return roles;
    }

    private static ByteRole[] ClassifyHandshake(ReadOnlySpan<byte> body)
    {
        var roles = new ByteRole[body.Length];
        roles.AsSpan().Fill(ByteRole.Continuation);

        // msg 1: e (32)
        if (body.Length == SecureLink.HandshakeMessage1Length)
        {
            Fill(roles, 0, 32, ByteRole.Ephemeral);
            return roles;
        }

        // msg 2: e (32) + enc(s) (48) + enc(payload) (17)
        if (body.Length == SecureLink.HandshakeMessage2Length)
        {
            Fill(roles, 0, 32, ByteRole.Ephemeral);
            Fill(roles, 32, 32, ByteRole.EncryptedStatic);
            Fill(roles, 64, 16, ByteRole.Tag);
            Fill(roles, 80, 1, ByteRole.HandshakePayload);
            Fill(roles, 81, 16, ByteRole.Tag);
            return roles;
        }

        // msg 3: enc(s) (48) + enc(payload) (17)
        if (body.Length == SecureLink.HandshakeMessage3Length)
        {
            Fill(roles, 0, 32, ByteRole.EncryptedStatic);
            Fill(roles, 32, 16, ByteRole.Tag);
            Fill(roles, 48, 1, ByteRole.HandshakePayload);
            Fill(roles, 49, 16, ByteRole.Tag);
        }

        return roles;
    }

    private static ByteRole[] ClassifySealed(ReadOnlySpan<byte> body)
    {
        var roles = new ByteRole[body.Length];
        if (body.Length >= LinkProtocol.TagLength)
        {
            Fill(roles, 0, body.Length - LinkProtocol.TagLength, ByteRole.Ciphertext);
            Fill(roles, body.Length - LinkProtocol.TagLength, LinkProtocol.TagLength, ByteRole.Tag);
        }
        else
        {
            roles.AsSpan().Fill(ByteRole.Continuation);
        }

        return roles;
    }

    private static void Fill(ByteRole[] roles, int start, int count, ByteRole role)
    {
        var end = Math.Min(roles.Length, start + count);
        for (var i = start; i < end; i++)
        {
            roles[i] = role;
        }
    }
}
