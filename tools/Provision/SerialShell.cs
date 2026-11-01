// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.IO.Ports;

namespace StonyGrey.BlueConnect.Provision;

/// <summary>
/// The J-Link VCOM shell channel: send one command, echo the device's
/// response lines. USB access is the possession proof for everything that
/// goes over this port.
/// </summary>
internal static class SerialShell
{
    public static string ResolvePort(string? port)
    {
        if (port is not null)
        {
            return port;
        }

        IEnumerable<string> candidates;
        if (OperatingSystem.IsWindows())
        {
            candidates = SerialPort.GetPortNames();
        }
        else if (OperatingSystem.IsMacOS())
        {
            candidates = Directory.GetFiles("/dev", "tty.usbmodem*");
        }
        else
        {
            // J-Link VCOM on Linux/WSL.
            candidates = Directory.Exists("/dev")
                ? Directory.GetFiles("/dev", "ttyACM*")
                : [];
        }

        var ordered = candidates.OrderBy(p => p, StringComparer.Ordinal).ToList();
        return ordered.Count switch
        {
            0 => throw new InvalidOperationException("No serial port found; is the DK connected over USB?"),
            1 => ordered[0],
            // Kits with several VCOM ports (nRF54L15-DK) put the shell on
            // only one of them; probe each for the Zephyr shell prompt.
            _ => ordered.FirstOrDefault(RespondsToShell) ?? ordered[0],
        };
    }

    private static bool RespondsToShell(string port)
    {
        try
        {
            using var serial = new SerialPort(port, 115200)
            {
                NewLine = "\n",
                ReadTimeout = 400,
                WriteTimeout = 400,
            };
            serial.Open();
            serial.DiscardInBuffer();
            serial.Write("\n");
            Thread.Sleep(150);
            return serial.BytesToRead > 0 && serial.ReadExisting().Contains("uart:~$");
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes a command in small paced chunks: an unpaced long line — a hex
    /// CCS or verifier — loses characters between the J-Link's USB CDC and
    /// the chip's UART and produces no error. Verified empirically: enlarging
    /// the shell's RX ring alone does not fix it; pacing does.
    /// </summary>
    private static void WritePaced(SerialPort serial, string text)
    {
        const int chunk = 24;
        for (var offset = 0; offset < text.Length; offset += chunk)
        {
            serial.Write(text.Substring(offset, Math.Min(chunk, text.Length - offset)));
            Thread.Sleep(8);
        }
    }

    /// <summary>
    /// Runs one shell command and captures the response lines instead of
    /// printing them (for read-backs like `ek cred`). Returns false when the
    /// device offered no output.
    /// </summary>
    public static bool RunCapture(string command, string? port, out List<string> lines)
    {
        lines = [];
        using var serial = new SerialPort(ResolvePort(port), 115200)
        {
            NewLine = "\n",
            ReadTimeout = 3000,
            WriteTimeout = 3000,
        };
        serial.Open();
        serial.DiscardInBuffer();
        WritePaced(serial, command + "\n");

        while (true)
        {
            string line;
            try
            {
                line = serial.ReadLine().TrimEnd('\r');
            }
            catch (TimeoutException)
            {
                break;
            }

            if (line.Contains(command) || line.StartsWith("uart:~$"))
            {
                continue;
            }

            if (line.Length > 0)
            {
                lines.Add(line);
            }
        }

        return lines.Count > 0;
    }

    /// <summary>
    /// Runs one shell command and prints the response. Returns false when the
    /// device offered no output (firmware not running / wrong image).
    /// </summary>
    /// <summary>
    /// The board's advertised name from `vg info`'s "name:" line, so
    /// provisioning can default the QR's device name to it. Null when the
    /// firmware predates the line or the shell is quiet.
    /// </summary>
    public static string? ReadDeviceName(string? port)
    {
        if (!RunCapture("vg info", port, out var lines))
        {
            return null;
        }

        foreach (var line in lines)
        {
            var candidate = line.Trim();
            if (candidate.StartsWith("name: ", StringComparison.Ordinal))
            {
                var name = candidate["name: ".Length..].Trim();
                return name.Length > 0 ? name : null;
            }
        }

        return null;
    }

    public static bool Run(string command, string? port)
    {
        using var serial = new SerialPort(ResolvePort(port), 115200)
        {
            NewLine = "\n",
            ReadTimeout = 3000,
            WriteTimeout = 3000,
        };
        serial.Open();
        serial.DiscardInBuffer();
        WritePaced(serial, command + "\n");

        // Print every line the device offers until it goes quiet.
        var sawOutput = false;
        while (true)
        {
            string line;
            try
            {
                line = serial.ReadLine().TrimEnd('\r');
            }
            catch (TimeoutException)
            {
                break;
            }

            // Skip the shell's echo of our own command and its prompts.
            if (line.Contains(command) || line.StartsWith("uart:~$"))
            {
                continue;
            }

            if (line.Length > 0)
            {
                Console.WriteLine(line);
                sawOutput = true;
            }
        }

        return sawOutput;
    }
}
