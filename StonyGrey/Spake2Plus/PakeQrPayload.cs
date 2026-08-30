// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Spake2Plus;

/// <summary>
/// What the printed QR carries: <c>SGPK1:&lt;L|H&gt;:&lt;CODE&gt;[:&lt;NAME&gt;]</c>.
/// The prefix is the format version; parsers reject unknown prefixes.
/// </summary>
public sealed record PakeQrPayload(PakeTier Tier, string Code, string? DeviceName)
{
    private const string Prefix = "SGPK1";

    public string Encode()
    {
        var tier = Tier == PakeTier.High ? "H" : "L";
        return DeviceName is { Length: > 0 }
            ? $"{Prefix}:{tier}:{Code}:{DeviceName}"
            : $"{Prefix}:{tier}:{Code}";
    }

    public static bool TryParse(string text, out PakeQrPayload? payload)
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

        if (!SetupCode.IsValid(parts[2]))
        {
            return false;
        }

        payload = new PakeQrPayload(tier, parts[2], parts.Length == 4 ? parts[3] : null);
        return true;
    }
}
