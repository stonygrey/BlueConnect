// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Edhoc.Tests;

/// <summary>
/// End-to-end loopback with random ephemerals and freshly issued credentials,
/// plus tamper and mismatched-credential rejection.
/// </summary>
public class ProtocolTests
{
    private static readonly byte[] DeviceKid = [0x0a];
    private static readonly byte[] HostKid = [0x01];

    private static (EdhocInitiator Initiator, EdhocResponder Responder) NewPair(
        EdhocCredential? hostForResponder = null, EdhocCredential? deviceForInitiator = null)
    {
        var device = EdhocCredential.Generate(DeviceKid, "device");
        var host = EdhocCredential.Generate(HostKid, null);

        var initiator = new EdhocInitiator(
            host,
            deviceForInitiator ?? EdhocCredential.FromCcs(device.CredentialBytes.Span));
        var expected = hostForResponder ?? EdhocCredential.FromCcs(host.CredentialBytes.Span);
        var responder = new EdhocResponder(device, expected);
        return (initiator, responder);
    }

    [Fact]
    public void LoopbackDerivesEqualKeys()
    {
        var (initiator, responder) = NewPair();

        responder.ProcessMessage1(initiator.CreateMessage1());
        initiator.ProcessMessage2(responder.CreateMessage2());
        responder.ProcessMessage3(initiator.CreateMessage3());

        Assert.Equal(
            initiator.ResponderConnectionIdentifier.ToArray(),
            initiator.ResponderConnectionIdentifier.ToArray());
        Assert.Equal(initiator.Export(32768, [], 64), responder.Export(32768, [], 64));
        Assert.NotEqual(initiator.Export(32768, [], 64), initiator.Export(32769, [], 64));
        Assert.NotEqual(
            initiator.Export(32768, [], 64), initiator.Export(32768, [0x01], 64));
    }

    [Fact]
    public void LoopbackWithMessage4Confirms()
    {
        var (initiator, responder) = NewPair();

        responder.ProcessMessage1(initiator.CreateMessage1());
        initiator.ProcessMessage2(responder.CreateMessage2());
        responder.ProcessMessage3(initiator.CreateMessage3());
        initiator.ProcessMessage4(responder.CreateMessage4());

        Assert.Equal(initiator.Export(0, [], 16), responder.Export(0, [], 16));
    }

    [Fact]
    public void ExternalAuthorizationDataPassesThrough()
    {
        var (initiator, responder) = NewPair();
        byte[] ead1 = [0x01, 0x41, 0xaa];        // label 1, bstr h'aa'
        byte[] ead2 = [0x02];                    // label 2, no value

        responder.ProcessMessage1(initiator.CreateMessage1(ead1));
        Assert.Equal(ead1, responder.ReceivedEad1!.Value.ToArray());

        initiator.ProcessMessage2(responder.CreateMessage2(ead2));
        Assert.Equal(ead2, initiator.ReceivedEad2!.Value.ToArray());

        responder.ProcessMessage3(initiator.CreateMessage3(ead1));
        Assert.Equal(ead1, responder.ReceivedEad3!.Value.ToArray());

        initiator.ProcessMessage4(responder.CreateMessage4(ead2));
        Assert.Equal(ead2, initiator.ReceivedEad4!.Value.ToArray());
    }

    [Fact]
    public void CriticalEadIsRejected()
    {
        var (initiator, _) = NewPair();
        Assert.Throws<EdhocException>(() => initiator.CreateMessage1([0x20]));  // label -1
    }

    [Fact]
    public void TamperedMessage1FailsResponderAuthentication()
    {
        var (initiator, responder) = NewPair();

        var message1 = initiator.CreateMessage1();
        message1[^1] ^= 0x01;                    // C_I: still well-formed, different transcript
        responder.ProcessMessage1(message1);

        var message2 = responder.CreateMessage2();
        Assert.Throws<EdhocException>(() => initiator.ProcessMessage2(message2));
    }

    [Fact]
    public void TamperedMessage2IsRejected()
    {
        var (initiator, responder) = NewPair();

        responder.ProcessMessage1(initiator.CreateMessage1());
        var message2 = responder.CreateMessage2();
        message2[^1] ^= 0x01;
        Assert.Throws<EdhocException>(() => initiator.ProcessMessage2(message2));
    }

    [Fact]
    public void TamperedMessage3IsRejected()
    {
        var (initiator, responder) = NewPair();

        responder.ProcessMessage1(initiator.CreateMessage1());
        initiator.ProcessMessage2(responder.CreateMessage2());
        var message3 = initiator.CreateMessage3();
        message3[^1] ^= 0x01;
        Assert.Throws<EdhocException>(() => responder.ProcessMessage3(message3));
    }

    [Fact]
    public void TamperedMessage4IsRejected()
    {
        var (initiator, responder) = NewPair();

        responder.ProcessMessage1(initiator.CreateMessage1());
        initiator.ProcessMessage2(responder.CreateMessage2());
        responder.ProcessMessage3(initiator.CreateMessage3());
        var message4 = responder.CreateMessage4();
        message4[^1] ^= 0x01;
        Assert.Throws<EdhocException>(() => initiator.ProcessMessage4(message4));
    }

    [Fact]
    public void WrongInitiatorKeyFailsMac3()
    {
        // The responder expects a different credential under the same kid.
        var otherHost = EdhocCredential.Generate(HostKid, null);
        var (initiator, responder) = NewPair(hostForResponder: otherHost);

        responder.ProcessMessage1(initiator.CreateMessage1());
        initiator.ProcessMessage2(responder.CreateMessage2());
        var failure = Assert.Throws<EdhocException>(
            () => responder.ProcessMessage3(initiator.CreateMessage3()));
        Assert.Contains("MAC_3", failure.Message);
    }

    [Fact]
    public void WrongPinnedDeviceCredentialFailsMac2()
    {
        // The initiator pins a different device credential under the same kid.
        var otherDevice = EdhocCredential.Generate(DeviceKid, "device");
        var (initiator, responder) = NewPair(
            deviceForInitiator: EdhocCredential.FromCcs(otherDevice.CredentialBytes.Span));

        responder.ProcessMessage1(initiator.CreateMessage1());
        var failure = Assert.Throws<EdhocException>(
            () => initiator.ProcessMessage2(responder.CreateMessage2()));
        Assert.Contains("MAC_2", failure.Message);
    }

    [Fact]
    public void UnknownInitiatorKidIsRejected()
    {
        var device = EdhocCredential.Generate(DeviceKid, "device");
        var host = EdhocCredential.Generate(HostKid, null);
        var initiator = new EdhocInitiator(
            host, EdhocCredential.FromCcs(device.CredentialBytes.Span));
        var responder = new EdhocResponder(device, _ => null);

        responder.ProcessMessage1(initiator.CreateMessage1());
        initiator.ProcessMessage2(responder.CreateMessage2());
        var failure = Assert.Throws<EdhocException>(
            () => responder.ProcessMessage3(initiator.CreateMessage3()));
        Assert.Contains("unknown credential kid", failure.Message);
    }
}
