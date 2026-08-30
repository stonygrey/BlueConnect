// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Edhoc.Tests;

using V = Rfc9529Trace2Vectors;

/// <summary>Every out-of-order or repeated call must throw, like Spake2Plus.</summary>
public class MisuseTests
{
    private static EdhocInitiator NewInitiator() => new(
        V.InitiatorCredentialWithKey(), EdhocCredential.FromCcs(V.CredR), V.CI, [6, 2], V.X);

    private static EdhocResponder NewResponder()
    {
        var credI = EdhocCredential.FromCcs(V.CredI);
        return new EdhocResponder(
            V.ResponderCredentialWithKey(),
            kid => kid.Span.SequenceEqual(credI.Kid.Span) ? credI : null,
            V.CR, V.Y);
    }

    [Fact]
    public void InitiatorEnforcesCallOrder()
    {
        var initiator = NewInitiator();
        Assert.Throws<EdhocException>(() => initiator.ProcessMessage2(V.Message2));
        Assert.Throws<EdhocException>(() => initiator.CreateMessage3());
        Assert.Throws<EdhocException>(() => initiator.Export(0, [], 16));
        Assert.Throws<EdhocException>(() => initiator.ResponderConnectionIdentifier);

        initiator.CreateMessage1();
        Assert.Throws<EdhocException>(() => initiator.CreateMessage1());
        Assert.Throws<EdhocException>(() => initiator.CreateMessage3());
        Assert.Throws<EdhocException>(() => initiator.ProcessMessage4(V.Message4));

        initiator.ProcessMessage2(V.Message2);
        Assert.Throws<EdhocException>(() => initiator.ProcessMessage2(V.Message2));
        Assert.Throws<EdhocException>(() => initiator.Export(0, [], 16));

        initiator.CreateMessage3();
        Assert.Throws<EdhocException>(() => initiator.CreateMessage3());

        initiator.ProcessMessage4(V.Message4);
        Assert.Throws<EdhocException>(() => initiator.ProcessMessage4(V.Message4));
    }

    [Fact]
    public void ResponderEnforcesCallOrder()
    {
        var responder = NewResponder();
        Assert.Throws<EdhocException>(() => responder.CreateMessage2());
        Assert.Throws<EdhocException>(() => responder.ProcessMessage3(V.Message3));
        Assert.Throws<EdhocException>(() => responder.Export(0, [], 16));
        Assert.Throws<EdhocException>(() => responder.InitiatorConnectionIdentifier);

        responder.ProcessMessage1(V.Message1);
        Assert.Throws<EdhocException>(() => responder.ProcessMessage1(V.Message1));
        Assert.Throws<EdhocException>(() => responder.ProcessMessage3(V.Message3));
        Assert.Throws<EdhocException>(() => responder.CreateMessage4());

        responder.CreateMessage2();
        Assert.Throws<EdhocException>(() => responder.CreateMessage2());

        responder.ProcessMessage3(V.Message3);
        Assert.Throws<EdhocException>(() => responder.ProcessMessage3(V.Message3));

        responder.CreateMessage4();
        Assert.Throws<EdhocException>(() => responder.CreateMessage4());
    }

    [Fact]
    public void OwnCredentialsRequireThePrivateKey()
    {
        var publicOnly = EdhocCredential.FromCcs(V.CredI);
        var peer = EdhocCredential.FromCcs(V.CredR);
        Assert.Throws<EdhocException>(() => new EdhocInitiator(publicOnly, peer));
        Assert.Throws<EdhocException>(
            () => new EdhocResponder(EdhocCredential.FromCcs(V.CredR), publicOnly));
    }

    [Fact]
    public void ConstructorRejectsBadArguments()
    {
        var own = V.InitiatorCredentialWithKey();
        var peer = EdhocCredential.FromCcs(V.CredR);

        Assert.Throws<EdhocException>(() => new EdhocInitiator(own, peer, []));
        Assert.Throws<EdhocException>(() => new EdhocInitiator(own, peer, suitesI: []));
        Assert.Throws<EdhocException>(() => new EdhocInitiator(own, peer, suitesI: [6]));
        Assert.Throws<EdhocException>(() => new EdhocInitiator(own, peer, suitesI: [2, 6]));
    }

    [Fact]
    public void ExportValidatesItsArguments()
    {
        var initiator = NewInitiator();
        initiator.CreateMessage1();
        initiator.ProcessMessage2(V.Message2);
        initiator.CreateMessage3();

        Assert.Throws<EdhocException>(() => initiator.Export(-1, [], 16));
        Assert.Throws<EdhocException>(() => initiator.Export(0, [], 0));
    }
}
