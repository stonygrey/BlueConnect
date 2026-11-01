// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Reactive.Subjects;

using Google.Protobuf;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Core;
using StonyGrey.BlueConnect.Secure;
using StonyGrey.BlueConnect.Verdigris.Protocol;

namespace Zaffre.Services.Simulated;

/// <summary>
/// Fake peripheral used with <see cref="SimulatedScanner"/>: realistic connect
/// latency, the occasional failure, and a miniature Verdigris responder that
/// mirrors the firmware — Noise XX sessions only, and the pairing ceremony
/// (the button press is simulated by a short delay). The sim device's
/// identity and pairing survive app restarts via Preferences.
/// </summary>
public sealed class SimulatedPeripheral : IPeripheral
{
    private const string IdentityPreference = "zaffre.sim.device.identity";
    private const string PinPreference = "zaffre.sim.device.pin";

    private readonly PeripheralState _state = new();
    private readonly Subject<PeripheralState> _stateChanges = new();
    private readonly SemaphoreSlim _packetSignal = new(0);
    private readonly LinkIdentity _identity;
    private DateTimeOffset _lastActivity = DateTimeOffset.Now;
    private bool _isConnected;
    private bool _disposed;

    private ResponderEndpoint? _endpoint;
    private CancellationTokenSource? _responderCts;
    private Task? _responder;
    private long _simTimeOffsetMs;
    private string? _simName;
    private bool _simLed;

    public SimulatedPeripheral(IAdvertisement advertisement)
    {
        Advertisement = advertisement;

        var stored = Preferences.Default.Get<string?>(IdentityPreference, null);
        if (stored is null)
        {
            _identity = LinkIdentity.Generate();
            Preferences.Default.Set(IdentityPreference, Convert.ToBase64String(_identity.StaticPrivateKey));
        }
        else
        {
            _identity = new LinkIdentity(Convert.FromBase64String(stored));
        }
    }

    /// <summary>The simulated device's pairing state (advertised by <see cref="SimulatedScanner"/>).</summary>
    public static bool IsDevicePaired => PinLow is not null || PinHigh is not null;

    private static byte[]? PinLow
    {
        get => ReadPin(PinPreference + ".lo");
        set => WritePin(PinPreference + ".lo", value);
    }

    private static byte[]? PinHigh
    {
        get => ReadPin(PinPreference + ".hi");
        set => WritePin(PinPreference + ".hi", value);
    }

    private static byte[]? ReadPin(string key)
    {
        var pin = Preferences.Default.Get<string?>(key, null);
        return pin is null ? null : Convert.FromBase64String(pin);
    }

    private static void WritePin(string key, byte[]? value)
    {
        if (value is null)
        {
            Preferences.Default.Remove(key);
        }
        else
        {
            Preferences.Default.Set(key, Convert.ToBase64String(value));
        }
    }

    /// <summary>The factory enrollment key (a test may set the preference); a session with it may only Enroll.</summary>
    private static byte[]? PinEnroll => ReadPin(PinPreference + ".en");

    private static bool IsEnrollmentKey(byte[]? remoteKey)
        => remoteKey is not null && PinEnroll is { } en && remoteKey.AsSpan().SequenceEqual(en);

    /// <summary>Mirrors the firmware's slot lookup for a session's proven key.</summary>
    private static Privilege? PrivilegeOf(byte[]? remoteKey)
    {
        if (remoteKey is null)
        {
            return null;
        }

        if (PinHigh is { } high && remoteKey.AsSpan().SequenceEqual(high))
        {
            return Privilege.High;
        }

        if (PinLow is { } low && remoteKey.AsSpan().SequenceEqual(low))
        {
            return Privilege.Low;
        }

        return null;
    }

    public IAdvertisement Advertisement { get; }

    public TimeSpan IdleTime => DateTimeOffset.Now - _lastActivity;

    public bool IsConnected => _isConnected;

    public int MTU => _isConnected ? 247 : 0;

    public IDisposable Subscribe(IObserver<PeripheralState> observer) => _stateChanges.Subscribe(observer);

    public async Task Connect(CancellationToken cancellationToken = default)
    {
        await Task.Delay(Random.Shared.Next(80, 400), cancellationToken).ConfigureAwait(false);

        if (Random.Shared.Next(100) < 7)
        {
            throw new BleTransportException("Simulated connection failure.");
        }

        _isConnected = true;
        _endpoint = new ResponderEndpoint(this);
        _responderCts = new CancellationTokenSource();
        _responder = Task.Run(() => RunResponder(_endpoint, _responderCts.Token));
        Touch();
        Publish(ConnectionState.Connected);
    }

    public async Task Disconnect(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return;
        }

        await Task.Delay(Random.Shared.Next(20, 80), cancellationToken).ConfigureAwait(false);
        _isConnected = false;
        StopResponder();
        Publish(ConnectionState.Disconnected);
    }

    public async Task<byte[]> Read(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_state.Received.TryDequeue(out var payload))
            {
                return payload;
            }

            await _packetSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task Write(IEnumerable<byte> bytes, CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _endpoint is null)
        {
            throw new BleTransportException("Peripheral is not connected.");
        }

        var payload = bytes as byte[] ?? bytes.ToArray();
        await Task.Delay(Random.Shared.Next(15, 60), cancellationToken).ConfigureAwait(false);

        if (Random.Shared.Next(100) < 3)
        {
            throw new BleTransportException("Simulated command failure.");
        }

        _endpoint.FeedFromCentral(payload);
        Touch();
        Publish(ConnectionState.Connected);
    }

    /// <summary>The firmware's dispatcher, in miniature (Noise via Torann).</summary>
    private async Task RunResponder(ResponderEndpoint endpoint, CancellationToken ct)
    {
        var segments = new SegmentLink(endpoint, StonyGrey.BlueConnect.Verdigris.VerdigrisPeripheral.MaxFrame);
        Torann.KeyPair? staticKeyPair = null;
        Torann.HandshakeState? handshake = null;
        Torann.Transport? transport = null;
        byte[]? remoteKey = null;
        var buffer = new byte[StonyGrey.BlueConnect.Verdigris.VerdigrisPeripheral.MaxFrame + LinkProtocol.TagLength];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var (type, payload) = FrameType.Split(await segments.Read(ct).ConfigureAwait(false));

                switch (type)
                {
                    case FrameType.Handshake when handshake is null ||
                                                   payload.Length == SecureLink.HandshakeMessage1Length ||
                                                   payload.Length == SecureLink.IkHandshakeMessage1Length:
                        // A message 1 — a fresh session, or (like the firmware)
                        // the client superseding its own established session
                        // over a connection the platform kept alive. Its length
                        // selects the pattern: 32 = XX, 97 = IK (pinned client).
                        var ik = payload.Length == SecureLink.IkHandshakeMessage1Length;
                        if (!ik && payload.Length != SecureLink.HandshakeMessage1Length)
                        {
                            throw new LinkSecurityException("Handshake message 1 has an unexpected length.");
                        }

                        transport?.Dispose();
                        transport = null;
                        remoteKey = null;
                        handshake?.Dispose();
                        staticKeyPair?.Dispose();
                        staticKeyPair = LinkProtocol.Protocol.CreateKeyPair(_identity.StaticPrivateKey);
                        handshake = (ik ? LinkProtocol.PinnedProtocol : LinkProtocol.Protocol)
                            .CreateHandshake(initiator: false, SecureLink.Prologue, s: staticKeyPair);
                        handshake.ReadMessage(payload, buffer);
                        var (length2, _, ikSplit) = handshake.WriteMessage(SecureLink.HandshakePayload, buffer);
                        await segments.Write(FrameType.Bundle(FrameType.Handshake, buffer.AsSpan(0, length2)), ct).ConfigureAwait(false);
                        if (ikSplit != null)
                        {
                            // IK completes at message 2.
                            remoteKey = handshake.RemoteStaticPublicKey;
                            transport = ikSplit;
                        }

                        break;

                    case FrameType.Handshake when transport is null:
                        if (payload.Length != SecureLink.HandshakeMessage3Length)
                        {
                            throw new LinkSecurityException("Handshake message 3 has an unexpected length.");
                        }

                        var (payloadLength3, _, split) = handshake!.ReadMessage(payload, buffer);
                        if (!buffer.AsSpan(0, payloadLength3).SequenceEqual(SecureLink.HandshakePayload))
                        {
                            split?.Dispose();
                            throw new LinkSecurityException("Handshake payload does not match the protocol constant.");
                        }

                        // Any proven key may hold a session; HandleRequest gates
                        // every command by the key's privilege slot.
                        remoteKey = handshake.RemoteStaticPublicKey;
                        transport = split;
                        break;

                    case FrameType.Handshake:
                        // Established, and not a message 1: like the firmware,
                        // drop the session rather than guess.
                        throw new LinkSecurityException("Handshake frame on an established session.");

                    case FrameType.Encrypted when transport is not null:
                        var requestLength = transport.ReadMessage(payload, buffer);
                        var response = await HandleRequest(buffer[..requestLength], remoteKey, ct).ConfigureAwait(false);
                        var sealedLength = transport.WriteMessage(response, buffer);
                        await segments.Write(FrameType.Bundle(FrameType.Encrypted, buffer.AsSpan(0, sealedLength)), ct).ConfigureAwait(false);
                        break;

                    case FrameType.Plaintext:
                        // Application frames must be sealed. Drop, like the firmware.
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is LinkSecurityException or System.Security.Cryptography.CryptographicException or InvalidOperationException)
        {
            // Bad handshake or forged frame: go quiet, like the firmware.
        }
        finally
        {
            handshake?.Dispose();
            transport?.Dispose();
            staticKeyPair?.Dispose();
        }
    }

    private async Task<byte[]> HandleRequest(byte[] body, byte[]? remoteKey, CancellationToken ct)
    {
        var request = Request.Parser.ParseFrom(body);
        var response = new Response { Id = request.Id, Status = Status.Ok };

        // Mirrors the firmware's authorization table: the session key's slot
        // is its privilege; a key in no slot may only Pair; low gets the
        // read-only subset; only high may change state.
        // Enrollment sessions are outside the tier ladder, as in the firmware.
        if (IsEnrollmentKey(remoteKey))
        {
            if (request.OpCase == Request.OpOneofCase.Enroll && request.Enroll.ClientPublicKey.Length == 32)
            {
                PinLow = request.Enroll.ClientPublicKey.ToByteArray();
                response.Ack = new AckResponse();
            }
            else
            {
                response.Status = Status.PermissionDenied;
                response.Detail = "an enrollment session may only Enroll";
            }

            return response.ToByteArray();
        }

        if (request.OpCase == Request.OpOneofCase.Enroll)
        {
            response.Status = Status.Unauthenticated;
            response.Detail = "Enroll needs a session opened with the enrollment key";
            return response.ToByteArray();
        }

        var privilege = PrivilegeOf(remoteKey);
        var required = request.OpCase switch
        {
            Request.OpOneofCase.Pair => (Privilege?)null,
            Request.OpOneofCase.GetTime or Request.OpOneofCase.GetSerialNumber or Request.OpOneofCase.GetLed or
            Request.OpOneofCase.Sqrt or Request.OpOneofCase.Fib or
            Request.OpOneofCase.Unpair => Privilege.Low,
            _ => Privilege.High,
        };

        if (required is not null)
        {
            if (privilege is null)
            {
                response.Status = Status.Unauthenticated;
                response.Detail = "pair with the device first";
                return response.ToByteArray();
            }

            if (privilege == Privilege.Low && required == Privilege.High)
            {
                response.Status = Status.PermissionDenied;
                response.Detail = "high privilege required";
                return response.ToByteArray();
            }
        }

        switch (request.OpCase)
        {
            case Request.OpOneofCase.Echo:
                response.Echo = new EchoResponse { Payload = request.Echo.Payload };
                break;

            case Request.OpOneofCase.Sqrt when request.Sqrt.Value < 0:
                response.Status = Status.InvalidArgument;
                response.Detail = "sqrt of a negative value";
                break;

            case Request.OpOneofCase.Sqrt:
                response.Sqrt = new SqrtResponse { Value = Math.Sqrt(request.Sqrt.Value) };
                break;

            case Request.OpOneofCase.Fib when request.Fib.N > 93:
                response.Status = Status.OutOfRange;
                response.Detail = "fib(n) overflows uint64 for n > 93";
                break;

            case Request.OpOneofCase.Fib:
                ulong a = 0, b = 1;
                for (var i = 0u; i < request.Fib.N; i++)
                {
                    (a, b) = (b, a + b);
                }

                response.Fib = new FibResponse { Value = a };
                break;

            case Request.OpOneofCase.GetTime:
                response.Time = new TimeResponse
                {
                    UnixMillis = _simTimeOffsetMs == 0 ? 0 : _simTimeOffsetMs + Environment.TickCount64,
                };
                break;

            case Request.OpOneofCase.SetSerialNumber:
                _simName = $"Verdigris {request.SetSerialNumber.SerialNumber}";
                response.Name = new NameResponse { Name = _simName };
                break;

            case Request.OpOneofCase.GetSerialNumber:
                response.Name = new NameResponse { Name = _simName ?? Advertisement.LocalName };
                break;

            case Request.OpOneofCase.GetLed:
                response.Led = new LedResponse { On = _simLed };
                break;

            case Request.OpOneofCase.SetLed:
                _simLed = request.SetLed.On;
                response.Led = new LedResponse { On = _simLed };
                break;

            case Request.OpOneofCase.SetTime:
                _simTimeOffsetMs = request.SetTime.UnixMillis - Environment.TickCount64;
                response.Time = new TimeResponse { UnixMillis = request.SetTime.UnixMillis };
                break;

            case Request.OpOneofCase.Pair when remoteKey is not null:
                var high = request.Pair.Privilege == Privilege.High;
                // Matching button is the possession proof (sim auto-confirms).

                // The "button press": a moment of human latency, then confirm.
                await Task.Delay(1500, ct).ConfigureAwait(false);
                if (high)
                {
                    PinHigh = remoteKey;
                }
                else
                {
                    PinLow = remoteKey;
                }

                response.Ack = new AckResponse();
                break;

            case Request.OpOneofCase.Pair:
                response.Status = Status.Unauthenticated;
                response.Detail = "pairing requires a secure session";
                break;

            case Request.OpOneofCase.Unpair:
                // Clears the caller's own slot only.
                if (privilege == Privilege.High)
                {
                    PinHigh = null;
                }
                else
                {
                    PinLow = null;
                }

                response.Ack = new AckResponse();
                break;

            default:
                response.Status = Status.UnknownOp;
                response.Detail = "unknown op";
                break;
        }

        return response.ToByteArray();
    }

    private void StopResponder()
    {
        _responderCts?.Cancel();
        _responderCts?.Dispose();
        _responderCts = null;
        _responder = null;
        _endpoint = null;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isConnected = false;
        StopResponder();
        _stateChanges.OnCompleted();
        _stateChanges.Dispose();
        _packetSignal.Dispose();
    }

    private void Touch() => _lastActivity = DateTimeOffset.Now;

    private void Publish(ConnectionState connectionState)
    {
        _state.ConnectionState = connectionState;
        _state.Rssi = Advertisement.Rssi.ValueDbm;
        _stateChanges.OnNext(_state);
    }

    /// <summary>
    /// The responder's view of the link: Read yields bytes the central wrote,
    /// Write surfaces bytes as notifications to the central.
    /// </summary>
    private sealed class ResponderEndpoint(SimulatedPeripheral owner) : IPeripheral
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<byte[]> _fromCentral = new();
        private readonly SemaphoreSlim _signal = new(0);

        public void FeedFromCentral(byte[] payload)
        {
            _fromCentral.Enqueue(payload);
            _signal.Release();
        }

        public IAdvertisement Advertisement => owner.Advertisement;
        public TimeSpan IdleTime => TimeSpan.Zero;
        public bool IsConnected => owner.IsConnected;
        public int MTU => owner.MTU;

        public Task Connect(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Disconnect(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<byte[]> Read(CancellationToken cancellationToken = default)
        {
            while (true)
            {
                if (_fromCentral.TryDequeue(out var payload))
                {
                    return payload;
                }

                await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public Task Write(IEnumerable<byte> bytes, CancellationToken cancellationToken = default)
        {
            owner._state.Received.Enqueue(bytes as byte[] ?? bytes.ToArray());
            owner._packetSignal.Release();
            return Task.CompletedTask;
        }

        public IDisposable Subscribe(IObserver<PeripheralState> observer) => throw new NotSupportedException();
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
