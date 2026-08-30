// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Buffers.Text;

using StonyGrey.BlueConnect.Secure;

namespace StonyGrey.BlueConnect.Provision;

/// <summary>
/// Tool-only Noise endorsement CA: generate an Ed25519 root, provision the
/// public half into the device over USB, and sign a Zaffre app's existing
/// Noise static. The private key never belongs in firmware or Zaffre.
/// </summary>
internal static class NoiseCa
{
    public const string DefaultKeyPath = "lib/noise-ca.key";
    public const string DefaultPubPath = "lib/noise-ca.pub";

    public static int Generate(string? keyPath, string? pubPath, bool force)
    {
        keyPath ??= DefaultKeyPath;
        pubPath ??= DefaultPubPath;

        if (File.Exists(keyPath) && !force)
        {
            Console.Error.WriteLine($"{keyPath} already exists; pass --force to rotate the CA.");
            return 2;
        }

        var (privateKey, publicKey) = ClientEndorsement.GenerateCa();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyPath))!);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(pubPath))!);
        File.WriteAllBytes(keyPath, privateKey);
        File.WriteAllText(pubPath, Convert.ToHexString(publicKey) + Environment.NewLine);

        Console.WriteLine($"CA private: {keyPath}  (gitignored; tool-only)");
        Console.WriteLine($"CA public:  {pubPath}");
        Console.WriteLine($"pub hex:    {Convert.ToHexString(publicKey)}");
        return 0;
    }

    public static int ProvisionRoot(string? port, string? pubPath)
    {
        pubPath ??= DefaultPubPath;
        var root = ReadKey32(pubPath, "CA public key");
        if (root is null)
        {
            return 1;
        }

        if (!SerialShell.Run($"vg ca-root {Convert.ToHexString(root)}", port))
        {
            Console.Error.WriteLine("No response; is the Noise firmware running?");
            return 1;
        }

        return 0;
    }

    public static int Sign(string subjectArg, string? keyPath, string? outPath)
    {
        keyPath ??= DefaultKeyPath;
        var privateKey = ReadKey32(keyPath, "CA private key");
        if (privateKey is null)
        {
            return 1;
        }

        var subject = ParseKey32(subjectArg);
        if (subject is null)
        {
            Console.Error.WriteLine("subject must be 32-byte hex or base64 (the app's Noise static public key).");
            return 2;
        }

        byte[] blob;
        try
        {
            blob = ClientEndorsement.Sign(subject, privateKey);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        Console.WriteLine(Convert.ToHexString(blob));
        if (outPath is not null)
        {
            if (outPath.EndsWith(".hex", StringComparison.OrdinalIgnoreCase) ||
                outPath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(outPath, Convert.ToHexString(blob) + Environment.NewLine);
            }
            else
            {
                File.WriteAllBytes(outPath, blob);
            }

            Console.WriteLine($"blob written to {outPath}");
        }

        return 0;
    }

    public static int Info(string? port, string? pubPath)
    {
        pubPath ??= DefaultPubPath;
        var keyPath = DefaultKeyPath;
        Console.WriteLine($"CA private: {(File.Exists(keyPath) ? keyPath : "missing")}");
        Console.WriteLine($"CA public:  {(File.Exists(pubPath) ? pubPath : "missing")}");
        if (File.Exists(pubPath))
        {
            var root = ReadKey32(pubPath, "CA public key");
            if (root is not null)
            {
                Console.WriteLine($"pub hex:    {Convert.ToHexString(root)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("device:");
        if (!SerialShell.Run("vg ca-root", port))
        {
            Console.Error.WriteLine("No response; is the Noise firmware running?");
            return 1;
        }

        return 0;
    }

    private static byte[]? ReadKey32(string path, string what)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"{what} not found at {path}. Run `lib/device noise-ca-gen` first.");
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == ClientEndorsement.CaKeyLength)
        {
            return bytes;
        }

        var text = File.ReadAllText(path).Trim();
        var parsed = ParseKey32(text);
        if (parsed is null)
        {
            Console.Error.WriteLine($"{what} at {path} is not 32 bytes (raw or hex).");
        }

        return parsed;
    }

    internal static byte[]? ParseKey32(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            if (text.Length == 64 && text.All(char.IsAsciiHexDigit))
            {
                return Convert.FromHexString(text);
            }
        }
        catch (FormatException)
        {
        }

        try
        {
            var bytes = Convert.FromBase64String(text);
            if (bytes.Length == 32)
            {
                return bytes;
            }
        }
        catch (FormatException)
        {
        }

        try
        {
            var bytes = Base64Url.DecodeFromChars(text);
            if (bytes.Length == 32)
            {
                return bytes;
            }
        }
        catch (FormatException)
        {
        }

        if (File.Exists(text))
        {
            return ReadKey32(text, "subject public key");
        }

        return null;
    }
}
