// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Logging;

internal static class CallerTag
{
    public static string Format(string file, string member, int line)
        => $"{Path.GetFileNameWithoutExtension(file)}.{member.Trim('<', '>', '$')}:{line}";
}
