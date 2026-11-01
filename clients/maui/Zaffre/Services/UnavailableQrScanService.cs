// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Zaffre.Services.Abstractions;

namespace Zaffre.Services;

/// <summary>
/// The no-camera fallback (Windows, and any platform until the scanner is
/// registered): scanning reports unsupported and the pairing card shows
/// no Provision button.
/// </summary>
public sealed class UnavailableQrScanService : IQrScanService
{
    public bool IsSupported => false;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<string?> ScanAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
