// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.BlueConnect.Secure;

/// <summary>
/// The Noise suite this link speaks, and its sizes. Two patterns over one
/// suite: XX for first contact (both statics exchanged in-band, trust
/// created afterwards by pin or ceremony) and IK once this side holds the
/// peer's key — one round trip shorter, and the device's identity never
/// travels. The implementation is Torann (validated against the complete
/// cacophony and snow reference suites); the firmware mirrors both patterns
/// in C over PSA crypto, selecting by the first handshake frame's length.
/// </summary>
public static class LinkProtocol
{
    public const string Name = "Noise_XX_25519_AESGCM_SHA256";

    public const string PinnedName = "Noise_IK_25519_AESGCM_SHA256";

    /// <summary>X25519 key length.</summary>
    public const int KeyLength = 32;

    /// <summary>AES-GCM authentication tag length.</summary>
    public const int TagLength = 16;

    public static readonly Torann.NoiseProtocol Protocol = Torann.NoiseProtocol.Parse(Name);

    public static readonly Torann.NoiseProtocol PinnedProtocol = Torann.NoiseProtocol.Parse(PinnedName);
}
