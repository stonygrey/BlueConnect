// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

using global::CoreBluetooth;
using CoreFoundation;
using Foundation;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Core;

namespace StonyGrey.BlueConnect.CoreBluetooth;

/// <summary>
/// CoreBluetooth-backed scanner. Scans continuously for peripherals advertising
/// <see cref="BlueConnectOptions.ServiceUuid"/>; CoreBluetooth aggregates
/// advertisement and scan response data, so discoveries arrive here already merged.
/// </summary>
public class Scanner : ScannerBase
{
    private readonly CBUUID _serviceUuid;

    private readonly DispatchQueue _queue = new("com.stonygrey.blueconnect.ble");
    private readonly CBCentralManager _central;
    private readonly CentralDelegate _centralDelegate;
    private readonly ConcurrentDictionary<string, CBPeripheral> _nativePeripherals = new();
    private TaskCompletionSource? _startTcs;
    private volatile bool _startRequested;

    public Scanner(IOptions<BlueConnectOptions> options, ILogger<Scanner> logger)
        : base(options, logger)
    {
        _serviceUuid = CBUUID.FromString(Options.ServiceUuid.ToString());
        _centralDelegate = new CentralDelegate(this);
        _central = new CBCentralManager(_centralDelegate, _queue);
    }

    public override (int MinDbm, int MaxDbm) RssiLimits => (-127, 0);

    public override bool IsScanning => _central.IsScanning;

    /// <summary>
    /// Begins scanning. The returned task completes once scanning is actually
    /// running, which may wait on the central powering on.
    /// </summary>
    public override Task Start(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (_central.IsScanning)
        {
            return Task.CompletedTask;
        }

        _startRequested = true;
        var tcs = _startTcs;
        if (tcs is null)
        {
            var fresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            tcs = Interlocked.CompareExchange(ref _startTcs, fresh, null) ?? fresh;
        }

        TryStartScan();
        return tcs.Task.WaitAsync(cancellationToken);
    }

    public override IPeripheral CreatePeripheral(IAdvertisement advertisement)
        => TryGetCached(advertisement.Id, out var cached) && _nativePeripherals.TryGetValue(advertisement.Id, out var native)
            ? new Peripheral(this, _central, native, cached, Options)
            : throw new InvalidOperationException($"'{advertisement.LocalName}' ({advertisement.Id}) is no longer in the scan cache.");

    protected override void OnExpired(IAdvertisement advertisement)
        => _nativePeripherals.TryRemove(advertisement.Id, out _);

    protected override void ReleaseCore()
    {
        _startRequested = false;
        Interlocked.Exchange(ref _startTcs, null)?.TrySetCanceled();
        if (_central.IsScanning)
        {
            _central.StopScan();
        }

        _centralDelegate.Dispose();
        _central.Dispose();
        _queue.Dispose();
    }

    private void OnStateUpdated() => TryStartScan();

    private void TryStartScan()
    {
        if (!_startRequested)
        {
            return;
        }

        switch (_central.State)
        {
            case CBManagerState.PoweredOn:
                if (!_central.IsScanning)
                {
                    _central.ScanForPeripherals(
                        new[] { _serviceUuid },
                        new PeripheralScanningOptions { AllowDuplicatesKey = true });
                }

                _startTcs?.TrySetResult();
                break;

            case CBManagerState.Unsupported or CBManagerState.Unauthorized:
                _startTcs?.TrySetException(
                    new BleTransportException($"Bluetooth is unavailable: {_central.State}."));
                break;

            // Unknown / Resetting / PoweredOff: keep waiting for the next state update.
        }
    }

    private void OnDiscovered(CBPeripheral peripheral, NSDictionary advertisementData, NSNumber rssi)
    {
        var id = peripheral.Identifier.AsString();
        var localName = advertisementData[CBAdvertisement.DataLocalNameKey] as NSString;
        var name = localName?.ToString() ?? peripheral.Name ?? "(unnamed)";
        var isConnectable = (advertisementData[CBAdvertisement.IsConnectable] as NSNumber)?.BoolValue ?? false;
        var dbm = rssi.Int32Value;
        var now = DateTimeOffset.Now;

        // The Stony Grey manufacturer data (company id + status byte); only a
        // packet that carries it updates the flag — others keep the last value.
        bool? secured = null;
        if (advertisementData[CBAdvertisement.DataManufacturerDataKey] is NSData manufacturerData &&
            manufacturerData.Length >= 3)
        {
            var bytes = manufacturerData.ToArray();
            if (bytes[0] == (Options.ManufacturerCompanyId & 0xFF) &&
                bytes[1] == (Options.ManufacturerCompanyId >> 8))
            {
                secured = (bytes[2] & BlueConnectConstants.SecuredStatusMask) != 0;
            }
        }

        _nativePeripherals[id] = peripheral;

        var advertisement = TryGetCached(id, out var previous)
            ? new Advertisement(id, name, isConnectable, secured ?? previous.IsSecured, MergeRssi(previous.Rssi, dbm), now)
            : new Advertisement(id, name, isConnectable, secured ?? false, new Rssi(dbm, dbm, dbm), now);

        Upsert(advertisement);
    }

    internal void OnConnected(CBPeripheral peripheral)
    {
        if (TryGetConnection(peripheral.Identifier.AsString(), out var p) && p is Peripheral target)
        {
            target.OnConnected();
        }
    }

    internal void OnConnectFailed(CBPeripheral peripheral, NSError? error)
    {
        if (TryGetConnection(peripheral.Identifier.AsString(), out var p) && p is Peripheral target)
        {
            target.OnConnectFailed(error);
        }
    }

    internal void OnDisconnected(CBPeripheral peripheral, NSError? error)
    {
        if (TryGetConnection(peripheral.Identifier.AsString(), out var p) && p is Peripheral target)
        {
            target.OnDisconnected(error);
        }
    }

    private sealed class CentralDelegate(Scanner scanner) : CBCentralManagerDelegate
    {
        public override void UpdatedState(CBCentralManager central) => scanner.OnStateUpdated();

        public override void DiscoveredPeripheral(CBCentralManager central, CBPeripheral peripheral, NSDictionary advertisementData, NSNumber RSSI)
            => scanner.OnDiscovered(peripheral, advertisementData, RSSI);

        public override void ConnectedPeripheral(CBCentralManager central, CBPeripheral peripheral)
            => scanner.OnConnected(peripheral);

        public override void FailedToConnectPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError? error)
            => scanner.OnConnectFailed(peripheral, error);

        public override void DisconnectedPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError? error)
            => scanner.OnDisconnected(peripheral, error);
    }
}
