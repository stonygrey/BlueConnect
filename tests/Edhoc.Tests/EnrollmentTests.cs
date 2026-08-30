// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Xunit;

namespace StonyGrey.Edhoc.Tests;

/// <summary>
/// The enrollment profile: neither side knows the other's credential up
/// front, so message_2 and message_3 carry the credentials by value (kccs)
/// and each side exposes what it authenticated for the caller to pin.
/// </summary>
public class EnrollmentTests
{
    [Fact]
    public void EnrollmentRoundTripExchangesCredentialsByValue()
    {
        var initiatorCred = EdhocCredential.Generate([0x02], subject: null);
        var responderCred = EdhocCredential.Generate([0x0A], subject: null);

        var initiator = EdhocInitiator.CreateEnrolling(initiatorCred);
        var responder = new EdhocResponder(
            responderCred, _ => null, connectionIdentifier: null, allowUnknownInitiator: true);

        var ead1 = EdhocEad.EncodeLabel(3737);
        var message1 = initiator.CreateMessage1(ead1);
        responder.ProcessMessage1(message1);
        Assert.True(EdhocEad.ContainsLabel(responder.ReceivedEad1, 3737));

        var message2 = responder.CreateMessage2(credentialByValue: true);
        initiator.ProcessMessage2(message2);
        Assert.Equal(responderCred.CredentialBytes.ToArray(), initiator.ResponderCredential.CredentialBytes.ToArray());

        var message3 = initiator.CreateMessage3();
        responder.ProcessMessage3(message3);
        Assert.Equal(initiatorCred.CredentialBytes.ToArray(), responder.InitiatorCredential.CredentialBytes.ToArray());

        // Both exporters agree — the handshake authenticated end to end.
        Assert.Equal(
            initiator.Export(32768, [], 64),
            responder.Export(32768, [], 64));
    }

    [Fact]
    public void PinnedResponderRejectsCredentialByValueWhenNotAllowed()
    {
        var initiatorCred = EdhocCredential.Generate([0x02], subject: null);
        var responderCred = EdhocCredential.Generate([0x0A], subject: null);

        var initiator = EdhocInitiator.CreateEnrolling(initiatorCred);
        var responder = new EdhocResponder(responderCred, _ => null);

        responder.ProcessMessage1(initiator.CreateMessage1());
        initiator.ProcessMessage2(responder.CreateMessage2(credentialByValue: true));
        var message3 = initiator.CreateMessage3();
        Assert.Throws<EdhocException>(() => responder.ProcessMessage3(message3));
    }

    [Fact]
    public void EnrollingInitiatorRequiresCredentialByValueInMessage2()
    {
        var initiatorCred = EdhocCredential.Generate([0x02], subject: null);
        var responderCred = EdhocCredential.Generate([0x0A], subject: null);

        var initiator = EdhocInitiator.CreateEnrolling(initiatorCred);
        var responder = new EdhocResponder(responderCred, _ => null, null, allowUnknownInitiator: true);

        responder.ProcessMessage1(initiator.CreateMessage1());
        var message2 = responder.CreateMessage2(); // kid form
        Assert.Throws<EdhocException>(() => initiator.ProcessMessage2(message2));
    }

    [Fact]
    public void EnrollmentEadIsValueLessAndPresentOnMessage1()
    {
        // BlueConnect's firmware accepts kccs only when EAD_1 is exactly
        // label 3737 (valueless). The C# responder leaves that policy to
        // the caller; this locks the encoding the firmware parses.
        var ead = EdhocEad.EncodeLabel(3737);
        Assert.True(EdhocEad.ContainsLabel(ead, 3737));
        Assert.False(EdhocEad.ContainsLabel(ead, 3736));

        var initiator = EdhocInitiator.CreateEnrolling(
            EdhocCredential.Generate([0x01], subject: null));
        var message1 = initiator.CreateMessage1(ead);
        var parsed = EdhocMessageCodec.ParseMessage1(message1);
        Assert.True(EdhocEad.ContainsLabel(parsed.Ead1, 3737));
    }
}
