// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace Zaffre.Services.Abstractions;

/// <summary>
/// How a stored credential was obtained. Persisted by the credential
/// services as a lower-case string; a value stored before origins were
/// recorded reads as <see cref="Paired"/>.
/// </summary>
public enum CredentialOrigin
{
    /// <summary>The button ceremony on the device.</summary>
    Paired,

    /// <summary>A credential QR written over USB; the device slot is locked.</summary>
    Provisioned,

    /// <summary>An enrollment QR: the app's own key was written into the device's low slot.</summary>
    Enrolled,
}

internal static class CredentialOriginStorage
{
    /// <summary>The persisted form; <c>null</c> (legacy) reads as <see cref="CredentialOrigin.Paired"/>.</summary>
    public static CredentialOrigin Parse(string? stored) => stored switch
    {
        "provisioned" => CredentialOrigin.Provisioned,
        "enrolled" => CredentialOrigin.Enrolled,
        _ => CredentialOrigin.Paired,
    };

    public static string Format(CredentialOrigin origin) => origin switch
    {
        CredentialOrigin.Provisioned => "provisioned",
        CredentialOrigin.Enrolled => "enrolled",
        _ => "paired",
    };
}
