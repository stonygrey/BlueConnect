// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Edhoc.Tests;

using V = Rfc9529Trace2Vectors;

public class ErrorMessageTests
{
    [Fact]
    public void UnspecifiedErrorRoundTrips()
    {
        var encoded = EdhocErrorMessage.Create("boom");
        Assert.True(EdhocErrorMessage.TryParse(encoded, out var code, out var diagnostic, out var suites));
        Assert.Equal(1, code);
        Assert.Equal("boom", diagnostic);
        Assert.Null(suites);
    }

    [Fact]
    public void WrongSuitesErrorMatchesTheTraceBytes()
    {
        // RFC 9529 §3.2: SUITES_R = 2 encodes as the sequence 0x02 0x02.
        Assert.Equal(V.WrongSuiteError, EdhocErrorMessage.CreateWrongSuites(2));

        Assert.True(EdhocErrorMessage.TryParse(
            V.WrongSuiteError, out var code, out _, out var suites));
        Assert.Equal(2, code);
        Assert.Equal([2], suites!);
    }

    [Fact]
    public void WrongSuitesErrorSupportsPreferenceLists()
    {
        var encoded = EdhocErrorMessage.CreateWrongSuites(6, 2);
        Assert.Equal(Convert.FromHexString("02820602"), encoded);

        Assert.True(EdhocErrorMessage.TryParse(encoded, out _, out _, out var suites));
        Assert.Equal([6, 2], suites!);

        Assert.Throws<EdhocException>(() => EdhocErrorMessage.CreateWrongSuites());
    }

    [Fact]
    public void NonErrorsDoNotParse()
    {
        Assert.False(EdhocErrorMessage.TryParse(V.Message2, out _, out _, out _));
        Assert.False(EdhocErrorMessage.TryParse([], out _, out _, out _));
        Assert.False(EdhocErrorMessage.TryParse([0x01], out _, out _, out _));
    }

    [Fact]
    public void InitiatorSurfacesAReceivedError()
    {
        var initiator = new EdhocInitiator(
            V.InitiatorCredentialWithKey(), EdhocCredential.FromCcs(V.CredR), V.CI, [6, 2], V.X);
        initiator.CreateMessage1();

        var received = Assert.Throws<EdhocErrorReceivedException>(
            () => initiator.ProcessMessage2(V.WrongSuiteError));
        Assert.Equal(2, received.ErrorCode);
        Assert.Equal([2], received.SupportedSuites!);

        var initiator2 = new EdhocInitiator(
            V.InitiatorCredentialWithKey(), EdhocCredential.FromCcs(V.CredR), V.CI, [6, 2], V.X);
        initiator2.CreateMessage1();
        var unspecified = Assert.Throws<EdhocErrorReceivedException>(
            () => initiator2.ProcessMessage2(EdhocErrorMessage.Create("no slot")));
        Assert.Equal(1, unspecified.ErrorCode);
        Assert.Equal("no slot", unspecified.Diagnostic);
    }

    [Fact]
    public void ResponderSurfacesAReceivedError()
    {
        var credI = EdhocCredential.FromCcs(V.CredI);
        var responder = new EdhocResponder(
            V.ResponderCredentialWithKey(),
            kid => kid.Span.SequenceEqual(credI.Kid.Span) ? credI : null,
            V.CR, V.Y);
        responder.ProcessMessage1(V.Message1);
        responder.CreateMessage2();

        var received = Assert.Throws<EdhocErrorReceivedException>(
            () => responder.ProcessMessage3(EdhocErrorMessage.Create("abort")));
        Assert.Equal("abort", received.Diagnostic);
    }

    [Fact]
    public void UnsupportedMethodDrawsAnError()
    {
        var responder = new EdhocResponder(
            V.ResponderCredentialWithKey(), EdhocCredential.FromCcs(V.CredI), V.CR);

        // The trace message_1 with METHOD rewritten to 0.
        var message1 = V.Message1.ToArray();
        Assert.Equal(0x03, message1[0]);
        message1[0] = 0x00;

        var failure = Assert.Throws<EdhocException>(() => responder.ProcessMessage1(message1));
        Assert.NotNull(failure.ErrorMessageToSend);
        Assert.True(EdhocErrorMessage.TryParse(failure.ErrorMessageToSend, out var code, out _, out _));
        Assert.Equal(1, code);
    }
}
