// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices.WindowsRuntime;

using Microsoft.Extensions.Logging;

using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Core;

namespace StonyGrey.BlueConnect.WinRT;

/// <summary>
/// WinRT-backed peripheral bound to the GATT service and characteristics
/// in <see cref="BlueConnectOptions"/>. Windows has no explicit GATT
/// disconnect; the OS drops the link when the last handle is closed, so
/// disconnecting unsubscribes and disposes the native objects.
/// </summary>
public class Peripheral : PeripheralBase
{

    private readonly Advertisement _advertisement;

    private BluetoothLEDevice? _device;
    private GattDeviceService? _service;
    private GattCharacteristic? _rx;
    private GattCharacteristic? _tx;

    internal Peripheral(Scanner scanner, Advertisement advertisement, BlueConnectOptions options)
        : base(scanner, advertisement, options)
        => _advertisement = advertisement;

    public override int MTU => IsConnected && _service?.Session is { } session
        ? Math.Max(20, session.MaxPduSize - 3)
        : 0;

    protected override async Task ConnectCore(CancellationToken cancellationToken)
    {
        _device = await BluetoothLEDevice.FromBluetoothAddressAsync(_advertisement.Address)
            .AsTask(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BleTransportException($"'{_advertisement.LocalName}' is not reachable.");
        _device.ConnectionStatusChanged += OnConnectionStatusChanged;

        // The uncached service query is what actually establishes the link.
        var services = await _device.GetGattServicesForUuidAsync(Options.ServiceUuid, BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
        {
            throw new BleTransportException(
                $"Peripheral does not expose GATT service {Options.ServiceUuid} ({services.Status}).");
        }

        _service = services.Services[0];

        var rx = await _service.GetCharacteristicsForUuidAsync(Options.RxCharacteristicUuid, BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        _rx = rx.Status == GattCommunicationStatus.Success && rx.Characteristics.Count > 0
            ? rx.Characteristics[0]
            : throw new BleTransportException($"RX characteristic {Options.RxCharacteristicUuid} not found.");

        var tx = await _service.GetCharacteristicsForUuidAsync(Options.TxCharacteristicUuid, BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        _tx = tx.Status == GattCommunicationStatus.Success && tx.Characteristics.Count > 0
            ? tx.Characteristics[0]
            : throw new BleTransportException($"TX characteristic {Options.TxCharacteristicUuid} not found.");

        _tx.ValueChanged += OnValueChanged;
        var subscribe = await _tx.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (subscribe != GattCommunicationStatus.Success)
        {
            throw new BleTransportException($"Subscribing to TX notifications failed: {subscribe}.");
        }
    }

    protected override async Task DisconnectCore(CancellationToken cancellationToken)
    {
        if (_tx is { } tx)
        {
            tx.ValueChanged -= OnValueChanged;
            try
            {
                await tx.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Best effort; the device may already be gone.
                Logger.Here().LogDebug(ex, "Unsubscribing TX on '{Device}' during disconnect failed.", Advertisement.LocalName);
            }
        }

        DisposeNative();
    }

    protected override async Task WriteChunk(byte[] chunk, CancellationToken cancellationToken)
    {
        if (_rx is null)
        {
            throw new InvalidOperationException("Peripheral is not connected.");
        }

        var status = await _rx.WriteValueAsync(chunk.AsBuffer(), GattWriteOption.WriteWithResponse)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        if (status != GattCommunicationStatus.Success)
        {
            throw new BleTransportException($"Write to RX failed: {status}.");
        }
    }

    protected override void AbortConnect() => DisposeNative();

    protected override void ReleaseCore() => DisposeNative();

    private void DisposeNative()
    {
        // Interlocked claims make teardown exactly-once even when a requested
        // disconnect, an unexpected-drop callback, and disposal race.
        if (Interlocked.Exchange(ref _tx, null) is { } tx)
        {
            tx.ValueChanged -= OnValueChanged;
        }

        Interlocked.Exchange(ref _rx, null);
        Interlocked.Exchange(ref _service, null)?.Dispose();

        if (Interlocked.Exchange(ref _device, null) is { } device)
        {
            device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            device.Dispose();
        }
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        => EnqueueReceived(args.CharacteristicValue.ToArray());

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            OnUnexpectedDisconnect();
        }
    }
}
