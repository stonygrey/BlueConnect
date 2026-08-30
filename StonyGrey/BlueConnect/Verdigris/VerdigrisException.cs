// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Verdigris.Protocol;

namespace StonyGrey.BlueConnect.Verdigris;

/// <summary>
/// The device answered and rejected the request: an application-level error
/// from a healthy link, as opposed to <see cref="BleTransportException"/>.
/// </summary>
public class VerdigrisException : BlueConnectException
{
    public VerdigrisException(Status status, string? detail)
        : base($"The device rejected the request: {status}{(string.IsNullOrEmpty(detail) ? string.Empty : $" ({detail})")}")
    {
        Status = status;
        Detail = string.IsNullOrEmpty(detail) ? null : detail;
    }

    public Status Status { get; }

    public string? Detail { get; }
}
