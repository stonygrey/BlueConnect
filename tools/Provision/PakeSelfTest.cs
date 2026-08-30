// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Security.Cryptography;

using Microsoft.Extensions.Logging.Abstractions;

using StonyGrey.BlueConnect.Core;
using StonyGrey.BlueConnect.Secure;

using StonyGrey.Spake2Plus;

namespace StonyGrey.BlueConnect.Provision;

/// <summary>
/// Loopback validation of the PAKE link: a real <see cref="PakeLink"/> prover
/// runs against an in-process device simulator speaking the exact 0x03 wire
/// dialect the firmware implements (Start/Respond/Confirm/Status, then
/// AES-256-GCM transport). Proves the happy path at both tiers and the
/// negative cases: wrong code, unprovisioned tier, tampered confirmation,
/// tampered and replayed transport frames, truncated messages.
/// </summary>
internal static class PakeSelfTest
{
    private const string Code = "AB3D-EF6H-JK8M-NP2R";
    private const string WrongCode = "2222-3333-4444-5555";

    public static int Run()
    {
        Check(PakeLink.StartLength == 68, "Start message length");
        Check(PakeLink.RespondLength == 98, "Respond message length");
        Check(PakeLink.ConfirmLength == 33, "Confirm message length");
        Check(PakeLink.StatusLength == 2, "Status message length");

        foreach (var tier in (PakeTier[])[PakeTier.Low, PakeTier.High])
        {
            RoundTrip(tier);
        }

        WrongCodeIsRejected();
        UnprovisionedTierIsRefused();
        TamperedConfirmVIsRejected();
        TamperedTransportFrameIsRejected();
        ReplayedTransportFrameIsRejected();
        TruncatedRespondIsRejected();

        Console.WriteLine("PAKE self-test PASSED.");
        return 0;
    }

    private static void RoundTrip(PakeTier tier)
    {
        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new PakeLink(phonePipe, 4096, PakeSecurity.FromCode(Code, tier), NullLogger.Instance);
        var device = new DeviceSim(devicePipe, Code, tier);
        var deviceSide = Task.Run(() => device.RunOnce(echoes: 1));

        var payload = new byte[1536];
        RandomNumberGenerator.Fill(payload);
        link.Send(payload, Cancel().Token).GetAwaiter().GetResult();
        var reply = link.Receive(Cancel().Token).GetAwaiter().GetResult();
        deviceSide.GetAwaiter().GetResult();

        Check(reply.AsSpan().SequenceEqual(payload), $"sealed round-trip at {tier} tier");
    }

    private static void WrongCodeIsRejected()
    {
        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new PakeLink(phonePipe, 4096, PakeSecurity.FromCode(WrongCode, PakeTier.Low), NullLogger.Instance);
        var device = new DeviceSim(devicePipe, Code, PakeTier.Low);
        _ = Task.Run(() => device.RunOnce(echoes: 0));

        // The prover detects the mismatch in confirmV before sending anything
        // an attacker could use.
        Throws(() => link.Establish(Cancel().Token).GetAwaiter().GetResult(), "wrong code rejected by prover");
    }

    private static void UnprovisionedTierIsRefused()
    {
        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new PakeLink(phonePipe, 4096, PakeSecurity.FromCode(Code, PakeTier.High), NullLogger.Instance);
        var device = new DeviceSim(devicePipe, Code, PakeTier.Low); // only low is provisioned
        _ = Task.Run(() => device.RunOnce(echoes: 0));

        Throws(() => link.Establish(Cancel().Token).GetAwaiter().GetResult(), "unprovisioned tier refused");
    }

    private static void TamperedConfirmVIsRejected()
    {
        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new PakeLink(phonePipe, 4096, PakeSecurity.FromCode(Code, PakeTier.Low), NullLogger.Instance);
        var device = new DeviceSim(devicePipe, Code, PakeTier.Low) { TamperConfirmV = true };
        _ = Task.Run(() => device.RunOnce(echoes: 0));

        Throws(() => link.Establish(Cancel().Token).GetAwaiter().GetResult(), "tampered confirmV rejected");
    }

    private static void TamperedTransportFrameIsRejected()
    {
        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new PakeLink(phonePipe, 4096, PakeSecurity.FromCode(Code, PakeTier.Low), NullLogger.Instance);
        var device = new DeviceSim(devicePipe, Code, PakeTier.Low);
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
        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new PakeLink(phonePipe, 4096, PakeSecurity.FromCode(Code, PakeTier.Low), NullLogger.Instance);
        var device = new DeviceSim(devicePipe, Code, PakeTier.Low) { ReplayEcho = true };
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

    private static void TruncatedRespondIsRejected()
    {
        var (phonePipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var link = new PakeLink(phonePipe, 4096, PakeSecurity.FromCode(Code, PakeTier.Low), NullLogger.Instance);
        var device = new DeviceSim(devicePipe, Code, PakeTier.Low) { TruncateRespond = true };
        _ = Task.Run(() => device.RunOnce(echoes: 0));

        Throws(() => link.Establish(Cancel().Token).GetAwaiter().GetResult(), "truncated Respond rejected");
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
            throw new InvalidOperationException($"PAKE self-test failed: {what}");
        }
    }

    /// <summary>
    /// The device side of the 0x03 dialect, as the firmware implements it:
    /// verifier-only storage, explicit Status verdicts, then the sealed
    /// transport with the Noise nonce discipline. Fault injection knobs
    /// stand in for the negative paths.
    /// </summary>
    private sealed class DeviceSim(InMemoryPeripheral pipe, string code, PakeTier provisionedTier)
    {
        private readonly SegmentLink _segments = new(pipe, 4096, logger: NullLogger.Instance);
        private readonly Spake2PlusRegistration _registration = BlueConnectPake.Register(code, provisionedTier);

        public bool TamperConfirmV { get; init; }
        public bool TruncateRespond { get; init; }
        public bool ReplayEcho { get; init; }

        public void RunOnce(int echoes)
        {
            // ---- Handshake ----
            var start = ReadPake();
            if (start.Length != PakeLink.StartLength || start[0] != PakeLink.MsgStart || start[1] != PakeLink.Version)
            {
                WriteStatus(PakeLink.StatusProtocolError);
                return;
            }

            var requestedTier = start[2] == PakeLink.TierHigh ? PakeTier.High : PakeTier.Low;
            if (requestedTier != provisionedTier)
            {
                WriteStatus(PakeLink.StatusNoVerifier);
                return;
            }

            var verifier = new Spake2PlusVerifier(
                _registration.W0, _registration.L,
                BlueConnectPake.ContextBytes, BlueConnectPake.EmptyIdentity, BlueConnectPake.EmptyIdentity);
            var (shareV, confirmV) = verifier.Respond(start.AsSpan(3));

            var respond = new byte[PakeLink.RespondLength];
            respond[0] = PakeLink.MsgRespond;
            shareV.CopyTo(respond, 1);
            confirmV.CopyTo(respond, 1 + shareV.Length);
            if (TamperConfirmV)
            {
                respond[^1] ^= 0x01;
            }

            Write(FrameType.PakeHandshake, TruncateRespond ? respond.AsSpan(0, respond.Length - 7) : respond);

            var confirm = ReadPake();
            if (confirm.Length != PakeLink.ConfirmLength || confirm[0] != PakeLink.MsgConfirm)
            {
                WriteStatus(PakeLink.StatusProtocolError);
                return;
            }

            byte[] sharedKey;
            try
            {
                sharedKey = verifier.VerifyConfirm(confirm.AsSpan(1));
            }
            catch (Spake2PlusException)
            {
                WriteStatus(PakeLink.StatusAuthFailed);
                return;
            }

            WriteStatus(PakeLink.StatusOk);

            // ---- Transport (mirror of PakeLink's key split) ----
            var transportKeys = HKDF.DeriveKey(
                HashAlgorithmName.SHA256, sharedKey, 64, salt: null, info: PakeLink.TransportInfo);
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

        private byte[] ReadPake() => Read(FrameType.PakeHandshake);

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
            => Write(FrameType.PakeHandshake, [PakeLink.MsgStatus, status]);
    }
}
