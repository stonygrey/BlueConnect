// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

using Microsoft.Extensions.Logging;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Core;

namespace StonyGrey.BlueConnect.Secure;

/// <summary>
/// A message pipe over a connected peripheral. Implementations own framing
/// (and, for the secure link, the Noise session); they do not own the
/// peripheral itself.
/// </summary>
public interface ILink : IDisposable, IAsyncDisposable
{
    Task Send(ReadOnlyMemory<byte> message, CancellationToken cancellationToken);

    Task<byte[]> Receive(CancellationToken cancellationToken);
}

/// <summary>
/// Encrypted, mutually authenticated link (via Torann), then every message
/// AEAD-sealed as a 0x02-type frame. An initiator holding a pinned peer key
/// opens with Noise IK — two messages, and the peer's identity never
/// travels; otherwise Noise XX runs and both statics are exchanged and
/// proven in-band, with a configured pin enforced before any traffic flows.
/// Any failure surfaces as <see cref="LinkSecurityException"/>.
/// </summary>
public class SecureLink : ILink
{
    /// <summary>
    /// Bound to the handshake transcript. Framing is below this layer: a
    /// segment-header change is a new characteristic UUID, not a prologue bump
    /// (see <c>documents/ble-framing.md</c> §8).
    /// </summary>
    public static readonly byte[] Prologue = "BlueConnectLink/1"u8.ToArray();

    /// <summary>
    /// Handshake messages 2 and 3 carry this single ignored byte instead of an
    /// empty payload: Android's native AES-GCM rejects zero-length inputs.
    /// </summary>
    public static readonly byte[] HandshakePayload = [0x5A];

    /// <summary>Message 1 is the initiator ephemeral and an empty payload.</summary>
    public static int HandshakeMessage1Length => LinkProtocol.KeyLength;

    /// <summary>Message 2: e + enc(s) + enc(<see cref="HandshakePayload"/>).</summary>
    public static int HandshakeMessage2Length =>
        LinkProtocol.KeyLength + LinkProtocol.KeyLength + LinkProtocol.TagLength + HandshakePayload.Length + LinkProtocol.TagLength;

    /// <summary>Message 3 with the dummy 0x5A payload: enc(s) + enc(<see cref="HandshakePayload"/>).</summary>
    public static int HandshakeMessage3Length =>
        LinkProtocol.KeyLength + LinkProtocol.TagLength + HandshakePayload.Length + LinkProtocol.TagLength;

    /// <summary>Message 3 when the initiator presents an endorsement blob.</summary>
    public static int HandshakeMessage3CertLength =>
        LinkProtocol.KeyLength + LinkProtocol.TagLength + ClientEndorsement.BlobLength + LinkProtocol.TagLength;

    /// <summary>IK message 1: e + enc(s) + enc(<see cref="HandshakePayload"/>).</summary>
    public static int IkHandshakeMessage1Length =>
        LinkProtocol.KeyLength + LinkProtocol.KeyLength + LinkProtocol.TagLength + HandshakePayload.Length + LinkProtocol.TagLength;

    /// <summary>IK message 2: e + enc(<see cref="HandshakePayload"/>).</summary>
    public static int IkHandshakeMessage2Length =>
        LinkProtocol.KeyLength + HandshakePayload.Length + LinkProtocol.TagLength;

    private readonly SegmentLink _segments;
    private readonly LinkSecurity _security;
    private readonly bool _initiator;
    private readonly ILogger _logger;
    private Torann.Transport? _transport;
    private bool _disposed;

    /// <summary>The peer's proven static key; available once the handshake has completed.</summary>
    public byte[]? RemoteStaticKey { get; private set; }

    public SecureLink(IPeripheral peripheral, int maxFrame, LinkSecurity security, bool initiator, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(logger);

        _segments = new SegmentLink(peripheral, maxFrame, logger: logger);
        _security = security;
        _initiator = initiator;
        _logger = logger;
    }

    public bool IsEstablished => _transport != null;

    /// <summary>
    /// Runs the Noise handshake — IK when this initiator holds a pinned peer
    /// key, XX otherwise (with any configured pin enforced) — before any
    /// traffic flows. The responder accepts either pattern, selected by the
    /// first message's length, exactly like the firmware.
    /// </summary>
    public async Task Establish(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsEstablished)
        {
            return;
        }

        using var staticKeyPair = LinkProtocol.Protocol.CreateKeyPair(_security.Identity.StaticPrivateKey);
        var message = new byte[Math.Max(HandshakeMessage2Length, HandshakeMessage3CertLength)];
        var payload = new byte[Math.Max(HandshakePayload.Length, ClientEndorsement.BlobLength) + LinkProtocol.TagLength];
        Torann.HandshakeState? handshake = null;
        Torann.Transport? transport;

        try
        {
            if (_initiator && _security.PinnedPeerKey is not null)
            {
                // IK: message 1 both proves this identity and shows we already
                // hold the peer's key; the reply completes the session. A
                // stale pin (the device was wiped or rotated) fails on the
                // device and yields silence — the caller's timeout, plus the
                // advertised pairing state, surface the reset.
                handshake = LinkProtocol.PinnedProtocol.CreateHandshake(
                    initiator: true, Prologue, s: staticKeyPair, remoteStatic: _security.PinnedPeerKey);

                var (length1, _, _) = Handshake(() => handshake.WriteMessage(HandshakePayload, message));
                await WriteTyped(FrameType.Handshake, message.AsMemory(0, length1), cancellationToken).ConfigureAwait(false);

                var message2 = await ReadHandshakeFrame(cancellationToken).ConfigureAwait(false);
                ExpectHandshakeLength(message2, IkHandshakeMessage2Length, 2);
                var (payloadLength2, _, split) = Handshake(() => handshake.ReadMessage(message2, payload));
                ExpectHandshakePayload(payload.AsSpan(0, payloadLength2));
                transport = split;
            }
            else if (_initiator)
            {
                handshake = LinkProtocol.Protocol.CreateHandshake(initiator: true, Prologue, s: staticKeyPair);

                var (length1, _, _) = Handshake(() => handshake.WriteMessage(default, message));
                await WriteTyped(FrameType.Handshake, message.AsMemory(0, length1), cancellationToken).ConfigureAwait(false);

                var message2 = await ReadHandshakeFrame(cancellationToken).ConfigureAwait(false);
                ExpectHandshakeLength(message2, HandshakeMessage2Length, 2);
                var (payloadLength2, _, _) = Handshake(() => handshake.ReadMessage(message2, payload));
                ExpectHandshakePayload(payload.AsSpan(0, payloadLength2));
                VerifyPeer(handshake.RemoteStaticPublicKey!);

                var xxPayload = InitiatorXxPayload(_security);
                _logger.Here().LogInformation(
                    "XX message 3 payload is {Kind} ({Len} B).",
                    xxPayload.Length == HandshakePayload.Length ? "dummy" : "endorsement",
                    xxPayload.Length);
                var (length3, _, split) = Handshake(() => handshake.WriteMessage(xxPayload, message));
                await WriteTyped(FrameType.Handshake, message.AsMemory(0, length3), cancellationToken).ConfigureAwait(false);
                transport = split;
            }
            else
            {
                // The first message's length selects the pattern: 32 = XX,
                // 97 = IK from a pinned initiator.
                var message1 = await ReadHandshakeFrame(cancellationToken).ConfigureAwait(false);
                if (message1.Length == IkHandshakeMessage1Length)
                {
                    handshake = LinkProtocol.PinnedProtocol.CreateHandshake(
                        initiator: false, Prologue, s: staticKeyPair);

                    var (payloadLength1, _, _) = Handshake(() => handshake.ReadMessage(message1, payload));
                    ExpectHandshakePayload(payload.AsSpan(0, payloadLength1));
                    VerifyPeer(handshake.RemoteStaticPublicKey!);

                    var (length2, _, split) = Handshake(() => handshake.WriteMessage(HandshakePayload, message));
                    await WriteTyped(FrameType.Handshake, message.AsMemory(0, length2), cancellationToken).ConfigureAwait(false);
                    transport = split;
                }
                else
                {
                    handshake = LinkProtocol.Protocol.CreateHandshake(initiator: false, Prologue, s: staticKeyPair);

                    ExpectHandshakeLength(message1, HandshakeMessage1Length, 1);
                    Handshake(() => handshake.ReadMessage(message1, payload));

                    var (length2, _, _) = Handshake(() => handshake.WriteMessage(HandshakePayload, message));
                    await WriteTyped(FrameType.Handshake, message.AsMemory(0, length2), cancellationToken).ConfigureAwait(false);

                    var message3 = await ReadHandshakeFrame(cancellationToken).ConfigureAwait(false);
                    ExpectHandshakeLength(message3, HandshakeMessage3Length, 3);
                    var (payloadLength3, _, split) = Handshake(() => handshake.ReadMessage(message3, payload));
                    ExpectHandshakePayload(payload.AsSpan(0, payloadLength3));
                    VerifyPeer(handshake.RemoteStaticPublicKey!);
                    transport = split;
                }
            }

            RemoteStaticKey = handshake.RemoteStaticPublicKey;
        }
        finally
        {
            handshake?.Dispose();
        }

        _transport = transport ?? throw new LinkSecurityException("The handshake completed without producing a transport.");
        _logger.Here().LogInformation("Secure link established; peer static key {Peer}.", Convert.ToHexString(RemoteStaticKey!));
    }

    public async Task Send(ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
    {
        await Establish(cancellationToken).ConfigureAwait(false);
        var sealedBuffer = new byte[message.Length + LinkProtocol.TagLength];
        var sealedLength = _transport!.WriteMessage(message.Span, sealedBuffer);
        await WriteTyped(FrameType.Encrypted, sealedBuffer.AsMemory(0, sealedLength), cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> Receive(CancellationToken cancellationToken)
    {
        await Establish(cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var (type, payload) = await ReadTyped(cancellationToken).ConfigureAwait(false);
            if (type == FrameType.Encrypted)
            {
                var plaintext = new byte[payload.Length];
                int length;
                try
                {
                    length = _transport!.ReadMessage(payload, plaintext);
                }
                catch (Exception ex) when (ex is CryptographicException or ArgumentException)
                {
                    throw new LinkSecurityException("AEAD authentication failed; the frame was forged, corrupted, or replayed.", ex);
                }

                return plaintext[..length];
            }

            if (type == FrameType.Plaintext)
            {
                throw new LinkSecurityException("Plaintext frame on a secure link.");
            }

            _logger.Here().LogDebug("Ignoring frame type {Type} on an established secure link.", type);
        }
    }

    /// <summary>
    /// XX message 3 payload: the compact endorsement when one is installed,
    /// otherwise the 0x5A dummy. IK always uses the dummy. Missing cert is not
    /// a failure — an unprovisioned app still handshakes.
    /// </summary>
    private static byte[] InitiatorXxPayload(LinkSecurity security)
    {
        if (security.Endorsement is { } cert && ClientEndorsement.IsCertBlob(cert))
        {
            return cert;
        }

        return HandshakePayload;
    }

    private void VerifyPeer(byte[] remoteStatic)
    {
        // Torann leaves non-contributory-key checks to the application (Noise
        // spec §12.1); an all-zero static is never a legitimate identity.
        if (!remoteStatic.AsSpan().ContainsAnyExcept((byte)0))
        {
            throw new LinkSecurityException("Peer presented an all-zero static key.");
        }

        if (_security.PinnedPeerKey is { } pin && !remoteStatic.AsSpan().SequenceEqual(pin))
        {
            throw new LinkSecurityException("Peer's static key does not match the paired key.");
        }
    }

    /// <summary>Runs one Torann handshake step, mapping its failures into the link taxonomy.</summary>
    private static T Handshake<T>(Func<T> step)
    {
        try
        {
            return step();
        }
        catch (Exception ex) when (ex is CryptographicException or InvalidOperationException or ArgumentException)
        {
            throw new LinkSecurityException("The handshake failed.", ex);
        }
    }

    private static void ExpectHandshakeLength(byte[] message, int expected, int number)
    {
        if (message.Length != expected)
        {
            throw new LinkSecurityException(
                $"Handshake message {number} is {message.Length} bytes; expected {expected}.");
        }
    }

    private static void ExpectHandshakePayload(ReadOnlySpan<byte> payload)
    {
        if (!payload.SequenceEqual(HandshakePayload))
        {
            throw new LinkSecurityException("Handshake payload does not match the protocol constant.");
        }
    }

    private Task WriteTyped(byte type, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var bundled = FrameType.Bundle(type, payload.Span);
        _logger.Here().LogInformation("{Line}", IoLog.Message("write", type, bundled));
        return _segments.Write(bundled, cancellationToken);
    }

    private async Task<(byte Type, byte[] Payload)> ReadTyped(CancellationToken cancellationToken)
    {
        var bundled = await _segments.Read(cancellationToken).ConfigureAwait(false);
        var (type, payload) = FrameType.Split(bundled);
        _logger.Here().LogInformation("{Line}", IoLog.Message("read", type, bundled));
        return (type, payload);
    }

    private async Task<byte[]> ReadHandshakeFrame(CancellationToken cancellationToken)
    {
        var (type, payload) = await ReadTyped(cancellationToken).ConfigureAwait(false);
        if (type != FrameType.Handshake)
        {
            throw new LinkSecurityException($"Expected a handshake frame but received type {type}.");
        }

        return payload;
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

    protected virtual ValueTask DisposeAsyncCore()
    {
        ReleaseResources();
        return ValueTask.CompletedTask;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        ReleaseResources();
    }

    private void ReleaseResources()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _transport?.Dispose();
        _transport = null;
    }
}
