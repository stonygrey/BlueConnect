// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Edhoc.Tests;

using V = Rfc9529Trace2Vectors;

/// <summary>
/// Known-answer tests against RFC 9529 §3 (method 3, cipher suite 2, CCS by
/// kid). Ephemerals are injected through the internal constructors; every
/// on-wire message and derived value must match the RFC byte-exactly. These
/// vectors are also what the firmware responder conforms to, so passing here
/// is the interop baseline.
/// </summary>
public class Rfc9529Trace2VectorTests
{
    [Fact]
    public void InitiatorMatchesVectors()
    {
        var initiator = new EdhocInitiator(
            V.InitiatorCredentialWithKey(), EdhocCredential.FromCcs(V.CredR),
            V.CI, [6, 2], V.X);

        Assert.Equal(V.Message1, initiator.CreateMessage1());

        initiator.ProcessMessage2(V.Message2);
        Assert.Equal(V.CR, initiator.ResponderConnectionIdentifier.ToArray());
        Assert.Null(initiator.ReceivedEad2);

        Assert.Equal(V.Message3, initiator.CreateMessage3());

        Assert.Equal(V.OscoreMasterSecret, initiator.Export(0, [], 16));
        Assert.Equal(V.OscoreMasterSalt, initiator.Export(1, [], 8));

        initiator.ProcessMessage4(V.Message4);
        Assert.Null(initiator.ReceivedEad4);
    }

    [Fact]
    public void ResponderMatchesVectors()
    {
        var credI = EdhocCredential.FromCcs(V.CredI);
        var responder = new EdhocResponder(
            V.ResponderCredentialWithKey(),
            kid => kid.Span.SequenceEqual(credI.Kid.Span) ? credI : null,
            V.CR, V.Y);

        responder.ProcessMessage1(V.Message1);
        Assert.Equal(V.CI, responder.InitiatorConnectionIdentifier.ToArray());
        Assert.Null(responder.ReceivedEad1);

        Assert.Equal(V.Message2, responder.CreateMessage2());

        responder.ProcessMessage3(V.Message3);
        Assert.Null(responder.ReceivedEad3);

        Assert.Equal(V.Message4, responder.CreateMessage4());

        Assert.Equal(V.OscoreMasterSecret, responder.Export(0, [], 16));
        Assert.Equal(V.OscoreMasterSalt, responder.Export(1, [], 8));
    }

    [Fact]
    public void SuiteNegotiationPrologueMatchesVectors()
    {
        // §3.1–3.2: the responder only supports suite 2, so a message_1
        // selecting suite 6 draws the ERR_CODE 2 error proposing suite 2.
        var credI = EdhocCredential.FromCcs(V.CredI);
        var responder = new EdhocResponder(V.ResponderCredentialWithKey(), credI, V.CR);

        var failure = Assert.Throws<EdhocException>(
            () => responder.ProcessMessage1(V.Message1FirstTime));
        Assert.Equal(V.WrongSuiteError, failure.ErrorMessageToSend);
    }

    [Fact]
    public void KeyScheduleMatchesVectors()
    {
        // Every §3.4–3.7 intermediate, computed directly against the internal
        // key schedule. This is the layer the firmware mirrors step by step.
        Assert.Equal(V.Th2, EdhocKeySchedule.TranscriptHash2(V.GY, V.Message1));
        Assert.Equal(V.GXy, EdhocCrypto.Agree(V.X, EdhocCrypto.DecodeXOnly(V.GY)));
        Assert.Equal(V.Prk2e, EdhocKeySchedule.Extract(V.Th2, V.GXy));

        Assert.Equal(V.Salt3e2m, EdhocKeySchedule.Kdf(
            V.Prk2e, EdhocKeySchedule.LabelSalt3e2m, V.Th2, 32));
        Assert.Equal(V.GRx, EdhocCrypto.Agree(V.X, EdhocCrypto.DecodePoint(V.PkRx, V.PkRy)));
        Assert.Equal(V.Prk3e2m, EdhocKeySchedule.Extract(V.Salt3e2m, V.GRx));

        var context2 = EdhocMessageCodec.Context2(V.CR, EdhocMessageCodec.EncodeIdCredMap(V.KidR), V.Th2, V.CredR, null);
        Assert.Equal(V.Mac2, EdhocKeySchedule.Kdf(
            V.Prk3e2m, EdhocKeySchedule.LabelMac2, context2, 8));

        Assert.Equal(V.Plaintext2, EdhocMessageCodec.EncodePlaintext2(V.CR, EdhocMessageCodec.EncodeIdCredCompact(V.KidR), V.Mac2, null));
        Assert.Equal(V.Keystream2, EdhocKeySchedule.Kdf(
            V.Prk2e, EdhocKeySchedule.LabelKeystream2, V.Th2, V.Plaintext2.Length));

        Assert.Equal(V.Th3, EdhocKeySchedule.TranscriptHash3(V.Th2, V.Plaintext2, V.CredR));

        Assert.Equal(V.Salt4e3m, EdhocKeySchedule.Kdf(
            V.Prk3e2m, EdhocKeySchedule.LabelSalt4e3m, V.Th3, 32));
        Assert.Equal(V.GIy, EdhocCrypto.Agree(V.Y, EdhocCrypto.DecodePoint(V.PkIx, V.PkIy)));
        Assert.Equal(V.Prk4e3m, EdhocKeySchedule.Extract(V.Salt4e3m, V.GIy));

        var context3 = EdhocMessageCodec.Context3(EdhocMessageCodec.EncodeIdCredMap(V.KidI), V.Th3, V.CredI, null);
        Assert.Equal(V.Mac3, EdhocKeySchedule.Kdf(
            V.Prk4e3m, EdhocKeySchedule.LabelMac3, context3, 8));

        Assert.Equal(V.Plaintext3, EdhocMessageCodec.EncodePlaintext3(EdhocMessageCodec.EncodeIdCredCompact(V.KidI), V.Mac3, null));
        Assert.Equal(V.K3, EdhocKeySchedule.Kdf(V.Prk3e2m, EdhocKeySchedule.LabelK3, V.Th3, 16));
        Assert.Equal(V.Iv3, EdhocKeySchedule.Kdf(V.Prk3e2m, EdhocKeySchedule.LabelIv3, V.Th3, 13));

        Assert.Equal(V.Th4, EdhocKeySchedule.TranscriptHash4(V.Th3, V.Plaintext3, V.CredI));
        Assert.Equal(V.K4, EdhocKeySchedule.Kdf(V.Prk4e3m, EdhocKeySchedule.LabelK4, V.Th4, 16));
        Assert.Equal(V.Iv4, EdhocKeySchedule.Kdf(V.Prk4e3m, EdhocKeySchedule.LabelIv4, V.Th4, 13));

        Assert.Equal(V.PrkOut, EdhocKeySchedule.Kdf(
            V.Prk4e3m, EdhocKeySchedule.LabelPrkOut, V.Th4, 32));
        Assert.Equal(V.PrkExporter, EdhocKeySchedule.Kdf(
            V.PrkOut, EdhocKeySchedule.LabelPrkExporter, [], 32));
    }
}
