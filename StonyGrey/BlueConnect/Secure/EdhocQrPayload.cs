// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Buffers.Text;

using StonyGrey.Edhoc;
using StonyGrey.Spake2Plus;

namespace StonyGrey.BlueConnect.Secure;

/// <summary>
/// What the printed EDHOC QR carries:
/// <c>SGEK1:&lt;L|H&gt;:&lt;base64url blob&gt;[:&lt;NAME&gt;]</c>, where the blob is
/// initiator private key (32) ‖ initiator kid (1) ‖ device CCS length (1) ‖
/// device CCS. The prefix is the format version; parsers reject unknown
/// prefixes. Like the PAKE setup code, the QR IS the credential — it carries
/// the slot's private key, so it grants access to whoever scans it.
/// </summary>
public sealed record EdhocQrPayload(
    PakeTier Tier, byte[] InitiatorPrivateKey, byte[] DeviceCcs, string? DeviceName)
{
    private const string Prefix = "SGEK1";
    private const int PrivateKeyLength = 32;
    private const int MaxCcsLength = 255;

    public byte InitiatorKid => Tier == PakeTier.High ? BlueConnectEdhoc.HighKid : BlueConnectEdhoc.LowKid;

    /// <summary>The link-security settings this payload provisions.</summary>
    public EdhocSecurity ToSecurity() => new(InitiatorPrivateKey, InitiatorKid, DeviceCcs);

    public string Encode()
    {
        if (InitiatorPrivateKey.Length != PrivateKeyLength || DeviceCcs.Length is 0 or > MaxCcsLength)
        {
            throw new LinkSecurityException("EDHOC QR payload fields are out of range.");
        }

        var blob = new byte[PrivateKeyLength + 2 + DeviceCcs.Length];
        InitiatorPrivateKey.CopyTo(blob, 0);
        blob[PrivateKeyLength] = InitiatorKid;
        blob[PrivateKeyLength + 1] = (byte)DeviceCcs.Length;
        DeviceCcs.CopyTo(blob, PrivateKeyLength + 2);

        var tier = Tier == PakeTier.High ? "H" : "L";
        var encoded = Base64Url.EncodeToString(blob);
        return DeviceName is { Length: > 0 }
            ? $"{Prefix}:{tier}:{encoded}:{DeviceName}"
            : $"{Prefix}:{tier}:{encoded}";
    }

    public static bool TryParse(string text, out EdhocQrPayload? payload)
    {
        payload = null;

        var parts = text.Trim().Split(':', 4);
        if (parts.Length < 3 || parts[0] != Prefix)
        {
            return false;
        }

        PakeTier tier;
        switch (parts[1])
        {
            case "L":
                tier = PakeTier.Low;
                break;
            case "H":
                tier = PakeTier.High;
                break;
            default:
                return false;
        }

        byte[] blob;
        try
        {
            blob = Base64Url.DecodeFromChars(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (blob.Length < PrivateKeyLength + 2)
        {
            return false;
        }

        var kid = blob[PrivateKeyLength];
        var ccsLength = blob[PrivateKeyLength + 1];
        if (blob.Length != PrivateKeyLength + 2 + ccsLength)
        {
            return false;
        }

        var expectedKid = tier == PakeTier.High ? BlueConnectEdhoc.HighKid : BlueConnectEdhoc.LowKid;
        if (kid != expectedKid)
        {
            return false;
        }

        var privateKey = blob[..PrivateKeyLength];
        var deviceCcs = blob[(PrivateKeyLength + 2)..];
        try
        {
            // The blob must hold a usable key pair and a parseable device CCS.
            _ = BlueConnectEdhoc.InitiatorCredential(privateKey, kid);
            _ = EdhocCredential.FromCcs(deviceCcs);
        }
        catch (EdhocException)
        {
            return false;
        }

        payload = new EdhocQrPayload(tier, privateKey, deviceCcs, parts.Length == 4 ? parts[3] : null);
        return true;
    }
}
