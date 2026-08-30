// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Net.Codecrete.QrCodeGenerator;

using StonyGrey.Spake2Plus;

namespace StonyGrey.BlueConnect.Provision;

/// <summary>
/// The factory-station flow, scaled to a desk: generate a setup code for a
/// tier, derive the SPAKE2+ verifier (w0 ‖ L) on the host, write only the
/// verifier to the device over the serial shell, and emit the matching QR.
/// The device never sees the code (the augmented property); the code exists
/// on the printed label and nowhere else.
/// </summary>
internal static class PakeProvision
{
    public static int Provision(string tierArg, string? port, string? code, string? name, string? outPath)
    {
        if (!TryParseTier(tierArg, out var tier, out var shellTier))
        {
            Console.Error.WriteLine("tier must be low or high");
            return 2;
        }

        if (code is not null)
        {
            // An operator-supplied code is a password someone chose; a
            // memorable one becomes an online-guessable master credential.
            // Refuse the degenerate ones and warn about the rest.
            string normalized;
            try
            {
                normalized = SetupCode.Normalize(code);
            }
            catch (Spake2PlusException ex)
            {
                Console.Error.WriteLine($"invalid setup code: {ex.Message}");
                return 2;
            }

            if (SetupCode.IsLowEntropy(normalized))
            {
                Console.Error.WriteLine(
                    "refusing --code: low-entropy (repeated groups, a long sequential run, " +
                    "or fewer than 8 distinct characters). Omit --code to generate an 80-bit random one.");
                return 2;
            }

            Console.Error.WriteLine(
                "warning: operator-supplied code. Generated codes are 80-bit CSPRNG output; " +
                "anything memorable materially weakens the offline-dictionary margin.");
        }

        code ??= SetupCode.Generate();
        Spake2PlusRegistration registration;
        try
        {
            registration = BlueConnectPake.Register(code, tier);
        }
        catch (Spake2PlusException ex)
        {
            Console.Error.WriteLine($"invalid setup code: {ex.Message}");
            return 2;
        }

        // Echo off first: the 194-hex argument would otherwise be echoed
        // back as wrapped noise.
        SerialShell.Run("shell echo off", port);
        var hex = Convert.ToHexString(registration.VerifierBlob);
        if (!SerialShell.Run($"vg set {shellTier} {hex}", port))
        {
            Console.Error.WriteLine("No response; is the PAKE firmware running?");
            return 1;
        }

        name ??= SerialShell.ReadDeviceName(port);

        var payload = new PakeQrPayload(tier, code, name).Encode();
        var qr = QrCode.EncodeText(payload, QrCode.Ecc.Medium);

        Console.WriteLine();
        Console.WriteLine($"tier:    {tierArg.ToLowerInvariant()}");
        Console.WriteLine($"code:    {code}");
        Console.WriteLine($"payload: {payload}");
        Console.WriteLine();
        QrOutput.PrintToConsole(qr);

        if (outPath is not null)
        {
            var caption = name is { Length: > 0 }
                ? $"{tierArg.ToLowerInvariant()} · {name}"
                : tierArg.ToLowerInvariant();
            File.WriteAllText(outPath, QrOutput.ToLabeledSvg(qr, code, caption));
            Console.WriteLine($"QR written to {outPath}");
        }

        return 0;
    }

    private static bool TryParseTier(string arg, out PakeTier tier, out string shellTier)
    {
        switch (arg.ToLowerInvariant())
        {
            case "low":
            case "lo":
                tier = PakeTier.Low;
                shellTier = "lo";
                return true;
            case "high":
            case "hi":
                tier = PakeTier.High;
                shellTier = "hi";
                return true;
            default:
                tier = default;
                shellTier = "";
                return false;
        }
    }

}
