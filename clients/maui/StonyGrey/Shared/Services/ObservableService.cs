// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Reactive.Subjects;

using StonyGrey.Shared.Services.Abstractions;

namespace StonyGrey.Shared.Services;

/// <summary>
/// Base for snapshot-plus-stream services: owns the BehaviorSubject (which
/// gives subscribers the current value immediately, honoring the
/// <see cref="IObservableService{TState}"/> contract), a serialization gate
/// for storage work, and the canonical disposal pattern. Derived classes
/// publish through <see cref="Publish"/> and put extra teardown in
/// <see cref="DisposeCore"/>; a service with async teardown (awaiting a
/// loop) overrides <see cref="DisposeAsyncCore"/> and ends it with
/// <see cref="ReleaseResources"/>.
/// </summary>
public abstract class ObservableService<TState> : IObservableService<TState>
{
    private readonly BehaviorSubject<TState> _state;
    private int _disposed;

    protected ObservableService(TState initialState)
    {
        _state = new BehaviorSubject<TState>(initialState);
    }

    public TState State => _state.Value;

    /// <summary>One-at-a-time gate for storage reads/writes.</summary>
    protected SemaphoreSlim Gate { get; } = new(1, 1);

    protected bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public IDisposable Subscribe(IObserver<TState> observer) => _state.Subscribe(observer);

    public virtual Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    protected void Publish(TState state) => _state.OnNext(state);

    protected void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

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

    /// <summary>
    /// The idempotent claim (hostile-concurrency rule: exactly one caller
    /// takes effect): completes the stream, runs <see cref="DisposeCore"/>, then
    /// drops the gate.
    /// </summary>
    protected void ReleaseResources()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _state.OnCompleted();
        _state.Dispose();
        DisposeCore();
        Gate.Dispose();
    }

    /// <summary>Extra teardown, inside the disposal claim.</summary>
    protected virtual void DisposeCore()
    {
    }
}
