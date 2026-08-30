// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Spake2Plus;

/// <summary>
/// Any SPAKE2+ failure: malformed peer values, confirmation mismatch (the
/// codes disagree), or misuse of the handshake state machine.
/// </summary>
public class Spake2PlusException : Exception
{
    public Spake2PlusException(string message)
        : base(message)
    {
    }

    public Spake2PlusException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
