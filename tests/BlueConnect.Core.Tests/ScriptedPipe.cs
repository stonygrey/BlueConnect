// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.Core.Tests;

/// <summary>
/// Test double: inbound ATT values are queued by the test, outbound writes
/// are recorded (and can be scripted to fail).
/// </summary>
internal sealed class ScriptedPipe : IPeripheral
{
    private readonly BlockingCollection<byte[]> _inbound = [];
    private readonly List<byte[]> _written = [];
    private int? _failAt;

    public int MTU { get; set; } = 20;

    public IReadOnlyList<byte[]> Written
    {
        get
        {
            lock (_written)
            {
                return [.. _written];
            }
        }
    }

    public void Enqueue(params byte[] pdu) => _inbound.Add(pdu);

    public void FailOnWrite(int index) => _failAt = index;

    public IAdvertisement Advertisement => throw new NotSupportedException();
    public TimeSpan IdleTime => TimeSpan.Zero;
    public bool IsConnected => true;

    public Task Connect(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task Disconnect(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<byte[]> Read(CancellationToken cancellationToken = default)
        => Task.Run(() => _inbound.Take(cancellationToken), cancellationToken);

    public Task Write(IEnumerable<byte> bytes, CancellationToken cancellationToken = default)
    {
        var payload = bytes as byte[] ?? bytes.ToArray();
        lock (_written)
        {
            if (_failAt == _written.Count)
            {
                _failAt = null;
                throw new BleTransportException("scripted write failure");
            }

            _written.Add(payload);
        }

        return Task.CompletedTask;
    }

    public IDisposable Subscribe(IObserver<PeripheralState> observer) => throw new NotSupportedException();
    public void Dispose() => _inbound.Dispose();
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
