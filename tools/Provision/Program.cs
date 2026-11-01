// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.IO.Ports;

using StonyGrey.BlueConnect.Provision;

// Zaffre device maintenance tool. It communicates with a board over the J-Link USB
// serial shell for inspection, recovery, and provisioning (USB access is the
// possession proof), and hosts the link-security loopback self-tests.
return args switch
{
    ["info", .. var rest] => Serial("vg info", OptionValue(rest, "--port")),
    ["clear", var tier, .. var rest] when !tier.StartsWith("--") =>
        Clear(tier, OptionValue(rest, "--port")),
    ["clear", .. var rest] => Serial("vg clear", OptionValue(rest, "--port")),
    ["confirm", .. var rest] => Serial("vg confirm", OptionValue(rest, "--port")),
    ["wipe", .. var rest] => Serial("vg wipe", OptionValue(rest, "--port")),
    ["console", .. var rest] => Console_(OptionValue(rest, "--port"), OptionValue(rest, "--seconds")),
    ["shell", var cmd, .. var rest] => Serial(cmd, OptionValue(rest, "--port")),
    ["self-test"] => SelfTest.Run(),
    ["interop"] => InteropTest.Run(),
    ["noise-provision", var tier, .. var rest] => NoiseProvision.Provision(
        tier, OptionValue(rest, "--port"), OptionValue(rest, "--name"), OptionValue(rest, "--out")),
    ["noise-ca-gen", .. var rest] => NoiseCa.Generate(
        OptionValue(rest, "--key"), OptionValue(rest, "--pub"), rest.Contains("--force")),
    ["noise-ca-root", .. var rest] => NoiseCa.ProvisionRoot(
        OptionValue(rest, "--port"), OptionValue(rest, "--pub")),
    ["noise-ca-sign", var subject, .. var rest] => NoiseCa.Sign(
        subject, OptionValue(rest, "--key"), OptionValue(rest, "--out")),
    ["noise-ca-info", .. var rest] => NoiseCa.Info(
        OptionValue(rest, "--port"), OptionValue(rest, "--pub")),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("""
        usage: device <command> [--port <serial port>]

        Device commands, over the `vg` shell on the USB serial port:

          info      Show the device's identity, slots (with provenance), and
                    session state.
          clear [low|high|all]
                    Clear credential slot(s) and their locks (default all;
                    same as holding Button 4 for 5 s).
          confirm   Confirm an in-progress pairing ceremony (same as the
                    matching button; USB access is the same possession proof).
          wipe      Factory wipe: slots, locks, and the device identity/key
                    (same as holding Buttons 2+3 for 5 s). Invalidates every
                    printed QR; pinned phones fail the pin check on their
                    next session.
          console   Echo the device's serial console for a while.

        Loopback tests (no device needed):

          self-test Run the Noise link loopback self-test.
          interop   Verify Torann and the hand-rolled Noise implementation
                    speak the same wire dialect, both roles.

        Provisioning (locks the slot against ceremonies; each QR carries the
        slot's private material — the label IS the credential):

          noise-provision <low|high|enroll> [--name <adv name>] [--out qr.svg]
                    low|high: generate an app identity for the tier, pin its
                    public key into the slot over serial, and print the QR
                    for the Zaffre Noise tab.
                    enroll: generate an enrollment key, pin it into the device's
                    enrollment slot over serial, and print the kit QR. A
                    phone that scans it enrolls its own key into the low
                    slot (one phone at a time) and discards the enrollment
                    key.

        Endorsement CA (Ed25519, tool-only private key; grants no privilege):

          noise-ca-gen [--key lib/noise-ca.key] [--pub lib/noise-ca.pub] [--force]
                    Generate a CA once. Private key is gitignored.
          noise-ca-root [--pub lib/noise-ca.pub]
                    Write the CA public key into Noise firmware (vg ca-root).
          noise-ca-sign <pubkey hex|base64> [--key lib/noise-ca.key] [--out blob]
                    Sign Zaffre's existing Noise static public key; print the
                    compact blob (hex). Does not invent a second key.
          noise-ca-info
                    Show whether a local CA exists and whether the device has
                    a root loaded.
        """);
    return 2;
}

static int Serial(string command, string? port)
    => SerialShell.Run(command, port) ? 0 : Quiet();

static int Clear(string tier, string? port)
{
    var shellArg = tier.ToLowerInvariant() switch
    {
        "low" or "lo" => "lo",
        "high" or "hi" => "hi",
        "all" => "all",
        _ => null,
    };
    if (shellArg is null)
    {
        Console.Error.WriteLine("tier must be low, high, or all");
        return 2;
    }

    return Serial($"vg clear {shellArg}", port);
}

static int Quiet()
{
    Console.Error.WriteLine("No response; is the right firmware running?");
    return 1;
}

static int Console_(string? port, string? seconds)
{
    var window = TimeSpan.FromSeconds(seconds is null ? 30 : int.Parse(seconds));

    using var serial = new SerialPort(SerialShell.ResolvePort(port), 115200) { NewLine = "\n", ReadTimeout = 1000 };
    serial.Open();
    var until = DateTime.UtcNow + window;
    while (DateTime.UtcNow < until)
    {
        try
        {
            Console.WriteLine(serial.ReadLine().TrimEnd('\r'));
        }
        catch (TimeoutException)
        {
        }
    }

    return 0;
}

static string? OptionValue(string[] args, string option)
{
    var index = Array.IndexOf(args, option);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
