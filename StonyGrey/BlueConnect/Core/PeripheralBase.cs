// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Reactive.Subjects;

using Microsoft.Extensions.Logging;

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.Core;

/// <summary>
/// Platform-agnostic peripheral machinery: connect/disconnect orchestration
/// with timeout handling, the received-payload queue behind <see cref="Read"/>,
/// MTU-chunked writes, state publication, and canonical disposal. Platform
/// backends implement the *Core methods against their native GATT stack and
/// feed inbound notifications through <see cref="EnqueueReceived"/>.
/// </summary>
public abstract class PeripheralBase : IPeripheral
{
    private readonly ScannerBase _scanner;
    private readonly PeripheralState _state = new();
    private readonly Subject<PeripheralState> _stateChanges = new();
    private readonly SemaphoreSlim _packetSignal = new(0);
    private DateTimeOffset _lastActivity = DateTimeOffset.Now;
    private volatile bool _isConnected;
    private int _disposed;

    protected PeripheralBase(ScannerBase scanner, IAdvertisement advertisement, BlueConnectOptions options)
    {
        _scanner = scanner;
        Advertisement = advertisement;
        Options = options;
        Logger = scanner.Logger;
    }

    protected BlueConnectOptions Options { get; }

    /// <summary>Inherited from the scanner that created this peripheral.</summary>
    protected ILogger Logger { get; }

    protected bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public IAdvertisement Advertisement { get; }

    public TimeSpan IdleTime => DateTimeOffset.Now - _lastActivity;

    public bool IsConnected => _isConnected;

    public abstract int MTU { get; }

    public IDisposable Subscribe(IObserver<PeripheralState> observer) => _stateChanges.Subscribe(observer);

    public async Task Connect(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Options.ConnectTimeoutSeconds));

        _scanner.Register(this);

        try
        {
            await ConnectCore(timeout.Token).ConfigureAwait(false);

            _isConnected = true;
            Touch();
            Publish(ConnectionState.Connected);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AbortConnect();
            throw new TimeoutException($"Connecting to '{Advertisement.LocalName}' timed out after {Options.ConnectTimeoutSeconds}s.");
        }
        catch
        {
            AbortConnect();
            throw;
        }
    }

    public async Task Disconnect(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return;
        }

        await DisconnectCore(cancellationToken).ConfigureAwait(false);
        _isConnected = false;
        Publish(ConnectionState.Disconnected);
    }

    public async Task<byte[]> Read(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        while (true)
        {
            if (_state.Received.TryDequeue(out var payload))
            {
                return payload;
            }

            await _packetSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task Write(IEnumerable<byte> bytes, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (!_isConnected)
        {
            throw new InvalidOperationException("Peripheral is not connected.");
        }

        var payload = bytes as byte[] ?? bytes.ToArray();
        var mtu = Math.Max(1, MTU);
        for (var offset = 0; offset < payload.Length; offset += mtu)
        {
            var chunk = payload.AsSpan(offset, Math.Min(mtu, payload.Length - offset)).ToArray();
            await WriteChunk(chunk, cancellationToken).ConfigureAwait(false);
        }

        Touch();
    }

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

    /// <summary>
    /// Disconnects (bounded) before releasing, so a subsequent connect attempt
    /// to the same peripheral never overlaps teardown still in flight.
    /// </summary>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            if (_isConnected)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await Disconnect(timeout.Token).ConfigureAwait(false);
            }
            else
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await AbortConnectAsync(timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Disposal must not throw; the cleanup below still runs.
            Logger.Here().LogWarning(ex, "Teardown of '{Device}' failed during async disposal; releasing resources anyway.", Advertisement.LocalName);
        }

        ReleaseResources();
    }

    /// <summary>
    /// The sync path cannot wait for teardown confirmation; it requests
    /// cancellation and releases immediately. Prefer <see cref="DisposeAsync"/>.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || IsDisposed)
        {
            return;
        }

        AbortConnect();
        ReleaseResources();
    }

    /// <summary>Establish the link and prepare NUS (discovery, notification subscription).</summary>
    protected abstract Task ConnectCore(CancellationToken cancellationToken);

    /// <summary>Tear the link down, waiting for platform confirmation where the stack provides it.</summary>
    protected abstract Task DisconnectCore(CancellationToken cancellationToken);

    /// <summary>Write one MTU-sized chunk to the NUS RX characteristic with response.</summary>
    protected abstract Task WriteChunk(byte[] chunk, CancellationToken cancellationToken);

    /// <summary>Synchronously cancel any in-flight connect/link without waiting for confirmation.</summary>
    protected abstract void AbortConnect();

    /// <summary>
    /// Asynchronously cancel an in-flight connect, waiting for platform
    /// confirmation where possible. Defaults to the sync abort.
    /// </summary>
    protected virtual Task AbortConnectAsync(CancellationToken cancellationToken)
    {
        AbortConnect();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs the teardown exactly once, no matter how many threads race into
    /// it: platform cleanup first (detaching native callbacks), then the
    /// shared resources.
    /// </summary>
    protected void ReleaseResources()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        ReleaseCore();
        _scanner.Unregister(this);
        _isConnected = false;
        _stateChanges.OnCompleted();
        _stateChanges.Dispose();
        _packetSignal.Dispose();
    }

    /// <summary>
    /// Platform teardown: release native handles and detach callbacks.
    /// Guaranteed to run exactly once, before the shared cleanup.
    /// </summary>
    protected virtual void ReleaseCore()
    {
    }

    /// <summary>Queues an inbound notification payload for <see cref="Read"/>.</summary>
    protected void EnqueueReceived(byte[] payload)
    {
        // A native notification can race disposal; drop it rather than touch
        // disposed state.
        if (IsDisposed)
        {
            return;
        }

        _state.Received.Enqueue(payload);
        Touch();
        Publish(_state.ConnectionState);
        try
        {
            _packetSignal.Release();
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Here().LogDebug(ex, "Notification from '{Device}' dropped: peripheral was disposed concurrently.", Advertisement.LocalName);
        }
    }

    /// <summary>Marks an unrequested link drop (requested disconnects go through <see cref="Disconnect"/>).</summary>
    protected void OnUnexpectedDisconnect()
    {
        if (!_isConnected)
        {
            return;
        }

        _isConnected = false;
        Publish(ConnectionState.Disconnected);
    }

    protected void Touch() => _lastActivity = DateTimeOffset.Now;

    private void Publish(ConnectionState connectionState)
    {
        _state.ConnectionState = connectionState;
        _state.Rssi = Advertisement.Rssi.ValueDbm;
        try
        {
            _stateChanges.OnNext(_state);
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Here().LogDebug(ex, "State change for '{Device}' dropped: peripheral was disposed concurrently.", Advertisement.LocalName);
        }
    }
}
