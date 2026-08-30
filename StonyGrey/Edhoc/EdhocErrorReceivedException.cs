// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Edhoc;

/// <summary>
/// Thrown when a Process* call receives a peer EDHOC error message
/// (RFC 9528 §6) instead of the expected protocol message.
/// </summary>
public sealed class EdhocErrorReceivedException : EdhocException
{
    public EdhocErrorReceivedException(int errorCode, string? diagnostic, int[]? supportedSuites)
        : base(errorCode switch
        {
            1 => $"Peer sent an EDHOC error: {diagnostic}",
            2 => "Peer rejected the cipher suite and proposed alternatives.",
            _ => $"Peer sent an EDHOC error with code {errorCode}.",
        })
    {
        ErrorCode = errorCode;
        Diagnostic = diagnostic;
        SupportedSuites = supportedSuites;
    }

    /// <summary>ERR_CODE per RFC 9528 §6: 1 = unspecified, 2 = wrong cipher suite.</summary>
    public int ErrorCode { get; }

    /// <summary>ERR_INFO diagnostic text when ERR_CODE is 1.</summary>
    public string? Diagnostic { get; }

    /// <summary>SUITES_R when ERR_CODE is 2, in the responder's order of preference.</summary>
    public int[]? SupportedSuites { get; }
}
