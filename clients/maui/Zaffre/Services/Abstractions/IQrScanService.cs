// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.Shared.Services.Abstractions;

namespace Zaffre.Services.Abstractions;

/// <summary>
/// Camera QR scanning for the Noise tab's Provision button. Returns the
/// decoded text of the first QR seen, or null when the user cancels or the
/// platform has no scanner (Windows).
/// </summary>
public interface IQrScanService : IService
{
    bool IsSupported { get; }

    Task<string?> ScanAsync(CancellationToken cancellationToken = default);
}
