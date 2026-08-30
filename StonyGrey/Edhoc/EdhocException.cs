// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Edhoc;

/// <summary>
/// Any EDHOC failure: malformed peer messages, authentication failure (a MAC
/// or AEAD tag did not verify), or misuse of the handshake state machine.
/// </summary>
public class EdhocException : Exception
{
    public EdhocException(string message)
        : base(message)
    {
    }

    public EdhocException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public EdhocException(string message, byte[] errorMessageToSend)
        : base(message)
    {
        ErrorMessageToSend = errorMessageToSend;
    }

    /// <summary>
    /// When the failure is one the peer should be told about, the ready-encoded
    /// EDHOC error message (RFC 9528 §6) to send. The library never sends
    /// anything itself; the transport decides whether to forward this.
    /// </summary>
    public byte[]? ErrorMessageToSend { get; }
}
