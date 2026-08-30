// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

using Org.BouncyCastle.Math;

using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;

namespace StonyGrey.Edhoc;

/// <summary>
/// The responder side of one EDHOC session (RFC 9528), method 3 (static-
/// static DH) with cipher suite 2. Single-use: drive it ProcessMessage1 →
/// CreateMessage2 → ProcessMessage3, after which <see cref="Export"/> yields
/// keying material; CreateMessage4 is optional explicit key confirmation.
/// The initiator is identified by the kid it presents in message_3 and
/// resolved to a credential through the resolver (or the single expected
/// credential) supplied up front.
/// </summary>
public sealed class EdhocResponder
{
    private enum Phase
    {
        Created,
        Message1Processed,
        Message2Sent,
        Complete,
    }

    private readonly EdhocCredential _own;
    private readonly Func<ReadOnlyMemory<byte>, EdhocCredential?> _resolvePeer;
    private readonly bool _allowUnknownInitiator;
    private EdhocCredential? _initiator;
    private readonly byte[] _cR;
    private readonly BigInteger _ephemeral;

    private Phase _phase;
    private bool _message4Created;
    private byte[]? _message1;
    private ECPoint? _gXPoint;
    private byte[]? _cI;
    private byte[]? _th3;
    private byte[]? _th4;
    private byte[]? _prk3e2m;
    private byte[]? _prk4e3m;
    private byte[]? _prkExporter;
    private byte[]? _receivedEad1;
    private byte[]? _receivedEad3;

    public EdhocResponder(
        EdhocCredential ownCredential,
        EdhocCredential expectedPeerCredential,
        byte[]? connectionIdentifier = null)
        : this(ownCredential,
               kid => kid.Span.SequenceEqual(expectedPeerCredential.Kid.Span)
                   ? expectedPeerCredential
                   : null,
               connectionIdentifier)
    {
    }

    /// <summary>
    /// Resolver form for responders that hold several initiator credentials:
    /// the kid presented in message_3 is looked up through
    /// <paramref name="peerCredentialResolver"/>; null means unknown and
    /// fails the handshake.
    /// </summary>
    public EdhocResponder(
        EdhocCredential ownCredential,
        Func<ReadOnlyMemory<byte>, EdhocCredential?> peerCredentialResolver,
        byte[]? connectionIdentifier = null,
        bool allowUnknownInitiator = false)
        : this(ownCredential, peerCredentialResolver, connectionIdentifier,
               new BigInteger(1, EdhocCrypto.RandomScalar()))
    {
        _allowUnknownInitiator = allowUnknownInitiator;
    }

    /// <summary>Fixed-ephemeral constructor for RFC 9529 vector tests.</summary>
    internal EdhocResponder(
        EdhocCredential ownCredential,
        Func<ReadOnlyMemory<byte>, EdhocCredential?> peerCredentialResolver,
        byte[]? connectionIdentifier,
        BigInteger ephemeral)
    {
        if (!ownCredential.HasPrivateKey)
        {
            throw new EdhocException("The responder's own credential must carry its private key.");
        }

        _cR = connectionIdentifier ?? EdhocInitiator.RandomConnectionIdentifier();
        if (_cR.Length == 0)
        {
            throw new EdhocException("The connection identifier must not be empty.");
        }

        _own = ownCredential;
        _resolvePeer = peerCredentialResolver;
        _ephemeral = ephemeral;
    }

    /// <summary>
    /// The initiator's credential as authenticated in message_3: the resolved
    /// pinned credential, or — when unknown initiators are allowed — the
    /// credential sent by value (kccs), for the caller to pin if the
    /// ceremony completes. Available after ProcessMessage3.
    /// </summary>
    public EdhocCredential InitiatorCredential =>
        _initiator ?? throw new EdhocException("The initiator credential is not known until ProcessMessage3 has run.");

    /// <summary>C_I as chosen by the initiator; available after ProcessMessage1.</summary>
    public ReadOnlyMemory<byte> InitiatorConnectionIdentifier =>
        _cI ?? throw new EdhocException("C_I is not known until ProcessMessage1 has run.");

    /// <summary>EAD_1 items the initiator sent, as a raw CBOR sequence, if any.</summary>
    public ReadOnlyMemory<byte>? ReceivedEad1 =>
        _receivedEad1 is null ? null : (ReadOnlyMemory<byte>?)_receivedEad1;

    /// <summary>EAD_3 items the initiator sent, as a raw CBOR sequence, if any.</summary>
    public ReadOnlyMemory<byte>? ReceivedEad3 =>
        _receivedEad3 is null ? null : (ReadOnlyMemory<byte>?)_receivedEad3;

    /// <summary>
    /// Consumes message_1. Throws an <see cref="EdhocException"/> whose
    /// <see cref="EdhocException.ErrorMessageToSend"/> carries a ready-encoded
    /// EDHOC error when the method or selected cipher suite is unsupported.
    /// RFC 9528 §5.2.
    /// </summary>
    public void ProcessMessage1(ReadOnlySpan<byte> message1)
    {
        Require(Phase.Created, "ProcessMessage1 may only be called once, first.");

        var raw = message1.ToArray();
        var parsed = EdhocMessageCodec.ParseMessage1(raw);
        if (parsed.Method != (int)EdhocMethod.StaticStatic)
        {
            throw new EdhocException(
                $"Method {parsed.Method} is not supported; only method 3 is implemented.",
                EdhocErrorMessage.Create("Only method 3 is supported."));
        }

        if (parsed.Suites[^1] != 2)
        {
            throw new EdhocException(
                $"Selected cipher suite {parsed.Suites[^1]} is not supported; only suite 2 is implemented.",
                EdhocErrorMessage.CreateWrongSuites(2));
        }

        _gXPoint = EdhocCrypto.DecodeXOnly(parsed.GX);
        _message1 = raw;
        _cI = parsed.CI;
        _receivedEad1 = parsed.Ead1;
        _phase = Phase.Message1Processed;
    }

    /// <summary>Builds message_2, authenticating this side to the initiator. When
    /// <paramref name="credentialByValue"/> is set — an enrolling initiator
    /// that cannot yet know this credential — ID_CRED_R carries the whole
    /// credential (kccs) instead of the kid. RFC 9528 §5.3.</summary>
    public byte[] CreateMessage2(byte[]? ead2 = null, bool credentialByValue = false)
    {
        Require(Phase.Message1Processed, "CreateMessage2 must follow ProcessMessage1.");
        var ead = EdhocCbor.ValidateExternalAuthorizationData(ead2);

        var gY = EdhocCrypto.XCoordinate(EdhocCrypto.PublicPoint(_ephemeral));
        var th2 = EdhocKeySchedule.TranscriptHash2(gY, _message1!);

        var prk2e = EdhocKeySchedule.Extract(th2, EdhocCrypto.Agree(_ephemeral, _gXPoint!));
        var salt3e2m = EdhocKeySchedule.Kdf(
            prk2e, EdhocKeySchedule.LabelSalt3e2m, th2, EdhocCrypto.HashLength);
        var prk3e2m = EdhocKeySchedule.Extract(
            salt3e2m, EdhocCrypto.Agree(_own.PrivateScalar!, _gXPoint!));

        var idCred2Compact = credentialByValue
            ? EdhocMessageCodec.EncodeIdCredKccs(_own.CredentialBytes)
            : EdhocMessageCodec.EncodeIdCredCompact(_own.Kid);
        var idCred2Full = credentialByValue
            ? idCred2Compact
            : EdhocMessageCodec.EncodeIdCredMap(_own.Kid);
        var context2 = EdhocMessageCodec.Context2(_cR, idCred2Full, th2, _own.CredentialBytes, ead);
        var mac2 = EdhocKeySchedule.Kdf(
            prk3e2m, EdhocKeySchedule.LabelMac2, context2, EdhocCrypto.MacLength);
        var plaintext2 = EdhocMessageCodec.EncodePlaintext2(_cR, idCred2Compact, mac2, ead);

        var keystream = EdhocKeySchedule.Kdf(
            prk2e, EdhocKeySchedule.LabelKeystream2, th2, plaintext2.Length);
        var ciphertext2 = EdhocInitiator.Xor(plaintext2, keystream);

        _th3 = EdhocKeySchedule.TranscriptHash3(th2, plaintext2, _own.CredentialBytes);
        _prk3e2m = prk3e2m;
        _phase = Phase.Message2Sent;

        return EdhocMessageCodec.EncodeMessage2(gY, ciphertext2);
    }

    /// <summary>
    /// Consumes message_3 and authenticates the initiator: the AEAD must open,
    /// the presented kid must resolve to a known credential, and MAC_3 must
    /// verify. Completes the key schedule: <see cref="Export"/> is legal
    /// afterwards. RFC 9528 §5.4.
    /// </summary>
    public void ProcessMessage3(ReadOnlySpan<byte> message3)
    {
        Require(Phase.Message2Sent, "ProcessMessage3 must follow CreateMessage2.");

        var ciphertext3 = EdhocMessageCodec.ParseCiphertextOnly(message3.ToArray(), "message_3");
        var k3 = EdhocKeySchedule.Kdf(
            _prk3e2m!, EdhocKeySchedule.LabelK3, _th3!, EdhocCrypto.AeadKeyLength);
        var iv3 = EdhocKeySchedule.Kdf(
            _prk3e2m!, EdhocKeySchedule.LabelIv3, _th3!, EdhocCrypto.AeadNonceLength);
        var plaintext3 = EdhocCrypto.CcmDecrypt(
            k3, iv3, EdhocMessageCodec.EncStructure(_th3!), ciphertext3);

        var (idCredI, mac3, ead3) = EdhocMessageCodec.ParsePlaintext3(plaintext3);
        EdhocCredential peer;
        byte[] idCred3Full;
        if (idCredI.Ccs is not null)
        {
            if (!_allowUnknownInitiator)
            {
                throw new EdhocException("Initiator sent a credential by value, but enrollment is not allowed.");
            }

            peer = EdhocCredential.FromCcs(idCredI.Ccs);
            idCred3Full = EdhocMessageCodec.EncodeIdCredKccs(idCredI.Ccs);
        }
        else
        {
            peer = _resolvePeer(idCredI.Kid!)
                ?? throw new EdhocException("Initiator presented an unknown credential kid.");
            idCred3Full = EdhocMessageCodec.EncodeIdCredMap(peer.Kid);
        }

        var salt4e3m = EdhocKeySchedule.Kdf(
            _prk3e2m!, EdhocKeySchedule.LabelSalt4e3m, _th3!, EdhocCrypto.HashLength);
        var prk4e3m = EdhocKeySchedule.Extract(
            salt4e3m, EdhocCrypto.Agree(_ephemeral, peer.PublicPoint));

        var context3 = EdhocMessageCodec.Context3(idCred3Full, _th3!, peer.CredentialBytes, ead3);
        var expectedMac3 = EdhocKeySchedule.Kdf(
            prk4e3m, EdhocKeySchedule.LabelMac3, context3, EdhocCrypto.MacLength);
        if (!CryptographicOperations.FixedTimeEquals(expectedMac3, mac3))
        {
            throw new EdhocException("Initiator authentication failed; MAC_3 did not verify.");
        }

        _initiator = peer;
        _th4 = EdhocKeySchedule.TranscriptHash4(_th3!, plaintext3, peer.CredentialBytes);
        var prkOut = EdhocKeySchedule.Kdf(
            prk4e3m, EdhocKeySchedule.LabelPrkOut, _th4, EdhocCrypto.HashLength);
        _prkExporter = EdhocKeySchedule.Kdf(
            prkOut, EdhocKeySchedule.LabelPrkExporter, [], EdhocCrypto.HashLength);
        _prk4e3m = prk4e3m;
        _receivedEad3 = ead3;
        _phase = Phase.Complete;
    }

    /// <summary>
    /// Builds the optional message_4, this side's explicit key confirmation.
    /// RFC 9528 §5.5.
    /// </summary>
    public byte[] CreateMessage4(byte[]? ead4 = null)
    {
        Require(Phase.Complete, "CreateMessage4 must follow ProcessMessage3.");
        if (_message4Created)
        {
            throw new EdhocException("CreateMessage4 may only be called once.");
        }

        _message4Created = true;
        var plaintext4 = EdhocCbor.ValidateExternalAuthorizationData(ead4) ?? [];
        var k4 = EdhocKeySchedule.Kdf(
            _prk4e3m!, EdhocKeySchedule.LabelK4, _th4!, EdhocCrypto.AeadKeyLength);
        var iv4 = EdhocKeySchedule.Kdf(
            _prk4e3m!, EdhocKeySchedule.LabelIv4, _th4!, EdhocCrypto.AeadNonceLength);
        var ciphertext4 = EdhocCrypto.CcmEncrypt(
            k4, iv4, EdhocMessageCodec.EncStructure(_th4!), plaintext4);
        return EdhocMessageCodec.EncodeCiphertextOnly(ciphertext4);
    }

    /// <summary>
    /// EDHOC_Exporter( label, context, length ), RFC 9528 §4.2.1. Legal once
    /// ProcessMessage3 has run; callable repeatedly with different labels.
    /// </summary>
    public byte[] Export(int label, ReadOnlySpan<byte> context, int length)
    {
        Require(Phase.Complete, "Export requires a completed handshake.");
        return EdhocInitiator.ExportFrom(_prkExporter!, label, context, length);
    }

    private void Require(Phase expected, string message)
    {
        if (_phase != expected)
        {
            throw new EdhocException(message);
        }
    }
}
