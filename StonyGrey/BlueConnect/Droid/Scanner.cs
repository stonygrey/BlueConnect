// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Android.Bluetooth;
using Android.Bluetooth.LE;
using Android.Content;
using Android.OS;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Core;

namespace StonyGrey.BlueConnect.Droid;

/// <summary>
/// Android-backed scanner over <see cref="BluetoothLeScanner"/>, filtered on
/// <see cref="BlueConnectOptions.ServiceUuid"/> (Android merges advertisement
/// and scan response into one <c>ScanResult</c>, so the filter sees the
/// service UUID even though the firmware puts it in the scan response).
///
/// Discovery data is copied out of the Java objects immediately and the
/// wrappers disposed — advertisements handed to clients are plain records.
/// Requires BLUETOOTH_SCAN (and BLUETOOTH_CONNECT to connect) at runtime.
/// </summary>
public class Scanner : ScannerBase
{
    private readonly VerdigrisScanCallback _callback;
    private BluetoothLeScanner? _scanner;
    private volatile bool _isScanning;

    public Scanner(IOptions<BlueConnectOptions> options, ILogger<Scanner> logger)
        : base(options, logger)
        => _callback = new VerdigrisScanCallback(this);

    public override (int MinDbm, int MaxDbm) RssiLimits => (-127, 20);

    public override bool IsScanning => _isScanning;

    public override Task Start(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        if (_isScanning)
        {
            return Task.CompletedTask;
        }

        var manager = Android.App.Application.Context.GetSystemService(Context.BluetoothService) as BluetoothManager;
        var adapter = manager?.Adapter;
        if (adapter is null || !adapter.IsEnabled)
        {
            return Task.FromException(new BleTransportException("Bluetooth is unavailable or turned off."));
        }

        _scanner = adapter.BluetoothLeScanner
            ?? throw new BleTransportException("BluetoothLeScanner is unavailable.");

        using var filterBuilder = new ScanFilter.Builder();
        using var parcelUuid = ParcelUuid.FromString(Options.ServiceUuid.ToString());
        var filter = filterBuilder.SetServiceUuid(parcelUuid).Build()
            ?? throw new InvalidOperationException("Failed to build scan filter.");

        using var settingsBuilder = new ScanSettings.Builder();
        var settings = settingsBuilder.SetScanMode(Android.Bluetooth.LE.ScanMode.LowLatency).Build()
            ?? throw new InvalidOperationException("Failed to build scan settings.");

        try
        {
            _scanner.StartScan(new[] { filter }, settings, _callback);
        }
        catch (Java.Lang.SecurityException ex)
        {
            return Task.FromException(new BleTransportException(
                "Bluetooth scan permission has not been granted (BLUETOOTH_SCAN).", ex));
        }

        _isScanning = true;
        return Task.CompletedTask;
    }

    public override IPeripheral CreatePeripheral(IAdvertisement advertisement)
        => TryGetCached(advertisement.Id, out var cached)
            ? new Peripheral(this, cached, Options)
            : throw new InvalidOperationException($"'{advertisement.LocalName}' ({advertisement.Id}) is no longer in the scan cache.");

    protected override void ReleaseCore()
    {
        var scanner = Interlocked.Exchange(ref _scanner, null);
        if (_isScanning && scanner is not null)
        {
            try
            {
                scanner.StopScan(_callback);
            }
            catch (Exception ex)
            {
                // The adapter may already be off.
                Logger.Here().LogDebug(ex, "Stopping the BLE scan during disposal failed.");
            }
        }

        _isScanning = false;
        _callback.Dispose();
        scanner?.Dispose();
    }

    private void OnScanResult(ScanResult result)
    {
        // Copy everything out of the Java objects, then dispose the wrappers:
        // nothing Java-owned survives past this method.
        string? id;
        string? name;
        bool connectable;
        int dbm;
        bool? secured = null;
        AuthenticationScheme? scheme = null;

        var device = result.Device;
        var record = result.ScanRecord;
        try
        {
            id = device?.Address;
            name = record?.DeviceName;
            connectable = !OperatingSystem.IsAndroidVersionAtLeast(26) || result.IsConnectable;
            dbm = result.Rssi;

            // Android strips the company id: the returned bytes are the status payload.
            var manufacturerData = record?.GetManufacturerSpecificData(Options.ManufacturerCompanyId);
            if (manufacturerData is { Length: >= 1 })
            {
                secured = (manufacturerData[0] & BlueConnectConstants.SecuredStatusMask) != 0;
                scheme = BlueConnectConstants.SchemeFromStatus(manufacturerData[0]);
            }
        }
        finally
        {
            record?.Dispose();
            device?.Dispose();
            result.Dispose();
        }

        if (id is null)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var displayName = string.IsNullOrEmpty(name) ? "(unnamed)" : name;

        var advertisement = TryGetCached(id, out var previous)
            ? new Advertisement(
                id,
                string.IsNullOrEmpty(name) ? previous.LocalName : name,
                connectable,
                secured ?? previous.IsSecured,
                scheme ?? previous.AuthenticationScheme,
                MergeRssi(previous.Rssi, dbm),
                now)
            : new Advertisement(id, displayName, connectable, secured ?? false, scheme ?? AuthenticationScheme.Unknown, new Rssi(dbm, dbm, dbm), now);

        Upsert(advertisement);
    }

    private void OnScanFailed(ScanFailure errorCode)
    {
        Logger.Here().LogWarning("BLE scan failed: {Error}.", errorCode);
        _isScanning = errorCode == ScanFailure.AlreadyStarted && _isScanning;
    }

    private sealed class VerdigrisScanCallback(Scanner scanner) : ScanCallback
    {
        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            if (result is not null)
            {
                scanner.OnScanResult(result);
            }
        }

        public override void OnBatchScanResults(IList<ScanResult>? results)
        {
            if (results is null)
            {
                return;
            }

            foreach (var result in results)
            {
                scanner.OnScanResult(result);
            }
        }

        public override void OnScanFailed(ScanFailure errorCode) => scanner.OnScanFailed(errorCode);
    }
}
