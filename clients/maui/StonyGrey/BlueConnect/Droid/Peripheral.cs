// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Android.Bluetooth;
using Android.Content;

using Microsoft.Extensions.Logging;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Core;

using JavaUuid = Java.Util.UUID;

namespace StonyGrey.BlueConnect.Droid;

/// <summary>
/// Android-backed peripheral bound to the GATT service and characteristics
/// in <see cref="BlueConnectOptions"/>. The device is re-acquired from the
/// adapter by MAC at connect time (nothing from the scan is retained), and
/// the GATT flow is: connect, request MTU, discover services, subscribe TX
/// notifications via the CCCD.
/// </summary>
public class Peripheral : PeripheralBase
{
    private static readonly JavaUuid ClientCharacteristicConfigUuid = JavaUuid.FromString("00002902-0000-1000-8000-00805f9b34fb")!;

    private readonly JavaUuid _serviceUuid;
    private readonly JavaUuid _rxUuid;
    private readonly JavaUuid _txUuid;

    private const int RequestedMtu = 247;

    private readonly VerdigrisGattCallback _callback;

    private BluetoothGatt? _gatt;
    private BluetoothGattCharacteristic? _rx;
    private BluetoothGattCharacteristic? _tx;
    private TaskCompletionSource? _connectTcs;
    private TaskCompletionSource? _mtuTcs;
    private TaskCompletionSource? _servicesTcs;
    private TaskCompletionSource? _subscribeTcs;
    private TaskCompletionSource? _writeTcs;
    private TaskCompletionSource? _disconnectTcs;
    private volatile int _mtu = 23;

    internal Peripheral(Scanner scanner, IAdvertisement advertisement, BlueConnectOptions options)
        : base(scanner, advertisement, options)
    {
        _serviceUuid = JavaUuid.FromString(options.ServiceUuid.ToString())!;
        _rxUuid = JavaUuid.FromString(options.RxCharacteristicUuid.ToString())!;
        _txUuid = JavaUuid.FromString(options.TxCharacteristicUuid.ToString())!;
        _callback = new VerdigrisGattCallback(this);
    }

    public override int MTU => IsConnected ? Math.Max(20, _mtu - 3) : 0;

    protected override async Task ConnectCore(CancellationToken cancellationToken)
    {
        var manager = Android.App.Application.Context.GetSystemService(Context.BluetoothService) as BluetoothManager;
        var adapter = manager?.Adapter
            ?? throw new BleTransportException("Bluetooth is unavailable.");
        using var device = adapter.GetRemoteDevice(Advertisement.Id)
            ?? throw new BleTransportException($"'{Advertisement.Id}' is not a valid device address.");

        _connectTcs = NewTcs();
        _gatt = device.ConnectGatt(Android.App.Application.Context, autoConnect: false, _callback, Android.Bluetooth.BluetoothTransports.Le)
            ?? throw new BleTransportException("ConnectGatt returned null.");
        await _connectTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        // MTU first so write chunking is correct before any traffic.
        _mtuTcs = NewTcs();
        if (_gatt.RequestMtu(RequestedMtu))
        {
            await _mtuTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        _servicesTcs = NewTcs();
        if (!_gatt.DiscoverServices())
        {
            throw new BleTransportException("Service discovery could not be started.");
        }

        await _servicesTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        var service = _gatt.GetService(_serviceUuid)
            ?? throw new BleTransportException($"Peripheral does not expose GATT service {Options.ServiceUuid}.");
        _rx = service.GetCharacteristic(_rxUuid)
            ?? throw new BleTransportException($"RX characteristic {Options.RxCharacteristicUuid} not found.");
        _tx = service.GetCharacteristic(_txUuid)
            ?? throw new BleTransportException($"TX characteristic {Options.TxCharacteristicUuid} not found.");

        if (!_gatt.SetCharacteristicNotification(_tx, true))
        {
            throw new BleTransportException("Enabling TX notifications failed.");
        }

        var descriptor = _tx.GetDescriptor(ClientCharacteristicConfigUuid)
            ?? throw new BleTransportException("TX CCCD descriptor not found.");

        _subscribeTcs = NewTcs();
        var enable = BluetoothGattDescriptor.EnableNotificationValue!.ToArray();
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            if (_gatt.WriteDescriptor(descriptor, enable) != (int)CurrentBluetoothStatusCodes.Success)
            {
                throw new BleTransportException("Writing the TX CCCD failed to start.");
            }
        }
        else
        {
#pragma warning disable CA1422 // legacy path for API < 33
            descriptor.SetValue(enable);
            if (!_gatt.WriteDescriptor(descriptor))
            {
                throw new BleTransportException("Writing the TX CCCD failed to start.");
            }
#pragma warning restore CA1422
        }

        await _subscribeTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task DisconnectCore(CancellationToken cancellationToken)
    {
        if (_gatt is not { } gatt)
        {
            return;
        }

        _disconnectTcs = NewTcs();
        gatt.Disconnect();
        try
        {
            await _disconnectTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CloseGatt();
        }
    }

    protected override async Task WriteChunk(byte[] chunk, CancellationToken cancellationToken)
    {
        if (_gatt is not { } gatt || _rx is not { } rx)
        {
            throw new InvalidOperationException("Peripheral is not connected.");
        }

        _writeTcs = NewTcs();
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            if (gatt.WriteCharacteristic(rx, chunk, (int)GattWriteType.Default) != (int)CurrentBluetoothStatusCodes.Success)
            {
                throw new BleTransportException("Write to RX failed to start.");
            }
        }
        else
        {
#pragma warning disable CA1422 // legacy path for API < 33
            rx.SetValue(chunk);
            rx.WriteType = GattWriteType.Default;
            if (!gatt.WriteCharacteristic(rx))
            {
                throw new BleTransportException("Write to RX failed to start.");
            }
#pragma warning restore CA1422
        }

        await _writeTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override void AbortConnect()
    {
        if (_gatt is { } gatt)
        {
            try
            {
                gatt.Disconnect();
            }
            catch (Exception ex)
            {
                // The adapter may already be off — a real radio event, not race noise.
                Logger.Here().LogInformation(ex, "Disconnecting '{Device}' during abort failed.", Advertisement.LocalName);
            }

            CloseGatt();
        }
    }

    protected override void ReleaseCore()
    {
        CloseGatt();
        _callback.Dispose();
    }

    private void CloseGatt()
    {
        // Interlocked claims make teardown exactly-once even when a requested
        // disconnect, an unexpected-drop callback, and disposal race.
        Interlocked.Exchange(ref _rx, null)?.Dispose();
        Interlocked.Exchange(ref _tx, null)?.Dispose();
        if (Interlocked.Exchange(ref _gatt, null) is { } gatt)
        {
            try
            {
                gatt.Close();
            }
            catch (Exception ex)
            {
                // Already closed.
                Logger.Here().LogDebug(ex, "Closing GATT for '{Device}' failed.", Advertisement.LocalName);
            }

            gatt.Dispose();
        }
    }

    private void HandleConnectionStateChange(GattStatus status, ProfileState newState)
    {
        switch (newState)
        {
            case ProfileState.Connected when status == GattStatus.Success:
                _connectTcs?.TrySetResult();
                break;

            case ProfileState.Disconnected:
                if (_disconnectTcs is { Task.IsCompleted: false } expected)
                {
                    expected.TrySetResult();
                    return;
                }

                var failure = new BleTransportException($"Peripheral disconnected: {status}.");
                _connectTcs?.TrySetException(failure);
                _mtuTcs?.TrySetException(failure);
                _servicesTcs?.TrySetException(failure);
                _subscribeTcs?.TrySetException(failure);
                _writeTcs?.TrySetException(failure);
                OnUnexpectedDisconnect();
                break;
        }
    }

    private void HandleMtuChanged(int mtu, GattStatus status)
    {
        if (status == GattStatus.Success)
        {
            _mtu = mtu;
        }
        else
        {
            // Not fatal; the default (23) applies.
            Logger.Here().LogInformation("MTU negotiation with '{Device}' failed ({Status}); using the default.", Advertisement.LocalName, status);
        }

        _mtuTcs?.TrySetResult();
    }

    private void HandleServicesDiscovered(GattStatus status)
        => Complete(_servicesTcs, status, "Service discovery");

    private void HandleDescriptorWrite(GattStatus status)
        => Complete(_subscribeTcs, status, "CCCD write");

    private void HandleCharacteristicWrite(GattStatus status)
        => Complete(_writeTcs, status, "Characteristic write");

    private void HandleNotification(byte[] value) => EnqueueReceived(value);

    private static void Complete(TaskCompletionSource? tcs, GattStatus status, string operation)
    {
        if (tcs is null)
        {
            return;
        }

        if (status == GattStatus.Success)
        {
            tcs.TrySetResult();
        }
        else
        {
            tcs.TrySetException(new BleTransportException($"{operation} failed: {status}."));
        }
    }

    private static TaskCompletionSource NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class VerdigrisGattCallback(Peripheral owner) : BluetoothGattCallback
    {
        public override void OnConnectionStateChange(BluetoothGatt? gatt, GattStatus status, ProfileState newState)
            => owner.HandleConnectionStateChange(status, newState);

        public override void OnMtuChanged(BluetoothGatt? gatt, int mtu, GattStatus status)
            => owner.HandleMtuChanged(mtu, status);

        public override void OnServicesDiscovered(BluetoothGatt? gatt, GattStatus status)
            => owner.HandleServicesDiscovered(status);

        public override void OnDescriptorWrite(BluetoothGatt? gatt, BluetoothGattDescriptor? descriptor, GattStatus status)
            => owner.HandleDescriptorWrite(status);

        public override void OnCharacteristicWrite(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic, GattStatus status)
            => owner.HandleCharacteristicWrite(status);

        // API 33+ delivers the payload directly; the legacy overload must not
        // also enqueue on those versions or notifications would double up.
        public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic, byte[] value)
            => owner.HandleNotification(value);

        public override void OnCharacteristicChanged(BluetoothGatt? gatt, BluetoothGattCharacteristic? characteristic)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                return;
            }

#pragma warning disable CA1422 // legacy path for API < 33
            if (characteristic?.GetValue() is { } value)
            {
                owner.HandleNotification(value);
            }
#pragma warning restore CA1422
        }
    }
}
