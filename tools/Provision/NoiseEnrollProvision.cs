// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Net.Codecrete.QrCodeGenerator;

using StonyGrey.BlueConnect.Secure;

namespace StonyGrey.BlueConnect.Provision;

/// <summary>
/// The factory flow for enrollment: generate an enrollment key, pin its public
/// half into the device's enrollment slot over the serial shell, read the
/// device's public key back, and emit the kit QR. A phone that scans it
/// enrolls its own key into the device's low slot (displacing any earlier
/// phone) and discards the enrollment key. The card proves possession of
/// the kit; it is not the phone's identity.
/// </summary>
internal static class NoiseEnrollProvision
{
    public static int Provision(string? port, string? name, string? outPath)
    {
        name ??= SerialShell.ReadDeviceName(port);

        var enrollment = LinkIdentity.Generate();

        if (!SerialShell.Run($"vg set en {Convert.ToHexString(enrollment.StaticPublicKey)}", port))
        {
            Console.Error.WriteLine("No response; is the Noise firmware running?");
            return 1;
        }

        if (!SerialShell.RunCapture("vg cred", port, out var lines))
        {
            Console.Error.WriteLine("The device offered no identity; is the Noise firmware running?");
            return 1;
        }

        var devicePub = NoiseProvision.ParseDevicePublicKey(lines);
        if (devicePub is null)
        {
            Console.Error.WriteLine("The device's `vg cred` output carried no usable public key.");
            return 1;
        }

        var payload = new NoiseEnrollmentPayload(enrollment.StaticPrivateKey, devicePub, name);
        var text = payload.Encode();
        var qr = QrCode.EncodeText(text, QrCode.Ecc.Medium);

        Console.WriteLine();
        Console.WriteLine("kind:    enrollment (low tier)");
        Console.WriteLine($"payload: {text}");
        Console.WriteLine();
        QrOutput.PrintToConsole(qr);

        if (outPath is not null)
        {
            var caption = name is { Length: > 0 } ? $"enrollment · {name}" : "enrollment";
            File.WriteAllText(outPath, QrOutput.ToLabeledSvg(qr, caption, "Noise enrollment", text));
            Console.WriteLine($"QR written to {outPath}");
        }

        return 0;
    }
}
