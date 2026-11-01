// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using global::CoreBluetooth;
using Foundation;

using Microsoft.Extensions.Logging;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Core;

namespace StonyGrey.BlueConnect.CoreBluetooth;

/// <summary>
/// CoreBluetooth-backed peripheral bound to the GATT service and
/// characteristics in <see cref="BlueConnectOptions"/>: writes target RX,
/// notifications on TX are queued for <see cref="PeripheralBase.Read"/>.
/// </summary>
public class Peripheral : PeripheralBase
{
    private readonly CBUUID _serviceUuid;
    private readonly CBUUID _rxUuid;
    private readonly CBUUID _txUuid;

    private readonly CBCentralManager _central;
    private readonly CBPeripheral _cb;
    private readonly PeripheralDelegate _delegate;

    private TaskCompletionSource? _connectTcs;
    private TaskCompletionSource? _disconnectTcs;
    private TaskCompletionSource? _servicesTcs;
    private TaskCompletionSource? _characteristicsTcs;
    private TaskCompletionSource? _writeTcs;
    private CBCharacteristic? _rx;
    private CBCharacteristic? _tx;

    internal Peripheral(Scanner scanner, CBCentralManager central, CBPeripheral cb, IAdvertisement advertisement, BlueConnectOptions options)
        : base(scanner, advertisement, options)
    {
        _serviceUuid = CBUUID.FromString(options.ServiceUuid.ToString());
        _rxUuid = CBUUID.FromString(options.RxCharacteristicUuid.ToString());
        _txUuid = CBUUID.FromString(options.TxCharacteristicUuid.ToString());
        _central = central;
        _cb = cb;
        _delegate = new PeripheralDelegate(this);
    }

    // WithoutResponse reports the true ATT payload (MTU-3). WithResponse would
    // report 512 on iOS because CoreBluetooth converts larger writes
    // into ATT long writes, which many peripherals (Zephyr by default) reject.
    public override int MTU => IsConnected ? (int)_cb.GetMaximumWriteValueLength(CBCharacteristicWriteType.WithoutResponse) : 0;

    protected override async Task ConnectCore(CancellationToken cancellationToken)
    {
        _cb.Delegate = _delegate;

        _connectTcs = NewTcs();
        _central.ConnectPeripheral(_cb);
        await _connectTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

        _servicesTcs = NewTcs();
        _cb.DiscoverServices(new[] { _serviceUuid });
        await _servicesTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        var service = _cb.Services?.FirstOrDefault(s => s.UUID.Uuid == _serviceUuid.Uuid)
            ?? throw new BleTransportException($"Peripheral does not expose GATT service {Options.ServiceUuid}.");

        _characteristicsTcs = NewTcs();
        _cb.DiscoverCharacteristics(new[] { _rxUuid, _txUuid }, service);
        await _characteristicsTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        _rx = service.Characteristics?.FirstOrDefault(c => c.UUID.Uuid == _rxUuid.Uuid)
            ?? throw new BleTransportException($"RX characteristic {Options.RxCharacteristicUuid} not found.");
        _tx = service.Characteristics?.FirstOrDefault(c => c.UUID.Uuid == _txUuid.Uuid)
            ?? throw new BleTransportException($"TX characteristic {Options.TxCharacteristicUuid} not found.");

        _cb.SetNotifyValue(true, _tx);
    }

    protected override async Task DisconnectCore(CancellationToken cancellationToken)
    {
        _disconnectTcs = NewTcs();
        _central.CancelPeripheralConnection(_cb);
        await _disconnectTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task WriteChunk(byte[] chunk, CancellationToken cancellationToken)
    {
        if (_rx is null)
        {
            throw new InvalidOperationException("Peripheral is not connected.");
        }

        _writeTcs = NewTcs();
        _cb.WriteValue(NSData.FromArray(chunk), _rx, CBCharacteristicWriteType.WithResponse);
        await _writeTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override void AbortConnect() => _central.CancelPeripheralConnection(_cb);

    protected override async Task AbortConnectAsync(CancellationToken cancellationToken)
    {
        if (_connectTcs is not { Task.IsCompleted: false })
        {
            return;
        }

        // A connect attempt is still pending; cancel it and wait for the
        // delegate to confirm so a retry never overlaps the cancellation.
        _disconnectTcs = NewTcs();
        _central.CancelPeripheralConnection(_cb);
        try
        {
            await _disconnectTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            Logger.Here().LogInformation(ex, "Timed out waiting for CoreBluetooth to confirm the aborted connect to '{Device}'.", Advertisement.LocalName);
        }
    }

    protected override void ReleaseCore()
    {
        _cb.Delegate = null!;
        _delegate.Dispose();
    }

    internal void OnConnected() => _connectTcs?.TrySetResult();

    internal void OnConnectFailed(NSError? error)
        => _connectTcs?.TrySetException(new BleTransportException($"Connect failed: {error?.LocalizedDescription ?? "unknown error"}"));

    internal void OnDisconnected(NSError? error)
    {
        if (_disconnectTcs is { Task.IsCompleted: false } expected)
        {
            expected.TrySetResult();
            return;
        }

        // Unexpected drop: fail whatever is in flight.
        var failure = new BleTransportException($"Peripheral disconnected: {error?.LocalizedDescription ?? "no error"}");
        _connectTcs?.TrySetException(failure);
        _servicesTcs?.TrySetException(failure);
        _characteristicsTcs?.TrySetException(failure);
        _writeTcs?.TrySetException(failure);
        OnUnexpectedDisconnect();
    }

    private void OnServicesDiscovered(NSError? error) => Complete(_servicesTcs, error);

    private void OnCharacteristicsDiscovered(NSError? error) => Complete(_characteristicsTcs, error);

    private void OnWrote(NSError? error) => Complete(_writeTcs, error);

    private void OnNotification(CBCharacteristic characteristic, NSError? error)
    {
        if (error is not null || characteristic.Value is null)
        {
            return;
        }

        EnqueueReceived(characteristic.Value.ToArray());
    }

    private static void Complete(TaskCompletionSource? tcs, NSError? error)
    {
        if (tcs is null)
        {
            return;
        }

        if (error is null)
        {
            tcs.TrySetResult();
        }
        else
        {
            tcs.TrySetException(new BleTransportException(error.LocalizedDescription));
        }
    }

    private static TaskCompletionSource NewTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class PeripheralDelegate(Peripheral owner) : CBPeripheralDelegate
    {
        public override void DiscoveredService(CBPeripheral peripheral, NSError? error)
            => owner.OnServicesDiscovered(error);

        public override void DiscoveredCharacteristics(CBPeripheral peripheral, CBService service, NSError? error)
            => owner.OnCharacteristicsDiscovered(error);

        public override void WroteCharacteristicValue(CBPeripheral peripheral, CBCharacteristic characteristic, NSError? error)
            => owner.OnWrote(error);

        public override void UpdatedCharacterteristicValue(CBPeripheral peripheral, CBCharacteristic characteristic, NSError? error)
            => owner.OnNotification(characteristic, error);
    }
}
