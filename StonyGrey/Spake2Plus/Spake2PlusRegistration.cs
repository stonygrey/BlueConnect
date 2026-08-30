// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;

using Org.BouncyCastle.Math;

namespace StonyGrey.Spake2Plus;

/// <summary>
/// The registration record derived from a setup code (RFC 9383 §3.2): the
/// prover keeps w0/w1; the verifier (the device) stores only w0 and
/// L = w1·G, from which the code cannot be recovered without a dictionary
/// attack — the augmented property.
/// </summary>
public sealed record Spake2PlusRegistration
{
    private Spake2PlusRegistration(byte[] w0, byte[] w1, byte[] l)
    {
        W0 = w0;
        W1 = w1;
        L = l;
    }

    /// <summary>32-byte scalar, reduced mod the group order.</summary>
    public byte[] W0 { get; }

    /// <summary>32-byte scalar, reduced mod the group order. Prover-only.</summary>
    public byte[] W1 { get; }

    /// <summary>w1·G, 65-byte uncompressed point. Verifier-only.</summary>
    public byte[] L { get; }

    /// <summary>
    /// w0 ‖ L (97 bytes) — the blob the device imports as a PSA
    /// PSA_KEY_TYPE_SPAKE2P_PUBLIC_KEY and persists in its verifier slot.
    /// </summary>
    public byte[] VerifierBlob
    {
        get
        {
            var blob = new byte[W0.Length + L.Length];
            W0.CopyTo(blob, 0);
            L.CopyTo(blob, W0.Length);
            return blob;
        }
    }

    /// <summary>
    /// Derives w0/w1/L from a setup code: PBKDF2-HMAC-SHA256 to 80 bytes,
    /// split into two 40-byte seeds, each reduced mod the group order
    /// (RFC 9383 §3.2 for P-256). The code is normalized first so scanned
    /// and hand-typed codes agree. Salt and iteration count are the caller's
    /// protocol profile and must match on every party that derives.
    /// </summary>
    public static Spake2PlusRegistration FromCode(string code, ReadOnlySpan<byte> salt, int iterations)
    {
        var password = Encoding.UTF8.GetBytes(SetupCode.Normalize(code));
        var derived = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iterations, HashAlgorithmName.SHA256,
            2 * Spake2PlusP256.ScalarSeedLength);

        var w0 = Spake2PlusP256.ToScalar(derived.AsSpan(0, Spake2PlusP256.ScalarSeedLength))
            .Mod(Spake2PlusP256.N);
        var w1 = Spake2PlusP256.ToScalar(derived.AsSpan(Spake2PlusP256.ScalarSeedLength))
            .Mod(Spake2PlusP256.N);

        if (w0.SignValue == 0 || w1.SignValue == 0)
        {
            // Probability ~2^-256; the provisioner reacts by generating a
            // fresh code rather than this library silently adjusting.
            throw new Spake2PlusException("Degenerate scalar derived from the code; use a different code.");
        }

        return FromScalars(Spake2PlusP256.EncodeScalar(w0), Spake2PlusP256.EncodeScalar(w1));
    }

    internal static Spake2PlusRegistration FromScalars(byte[] w0, byte[] w1)
    {
        var l = Spake2PlusP256.G.Multiply(new BigInteger(1, w1));
        return new Spake2PlusRegistration(w0, w1, Spake2PlusP256.Encode(l));
    }
}
