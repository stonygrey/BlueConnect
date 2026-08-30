// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Secure;

namespace StonyGrey.BlueConnect.Verdigris;

/// <summary>
/// Verdigris entry point. Lives here so the BlueConnect libraries carry no
/// dependency on Verdigris; referencing this assembly lights the API up on
/// any <see cref="IScanner"/> implementation.
/// </summary>
public static class ScannerExtensions
{
    /// <summary>
    /// Creates a Verdigris peripheral for a scanned advertisement. The returned
    /// instance owns the underlying <see cref="IPeripheral"/>: dispose it to
    /// dispose both. Every session is encrypted — Noise or PAKE, decided by
    /// the <paramref name="security"/> credentials; the handshake runs
    /// transparently on the first command after connecting.
    /// </summary>
    public static IVerdigrisPeripheral CreateVerdigrisPeripheral(
        this IScanner scanner,
        IAdvertisement advertisement,
        BlueConnectOptions options,
        ILogger logger,
        ILinkSecurity security)
    {
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(security);

        return new VerdigrisPeripheral(scanner.CreatePeripheral(advertisement), options, logger, security);
    }
}
