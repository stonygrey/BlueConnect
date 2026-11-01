// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Google.Protobuf;

using Microsoft.Extensions.Logging;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Secure;
using StonyGrey.BlueConnect.Verdigris.Protocol;

namespace StonyGrey.BlueConnect.Verdigris;

/// <summary>
/// Serialization adapter: encodes Verdigris requests as protobuf messages over
/// a Noise-encrypted <see cref="ILink"/> (the link owns the wire framing) and
/// decodes responses back off it. Requests are serialized through a gate
/// (GATT is serial) and correlated by id.
/// </summary>
public class VerdigrisPeripheral : IVerdigrisPeripheral
{
    /// <summary>
    /// Cap on a reassembled GATT message (type byte + ciphertext). The value
    /// is arbitrary and specific to this implementation; the cap is not
    /// negotiated, so it must equal the firmware <c>SEG_MAX_MESSAGE</c> and
    /// the two should be kept in sync. An encoded protobuf must leave room
    /// for the type byte and the AEAD tag.
    /// </summary>
    public const int MaxFrame = 2304;

    /// <summary>Largest encoded request that still fits under <see cref="MaxFrame"/> once typed and sealed.</summary>
    public static int MaxEncodedRequest => MaxFrame - 1 - LinkProtocol.TagLength;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SecureLink _link;
    private readonly TimeSpan _commandTimeout;
    private int _nextId;
    private bool _disposed;

    internal VerdigrisPeripheral(IPeripheral peripheral, BlueConnectOptions options, ILogger logger, LinkSecurity security)
    {
        ArgumentNullException.ThrowIfNull(peripheral);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(security);

        Peripheral = peripheral;
        Logger = logger;
        _commandTimeout = TimeSpan.FromSeconds(options.CommandTimeoutSeconds);
        _link = new SecureLink(peripheral, MaxFrame, security, initiator: true, logger);
    }

    /// <summary>The device's proven static key, once a secure session is established.</summary>
    public byte[]? RemoteStaticKey => _link.RemoteStaticKey;

    public IPeripheral Peripheral { get; }

    protected ILogger Logger { get; }

    public async Task<byte[]> Echo(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        var request = new Request { Echo = new EchoRequest { Payload = ByteString.CopyFrom(payload.Span) } };
        var response = await Invoke(request, Response.ResultOneofCase.Echo, cancellationToken).ConfigureAwait(false);
        return response.Echo.Payload.ToByteArray();
    }

    public async Task<double> SquareRoot(double value, CancellationToken cancellationToken = default)
    {
        var request = new Request { Sqrt = new SqrtRequest { Value = value } };
        var response = await Invoke(request, Response.ResultOneofCase.Sqrt, cancellationToken).ConfigureAwait(false);
        return response.Sqrt.Value;
    }

    public async Task<ulong> Fibonacci(uint n, CancellationToken cancellationToken = default)
    {
        var request = new Request { Fib = new FibRequest { N = n } };
        var response = await Invoke(request, Response.ResultOneofCase.Fib, cancellationToken).ConfigureAwait(false);
        return response.Fib.Value;
    }

    public async Task<DateTimeOffset> GetTime(CancellationToken cancellationToken = default)
    {
        var request = new Request { GetTime = new GetTimeRequest() };
        var response = await Invoke(request, Response.ResultOneofCase.Time, cancellationToken).ConfigureAwait(false);
        return DateTimeOffset.FromUnixTimeMilliseconds(response.Time.UnixMillis);
    }

    public async Task SetTime(DateTimeOffset time, CancellationToken cancellationToken = default)
    {
        var request = new Request { SetTime = new SetTimeRequest { UnixMillis = time.ToUnixTimeMilliseconds() } };
        await Invoke(request, Response.ResultOneofCase.Time, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> GetLed(CancellationToken cancellationToken = default)
    {
        var request = new Request { GetLed = new GetLedRequest() };
        var response = await Invoke(request, Response.ResultOneofCase.Led, cancellationToken).ConfigureAwait(false);
        return response.Led.On;
    }

    public async Task<bool> SetLed(bool on, CancellationToken cancellationToken = default)
    {
        var request = new Request { SetLed = new SetLedRequest { On = on } };
        var response = await Invoke(request, Response.ResultOneofCase.Led, cancellationToken).ConfigureAwait(false);
        return response.Led.On;
    }

    public async Task<string> SetSerialNumber(ushort serialNumber, CancellationToken cancellationToken = default)
    {
        var request = new Request { SetSerialNumber = new SetSerialNumberRequest { SerialNumber = serialNumber } };
        var response = await Invoke(request, Response.ResultOneofCase.Name, cancellationToken).ConfigureAwait(false);
        return response.Name.Name;
    }

    public async Task<string> GetSerialNumber(CancellationToken cancellationToken = default)
    {
        var request = new Request { GetSerialNumber = new GetSerialNumberRequest() };
        var response = await Invoke(request, Response.ResultOneofCase.Name, cancellationToken).ConfigureAwait(false);
        return response.Name.Name;
    }

    /// <summary>
    /// Longer than the firmware's 30 s pairing window (<c>PAIR_WINDOW_SECONDS</c>
    /// in link.c), which is an arbitrary implementation value. The two should
    /// be kept in sync, so that the host is still waiting when the device
    /// answers at the end of the window.
    /// </summary>
    private static readonly TimeSpan PairTimeout = TimeSpan.FromSeconds(40);

    public async Task<byte[]> Pair(Privilege privilege, CancellationToken cancellationToken = default)
    {
        var request = new Request { Pair = new PairRequest { Privilege = privilege } };
        await Invoke(request, Response.ResultOneofCase.Ack, cancellationToken, PairTimeout).ConfigureAwait(false);
        return RemoteStaticKey!;
    }

    public async Task Unpair(CancellationToken cancellationToken = default)
    {
        var request = new Request { Unpair = new UnpairRequest() };
        await Invoke(request, Response.ResultOneofCase.Ack, cancellationToken).ConfigureAwait(false);
    }

    public async Task Enroll(ReadOnlyMemory<byte> clientPublicKey, CancellationToken cancellationToken = default)
    {
        var request = new Request { Enroll = new EnrollRequest { ClientPublicKey = ByteString.CopyFrom(clientPublicKey.Span) } };
        await Invoke(request, Response.ResultOneofCase.Ack, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        Dispose(disposing: false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_disposed)
        {
            return;
        }

        await _link.DisposeAsync().ConfigureAwait(false);
        await Peripheral.DisposeAsync().ConfigureAwait(false);
        ReleaseResources();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            return;
        }

        _link.Dispose();
        Peripheral.Dispose();
        ReleaseResources();
    }

    private void ReleaseResources()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private async Task<Response> Invoke(Request request, Response.ResultOneofCase expected, CancellationToken cancellationToken, TimeSpan? timeoutOverride = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var commandTimeout = timeoutOverride ?? _commandTimeout;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(commandTimeout);

            request.Id = (uint)Interlocked.Increment(ref _nextId);
            var body = request.ToByteArray();
            if (body.Length > MaxEncodedRequest)
            {
                throw new InvalidOperationException(
                    $"Encoded request is {body.Length} bytes; the maximum is {MaxEncodedRequest} (type + AEAD tag must fit under {MaxFrame}).");
            }

            try
            {
                await _link.Send(body, timeout.Token).ConfigureAwait(false);

                while (true)
                {
                    var raw = await _link.Receive(timeout.Token).ConfigureAwait(false);

                    Response response;
                    try
                    {
                        response = Response.Parser.ParseFrom(raw);
                    }
                    catch (InvalidProtocolBufferException ex)
                    {
                        throw new BleTransportException("Undecodable Verdigris frame; the stream is desynchronized.", ex);
                    }

                    if (response.Id != request.Id)
                    {
                        Logger.Here().LogDebug("Dropping stale Verdigris response {StaleId} while awaiting {ExpectedId}.", response.Id, request.Id);
                        continue;
                    }

                    if (response.Status != Status.Ok)
                    {
                        throw new VerdigrisException(response.Status, response.Detail);
                    }

                    if (response.ResultCase != expected)
                    {
                        throw new BleTransportException($"Verdigris response carried {response.ResultCase} where {expected} was expected.");
                    }

                    return response;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Verdigris command timed out after {commandTimeout.TotalSeconds:F0}s.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
