// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Shared.Services.Abstractions;

public interface IService : IDisposable, IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
