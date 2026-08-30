// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.Core;

/// <summary>
/// Platform-agnostic scanner machinery: the advertisement cache with
/// expiration, the scan event stream, cache-first device retrieval, the
/// connected-peripheral registry, and canonical disposal. Platform backends
/// drive the radio and feed discoveries in through <see cref="Upsert"/>.
/// </summary>
public abstract class ScannerBase : IScanner
{
    private readonly Subject<ScanState> _scanEvents = new();
    private readonly ConcurrentDictionary<string, IAdvertisement> _cache = new();
    private readonly ConcurrentDictionary<string, IPeripheral> _connections = new();
    private readonly IDisposable _expiration;
    private int _disposed;

    protected ScannerBase(IOptions<BlueConnectOptions> options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        Options = options.Value;
        Logger = logger;
        _expiration = Observable
            .Interval(TimeSpan.FromSeconds(5))
            .Subscribe(_ => ExpireStaleEntries());
    }

    protected BlueConnectOptions Options { get; }

    /// <summary>Also used by the peripherals this scanner creates.</summary>
    protected internal ILogger Logger { get; }

    protected bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public IReadOnlyList<IAdvertisement> Peripherals => _cache.Values.ToArray();

    public abstract (int MinDbm, int MaxDbm) RssiLimits { get; }

    public abstract bool IsScanning { get; }

    public abstract Task Start(CancellationToken cancellationToken = default);

    public abstract IPeripheral CreatePeripheral(IAdvertisement advertisement);

    public IDisposable Subscribe(IObserver<ScanState> observer) => _scanEvents.Subscribe(observer);

    /// <summary>
    /// Retrieves a matching advertisement while scanning is running: the cache
    /// is consulted first, then the live scan stream. <paramref name="skipCache"/>
    /// ignores cached advertisements and waits for a fresh one.
    /// </summary>
    public async Task<IAdvertisement> ScanForDevice(
        Predicate<IAdvertisement> predicate,
        int timeoutSeconds = BlueConnectConstants.ScannerExpirationSeconds,
        bool skipCache = false,
        CancellationToken cancellationToken = default)
    {
        if (!skipCache && Peripherals.FirstOrDefault(a => predicate(a)) is { } cached)
        {
            return cached;
        }

        return await _scanEvents
            .Where(s => s.Type != ScanEventType.Expired && predicate(s.Advertisement))
            .Select(s => s.Advertisement)
            .FirstAsync()
            .Timeout(TimeSpan.FromSeconds(timeoutSeconds))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
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
        _expiration.Dispose();
        _scanEvents.OnCompleted();
        _scanEvents.Dispose();
    }

    /// <summary>
    /// Platform teardown: stop the radio and detach native callbacks.
    /// Guaranteed to run exactly once, before the shared cleanup.
    /// </summary>
    protected virtual void ReleaseCore()
    {
    }

    /// <summary>
    /// Records a discovery (new or updated) and emits the corresponding scan
    /// event. Platform callbacks construct the advertisement — having already
    /// copied everything they need out of any native objects — and hand it here.
    /// </summary>
    protected void Upsert(IAdvertisement advertisement)
    {
        // A native callback can race disposal; drop the event rather than
        // publish to a disposed subject.
        if (IsDisposed)
        {
            return;
        }

        var added = !_cache.ContainsKey(advertisement.Id);
        _cache[advertisement.Id] = advertisement;
        try
        {
            _scanEvents.OnNext(new ScanState(added ? ScanEventType.Added : ScanEventType.Updated, advertisement));
        }
        catch (ObjectDisposedException ex)
        {
            Logger.Here().LogDebug(ex, "Scan event for '{Device}' dropped: scanner was disposed concurrently.", advertisement.LocalName);
        }
    }

    protected bool TryGetCached(string id, out IAdvertisement advertisement)
        => _cache.TryGetValue(id, out advertisement!);

    /// <summary>Merges a new RSSI sample into a running min/max, ignoring out-of-range readings.</summary>
    protected Rssi MergeRssi(Rssi previous, int dbm)
        => dbm > RssiLimits.MinDbm && dbm < RssiLimits.MaxDbm
            ? new Rssi(dbm, Math.Min(previous.MinDbm, dbm), Math.Max(previous.MaxDbm, dbm))
            : previous;

    protected internal void Register(IPeripheral peripheral) => _connections[peripheral.Advertisement.Id] = peripheral;

    protected internal void Unregister(IPeripheral peripheral) => _connections.TryRemove(peripheral.Advertisement.Id, out _);

    /// <summary>Looks up a registered (connecting or connected) peripheral, e.g. to route central-level callbacks.</summary>
    protected bool TryGetConnection(string id, out IPeripheral peripheral)
        => _connections.TryGetValue(id, out peripheral!);

    private void ExpireStaleEntries()
    {
        var cutoff = DateTimeOffset.Now - TimeSpan.FromSeconds(BlueConnectConstants.ScannerExpirationSeconds);
        foreach (var (id, advertisement) in _cache)
        {
            // An entry stops advertising while we hold a connection to it; never expire those.
            if (advertisement.Timestamp < cutoff
                && !_connections.ContainsKey(id)
                && _cache.TryRemove(id, out var removed))
            {
                OnExpired(removed);
                _scanEvents.OnNext(new ScanState(ScanEventType.Expired, removed));
            }
        }
    }

    /// <summary>Hook for platforms that keep native state per cache entry.</summary>
    protected virtual void OnExpired(IAdvertisement advertisement)
    {
    }
}
