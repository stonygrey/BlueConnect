// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Security.Cryptography;

using Microsoft.Extensions.Logging;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Core;

using StonyGrey.Edhoc;

namespace StonyGrey.BlueConnect.Secure;

/// <summary>
/// The BlueConnect EDHOC profile: the app-specific constants layered on the
/// StonyGrey.Edhoc library (which itself stays profile-free). Method 3,
/// cipher suite 2, CCS credentials by kid; the kid doubles as the tier.
/// </summary>
public static class BlueConnectEdhoc
{
    /// <summary>Initiator kid for the low-privilege slot.</summary>
    public const byte LowKid = 0x01;

    /// <summary>Initiator kid for the high-privilege slot.</summary>
    public const byte HighKid = 0x02;

    /// <summary>The device's own credential kid.</summary>
    public const byte DeviceKid = 0x0A;

    /// <summary>
    /// EDHOC_Exporter label for the transport keys (private-use range). The
    /// firmware derives the same 64 bytes with EDHOC-KDF(PRK_exporter, label,
    /// h'', 64): bytes 0-31 seal initiator-to-device, 32-63 the reverse.
    /// </summary>
    public const int TransportExporterLabel = 32768;

    /// <summary>
    /// EAD_1 label announcing enrollment: the initiator holds no device
    /// credential, so both ends exchange credentials by value (kccs) and the
    /// session runs unprivileged until a pairing ceremony pins them.
    /// Value-less and non-critical.
    /// </summary>
    public const int EnrollmentEadLabel = 3737;

    /// <summary>
    /// Rebuilds the initiator credential for a stored private key. Subject is
    /// fixed null so the CCS bytes are reproducible everywhere.
    /// </summary>
    public static EdhocCredential InitiatorCredential(byte[] privateKey, byte kid) =>
        EdhocCredential.FromPrivateKey([kid], subject: null, privateKey);
}

/// <summary>
/// Link-security settings for an EDHOC session: the initiator's static DH
/// private key, its kid (which names the device slot: 0x01 low, 0x02 high),
/// and the device's pinned CCS bytes captured at provisioning time. A null
/// <paramref name="DeviceCcs"/> makes the link enroll instead: it announces
/// the enrollment EAD, verifies the device against the credential it sends
/// by value, and surfaces it as <see cref="EdhocLink.LearnedDeviceCcs"/> for
/// the caller to pin after the pairing ceremony succeeds.
/// </summary>
public sealed record EdhocSecurity(byte[] InitiatorPrivateKey, byte InitiatorKid, byte[]? DeviceCcs)
    : ILinkSecurity
{
    public ILink CreateLink(IPeripheral peripheral, int maxFrame, ILogger logger)
        => new EdhocLink(peripheral, maxFrame, this, logger);
}

/// <summary>
/// Encrypted, mutually authenticated link bootstrapped from provisioned
/// static DH credentials: an EDHOC (RFC 9528, method 3, suite 2) handshake as
/// 0x04-type frames, then every message AES-256-GCM-sealed as a 0x02-type
/// frame with the same nonce discipline as the Noise link. The phone is the
/// initiator; the device pins per-slot initiator credentials. Any failure
/// surfaces as <see cref="LinkSecurityException"/>.
/// </summary>
public class EdhocLink : ILink
{
    /// <summary>Message kinds inside a 0x04 frame (first payload byte).</summary>
    public const byte MsgM1 = 0x01;
    public const byte MsgM2 = 0x02;
    public const byte MsgM3 = 0x03;
    public const byte MsgStatus = 0x04;

    public const byte StatusOk = 0x00;
    public const byte StatusAuthFailed = 0x01;
    public const byte StatusNoCredential = 0x02;
    public const byte StatusRateLimited = 0x03;
    public const byte StatusProtocolError = 0x04;

    /// <summary>Status: kind + status code.</summary>
    public static int StatusLength => 2;

    private const int TagLength = 16;
    private const int NonceLength = 12;

    private readonly SegmentLink _segments;
    private readonly EdhocSecurity _security;
    private readonly ILogger _logger;

    private AesGcm? _sendCipher;
    private AesGcm? _receiveCipher;
    private ulong _sendCounter;
    private ulong _receiveCounter;
    private bool _disposed;

    public EdhocLink(IPeripheral peripheral, int maxFrame, EdhocSecurity security, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(security);
        ArgumentNullException.ThrowIfNull(logger);

        _segments = new SegmentLink(peripheral, maxFrame, logger: logger);
        _security = security;
        _logger = logger;
    }

    public bool IsEstablished => _sendCipher != null;

    /// <summary>
    /// The device credential an enrolling handshake learned (TOFU-verified by
    /// MAC_2 against itself). Null on a pinned session or before Establish.
    /// The caller pins it only once the pairing ceremony confirms.
    /// </summary>
    public byte[]? LearnedDeviceCcs { get; private set; }

    /// <summary>
    /// Runs the EDHOC handshake (initiator talks first) and waits for the
    /// device's explicit Status verdict before any traffic flows. The device
    /// is authenticated by message_2 against the pinned credential. Status
    /// after message_3 is an unauthenticated stand-in for EDHOC message_4;
    /// the first successful sealed frame is the actual key confirmation.
    /// </summary>
    public async Task Establish(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsEstablished)
        {
            return;
        }

        var enrolling = _security.DeviceCcs is null;
        EdhocInitiator initiator;
        byte[] message1;
        try
        {
            var own = BlueConnectEdhoc.InitiatorCredential(_security.InitiatorPrivateKey, _security.InitiatorKid);
            initiator = enrolling
                ? EdhocInitiator.CreateEnrolling(own)
                : new EdhocInitiator(own, EdhocCredential.FromCcs(_security.DeviceCcs));
            message1 = initiator.CreateMessage1(
                enrolling ? EdhocEad.EncodeLabel(BlueConnectEdhoc.EnrollmentEadLabel) : null);
        }
        catch (EdhocException ex)
        {
            throw new LinkSecurityException("The stored EDHOC credential is unusable.", ex);
        }

        await WriteTyped(
            FrameType.EdhocHandshake, Kinded(MsgM1, message1), cancellationToken).ConfigureAwait(false);

        var reply = await ReadEdhocFrame(cancellationToken).ConfigureAwait(false);
        if (reply.Length == StatusLength && reply[0] == MsgStatus)
        {
            // The device refused before running the exchange.
            throw StatusException(reply[1]);
        }

        if (reply.Length < 2 || reply[0] != MsgM2)
        {
            throw new LinkSecurityException("Expected the EDHOC message_2 frame.");
        }

        byte[] message3;
        try
        {
            initiator.ProcessMessage2(reply.AsSpan(1));
            message3 = initiator.CreateMessage3();
        }
        catch (EdhocException ex)
        {
            throw new LinkSecurityException(
                "The device failed EDHOC authentication against the pinned credential.", ex);
        }

        await WriteTyped(
            FrameType.EdhocHandshake, Kinded(MsgM3, message3), cancellationToken).ConfigureAwait(false);

        var status = await ReadEdhocFrame(cancellationToken).ConfigureAwait(false);
        if (status.Length != StatusLength || status[0] != MsgStatus)
        {
            throw new LinkSecurityException("Expected the EDHOC Status message to finish the handshake.");
        }

        if (status[1] != StatusOk)
        {
            throw StatusException(status[1]);
        }

        if (enrolling)
        {
            LearnedDeviceCcs = initiator.ResponderCredential.CredentialBytes.ToArray();
        }

        // EDHOC_Exporter -> one AES-256-GCM key per direction, same layout the
        // firmware derives: bytes 0-31 initiator->device, 32-63 device->initiator.
        var transportKeys = initiator.Export(BlueConnectEdhoc.TransportExporterLabel, [], 64);
        _sendCipher = new AesGcm(transportKeys.AsSpan(0, 32), TagLength);
        _receiveCipher = new AesGcm(transportKeys.AsSpan(32, 32), TagLength);
        _sendCounter = 0;
        _receiveCounter = 0;

        _logger.Here().LogInformation(
            "EDHOC link established with kid 0x{Kid:X2}.", _security.InitiatorKid);
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
                BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], _receiveCounter);
                try
                {
                    _receiveCipher!.Decrypt(
                        nonce, payload.AsSpan(0, plaintext.Length),
                        payload.AsSpan(plaintext.Length, TagLength), plaintext);
                }
                catch (Exception ex) when (ex is CryptographicException or ArgumentException)
                {
                    ReleaseResources();
                    throw new LinkSecurityException(
                        "AEAD authentication failed; the frame was forged, corrupted, or replayed.", ex);
                }

                _receiveCounter++;
                return plaintext;
            }

            if (type == FrameType.Plaintext)
            {
                throw new LinkSecurityException("Plaintext frame on a secure link.");
            }

            _logger.Here().LogDebug("Ignoring frame type {Type} on an established EDHOC link.", type);
        }
    }

    private static byte[] Kinded(byte kind, byte[] body)
    {
        var framed = new byte[1 + body.Length];
        framed[0] = kind;
        body.CopyTo(framed, 1);
        return framed;
    }

    private static LinkSecurityException StatusException(byte status) => status switch
    {
        StatusAuthFailed => new LinkSecurityException("The device rejected this credential; the keys do not match."),
        StatusNoCredential => new LinkSecurityException("The device has no credential provisioned for this tier."),
        StatusRateLimited => new LinkSecurityException("The device is rate-limiting handshake attempts; wait and retry."),
        _ => new LinkSecurityException($"The device rejected the EDHOC handshake (status {status})."),
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

    private async Task<byte[]> ReadEdhocFrame(CancellationToken cancellationToken)
    {
        var (type, payload) = await ReadTyped(cancellationToken).ConfigureAwait(false);
        if (type != FrameType.EdhocHandshake)
        {
            throw new LinkSecurityException($"Expected an EDHOC handshake frame but received type {type}.");
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
