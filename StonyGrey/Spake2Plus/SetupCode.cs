// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;

namespace StonyGrey.Spake2Plus;

/// <summary>
/// Setup codes: 80 bits of RNG output as 16 Crockford base32 characters,
/// displayed as XXXX-XXXX-XXXX-XXXX. Crockford's alphabet drops I, L, O, U
/// and folds the lookalikes on input, so hand-typed codes survive fonts and
/// transcription.
/// </summary>
public static class SetupCode
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Characters = 16;

    public static string Generate()
    {
        Span<byte> entropy = stackalloc byte[10];
        RandomNumberGenerator.Fill(entropy);

        // 16 five-bit groups from the 80 bits, MSB first.
        var code = new StringBuilder(Characters + 3);
        var acc = 0;
        var bits = 0;
        foreach (var b in entropy)
        {
            acc = (acc << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                code.Append(Alphabet[(acc >> bits) & 0x1F]);
                if (code.Length is 4 or 9 or 14)
                {
                    code.Append('-');
                }
            }
        }

        return code.ToString();
    }

    /// <summary>
    /// Canonical form used for key derivation: uppercase, no separators,
    /// Crockford lookalikes folded (I/L→1, O→0). Throws
    /// <see cref="Spake2PlusException"/> if the result is not 16 valid
    /// characters.
    /// </summary>
    public static string Normalize(string code)
    {
        var normalized = new StringBuilder(Characters);
        foreach (var raw in code)
        {
            var c = char.ToUpperInvariant(raw);
            c = c switch
            {
                '-' or ' ' => '\0',
                'I' or 'L' => '1',
                'O' => '0',
                _ => c,
            };

            if (c == '\0')
            {
                continue;
            }

            if (!Alphabet.Contains(c))
            {
                throw new Spake2PlusException($"Setup code contains an invalid character: '{raw}'.");
            }

            normalized.Append(c);
        }

        if (normalized.Length != Characters)
        {
            throw new Spake2PlusException($"Setup code must be {Characters} characters, got {normalized.Length}.");
        }

        return normalized.ToString();
    }

    public static bool IsValid(string code)
    {
        try
        {
            Normalize(code);
            return true;
        }
        catch (Spake2PlusException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when a valid code is too structured to be an 80-bit secret:
    /// few distinct symbols, a repeated 4-character group, or a long
    /// run of consecutive Crockford digits. Generated codes almost never
    /// trip this; operator-supplied ones often do.
    /// </summary>
    public static bool IsLowEntropy(string code)
    {
        var n = Normalize(code);

        if (n.Distinct().Count() < 8)
        {
            return true;
        }

        var g0 = n[..4];
        if (g0 == n[4..8] && g0 == n[8..12] && g0 == n[12..16])
        {
            return true;
        }

        var run = 1;
        var maxRun = 1;
        for (var i = 1; i < n.Length; i++)
        {
            var step = Alphabet.IndexOf(n[i]) - Alphabet.IndexOf(n[i - 1]);
            run = step is 1 or -1 ? run + 1 : 1;
            if (run > maxRun)
            {
                maxRun = run;
            }
        }

        return maxRun >= 8;
    }
}
