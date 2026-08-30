// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.Core.Tests;

/// <summary>Two cross-wired in-memory peripherals. Each <see cref="IPeripheral.Write"/> is one ATT value.</summary>
internal sealed class LoopbackPipe : IPeripheral
{
    private readonly BlockingCollection<byte[]> _inbound = [];
    private LoopbackPipe? _peer;

    public int MTU { get; set; } = 20;

    public static (LoopbackPipe A, LoopbackPipe B) Pair(int mtu = 20)
    {
        var a = new LoopbackPipe { MTU = mtu };
        var b = new LoopbackPipe { MTU = mtu };
        a._peer = b;
        b._peer = a;
        return (a, b);
    }

    public IAdvertisement Advertisement => throw new NotSupportedException();
    public TimeSpan IdleTime => TimeSpan.Zero;
    public bool IsConnected => true;

    public Task Connect(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task Disconnect(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<byte[]> Read(CancellationToken cancellationToken = default)
        => Task.Run(() => _inbound.Take(cancellationToken), cancellationToken);

    public Task Write(IEnumerable<byte> bytes, CancellationToken cancellationToken = default)
    {
        _peer!._inbound.Add(bytes as byte[] ?? bytes.ToArray(), cancellationToken);
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
