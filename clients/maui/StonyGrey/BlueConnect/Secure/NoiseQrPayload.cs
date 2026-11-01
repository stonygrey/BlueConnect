// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Buffers.Text;

namespace StonyGrey.BlueConnect.Secure;

/// <summary>
/// What a USB-provisioned Noise credential QR carries:
/// <c>SGNK1:&lt;L|H&gt;:&lt;base64url blob&gt;[:&lt;NAME&gt;]</c>, where the blob is
/// the app's X25519 private key (32) ‖ the device's X25519 public key (32).
/// The prefix is the format version; parsers reject unknown prefixes. The
/// QR IS the credential — it carries the slot's private key, so it grants
/// access to whoever scans it.
/// </summary>
public sealed record NoiseQrPayload(
    Tier Tier, byte[] AppPrivateKey, byte[] DevicePublicKey, string? DeviceName)
{
    private const string Prefix = "SGNK1";
    private const int KeyLength = 32;

    public string Encode()
    {
        if (AppPrivateKey.Length != KeyLength || DevicePublicKey.Length != KeyLength ||
            KeyIsZero(AppPrivateKey) || KeyIsZero(DevicePublicKey))
        {
            throw new LinkSecurityException("Noise QR payload fields are out of range.");
        }

        var blob = new byte[2 * KeyLength];
        AppPrivateKey.CopyTo(blob, 0);
        DevicePublicKey.CopyTo(blob, KeyLength);

        var tier = Tier == Tier.High ? "H" : "L";
        var encoded = Base64Url.EncodeToString(blob);
        return DeviceName is { Length: > 0 }
            ? $"{Prefix}:{tier}:{encoded}:{DeviceName}"
            : $"{Prefix}:{tier}:{encoded}";
    }

    public static bool TryParse(string text, out NoiseQrPayload? payload)
    {
        payload = null;

        var parts = text.Trim().Split(':', 4);
        if (parts.Length < 3 || parts[0] != Prefix)
        {
            return false;
        }

        Tier tier;
        switch (parts[1])
        {
            case "L":
                tier = Tier.Low;
                break;
            case "H":
                tier = Tier.High;
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

        if (blob.Length != 2 * KeyLength)
        {
            return false;
        }

        var appPrivate = blob[..KeyLength];
        var devicePublic = blob[KeyLength..];
        if (KeyIsZero(appPrivate) || KeyIsZero(devicePublic))
        {
            return false;
        }

        payload = new NoiseQrPayload(
            tier,
            appPrivate,
            devicePublic,
            parts.Length == 4 && parts[3].Length > 0 ? parts[3] : null);
        return true;
    }

    private static bool KeyIsZero(ReadOnlySpan<byte> key)
    {
        var acc = (byte)0;
        foreach (var b in key)
        {
            acc |= b;
        }

        return acc == 0;
    }
}
