// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Security.Cryptography;

using Microsoft.Extensions.Logging.Abstractions;

using StonyGrey.BlueConnect.Core;
using StonyGrey.BlueConnect.Secure;

using StonyGrey.Edhoc;
using StonyGrey.Spake2Plus;

namespace StonyGrey.BlueConnect.Provision;

/// <summary>
/// Loopback validation of the EDHOC link: a real <see cref="EdhocLink"/>
/// initiator runs against an in-process device simulator speaking the exact
/// 0x04 wire dialect the firmware implements (Msg1/Msg2/Msg3/Status, then
/// AES-256-GCM transport keyed from the EDHOC exporter). Proves the happy
/// path at both tiers and the negative cases: wrong initiator key,
/// unprovisioned slot, wrong pinned device credential, tampered message_2,
/// tampered and replayed transport frames, truncated messages, and the
/// Msg1-resets-session resync rule.
/// </summary>
internal static class EdhocSelfTest
{
    public static int Run()
    {
        foreach (var tier in (PakeTier[])[PakeTier.Low, PakeTier.High])
        {
            RoundTrip(tier);
        }

        EnrollmentPairsAndRoundTrips();
        EnrollmentRefusedWhenDeviceDoesNotAllowIt();
        WrongInitiatorKeyIsRejected();
        UnprovisionedSlotIsRefused();
        WrongPinnedDeviceCredentialIsRejected();
        TamperedMessage2IsRejected();
        TamperedTransportFrameIsRejected();
        ReplayedTransportFrameIsRejected();
        TruncatedMessage2IsRejected();
        Msg1MidHandshakeResyncs();

        Console.WriteLine("EDHOC self-test PASSED.");
        return 0;
    }

    private static (EdhocSecurity Security, DeviceSim.Slots Slots) Provision(PakeTier tier)
    {
        // What edhoc-provision does: issue the initiator credential for the
        // tier, hand the device the CCS bytes, pin the device's CCS.
        var kid = tier == PakeTier.High ? BlueConnectEdhoc.HighKid : BlueConnectEdhoc.LowKid;
        var initiator = EdhocCredential.Generate([kid], subject: null);
        var device = EdhocCredential.Generate([BlueConnectEdhoc.DeviceKid], subject: null);

        var security = new EdhocSecurity(
            initiator.PrivateKey!.Value.ToArray(), kid, device.CredentialBytes.ToArray());
        var slots = new DeviceSim.Slots(
            device,
            Low: tier == PakeTier.Low ? initiator.CredentialBytes.ToArray() : null,
            High: tier == PakeTier.High ? initiator.CredentialBytes.ToArray() : null);
        return (security, slots);
    }

    private static void RoundTrip(PakeTier tier)
    {
        var (security, slots) = Provision(tier);
        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new EdhocLink(phonePipe, 4096, security, NullLogger.Instance);
        var device = new DeviceSim(devicePipe, slots);
        var deviceSide = Task.Run(() => device.RunOnce(echoes: 1));

        var payload = new byte[1536];
        RandomNumberGenerator.Fill(payload);
        link.Send(payload, Cancel().Token).GetAwaiter().GetResult();
        var reply = link.Receive(Cancel().Token).GetAwaiter().GetResult();
        deviceSide.GetAwaiter().GetResult();

        Check(reply.AsSpan().SequenceEqual(payload), $"sealed round-trip at {tier} tier");
    }

    private static void EnrollmentPairsAndRoundTrips()
    {
        // Nothing provisioned anywhere: the phone enrolls (EAD 3737, both
        // credentials by value), learns the device credential, and the
        // learned pair then works as a normal pinned session — what the app
        // stores after the ceremony confirms.
        var initiator = EdhocCredential.Generate([BlueConnectEdhoc.LowKid], subject: null);
        var device = EdhocCredential.Generate([BlueConnectEdhoc.DeviceKid], subject: null);
        var enrollingSecurity = new EdhocSecurity(
            initiator.PrivateKey!.Value.ToArray(), BlueConnectEdhoc.LowKid, DeviceCcs: null);
        var slots = new DeviceSim.Slots(device, Low: null, High: null);

        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new EdhocLink(phonePipe, 4096, enrollingSecurity, NullLogger.Instance);
        var sim = new DeviceSim(devicePipe, slots) { AllowEnrollment = true };
        var deviceSide = Task.Run(() => sim.RunOnce(echoes: 1));

        var payload = new byte[512];
        RandomNumberGenerator.Fill(payload);
        link.Send(payload, Cancel().Token).GetAwaiter().GetResult();
        var reply = link.Receive(Cancel().Token).GetAwaiter().GetResult();
        deviceSide.GetAwaiter().GetResult();

        Check(reply.AsSpan().SequenceEqual(payload), "sealed round-trip on an enrolling session");
        Check(link.LearnedDeviceCcs is not null
            && link.LearnedDeviceCcs.AsSpan().SequenceEqual(device.CredentialBytes.Span),
            "enrollment learned the device credential");

        // Reconnect pinned both ways, as after the ceremony: the device has
        // pinned the initiator CCS, the phone stores the learned device CCS.
        var pinnedSecurity = new EdhocSecurity(
            enrollingSecurity.InitiatorPrivateKey, BlueConnectEdhoc.LowKid, link.LearnedDeviceCcs);
        var pinnedSlots = new DeviceSim.Slots(device, Low: initiator.CredentialBytes.ToArray(), High: null);
        var (phonePipe2, devicePipe2) = InMemoryPeripheral.CreatePair();
        using var pinnedLink = new EdhocLink(phonePipe2, 4096, pinnedSecurity, NullLogger.Instance);
        var pinnedSim = new DeviceSim(devicePipe2, pinnedSlots);
        var pinnedSide = Task.Run(() => pinnedSim.RunOnce(echoes: 1));

        link.Dispose();
        pinnedLink.Send(payload, Cancel().Token).GetAwaiter().GetResult();
        var pinnedReply = pinnedLink.Receive(Cancel().Token).GetAwaiter().GetResult();
        pinnedSide.GetAwaiter().GetResult();
        Check(pinnedReply.AsSpan().SequenceEqual(payload), "learned credentials work as a pinned session");
        Check(pinnedLink.LearnedDeviceCcs is null, "pinned session learns nothing");
    }

    private static void EnrollmentRefusedWhenDeviceDoesNotAllowIt()
    {
        // The device answers the enrollment EAD with its plain kid (as a
        // locked-down responder would): the enrolling phone must refuse the
        // session rather than trust an unverifiable credential.
        var initiator = EdhocCredential.Generate([BlueConnectEdhoc.LowKid], subject: null);
        var device = EdhocCredential.Generate([BlueConnectEdhoc.DeviceKid], subject: null);
        var enrollingSecurity = new EdhocSecurity(
            initiator.PrivateKey!.Value.ToArray(), BlueConnectEdhoc.LowKid, DeviceCcs: null);
        var slots = new DeviceSim.Slots(device, Low: initiator.CredentialBytes.ToArray(), High: null);

        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new EdhocLink(phonePipe, 4096, enrollingSecurity, NullLogger.Instance);
        var sim = new DeviceSim(devicePipe, slots); // AllowEnrollment stays false
        _ = Task.Run(() => sim.RunOnce(echoes: 0));

        Throws(() => link.Establish(Cancel().Token).GetAwaiter().GetResult(),
            "enrollment refused without a by-value device credential");
    }

    private static void WrongInitiatorKeyIsRejected()
    {
        var (security, slots) = Provision(PakeTier.Low);
        // A different key under the same kid: MAC_3 cannot verify.
        var rogue = EdhocCredential.Generate([BlueConnectEdhoc.LowKid], subject: null);
        var wrong = security with { InitiatorPrivateKey = rogue.PrivateKey!.Value.ToArray() };

        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new EdhocLink(phonePipe, 4096, wrong, NullLogger.Instance);
        var device = new DeviceSim(devicePipe, slots);
        _ = Task.Run(() => device.RunOnce(echoes: 0));

        Throws(() => link.Establish(Cancel().Token).GetAwaiter().GetResult(), "wrong initiator key rejected");
    }

    private static void UnprovisionedSlotIsRefused()
    {
        var (security, slots) = Provision(PakeTier.Low);
        // Ask for the high slot with a key the device never saw.
        var high = EdhocCredential.Generate([BlueConnectEdhoc.HighKid], subject: null);
        var wrongTier = new EdhocSecurity(
            high.PrivateKey!.Value.ToArray(), BlueConnectEdhoc.HighKid, security.DeviceCcs);

        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new EdhocLink(phonePipe, 4096, wrongTier, NullLogger.Instance);
        var device = new DeviceSim(devicePipe, slots);
        _ = Task.Run(() => device.RunOnce(echoes: 0));

        Throws(() => link.Establish(Cancel().Token).GetAwaiter().GetResult(), "unprovisioned slot refused");
    }

    private static void WrongPinnedDeviceCredentialIsRejected()
    {
        var (security, slots) = Provision(PakeTier.Low);
        // The initiator pins a different device credential: MAC_2 fails at the
        // phone before message_3 reveals anything.
        var otherDevice = EdhocCredential.Generate([BlueConnectEdhoc.DeviceKid], subject: null);
        var wrong = security with { DeviceCcs = otherDevice.CredentialBytes.ToArray() };

        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new EdhocLink(phonePipe, 4096, wrong, NullLogger.Instance);
        var device = new DeviceSim(devicePipe, slots);
        _ = Task.Run(() => device.RunOnce(echoes: 0));

        Throws(() => link.Establish(Cancel().Token).GetAwaiter().GetResult(), "wrong pinned device credential rejected");
    }

    private static void TamperedMessage2IsRejected()
    {
        var (security, slots) = Provision(PakeTier.Low);
        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new EdhocLink(phonePipe, 4096, security, NullLogger.Instance);
        var device = new DeviceSim(devicePipe, slots) { TamperMessage2 = true };
        _ = Task.Run(() => device.RunOnce(echoes: 0));

        Throws(() => link.Establish(Cancel().Token).GetAwaiter().GetResult(), "tampered message_2 rejected");
    }

    private static void TamperedTransportFrameIsRejected()
    {
        var (security, slots) = Provision(PakeTier.Low);
        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new EdhocLink(phonePipe, 4096, security, NullLogger.Instance);
        var device = new DeviceSim(devicePipe, slots);
        var deviceSide = Task.Run(() => device.RunOnce(echoes: 1));

        link.Establish(Cancel().Token).GetAwaiter().GetResult();
        phonePipe.TamperNext = true; // corrupt the next inbound data frame
        link.Send(new byte[64], Cancel().Token).GetAwaiter().GetResult();
        Throws(() => link.Receive(Cancel().Token).GetAwaiter().GetResult(), "tampered transport frame rejected");
        try
        {
            deviceSide.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // The simulator may observe the torn-down pipe; not under test.
        }
    }

    private static void ReplayedTransportFrameIsRejected()
    {
        var (security, slots) = Provision(PakeTier.Low);
        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new EdhocLink(phonePipe, 4096, security, NullLogger.Instance);
        var device = new DeviceSim(devicePipe, slots) { ReplayEcho = true };
        var deviceSide = Task.Run(() => device.RunOnce(echoes: 1));

        var ping = new byte[] { 1, 2, 3, 4 };
        link.Send(ping, Cancel().Token).GetAwaiter().GetResult();
        var first = link.Receive(Cancel().Token).GetAwaiter().GetResult();
        Check(first.AsSpan().SequenceEqual(ping), "reply before replay");

        // The simulator re-sends the identical sealed frame; the receive
        // counter has moved on, so authentication must fail.
        Throws(() => link.Receive(Cancel().Token).GetAwaiter().GetResult(), "replayed transport frame rejected");
        deviceSide.GetAwaiter().GetResult();
    }

    private static void TruncatedMessage2IsRejected()
    {
        var (security, slots) = Provision(PakeTier.Low);
        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new EdhocLink(phonePipe, 4096, security, NullLogger.Instance);
        var device = new DeviceSim(devicePipe, slots) { TruncateMessage2 = true };
        _ = Task.Run(() => device.RunOnce(echoes: 0));

        Throws(() => link.Establish(Cancel().Token).GetAwaiter().GetResult(), "truncated message_2 rejected");
    }

    private static void Msg1MidHandshakeResyncs()
    {
        // A fresh Msg1 after the device sent message_2 must reset the device's
        // session and start over, not wedge it. The phone side is hand-rolled
        // here because abandoning a handshake mid-flight over one connection
        // is exactly what EdhocLink never does.
        var (security, slots) = Provision(PakeTier.Low);
        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        var device = new DeviceSim(devicePipe, slots);
        var deviceSide = Task.Run(() => device.RunResyncProbe());

        var segments = new SegmentLink(phonePipe, 4096, logger: NullLogger.Instance);
        var own = BlueConnectEdhoc.InitiatorCredential(security.InitiatorPrivateKey, security.InitiatorKid);
        var deviceCred = EdhocCredential.FromCcs(security.DeviceCcs);

        // Attempt A: send message_1, read message_2, walk away.
        var abandoned = new EdhocInitiator(own, deviceCred);
        WriteEdhoc(segments, EdhocLink.MsgM1, abandoned.CreateMessage1());
        _ = ReadEdhocFrame(segments);

        // Attempt B over the same connection must establish cleanly.
        var fresh = new EdhocInitiator(own, deviceCred);
        WriteEdhoc(segments, EdhocLink.MsgM1, fresh.CreateMessage1());
        var message2 = ReadEdhocFrame(segments);
        Check(message2.Length >= 2 && message2[0] == EdhocLink.MsgM2, "message_2 after resync");
        fresh.ProcessMessage2(message2.AsSpan(1));
        WriteEdhoc(segments, EdhocLink.MsgM3, fresh.CreateMessage3());
        var status = ReadEdhocFrame(segments);
        Check(status.Length == EdhocLink.StatusLength
            && status[0] == EdhocLink.MsgStatus && status[1] == EdhocLink.StatusOk,
            "handshake completes after resync");

        deviceSide.GetAwaiter().GetResult();
        Check(device.SawResync, "Msg1 mid-handshake resets the session");
    }

    private static void WriteEdhoc(SegmentLink segments, byte kind, byte[] body)
    {
        var framed = new byte[1 + body.Length];
        framed[0] = kind;
        body.CopyTo(framed, 1);
        segments.Write(
            FrameType.Bundle(FrameType.EdhocHandshake, framed),
            CancellationToken.None).GetAwaiter().GetResult();
    }

    private static byte[] ReadEdhocFrame(SegmentLink segments)
    {
        var (type, payload) = FrameType.Split(
            segments.Read(Cancel().Token).GetAwaiter().GetResult());
        Check(type == FrameType.EdhocHandshake, "EDHOC frame type on the resync probe");
        return payload;
    }

    private static CancellationTokenSource Cancel() => new(TimeSpan.FromSeconds(10));

    private static void Throws(Action action, string what)
    {
        try
        {
            action();
            Check(false, what);
        }
        catch (LinkSecurityException)
        {
            Check(true, what);
        }
    }

    private static void Check(bool condition, string what)
    {
        Console.WriteLine($"  {(condition ? "ok" : "FAIL")}: {what}");
        if (!condition)
        {
            throw new InvalidOperationException($"EDHOC self-test failed: {what}");
        }
    }

    /// <summary>
    /// The device side of the 0x04 dialect, as the firmware implements it:
    /// per-slot pinned initiator credentials, kid-to-slot lookup at
    /// message_3, explicit Status verdicts, Msg1-resets-session, then the
    /// sealed transport with the Noise nonce discipline. Fault injection
    /// knobs stand in for the negative paths.
    /// </summary>
    private sealed class DeviceSim(InMemoryPeripheral pipe, EdhocSelfTest.DeviceSim.Slots slots)
    {
        /// <summary>Own credential plus per-slot pinned initiator CCS bytes.</summary>
        public sealed record Slots(EdhocCredential Device, byte[]? Low, byte[]? High);

        private readonly SegmentLink _segments = new(pipe, 4096, logger: NullLogger.Instance);

        public bool AllowEnrollment { get; init; }
        public bool TamperMessage2 { get; init; }
        public bool TruncateMessage2 { get; init; }
        public bool ReplayEcho { get; init; }
        public bool SawResync { get; private set; }

        public void RunOnce(int echoes)
        {
            var responder = Handshake(ReadEdhoc());
            if (responder is null)
            {
                return;
            }

            Transport(responder, echoes);
        }

        /// <summary>
        /// Reads a first Msg1, answers message_2, then treats the next frame —
        /// expected to be another Msg1 from a fresh link — as a resync,
        /// restarting the handshake exactly as the firmware's
        /// Msg1-in-any-phase rule does.
        /// </summary>
        public void RunResyncProbe()
        {
            var frame = ReadEdhoc();
            var first = NewResponder();
            first.ProcessMessage1(frame.AsSpan(1).ToArray());
            Write(FrameType.EdhocHandshake, Kinded(EdhocLink.MsgM2, first.CreateMessage2()));

            // The phone under test was started fresh, so instead of Msg3 for
            // this session it opens with a new Msg1: reset and run clean.
            var next = ReadEdhoc();
            if (next[0] == EdhocLink.MsgM1)
            {
                SawResync = true;
                var responder = Handshake(next);
                if (responder is not null)
                {
                    Transport(responder, echoes: 0);
                }
            }
        }

        private EdhocResponder? Handshake(byte[] frame)
        {
            if (frame.Length < 2 || frame[0] != EdhocLink.MsgM1)
            {
                WriteStatus(EdhocLink.StatusProtocolError);
                return null;
            }

            var responder = NewResponder();
            try
            {
                responder.ProcessMessage1(frame.AsSpan(1).ToArray());
            }
            catch (EdhocException)
            {
                WriteStatus(EdhocLink.StatusProtocolError);
                return null;
            }

            // The firmware's enrollment rule: the EAD 3737 flag makes both
            // ends carry credentials by value (when the device allows it).
            var enrolling = AllowEnrollment && EdhocEad.ContainsLabel(
                responder.ReceivedEad1, BlueConnectEdhoc.EnrollmentEadLabel);
            var message2 = responder.CreateMessage2(credentialByValue: enrolling);
            if (TamperMessage2)
            {
                message2[^1] ^= 0x01;
            }

            Write(FrameType.EdhocHandshake, Kinded(
                EdhocLink.MsgM2,
                TruncateMessage2 ? message2.AsSpan(0, message2.Length - 7).ToArray() : message2));

            var message3 = ReadEdhoc();
            if (message3.Length < 2 || message3[0] != EdhocLink.MsgM3)
            {
                WriteStatus(EdhocLink.StatusProtocolError);
                return null;
            }

            try
            {
                responder.ProcessMessage3(message3.AsSpan(1).ToArray());
            }
            catch (EdhocException ex)
            {
                WriteStatus(ex.Message.Contains("unknown credential kid")
                    ? EdhocLink.StatusNoCredential
                    : EdhocLink.StatusAuthFailed);
                return null;
            }

            WriteStatus(EdhocLink.StatusOk);
            return responder;
        }

        private void Transport(EdhocResponder responder, int echoes)
        {
            // Mirror of EdhocLink's key split.
            var transportKeys = responder.Export(BlueConnectEdhoc.TransportExporterLabel, [], 64);
            using var receive = new AesGcm(transportKeys.AsSpan(0, 32), 16);
            using var send = new AesGcm(transportKeys.AsSpan(32, 32), 16);
            ulong receiveCounter = 0;
            ulong sendCounter = 0;

            Span<byte> nonce = stackalloc byte[12];
            for (var i = 0; i < echoes; i++)
            {
                var frame = Read(FrameType.Encrypted);
                var plaintext = new byte[frame.Length - 16];
                nonce.Clear();
                BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], receiveCounter++);
                receive.Decrypt(nonce, frame.AsSpan(0, plaintext.Length), frame.AsSpan(plaintext.Length, 16), plaintext);

                var sealedBuffer = new byte[plaintext.Length + 16];
                nonce.Clear();
                BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], sendCounter++);
                send.Encrypt(nonce, plaintext, sealedBuffer.AsSpan(0, plaintext.Length), sealedBuffer.AsSpan(plaintext.Length, 16));
                Write(FrameType.Encrypted, sealedBuffer);

                if (ReplayEcho)
                {
                    Write(FrameType.Encrypted, sealedBuffer);
                }
            }
        }

        private EdhocResponder NewResponder() =>
            new(slots.Device, ResolveKid, allowUnknownInitiator: AllowEnrollment);

        private EdhocCredential? ResolveKid(ReadOnlyMemory<byte> kid)
        {
            var stored = kid.Span.SequenceEqual([BlueConnectEdhoc.LowKid]) ? slots.Low
                : kid.Span.SequenceEqual([BlueConnectEdhoc.HighKid]) ? slots.High
                : null;
            return stored is null ? null : EdhocCredential.FromCcs(stored);
        }

        private static byte[] Kinded(byte kind, byte[] body)
        {
            var framed = new byte[1 + body.Length];
            framed[0] = kind;
            body.CopyTo(framed, 1);
            return framed;
        }

        private byte[] ReadEdhoc() => Read(FrameType.EdhocHandshake);

        private byte[] Read(byte expectedType)
        {
            var bundled = _segments.Read(CancellationToken.None).GetAwaiter().GetResult();
            var (type, payload) = FrameType.Split(bundled);
            if (type != expectedType)
            {
                throw new InvalidOperationException($"Device simulator expected frame type {expectedType}, got {type}.");
            }

            return payload;
        }

        private void Write(byte type, ReadOnlySpan<byte> payload)
            => _segments.Write(FrameType.Bundle(type, payload), CancellationToken.None).GetAwaiter().GetResult();

        private void WriteStatus(byte status)
            => Write(FrameType.EdhocHandshake, [EdhocLink.MsgStatus, status]);
    }
}
