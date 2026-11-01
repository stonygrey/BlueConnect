// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using System.Text.Json;

namespace StonyGrey.BlueConnect.Abstractions;

public enum ConnectionState
{
    Disconnected,
    Connected
}

public record PeripheralState
{
    public ConnectionState ConnectionState { get; set; } = ConnectionState.Disconnected;

    public int Rssi { get; set; }

    public ConcurrentQueue<byte[]> Received { get; } = new();
}

public interface IPeripheral : IObservable<PeripheralState>, IDisposable, IAsyncDisposable
{
    IAdvertisement Advertisement { get; }

    TimeSpan IdleTime { get; }

    bool IsConnected { get; }

    int MTU { get; }

    Task Connect(CancellationToken cancellationToken = default);

    Task Disconnect(CancellationToken cancellationToken = default);

    Task<byte[]> Read(CancellationToken cancellationToken = default);

    Task Write(IEnumerable<byte> bytes, CancellationToken cancellationToken = default);
}
