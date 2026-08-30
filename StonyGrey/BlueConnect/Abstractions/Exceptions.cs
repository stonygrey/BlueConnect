// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.BlueConnect.Abstractions;

/// <summary>Base for all failures raised by the BlueConnect libraries.</summary>
public class BlueConnectException : Exception
{
    public BlueConnectException(string message)
        : base(message)
    {
    }

    public BlueConnectException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The link failed: connect refused or timed out at the radio level, the
/// connection dropped, a GATT operation failed, or the radio is unavailable.
/// Distinct from application-level errors reported by a healthy device.
/// </summary>
public class BleTransportException : BlueConnectException
{
    public BleTransportException(string message)
        : base(message)
    {
    }

    public BleTransportException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
