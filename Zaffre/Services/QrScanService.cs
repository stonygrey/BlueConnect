// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using BarcodeScanning;

using Zaffre.Services.Abstractions;

namespace Zaffre.Services;

/// <summary>
/// Camera QR scanning via BarcodeScanning.Native.Maui: asks for the camera
/// permission, pushes the modal <see cref="QrScanPage"/> over the Blazor UI,
/// and returns the first QR's text (null on cancel or denied permission).
/// </summary>
public sealed class QrScanService : IQrScanService
{
    public bool IsSupported => true;

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<string?> ScanAsync(CancellationToken cancellationToken = default)
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (!await Methods.AskForRequiredPermissionAsync())
            {
                return null;
            }

            var navigation = (Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation)
                ?? throw new InvalidOperationException("No navigation surface for the scanner page.");

            var page = new QrScanPage();
            await navigation.PushModalAsync(page);
            return await page.Result;
        });

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
