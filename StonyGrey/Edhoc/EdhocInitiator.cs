// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

using Org.BouncyCastle.Math;

using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;

namespace StonyGrey.Edhoc;

/// <summary>
/// The initiator side of one EDHOC session (RFC 9528), method 3 (static-
/// static DH) with cipher suite 2. Single-use: drive it CreateMessage1 →
/// ProcessMessage2 → CreateMessage3, after which <see cref="Export"/> yields
/// keying material; ProcessMessage4 is optional explicit key confirmation.
/// The responder is authenticated during ProcessMessage2 against the expected
/// credential supplied up front.
/// </summary>
public sealed class EdhocInitiator
{
    private enum Phase
    {
        Created,
        Message1Sent,
        Message2Processed,
        Complete,
    }

    private readonly EdhocCredential _own;
    private readonly bool _enrolling;
    private EdhocCredential? _peer;
    private readonly byte[] _cI;
    private readonly int[] _suites;
    private readonly BigInteger _ephemeral;
    private readonly byte[] _gX;

    private Phase _phase;
    private bool _message4Processed;
    private byte[]? _message1;
    private ECPoint? _gYPoint;
    private byte[]? _cR;
    private byte[]? _th3;
    private byte[]? _th4;
    private byte[]? _prk3e2m;
    private byte[]? _prk4e3m;
    private byte[]? _prkExporter;
    private byte[]? _receivedEad2;
    private byte[]? _receivedEad4;

    public EdhocInitiator(
        EdhocCredential ownCredential,
        EdhocCredential expectedPeerCredential,
        byte[]? connectionIdentifier = null,
        int[]? suitesI = null)
        : this(ownCredential, expectedPeerCredential, connectionIdentifier, suitesI,
               new BigInteger(1, EdhocCrypto.RandomScalar()))
    {
    }

    /// <summary>
    /// Enrollment form: no peer credential is known yet. The responder must
    /// send its credential by value (kccs) in message_2 — verified and then
    /// exposed as <see cref="ResponderCredential"/> for the caller to pin —
    /// and this side sends its own credential by value in message_3.
    /// Trust-on-first-use: the caller decides whether to keep the result.
    /// </summary>
    public static EdhocInitiator CreateEnrolling(
        EdhocCredential ownCredential,
        byte[]? connectionIdentifier = null,
        int[]? suitesI = null) =>
        new(ownCredential, connectionIdentifier, suitesI,
            new BigInteger(1, EdhocCrypto.RandomScalar()));

    private EdhocInitiator(
        EdhocCredential ownCredential,
        byte[]? connectionIdentifier,
        int[]? suitesI,
        BigInteger ephemeral)
        : this(ownCredential, ownCredential, connectionIdentifier, suitesI, ephemeral)
    {
        _peer = null;
        _enrolling = true;
    }

    /// <summary>Fixed-ephemeral constructor for RFC 9529 vector tests.</summary>
    internal EdhocInitiator(
        EdhocCredential ownCredential,
        EdhocCredential expectedPeerCredential,
        byte[]? connectionIdentifier,
        int[]? suitesI,
        BigInteger ephemeral)
    {
        if (!ownCredential.HasPrivateKey)
        {
            throw new EdhocException("The initiator's own credential must carry its private key.");
        }

        _suites = suitesI ?? [2];
        if (_suites.Length == 0 || _suites[^1] != 2)
        {
            throw new EdhocException("The selected cipher suite (last of SUITES_I) must be 2.");
        }

        _cI = connectionIdentifier ?? RandomConnectionIdentifier();
        if (_cI.Length == 0)
        {
            throw new EdhocException("The connection identifier must not be empty.");
        }

        _own = ownCredential;
        _peer = expectedPeerCredential;
        _ephemeral = ephemeral;
        _gX = EdhocCrypto.XCoordinate(EdhocCrypto.PublicPoint(ephemeral));
    }

    /// <summary>
    /// The responder's credential. For a pinned session this is the expected
    /// credential supplied up front; for an enrolling session it is the
    /// credential the responder sent by value, available after
    /// ProcessMessage2 — the caller pins it if the ceremony completes.
    /// </summary>
    public EdhocCredential ResponderCredential =>
        _peer ?? throw new EdhocException("The responder credential is not known until ProcessMessage2 has run.");

    /// <summary>C_R as chosen by the responder; available after ProcessMessage2.</summary>
    public ReadOnlyMemory<byte> ResponderConnectionIdentifier =>
        _cR ?? throw new EdhocException("C_R is not known until ProcessMessage2 has run.");

    /// <summary>EAD_2 items the responder sent, as a raw CBOR sequence, if any.</summary>
    public ReadOnlyMemory<byte>? ReceivedEad2 =>
        _receivedEad2 is null ? null : (ReadOnlyMemory<byte>?)_receivedEad2;

    /// <summary>EAD_4 items the responder sent, as a raw CBOR sequence, if any.</summary>
    public ReadOnlyMemory<byte>? ReceivedEad4 =>
        _receivedEad4 is null ? null : (ReadOnlyMemory<byte>?)_receivedEad4;

    /// <summary>Builds message_1. RFC 9528 §5.2.</summary>
    public byte[] CreateMessage1(byte[]? ead1 = null)
    {
        Require(Phase.Created, "CreateMessage1 may only be called once, first.");
        var ead = EdhocCbor.ValidateExternalAuthorizationData(ead1);
        var message1 = EdhocMessageCodec.EncodeMessage1(
            (int)EdhocMethod.StaticStatic, _suites, _gX, _cI, ead);

        // A private copy feeds TH_2, so the caller mutating the returned
        // buffer cannot skew this side's transcript.
        _message1 = (byte[])message1.Clone();
        _phase = Phase.Message1Sent;
        return message1;
    }

    /// <summary>
    /// Consumes message_2 and authenticates the responder: the received kid
    /// and MAC_2 must match the expected peer credential. Throws
    /// <see cref="EdhocErrorReceivedException"/> when the responder replied
    /// with an EDHOC error message instead. RFC 9528 §5.3.
    /// </summary>
    public void ProcessMessage2(ReadOnlySpan<byte> message2)
    {
        Require(Phase.Message1Sent, "ProcessMessage2 must follow CreateMessage1.");

        var (gY, ciphertext2) = EdhocMessageCodec.ParseMessage2(message2.ToArray());
        var th2 = EdhocKeySchedule.TranscriptHash2(gY, _message1!);
        _gYPoint = EdhocCrypto.DecodeXOnly(gY);

        var prk2e = EdhocKeySchedule.Extract(th2, EdhocCrypto.Agree(_ephemeral, _gYPoint));
        var keystream = EdhocKeySchedule.Kdf(
            prk2e, EdhocKeySchedule.LabelKeystream2, th2, ciphertext2.Length);
        var plaintext2 = Xor(ciphertext2, keystream);

        var (cR, idCredR, mac2, ead2) = EdhocMessageCodec.ParsePlaintext2(plaintext2);
        byte[] idCredFull2;
        if (_enrolling)
        {
            if (idCredR.Ccs is null)
            {
                throw new EdhocException(
                    "Enrollment requires the responder to send its credential by value (kccs).");
            }

            _peer = EdhocCredential.FromCcs(idCredR.Ccs);
            idCredFull2 = EdhocMessageCodec.EncodeIdCredKccs(idCredR.Ccs);
        }
        else
        {
            if (idCredR.Kid is null || !idCredR.Kid.AsSpan().SequenceEqual(_peer!.Kid.Span))
            {
                throw new EdhocException("Responder presented an unexpected credential kid.");
            }

            idCredFull2 = EdhocMessageCodec.EncodeIdCredMap(_peer.Kid);
        }

        var salt3e2m = EdhocKeySchedule.Kdf(
            prk2e, EdhocKeySchedule.LabelSalt3e2m, th2, EdhocCrypto.HashLength);
        var prk3e2m = EdhocKeySchedule.Extract(
            salt3e2m, EdhocCrypto.Agree(_ephemeral, _peer.PublicPoint));

        var context2 = EdhocMessageCodec.Context2(cR, idCredFull2, th2, _peer.CredentialBytes, ead2);
        var expectedMac2 = EdhocKeySchedule.Kdf(
            prk3e2m, EdhocKeySchedule.LabelMac2, context2, EdhocCrypto.MacLength);
        if (!CryptographicOperations.FixedTimeEquals(expectedMac2, mac2))
        {
            throw new EdhocException("Responder authentication failed; MAC_2 did not verify.");
        }

        _th3 = EdhocKeySchedule.TranscriptHash3(th2, plaintext2, _peer!.CredentialBytes);
        _prk3e2m = prk3e2m;
        _cR = cR;
        _receivedEad2 = ead2;
        _phase = Phase.Message2Processed;
    }

    /// <summary>
    /// Builds message_3, authenticating this side to the responder, and
    /// completes the key schedule: <see cref="Export"/> is legal afterwards.
    /// RFC 9528 §5.4.
    /// </summary>
    public byte[] CreateMessage3(byte[]? ead3 = null)
    {
        Require(Phase.Message2Processed, "CreateMessage3 must follow ProcessMessage2.");
        var ead = EdhocCbor.ValidateExternalAuthorizationData(ead3);

        var salt4e3m = EdhocKeySchedule.Kdf(
            _prk3e2m!, EdhocKeySchedule.LabelSalt4e3m, _th3!, EdhocCrypto.HashLength);
        var prk4e3m = EdhocKeySchedule.Extract(
            salt4e3m, EdhocCrypto.Agree(_own.PrivateScalar!, _gYPoint!));

        var idCred3Compact = _enrolling
            ? EdhocMessageCodec.EncodeIdCredKccs(_own.CredentialBytes)
            : EdhocMessageCodec.EncodeIdCredCompact(_own.Kid);
        var idCred3Full = _enrolling
            ? idCred3Compact
            : EdhocMessageCodec.EncodeIdCredMap(_own.Kid);
        var context3 = EdhocMessageCodec.Context3(idCred3Full, _th3!, _own.CredentialBytes, ead);
        var mac3 = EdhocKeySchedule.Kdf(
            prk4e3m, EdhocKeySchedule.LabelMac3, context3, EdhocCrypto.MacLength);
        var plaintext3 = EdhocMessageCodec.EncodePlaintext3(idCred3Compact, mac3, ead);

        var k3 = EdhocKeySchedule.Kdf(
            _prk3e2m!, EdhocKeySchedule.LabelK3, _th3!, EdhocCrypto.AeadKeyLength);
        var iv3 = EdhocKeySchedule.Kdf(
            _prk3e2m!, EdhocKeySchedule.LabelIv3, _th3!, EdhocCrypto.AeadNonceLength);
        var ciphertext3 = EdhocCrypto.CcmEncrypt(
            k3, iv3, EdhocMessageCodec.EncStructure(_th3!), plaintext3);

        _th4 = EdhocKeySchedule.TranscriptHash4(_th3!, plaintext3, _own.CredentialBytes);
        var prkOut = EdhocKeySchedule.Kdf(
            prk4e3m, EdhocKeySchedule.LabelPrkOut, _th4, EdhocCrypto.HashLength);
        _prkExporter = EdhocKeySchedule.Kdf(
            prkOut, EdhocKeySchedule.LabelPrkExporter, [], EdhocCrypto.HashLength);
        _prk4e3m = prk4e3m;
        _phase = Phase.Complete;

        return EdhocMessageCodec.EncodeCiphertextOnly(ciphertext3);
    }

    /// <summary>
    /// Consumes the optional message_4, the responder's explicit key
    /// confirmation. RFC 9528 §5.5.
    /// </summary>
    public void ProcessMessage4(ReadOnlySpan<byte> message4)
    {
        Require(Phase.Complete, "ProcessMessage4 must follow CreateMessage3.");
        if (_message4Processed)
        {
            throw new EdhocException("ProcessMessage4 may only be called once.");
        }

        _message4Processed = true;
        var ciphertext4 = EdhocMessageCodec.ParseCiphertextOnly(message4.ToArray(), "message_4");
        var k4 = EdhocKeySchedule.Kdf(
            _prk4e3m!, EdhocKeySchedule.LabelK4, _th4!, EdhocCrypto.AeadKeyLength);
        var iv4 = EdhocKeySchedule.Kdf(
            _prk4e3m!, EdhocKeySchedule.LabelIv4, _th4!, EdhocCrypto.AeadNonceLength);
        var plaintext4 = EdhocCrypto.CcmDecrypt(
            k4, iv4, EdhocMessageCodec.EncStructure(_th4!), ciphertext4);
        _receivedEad4 = EdhocMessageCodec.ParsePlaintext4(plaintext4);
    }

    /// <summary>
    /// EDHOC_Exporter( label, context, length ), RFC 9528 §4.2.1. Legal once
    /// CreateMessage3 has run; callable repeatedly with different labels.
    /// </summary>
    public byte[] Export(int label, ReadOnlySpan<byte> context, int length)
    {
        Require(Phase.Complete, "Export requires a completed handshake.");
        return ExportFrom(_prkExporter!, label, context, length);
    }

    internal static byte[] ExportFrom(byte[] prkExporter, int label, ReadOnlySpan<byte> context, int length)
    {
        if (label < 0)
        {
            throw new EdhocException("Exporter labels are non-negative.");
        }

        if (length <= 0)
        {
            throw new EdhocException("Export length must be positive.");
        }

        return EdhocKeySchedule.Kdf(prkExporter, label, context, length);
    }

    internal static byte[] RandomConnectionIdentifier()
    {
        // One byte from the CBOR-int identity range (RFC 9528 §3.3.2).
        var value = RandomNumberGenerator.GetInt32(48);
        return [value < 24 ? (byte)value : (byte)(0x20 + value - 24)];
    }

    internal static byte[] Xor(byte[] data, byte[] keystream)
    {
        var result = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ keystream[i]);
        }

        return result;
    }

    private void Require(Phase expected, string message)
    {
        if (_phase != expected)
        {
            throw new EdhocException(message);
        }
    }
}
