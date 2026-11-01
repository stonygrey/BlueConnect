// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.BlueConnect.Core;

/// <summary>
/// Stable, parseable IO log lines for device reads and writes. Smalt looks for
/// <see cref="Prefix"/>.
/// </summary>
public static class IoLog
{
    public const string Prefix = "_BLE_IO";

    public static string Message(string direction, byte type, ReadOnlySpan<byte> bytes)
        => $"{Prefix} message {direction} type={type:x2} len={bytes.Length} hex={Convert.ToHexString(bytes)}";

    public static string Segment(string direction, int seq, byte flags, ReadOnlySpan<byte> bytes)
        => $"{Prefix} segment {direction} seq={seq} flags={FormatFlags(flags)} len={bytes.Length} hex={Convert.ToHexString(bytes)}";

    public static string FormatFlags(byte flags)
    {
        var parts = new List<string>(3);
        if ((flags & SegmentHeader.Start) != 0)
        {
            parts.Add("start");
        }

        if ((flags & SegmentHeader.End) != 0)
        {
            parts.Add("end");
        }

        if ((flags & SegmentHeader.Abort) != 0)
        {
            parts.Add("abort");
        }

        return parts.Count == 0 ? "none" : string.Join('+', parts);
    }
}
