// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Reactive.Disposables;

using ReactiveUI.Reactive;

namespace Zaffre.ViewModels;

/// <summary>
/// Base for view models: ReactiveUI change notification plus the canonical
/// dispose/async-dispose pattern over a shared <see cref="Disposables"/> bag.
/// </summary>
public abstract class ViewModelBase : ReactiveObject, IDisposable, IAsyncDisposable
{
    private bool _disposed;

    /// <summary>Subscriptions and other resources released when the view model is disposed.</summary>
    protected CompositeDisposable Disposables { get; } = [];

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(disposing: false);
        GC.SuppressFinalize(this);
    }

    protected virtual ValueTask DisposeAsyncCore()
    {
        ReleaseResources();
        return ValueTask.CompletedTask;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseResources();
        }
    }

    private void ReleaseResources()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Disposables.Dispose();
    }
}
