// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.RegularExpressions;

using StonyGrey.BlueConnect.Core;

namespace StonyGrey.BlueConnect.Smalt;

internal enum IoKind
{
    Message,
    Segment,
}

internal sealed partial class IoLine
{
    public required IoKind Kind { get; init; }
    public required string Direction { get; init; }
    public byte? Type { get; init; }
    public int? Seq { get; init; }
    public string? Flags { get; init; }
    public required byte[] Bytes { get; init; }

    public static IoLine? TryParse(string line)
    {
        var match = Pattern().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var hex = match.Groups["hex"].Value;
        if (hex.Length % 2 != 0)
        {
            return null;
        }

        var bytes = Convert.FromHexString(hex);
        var kind = match.Groups["kind"].Value == "message" ? IoKind.Message : IoKind.Segment;
        byte? type = null;
        if (match.Groups["type"].Success &&
            byte.TryParse(match.Groups["type"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedType))
        {
            type = parsedType;
        }

        int? seq = null;
        if (match.Groups["seq"].Success && int.TryParse(match.Groups["seq"].Value, out var parsedSeq))
        {
            seq = parsedSeq;
        }

        return new IoLine
        {
            Kind = kind,
            Direction = match.Groups["dir"].Value,
            Type = type,
            Seq = seq,
            Flags = match.Groups["flags"].Success ? match.Groups["flags"].Value : null,
            Bytes = bytes,
        };
    }

    [GeneratedRegex(
        @"_BLE_IO (?<kind>message|segment) (?<dir>read|write)(?: type=(?<type>[0-9a-fA-F]{2}))?(?: seq=(?<seq>\d+))?(?: flags=(?<flags>[^\s]+))? len=\d+ hex=(?<hex>[0-9A-Fa-f]*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
