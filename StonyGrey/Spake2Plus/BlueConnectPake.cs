// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Text;

namespace StonyGrey.Spake2Plus;

/// <summary>Which verifier slot a code/session belongs to.</summary>
public enum PakeTier
{
    Low,
    High,
}

/// <summary>
/// The BlueConnect SPAKE2+ profile: the constants every deriving party (the
/// provisioning tool and the phone) must agree on, and that the firmware's
/// PSA side sees only downstream of (it imports the finished verifier and
/// shares the context string). Versioned via the "/1" suffix — a change to
/// any value here is a new profile.
/// </summary>
public static class BlueConnectPake
{
    /// <summary>RFC 9383 Context; also the root of the PBKDF2 salts.</summary>
    public const string Context = "BlueConnectPake/1";

    public static readonly byte[] ContextBytes = Encoding.UTF8.GetBytes(Context);

    /// <summary>idProver/idVerifier are empty in this profile.</summary>
    public static readonly byte[] EmptyIdentity = [];

    /// <summary>
    /// PBKDF2-HMAC-SHA256 iteration count. Runs on the phone once per app
    /// session per tier, and in the provisioning tool.
    /// </summary>
    public const int Pbkdf2Iterations = 100_000;

    /// <summary>The tier lives in the salt, so the two codes never collide.</summary>
    public static byte[] SaltFor(PakeTier tier)
        => Encoding.UTF8.GetBytes(Context + (tier == PakeTier.High ? "|high" : "|low"));

    public static Spake2PlusRegistration Register(string code, PakeTier tier)
        => Spake2PlusRegistration.FromCode(code, SaltFor(tier), Pbkdf2Iterations);

    public static Spake2PlusProver CreateProver(Spake2PlusRegistration registration)
        => new(registration.W0, registration.W1, ContextBytes, EmptyIdentity, EmptyIdentity);

    public static Spake2PlusVerifier CreateVerifier(Spake2PlusRegistration registration)
        => new(registration.W0, registration.L, ContextBytes, EmptyIdentity, EmptyIdentity);
}
