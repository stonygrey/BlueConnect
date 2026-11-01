// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Buffers.Text;

namespace StonyGrey.BlueConnect.Secure;

/// <summary>
/// What a factory enrollment QR carries:
/// <c>SGNE1:&lt;base64url blob&gt;[:&lt;NAME&gt;]</c>, where the blob is the
/// enrollment X25519 private key (32) ‖ the device's X25519 public key (32).
/// Unlike an <c>SGNK1</c> credential, the enrollment key is not the phone's
/// identity: a session opened with it may only <c>Enroll</c> the phone's own
/// key into the device's low slot, after which the phone discards it. The
/// card proves possession of the kit; the phone's key never leaves the phone.
/// </summary>
public sealed record NoiseEnrollmentPayload(byte[] EnrollmentPrivateKey, byte[] DevicePublicKey, string? DeviceName)
{
    public const string Prefix = "SGNE1";
    private const int KeyLength = 32;

    public string Encode()
    {
        if (EnrollmentPrivateKey.Length != KeyLength || DevicePublicKey.Length != KeyLength ||
            KeyIsZero(EnrollmentPrivateKey) || KeyIsZero(DevicePublicKey))
        {
            throw new LinkSecurityException("Noise enrollment payload fields are out of range.");
        }

        var blob = new byte[2 * KeyLength];
        EnrollmentPrivateKey.CopyTo(blob, 0);
        DevicePublicKey.CopyTo(blob, KeyLength);
        var encoded = Base64Url.EncodeToString(blob);
        return DeviceName is { Length: > 0 } ? $"{Prefix}:{encoded}:{DeviceName}" : $"{Prefix}:{encoded}";
    }

    public static bool TryParse(string text, out NoiseEnrollmentPayload? payload)
    {
        payload = null;

        var parts = text.Trim().Split(':', 3);
        if (parts.Length < 2 || parts[0] != Prefix)
        {
            return false;
        }

        byte[] blob;
        try
        {
            blob = Base64Url.DecodeFromChars(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (blob.Length != 2 * KeyLength)
        {
            return false;
        }

        var enrollmentPrivate = blob[..KeyLength];
        var devicePublic = blob[KeyLength..];
        if (KeyIsZero(enrollmentPrivate) || KeyIsZero(devicePublic))
        {
            return false;
        }

        payload = new NoiseEnrollmentPayload(
            enrollmentPrivate, devicePublic,
            parts.Length == 3 && parts[2].Length > 0 ? parts[2] : null);
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
