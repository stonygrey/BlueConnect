// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Windows.Devices.Bluetooth.Advertisement;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Core;

namespace StonyGrey.BlueConnect.WinRT;

/// <summary>
/// WinRT-backed scanner over <see cref="BluetoothLEAdvertisementWatcher"/>.
///
/// The watcher runs unfiltered on purpose: the name is in the primary
/// advertisement and the service UUID in the scan response, and a
/// ServiceUuids watcher filter would only surface the scan-response
/// packets (losing the name). Packets are merged per address instead, and an
/// entry is only surfaced once <see cref="BlueConnectOptions.ServiceUuid"/>
/// has been seen from it.
/// </summary>
public class Scanner : ScannerBase
{
    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private readonly ConcurrentDictionary<string, Advertisement> _pending = new();

    public Scanner(IOptions<BlueConnectOptions> options, ILogger<Scanner> logger)
        : base(options, logger)
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
        };
        _watcher.Received += OnReceived;
    }

    public override (int MinDbm, int MaxDbm) RssiLimits => (-127, 20);

    public override bool IsScanning => _watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started;

    /// <summary>
    /// Begins scanning. Unlike CoreBluetooth there is no power-on wait; the
    /// watcher aborts (asynchronously) if the radio is unavailable.
    /// </summary>
    public override Task Start(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
        {
            return Task.CompletedTask;
        }

        _watcher.Start();

        return _watcher.Status == BluetoothLEAdvertisementWatcherStatus.Aborted
            ? Task.FromException(new BleTransportException("Bluetooth is unavailable: the advertisement watcher aborted."))
            : Task.CompletedTask;
    }

    public override IPeripheral CreatePeripheral(IAdvertisement advertisement)
        => TryGetCached(advertisement.Id, out var cached) && cached is Advertisement winrt
            ? new Peripheral(this, winrt, Options)
            : throw new InvalidOperationException($"'{advertisement.LocalName}' ({advertisement.Id}) is no longer in the scan cache.");

    protected override void ReleaseCore()
    {
        _watcher.Received -= OnReceived;
        if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
        {
            _watcher.Stop();
        }
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var id = FormatMac(args.BluetoothAddress);
        var name = args.Advertisement.LocalName;
        var hasService = args.Advertisement.ServiceUuids.Contains(Options.ServiceUuid);
        var connectable = args.IsConnectable;
        var dbm = args.RawSignalStrengthInDBm;
        var now = DateTimeOffset.Now;

        // The Stony Grey manufacturer data (status byte); only a packet that
        // carries it updates the flag — others keep the last value.
        bool? secured = null;
        AuthenticationScheme? scheme = null;
        foreach (var manufacturerData in args.Advertisement.ManufacturerData)
        {
            if (manufacturerData.CompanyId == Options.ManufacturerCompanyId &&
                manufacturerData.Data.Length >= 1)
            {
                var status = new byte[1];
                Windows.Storage.Streams.DataReader.FromBuffer(manufacturerData.Data).ReadBytes(status);
                secured = (status[0] & BlueConnectConstants.SecuredStatusMask) != 0;
                scheme = BlueConnectConstants.SchemeFromStatus(status[0]);
                break;
            }
        }

        // Already surfaced: merge against the shared cache and update.
        if (TryGetCached(id, out var cached) && cached is Advertisement existing)
        {
            Upsert(existing with
            {
                LocalName = string.IsNullOrEmpty(name) ? existing.LocalName : name,
                IsConnectable = existing.IsConnectable || connectable,
                IsSecured = secured ?? existing.IsSecured,
                AuthenticationScheme = scheme ?? existing.AuthenticationScheme,
                Rssi = MergeRssi(existing.Rssi, dbm),
                Timestamp = now,
            });
            return;
        }

        // Not yet surfaced: merge packets until the configured service UUID has been seen.
        var merged = _pending.AddOrUpdate(
            id,
            _ => new Advertisement(id, name, connectable, secured ?? false, scheme ?? AuthenticationScheme.Unknown, new Rssi(dbm, dbm, dbm), now, args.BluetoothAddress),
            (_, pending) => pending with
            {
                LocalName = string.IsNullOrEmpty(name) ? pending.LocalName : name,
                IsConnectable = pending.IsConnectable || connectable,
                IsSecured = secured ?? pending.IsSecured,
                AuthenticationScheme = scheme ?? pending.AuthenticationScheme,
                Rssi = MergeRssi(pending.Rssi, dbm),
                Timestamp = now,
            });

        if (hasService)
        {
            _pending.TryRemove(id, out _);
            Upsert(merged);
        }

        PruneStalePending(now);
    }

    private void PruneStalePending(DateTimeOffset now)
    {
        var cutoff = now - TimeSpan.FromSeconds(BlueConnectConstants.ScannerExpirationSeconds);
        foreach (var (id, pending) in _pending)
        {
            if (pending.Timestamp < cutoff)
            {
                _pending.TryRemove(id, out _);
            }
        }
    }

    private static string FormatMac(ulong address)
        => string.Join(":", Enumerable.Range(0, 6).Reverse().Select(i => ((address >> (8 * i)) & 0xFF).ToString("X2")));
}
