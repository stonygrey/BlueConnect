// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Net.Codecrete.QrCodeGenerator;

using StonyGrey.BlueConnect.Secure;

using StonyGrey.Edhoc;
using StonyGrey.Spake2Plus;

namespace StonyGrey.BlueConnect.Provision;

/// <summary>
/// The factory-station flow for the EDHOC firmware: issue a fresh initiator
/// key pair for a tier, write its credential (CCS) to the device's slot over
/// the serial shell, read the device's own credential back, and emit the QR
/// that carries the whole relationship (initiator private key + pinned device
/// credential — the printed label IS the credential, like the PAKE setup
/// code). CCS bytes travel verbatim in both directions: they participate
/// byte-exactly in the EDHOC transcript MACs and are never re-encoded.
/// </summary>
internal static class EdhocProvision
{
    public static int Provision(string tierArg, string? port, string? name, string? outPath)
    {
        if (!TryParseTier(tierArg, out var tier, out var shellTier))
        {
            Console.Error.WriteLine("tier must be low or high");
            return 2;
        }

        name ??= SerialShell.ReadDeviceName(port);

        var kid = tier == PakeTier.High ? BlueConnectEdhoc.HighKid : BlueConnectEdhoc.LowKid;
        var initiator = EdhocCredential.Generate([kid], subject: null);

        // Echo off first: the long hex arguments would otherwise be echoed
        // back as wrapped noise.
        SerialShell.Run("shell echo off", port);
        var credIHex = Convert.ToHexString(initiator.CredentialBytes.Span);
        if (!SerialShell.Run($"vg set {shellTier} {credIHex}", port))
        {
            Console.Error.WriteLine("No response; is the EDHOC firmware running?");
            return 1;
        }

        if (!SerialShell.RunCapture("vg cred", port, out var lines))
        {
            Console.Error.WriteLine("The device offered no credential; run `vg keygen` and retry.");
            return 1;
        }

        var deviceCcs = ParseDeviceCredential(lines);
        if (deviceCcs is null)
        {
            Console.Error.WriteLine("The device's `vg cred` output was not a usable CCS.");
            return 1;
        }

        var payload = new EdhocQrPayload(
            tier, initiator.PrivateKey!.Value.ToArray(), deviceCcs, name);
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
            File.WriteAllText(outPath, QrOutput.ToLabeledSvg(qr, caption, "EDHOC credential", text));
            Console.WriteLine($"QR written to {outPath}");
        }

        return 0;
    }

    /// <summary>
    /// Finds the hex CCS among the shell lines and validates it: it must
    /// parse as a credential carrying the device kid.
    /// </summary>
    private static byte[]? ParseDeviceCredential(List<string> lines)
    {
        foreach (var line in lines)
        {
            var candidate = line.Trim();
            if (candidate.Length is < 20 or > 512 || candidate.Length % 2 != 0)
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromHexString(candidate);
            }
            catch (FormatException)
            {
                continue;
            }

            try
            {
                var credential = EdhocCredential.FromCcs(bytes);
                if (credential.Kid.Span.SequenceEqual([BlueConnectEdhoc.DeviceKid]))
                {
                    return bytes;
                }
            }
            catch (EdhocException)
            {
            }
        }

        return null;
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
