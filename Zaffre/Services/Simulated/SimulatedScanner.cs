// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;

using StonyGrey.BlueConnect.Abstractions;

namespace Zaffre.Services.Simulated;

/// <summary>
/// Stand-in scanner for the iOS simulator, which has no Bluetooth hardware.
/// Emits a handful of fake Nordic dev boards with wandering RSSI.
/// </summary>
public sealed class SimulatedScanner : IScanner, IDisposable
{
    private sealed record Ad(string Id, string LocalName, bool IsConnectable, bool IsSecured, AuthenticationScheme AuthenticationScheme, Rssi Rssi, DateTimeOffset Timestamp) : IAdvertisement
    {
        public bool Equals(IAdvertisement? other) => other is not null && Id == other.Id;
    }

    private static readonly string[] Names = ["nRF52840-DK", "nRF5340-DK", "nRF52-DK"];

    private readonly Subject<ScanState> _events = new();
    private readonly ConcurrentDictionary<string, Ad> _cache = new();
    private IDisposable? _timer;
    private bool _disposed;

    public IReadOnlyList<IAdvertisement> Peripherals => _cache.Values.ToArray();

    public (int MinDbm, int MaxDbm) RssiLimits => (-127, 0);

    public bool IsScanning => _timer is not null;

    public IDisposable Subscribe(IObserver<ScanState> observer) => _events.Subscribe(observer);

    public Task Start(CancellationToken cancellationToken = default)
    {
        _timer ??= Observable
            .Interval(TimeSpan.FromSeconds(1))
            .Subscribe(_ => EmitAdvertisements());
        return Task.CompletedTask;
    }

    public IPeripheral CreatePeripheral(IAdvertisement advertisement) => new SimulatedPeripheral(advertisement);

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

        return await _events
            .Where(s => s.Type != ScanEventType.Expired && predicate(s.Advertisement))
            .Select(s => s.Advertisement)
            .FirstAsync()
            .Timeout(TimeSpan.FromSeconds(timeoutSeconds))
            .ToTask(cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Dispose();
        _events.OnCompleted();
        _events.Dispose();
    }

    private void EmitAdvertisements()
    {
        // Mirrors the firmware: the simulated device advertises its pairing state.
        var secured = SimulatedPeripheral.IsDevicePaired;

        for (var i = 0; i < Names.Length; i++)
        {
            var id = $"SIM-{i:D4}";
            var dbm = -40 - (i * 15) + Random.Shared.Next(-6, 7);
            var added = !_cache.ContainsKey(id);
            var ad = _cache.AddOrUpdate(
                id,
                _ => new Ad(id, Names[i], true, secured, (i % 3) switch { 0 => AuthenticationScheme.Noise, 1 => AuthenticationScheme.Pake, _ => AuthenticationScheme.Edhoc }, new Rssi(dbm, dbm, dbm), DateTimeOffset.Now),
                (_, existing) => existing with
                {
                    IsSecured = secured,
                    Rssi = new Rssi(dbm, Math.Min(existing.Rssi.MinDbm, dbm), Math.Max(existing.Rssi.MaxDbm, dbm)),
                    Timestamp = DateTimeOffset.Now,
                });

            _events.OnNext(new ScanState(added ? ScanEventType.Added : ScanEventType.Updated, ad));
        }
    }
}
