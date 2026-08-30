// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Net.Codecrete.QrCodeGenerator;

using StonyGrey.BlueConnect.Secure;
using StonyGrey.Spake2Plus;

namespace StonyGrey.BlueConnect.Provision;

/// <summary>
/// The factory-station flow for the Noise firmware: mint a fresh app
/// identity for a tier, pin its public key into the device's slot over the
/// serial shell (which locks the slot — no ceremony may replace it), read
/// the device's public key back, and emit the QR that carries the whole
/// relationship. The printed label IS the credential.
/// </summary>
internal static class NoiseProvision
{
    public static int Provision(string tierArg, string? port, string? name, string? outPath)
    {
        PakeTier tier;
        string shellTier;
        switch (tierArg.ToLowerInvariant())
        {
            case "low":
            case "lo":
                tier = PakeTier.Low;
                shellTier = "lo";
                break;
            case "high":
            case "hi":
                tier = PakeTier.High;
                shellTier = "hi";
                break;
            default:
                Console.Error.WriteLine("tier must be low or high");
                return 2;
        }

        name ??= SerialShell.ReadDeviceName(port);

        var identity = LinkIdentity.Generate();

        if (!SerialShell.Run($"vg set {shellTier} {Convert.ToHexString(identity.StaticPublicKey)}", port))
        {
            Console.Error.WriteLine("No response; is the Noise firmware running?");
            return 1;
        }

        if (!SerialShell.RunCapture("vg cred", port, out var lines))
        {
            Console.Error.WriteLine("The device offered no identity; is the Noise firmware running?");
            return 1;
        }

        var devicePub = ParseDevicePublicKey(lines);
        if (devicePub is null)
        {
            Console.Error.WriteLine("The device's `vg cred` output carried no usable public key.");
            return 1;
        }

        var payload = new NoiseQrPayload(tier, identity.StaticPrivateKey, devicePub, name);
        var text = payload.Encode();
        var qr = QrCode.EncodeText(text, QrCode.Ecc.Medium);

        Console.WriteLine();
        Console.WriteLine($"tier:    {tierArg.ToLowerInvariant()}");
        Console.WriteLine($"payload: {text}");
        Console.WriteLine();
        QrOutput.PrintToConsole(qr);

        if (outPath is not null)
        {
            var caption = name is { Length: > 0 }
                ? $"{tierArg.ToLowerInvariant()} · {name}"
                : tierArg.ToLowerInvariant();
            File.WriteAllText(outPath, QrOutput.ToLabeledSvg(qr, caption, "Noise credential", text));
            Console.WriteLine($"QR written to {outPath}");
        }

        return 0;
    }

    /// <summary>Finds the bare HEX64 line `vg cred` prints among the shell output.</summary>
    private static byte[]? ParseDevicePublicKey(List<string> lines)
    {
        foreach (var line in lines)
        {
            var candidate = line.Trim();
            if (candidate.Length != 64)
            {
                continue;
            }

            try
            {
                return Convert.FromHexString(candidate);
            }
            catch (FormatException)
            {
            }
        }

        return null;
    }
}
