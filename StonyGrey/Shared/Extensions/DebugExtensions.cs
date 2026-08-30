// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

// Compile-time caller info for System.Diagnostics.Debug via the Here() pattern:
//
//     Debug.Here().WriteLine("ready");
//
// Same File.Member:Line prefix as logger.Here(). C# 14 static extension members
// attach Here() to the Debug type itself.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace StonyGrey.Logging;

public static class DebugExtensions
{
    extension(Debug)
    {
        public static DebugWriter Here(
            [CallerFilePath] string file = "",
            [CallerMemberName] string member = "",
            [CallerLineNumber] int line = 0)
            => new(CallerTag.Format(file, member, line));
    }
}

public readonly struct DebugWriter(string caller)
{
    [Conditional("DEBUG")]
    public void Write(string? message) => Debug.Write(Prefix(message));

    [Conditional("DEBUG")]
    public void Write(object? value) => Debug.Write(Prefix(value));

    [Conditional("DEBUG")]
    public void Write(string? message, string? category) => Debug.Write(Prefix(message), category);

    [Conditional("DEBUG")]
    public void Write(object? value, string? category) => Debug.Write(Prefix(value), category);

    [Conditional("DEBUG")]
    public void WriteLine() => Debug.WriteLine(caller);

    [Conditional("DEBUG")]
    public void WriteLine(string? message) => Debug.WriteLine(Prefix(message));

    [Conditional("DEBUG")]
    public void WriteLine(object? value) => Debug.WriteLine(Prefix(value));

    [Conditional("DEBUG")]
    public void WriteLine(string? message, string? category) => Debug.WriteLine(Prefix(message), category);

    [Conditional("DEBUG")]
    public void WriteLine(object? value, string? category) => Debug.WriteLine(Prefix(value), category);

    [Conditional("DEBUG")]
    public void WriteIf(bool condition, string? message) => Debug.WriteIf(condition, Prefix(message));

    [Conditional("DEBUG")]
    public void WriteIf(bool condition, object? value) => Debug.WriteIf(condition, Prefix(value));

    [Conditional("DEBUG")]
    public void WriteIf(bool condition, string? message, string? category)
        => Debug.WriteIf(condition, Prefix(message), category);

    [Conditional("DEBUG")]
    public void WriteIf(bool condition, object? value, string? category)
        => Debug.WriteIf(condition, Prefix(value), category);

    [Conditional("DEBUG")]
    public void WriteLineIf(bool condition, string? message) => Debug.WriteLineIf(condition, Prefix(message));

    [Conditional("DEBUG")]
    public void WriteLineIf(bool condition, object? value) => Debug.WriteLineIf(condition, Prefix(value));

    [Conditional("DEBUG")]
    public void WriteLineIf(bool condition, string? message, string? category)
        => Debug.WriteLineIf(condition, Prefix(message), category);

    [Conditional("DEBUG")]
    public void WriteLineIf(bool condition, object? value, string? category)
        => Debug.WriteLineIf(condition, Prefix(value), category);

    private string Prefix(object? value) => $"{caller} {value}";
}
