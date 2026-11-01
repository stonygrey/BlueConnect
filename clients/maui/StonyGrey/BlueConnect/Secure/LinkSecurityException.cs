// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.Secure;

/// <summary>
/// A link-security failure: handshake failure, pin mismatch, or AEAD
/// authentication failure. Distinct from <see cref="BleTransportException"/>
/// (the radio link itself) — this means the bytes arrived but could not be
/// trusted.
/// </summary>
public class LinkSecurityException : BlueConnectException
{
    public LinkSecurityException(string message)
        : base(message)
    {
    }

    public LinkSecurityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
