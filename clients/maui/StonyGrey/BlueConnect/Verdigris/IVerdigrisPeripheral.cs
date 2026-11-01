// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using StonyGrey.BlueConnect.Abstractions;

namespace StonyGrey.BlueConnect.Verdigris;

/// <summary>
/// The Verdigris command/response API over a connected <see cref="IPeripheral"/>.
/// Values come back as values; anything abnormal throws — <see cref="VerdigrisException"/>
/// when the device rejects a request, <see cref="BleTransportException"/> when the
/// link fails, <see cref="TimeoutException"/> when a command exceeds the configured
/// command timeout.
/// </summary>
public interface IVerdigrisPeripheral : IDisposable, IAsyncDisposable
{
    /// <summary>The underlying peripheral. Owned: disposing this instance disposes it.</summary>
    IPeripheral Peripheral { get; }

    Task<byte[]> Echo(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);

    Task<double> SquareRoot(double value, CancellationToken cancellationToken = default);

    Task<ulong> Fibonacci(uint n, CancellationToken cancellationToken = default);

    Task<DateTimeOffset> GetTime(CancellationToken cancellationToken = default);

    Task SetTime(DateTimeOffset time, CancellationToken cancellationToken = default);

    /// <summary>Reads the app-controlled LED (LED 1 on the DK). Low privilege.</summary>
    Task<bool> GetLed(CancellationToken cancellationToken = default);

    /// <summary>Switches the app-controlled LED; returns the resulting state. High privilege.</summary>
    Task<bool> SetLed(bool on, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the serial number in the device's advertised "Verdigris NNNN"
    /// name (persisted on-device); returns the resulting name. High privilege.
    /// </summary>
    Task<string> SetSerialNumber(ushort serialNumber, CancellationToken cancellationToken = default);

    /// <summary>Reads the device's advertised name. Low privilege.</summary>
    Task<string> GetSerialNumber(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the pairing ceremony over a secure session: the device arms its
    /// confirmation window (LED 3 + Button 3 = low, LED 4 + Button 4 = high)
    /// and this call completes once the matching button is pressed
    /// (VerdigrisException otherwise). On success the session's client key is
    /// pinned into the requested privilege slot; returns the device's proven
    /// static key for the caller to pin.
    /// </summary>
    Task<byte[]> Pair(Protocol.Privilege privilege, CancellationToken cancellationToken = default);

    /// <summary>Clears the caller's own pairing slot on the device.</summary>
    Task Unpair(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enrollment (the session must have been opened with the device's
    /// factory enrollment key, which may send nothing else): pins
    /// <paramref name="clientPublicKey"/> into the device's low-privilege
    /// slot, replacing any earlier occupant.
    /// </summary>
    Task Enroll(ReadOnlyMemory<byte> clientPublicKey, CancellationToken cancellationToken = default);
}
