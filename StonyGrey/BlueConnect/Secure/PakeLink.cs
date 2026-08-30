// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Security.Cryptography;

using Microsoft.Extensions.Logging;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Core;

using StonyGrey.Spake2Plus;

namespace StonyGrey.BlueConnect.Secure;

/// <summary>
/// Link-security settings for a SPAKE2+ session: the scalars derived from a
/// setup code, and the tier whose verifier slot the device must prove. The
/// code itself is not held here — derive once per app session via
/// <see cref="FromCode"/> (PBKDF2 is deliberately slow).
/// </summary>
public sealed record PakeSecurity(byte[] W0, byte[] W1, PakeTier Tier) : ILinkSecurity
{
    public static PakeSecurity FromCode(string code, PakeTier tier)
    {
        var registration = BlueConnectPake.Register(code, tier);
        return new PakeSecurity(registration.W0, registration.W1, tier);
    }

    public ILink CreateLink(IPeripheral peripheral, int maxFrame, ILogger logger)
        => new PakeLink(peripheral, maxFrame, this, logger);
}

/// <summary>
/// Encrypted, mutually authenticated link bootstrapped from a setup code:
/// a SPAKE2+ (RFC 9383, P256-SHA256) handshake as 0x03-type frames, then
/// every message AES-256-GCM-sealed as a 0x02-type frame with the same
/// nonce discipline as the Noise link. The phone is the prover; the device
/// stores only a verifier. Any failure surfaces as
/// <see cref="LinkSecurityException"/>.
/// </summary>
public class PakeLink : ILink
{
    /// <summary>Message kinds inside a 0x03 frame (first payload byte).</summary>
    public const byte MsgStart = 0x01;
    public const byte MsgRespond = 0x02;
    public const byte MsgConfirm = 0x03;
    public const byte MsgStatus = 0x04;

    /// <summary>Wire format version carried in the Start message.</summary>
    public const byte Version = 0x01;

    public const byte TierLow = 0x01;
    public const byte TierHigh = 0x02;

    public const byte StatusOk = 0x00;
    public const byte StatusAuthFailed = 0x01;
    public const byte StatusNoVerifier = 0x02;
    public const byte StatusRateLimited = 0x03;
    public const byte StatusProtocolError = 0x04;

    private const int PointLength = 65;
    private const int MacLength = 32;
    private const int TagLength = 16;
    private const int NonceLength = 12;

    /// <summary>Start: kind + version + tier + shareP.</summary>
    public static int StartLength => 3 + PointLength;

    /// <summary>Respond: kind + shareV + confirmV.</summary>
    public static int RespondLength => 1 + PointLength + MacLength;

    /// <summary>Confirm: kind + confirmP.</summary>
    public static int ConfirmLength => 1 + MacLength;

    /// <summary>Status: kind + status code.</summary>
    public static int StatusLength => 2;

    /// <summary>HKDF info for the transport keys derived from K_shared.</summary>
    public static readonly byte[] TransportInfo = "BlueConnectPake/1 transport"u8.ToArray();

    private readonly SegmentLink _segments;
    private readonly PakeSecurity _security;
    private readonly ILogger _logger;

    private AesGcm? _sendCipher;
    private AesGcm? _receiveCipher;
    private ulong _sendCounter;
    private ulong _receiveCounter;
    private bool _disposed;

    public PakeLink(IPeripheral peripheral, int maxFrame, PakeSecurity security, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(logger);

        _segments = new SegmentLink(peripheral, maxFrame, logger: logger);
        _security = security;
        _logger = logger;
    }

    public bool IsEstablished => _sendCipher != null;

    /// <summary>
    /// Runs the SPAKE2+ handshake (prover talks first) and waits for the
    /// device's explicit Status verdict before any traffic flows.
    /// </summary>
    public async Task Establish(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsEstablished)
        {
            return;
        }

        var prover = new Spake2PlusProver(
            _security.W0, _security.W1,
            BlueConnectPake.ContextBytes, BlueConnectPake.EmptyIdentity, BlueConnectPake.EmptyIdentity);

        var start = new byte[StartLength];
        start[0] = MsgStart;
        start[1] = Version;
        start[2] = _security.Tier == PakeTier.High ? TierHigh : TierLow;
        prover.GenerateShare().CopyTo(start, 3);
        await WriteTyped(FrameType.PakeHandshake, start, cancellationToken).ConfigureAwait(false);

        var respond = await ReadPakeFrame(cancellationToken).ConfigureAwait(false);
        if (respond.Length == StatusLength && respond[0] == MsgStatus)
        {
            // The device refused before running the exchange.
            throw StatusException(respond[1]);
        }

        if (respond.Length != RespondLength || respond[0] != MsgRespond)
        {
            throw new LinkSecurityException(
                $"Expected the PAKE Respond message ({RespondLength} bytes) but received {respond.Length}.");
        }

        byte[] confirmP;
        byte[] sharedKey;
        try
        {
            (confirmP, sharedKey) = prover.Finish(
                respond.AsSpan(1, PointLength), respond.AsSpan(1 + PointLength, MacLength));
        }
        catch (Spake2PlusException ex)
        {
            throw new LinkSecurityException("The setup code did not match the device's verifier.", ex);
        }

        var confirm = new byte[ConfirmLength];
        confirm[0] = MsgConfirm;
        confirmP.CopyTo(confirm, 1);
        await WriteTyped(FrameType.PakeHandshake, confirm, cancellationToken).ConfigureAwait(false);

        var status = await ReadPakeFrame(cancellationToken).ConfigureAwait(false);
        if (status.Length != StatusLength || status[0] != MsgStatus)
        {
            throw new LinkSecurityException("Expected the PAKE Status message to finish the handshake.");
        }

        if (status[1] != StatusOk)
        {
            throw StatusException(status[1]);
        }

        // K_shared -> one AES-256-GCM key per direction, same layout the
        // firmware derives: bytes 0-31 prover->verifier, 32-63 verifier->prover.
        var transportKeys = HKDF.DeriveKey(
            HashAlgorithmName.SHA256, sharedKey, 64, salt: null, info: TransportInfo);
        _sendCipher = new AesGcm(transportKeys.AsSpan(0, 32), TagLength);
        _receiveCipher = new AesGcm(transportKeys.AsSpan(32, 32), TagLength);
        _sendCounter = 0;
        _receiveCounter = 0;

        _logger.Here().LogInformation(
            "PAKE link established at {Tier} tier.", _security.Tier);
    }

    public async Task Send(ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
    {
        await Establish(cancellationToken).ConfigureAwait(false);

        // GCM nonce reuse is fatal: refuse to wrap, like the firmware does.
        if (_sendCounter == ulong.MaxValue)
        {
            throw new LinkSecurityException("Transport nonce space exhausted; reconnect.");
        }

        var sealedBuffer = new byte[message.Length + TagLength];
        Span<byte> nonce = stackalloc byte[NonceLength];
        BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], _sendCounter++);
        _sendCipher!.Encrypt(
            nonce, message.Span, sealedBuffer.AsSpan(0, message.Length),
            sealedBuffer.AsSpan(message.Length, TagLength));

        await WriteTyped(FrameType.Encrypted, sealedBuffer, cancellationToken).ConfigureAwait(false);
    }

    public async Task<byte[]> Receive(CancellationToken cancellationToken)
    {
        await Establish(cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var (type, payload) = await ReadTyped(cancellationToken).ConfigureAwait(false);
            if (type == FrameType.Encrypted)
            {
                if (payload.Length < TagLength)
                {
                    throw new LinkSecurityException("Sealed frame shorter than an AEAD tag.");
                }

                if (_receiveCounter == ulong.MaxValue)
                {
                    throw new LinkSecurityException("Transport nonce space exhausted; reconnect.");
                }

                var plaintext = new byte[payload.Length - TagLength];
                Span<byte> nonce = stackalloc byte[NonceLength];
                BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], _receiveCounter++);
                try
                {
                    _receiveCipher!.Decrypt(
                        nonce, payload.AsSpan(0, plaintext.Length),
                        payload.AsSpan(plaintext.Length, TagLength), plaintext);
                }
                catch (Exception ex) when (ex is CryptographicException or ArgumentException)
                {
                    throw new LinkSecurityException(
                        "AEAD authentication failed; the frame was forged, corrupted, or replayed.", ex);
                }

                return plaintext;
            }

            if (type == FrameType.Plaintext)
            {
                throw new LinkSecurityException("Plaintext frame on a secure link.");
            }

            _logger.Here().LogDebug("Ignoring frame type {Type} on an established PAKE link.", type);
        }
    }

    private static LinkSecurityException StatusException(byte status) => status switch
    {
        StatusAuthFailed => new LinkSecurityException("The setup code did not match the device's verifier."),
        StatusNoVerifier => new LinkSecurityException("The device has no verifier provisioned for this tier."),
        StatusRateLimited => new LinkSecurityException("The device is rate-limiting pairing attempts; wait and retry."),
        _ => new LinkSecurityException($"The device rejected the PAKE handshake (status {status})."),
    };

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

    private async Task<byte[]> ReadPakeFrame(CancellationToken cancellationToken)
    {
        var (type, payload) = await ReadTyped(cancellationToken).ConfigureAwait(false);
        if (type != FrameType.PakeHandshake)
        {
            throw new LinkSecurityException($"Expected a PAKE handshake frame but received type {type}.");
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
        _sendCipher?.Dispose();
        _receiveCipher?.Dispose();
        _sendCipher = null;
        _receiveCipher = null;
    }
}
