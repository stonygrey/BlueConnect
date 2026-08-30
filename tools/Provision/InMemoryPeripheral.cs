// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.Provision;

/// <summary>
/// Two cross-wired in-memory peripherals for the loopback self-tests; only
/// Read/Write matter here.
/// </summary>
internal sealed class InMemoryPeripheral : IPeripheral
{
    private readonly BlockingCollection<byte[]> _inbound = [];
    private InMemoryPeripheral? _peer;

    public bool TamperNext { get; set; }

    public static (InMemoryPeripheral, InMemoryPeripheral) CreatePair()
    {
        var a = new InMemoryPeripheral();
        var b = new InMemoryPeripheral();
        a._peer = b;
        b._peer = a;
        return (a, b);
    }

    public IAdvertisement Advertisement => throw new NotSupportedException();
    public TimeSpan IdleTime => TimeSpan.Zero;
    public bool IsConnected => true;
    public int MTU => 244;

    public Task Connect(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task Disconnect(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<byte[]> Read(CancellationToken cancellationToken = default)
    {
        var chunk = _inbound.Take(cancellationToken);
        if (TamperNext)
        {
            TamperNext = false;
            chunk[^1] ^= 0x01;
        }

        return Task.FromResult(chunk);
    }

    public Task Write(IEnumerable<byte> bytes, CancellationToken cancellationToken = default)
    {
        _peer!._inbound.Add(bytes.ToArray(), cancellationToken);
        return Task.CompletedTask;
    }

    /// <summary>Push one complete (START|END) segment onto this peripheral's inbound queue.</summary>
    public void InjectFramed(byte type, ReadOnlySpan<byte> payload)
    {
        // seq 0, START|END, then type ‖ payload.
        var segment = new byte[2 + payload.Length];
        segment[0] = 0x03;
        segment[1] = type;
        payload.CopyTo(segment.AsSpan(2));
        _inbound.Add(segment);
    }

    public IDisposable Subscribe(IObserver<PeripheralState> observer) => throw new NotSupportedException();
    public void Dispose() { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
