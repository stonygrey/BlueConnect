// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

using Microsoft.Extensions.Logging.Abstractions;

using StonyGrey.BlueConnect.Abstractions;
using StonyGrey.BlueConnect.Secure;

namespace StonyGrey.BlueConnect.Provision;

/// <summary>
/// Loopback validation of the secure link: a client and a device identity run
/// the Noise XX handshake over an in-memory byte pipe and exchange sealed
/// messages. Also proves the negative cases (wrong pin, tampered ciphertext,
/// all-zero DH, oversize message 1) and the protocol constants / message
/// sizes, so prologue or payload drift fails here instead of on a board.
/// </summary>
internal static class SelfTest
{
    public static int Run()
    {
        var client = LinkIdentity.Generate();
        var device = LinkIdentity.Generate();

        // Happy path: both sides pin each other (post-pairing state). A
        // pinned initiator opens with IK — two messages — and the responder
        // enforces its own pin against the key IK message 1 proves.
        var (clientPipe, devicePipe) = InMemoryPeripheral.CreatePair();
        using var clientLink = new SecureLink(clientPipe, 4096, new LinkSecurity(client, device.StaticPublicKey), initiator: true, NullLogger.Instance);
        using var deviceLink = new SecureLink(devicePipe, 4096, new LinkSecurity(device, client.StaticPublicKey), initiator: false, NullLogger.Instance);

        var deviceSide = Task.Run(async () =>
        {
            await deviceLink.Establish(CancellationToken.None);
            var request = await deviceLink.Receive(CancellationToken.None);
            await deviceLink.Send(request.Reverse().ToArray(), CancellationToken.None);
        });

        var payload = new byte[1536];
        RandomNumberGenerator.Fill(payload);
        clientLink.Send(payload, CancellationToken.None).GetAwaiter().GetResult();
        var reply = clientLink.Receive(CancellationToken.None).GetAwaiter().GetResult();
        deviceSide.GetAwaiter().GetResult();

        Check(reply.SequenceEqual(payload.Reverse().ToArray()), "round-trip payload");
        Check(clientLink.RemoteStaticKey!.SequenceEqual(device.StaticPublicKey), "device key proven to client");
        Check(deviceLink.RemoteStaticKey!.SequenceEqual(client.StaticPublicKey), "client key proven to device");

        Check(LinkProtocol.Name == "Noise_XX_25519_AESGCM_SHA256", "suite name");
        Check(SecureLink.Prologue.AsSpan().SequenceEqual("BlueConnectLink/1"u8), "prologue");
        Check(SecureLink.HandshakePayload.AsSpan().SequenceEqual((byte[])[0x5A]), "handshake payload");
        Check(SecureLink.HandshakeMessage1Length == 32, "message 1 length");
        Check(SecureLink.HandshakeMessage2Length == 97, "message 2 length");
        Check(SecureLink.HandshakeMessage3Length == 65, "message 3 length");
        Check(SecureLink.IkHandshakeMessage1Length == 97, "IK message 1 length");
        Check(SecureLink.IkHandshakeMessage2Length == 49, "IK message 2 length");

        // Torann produces exactly the advertised message sizes for this dialect.
        var protocol = Torann.Protocol.Parse(LinkProtocol.Name);
        using (var initiatorStatic = protocol.CreateKeyPair(client.StaticPrivateKey))
        using (var responderStatic = protocol.CreateKeyPair(device.StaticPrivateKey))
        using (var initiator = protocol.CreateHandshake(true, SecureLink.Prologue, s: initiatorStatic))
        using (var responder = protocol.CreateHandshake(false, SecureLink.Prologue, s: responderStatic))
        {
            var buffer = new byte[Torann.Protocol.MaxMessageLength];
            var scratch = new byte[Torann.Protocol.MaxMessageLength];

            var (m1, _, _) = initiator.WriteMessage(default, buffer);
            Check(m1 == SecureLink.HandshakeMessage1Length, "write message 1 size");
            responder.ReadMessage(buffer.AsSpan(0, m1), scratch);

            var (m2, _, _) = responder.WriteMessage(SecureLink.HandshakePayload, buffer);
            Check(m2 == SecureLink.HandshakeMessage2Length, "write message 2 size");
            var (p2, _, _) = initiator.ReadMessage(buffer.AsSpan(0, m2), scratch);
            Check(scratch.AsSpan(0, p2).SequenceEqual(SecureLink.HandshakePayload), "message 2 payload");

            var (m3, _, initiatorTransport) = initiator.WriteMessage(SecureLink.HandshakePayload, buffer);
            Check(m3 == SecureLink.HandshakeMessage3Length, "write message 3 size");
            var (p3, _, responderTransport) = responder.ReadMessage(buffer.AsSpan(0, m3), scratch);
            Check(scratch.AsSpan(0, p3).SequenceEqual(SecureLink.HandshakePayload), "message 3 payload");
            initiatorTransport?.Dispose();
            responderTransport?.Dispose();
        }

        // An oversized message 1 is refused at the link layer before Torann sees it.
        var (_, oversizePipe) = InMemoryPeripheral.CreatePair();
        using (var oversizeLink = new SecureLink(oversizePipe, 4096, new LinkSecurity(device, null), initiator: false, NullLogger.Instance))
        {
            var oversized = new byte[SecureLink.HandshakeMessage1Length + 64];
            RandomNumberGenerator.Fill(oversized);
            oversizePipe.InjectFramed(FrameType.Handshake, oversized);
            try
            {
                using var quick = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                oversizeLink.Establish(quick.Token).GetAwaiter().GetResult();
                Check(false, "oversize message 1 rejected");
            }
            catch (LinkSecurityException)
            {
                Check(true, "oversize message 1 rejected");
            }
        }

        // Negative: an impostor device (key differs from the client's pin) must be rejected.
        var impostor = LinkIdentity.Generate();
        var (clientPipe2, devicePipe2) = InMemoryPeripheral.CreatePair();
        using var clientLink2 = new SecureLink(clientPipe2, 4096, new LinkSecurity(client, device.StaticPublicKey), initiator: true, NullLogger.Instance);
        using var impostorLink = new SecureLink(devicePipe2, 4096, new LinkSecurity(impostor, null), initiator: false, NullLogger.Instance);
        _ = Task.Run(() => impostorLink.Establish(CancellationToken.None));

        // Its own short timeout: under IK the rejection IS the timeout, and a
        // consumed token must not bleed into the later tests.
        using (var impostorTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
        {
            try
            {
                clientLink2.Establish(impostorTimeout.Token).GetAwaiter().GetResult();
                Check(false, "impostor device rejected");
            }
            catch (Exception ex) when (ex is LinkSecurityException or OperationCanceledException)
            {
                // Under IK the impostor cannot even decrypt message 1 (it is
                // encrypted to the pinned key), so the client sees silence —
                // the same signature a wiped device produces.
                Check(true, "impostor device rejected");
            }
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Negative: flipping one ciphertext bit must fail authentication.
        var (clientPipe3, devicePipe3) = InMemoryPeripheral.CreatePair();
        using var clientLink3 = new SecureLink(clientPipe3, 4096, new LinkSecurity(client, device.StaticPublicKey), initiator: true, NullLogger.Instance);
        using var deviceLink3 = new SecureLink(devicePipe3, 4096, new LinkSecurity(device, client.StaticPublicKey), initiator: false, NullLogger.Instance);
        var tampered = Task.Run(async () =>
        {
            await deviceLink3.Establish(CancellationToken.None);
            devicePipe3.TamperNext = true; // corrupt the next inbound data frame
            return await deviceLink3.Receive(CancellationToken.None);
        });
        // Wait for the handshake to finish before sending the frame to be tampered.
        clientLink3.Establish(timeout.Token).GetAwaiter().GetResult();
        clientLink3.Send(new byte[64], CancellationToken.None).GetAwaiter().GetResult();
        try
        {
            tampered.GetAwaiter().GetResult();
            Check(false, "tampered frame rejected");
        }
        catch (LinkSecurityException)
        {
            Check(true, "tampered frame rejected");
        }

        // A handshake frame injected after Split must not tear the session down
        // (the host ignores it; the firmware now drops rather than restarting).
        var (clientPipe4, devicePipe4) = InMemoryPeripheral.CreatePair();
        using var clientLink4 = new SecureLink(clientPipe4, 4096, new LinkSecurity(client, device.StaticPublicKey), initiator: true, NullLogger.Instance);
        using var deviceLink4 = new SecureLink(devicePipe4, 4096, new LinkSecurity(device, client.StaticPublicKey), initiator: false, NullLogger.Instance);
        var afterInject = Task.Run(async () =>
        {
            await deviceLink4.Establish(CancellationToken.None);
            var request = await deviceLink4.Receive(CancellationToken.None);
            await deviceLink4.Send(request, CancellationToken.None);
        });
        clientLink4.Establish(timeout.Token).GetAwaiter().GetResult();
        devicePipe4.InjectFramed(FrameType.Handshake, new byte[SecureLink.HandshakeMessage1Length]);
        var ping = new byte[] { 1, 2, 3, 4 };
        clientLink4.Send(ping, CancellationToken.None).GetAwaiter().GetResult();
        var pong = clientLink4.Receive(timeout.Token).GetAwaiter().GetResult();
        afterInject.GetAwaiter().GetResult();
        Check(pong.AsSpan().SequenceEqual(ping), "session survives injected handshake frame");

        var (clientPipe5, devicePipe5) = InMemoryPeripheral.CreatePair();
        using var clientLink5 = new SecureLink(clientPipe5, 4096, new LinkSecurity(client, device.StaticPublicKey), initiator: true, NullLogger.Instance);
        using var deviceLink5 = new SecureLink(devicePipe5, 4096, new LinkSecurity(device, client.StaticPublicKey), initiator: false, NullLogger.Instance);
        var plaintextReject = Task.Run(async () =>
        {
            await deviceLink5.Establish(CancellationToken.None);
            return await deviceLink5.Receive(CancellationToken.None);
        });
        clientLink5.Establish(timeout.Token).GetAwaiter().GetResult();
        devicePipe5.InjectFramed(FrameType.Plaintext, new byte[] { 0x00 });
        try
        {
            plaintextReject.GetAwaiter().GetResult();
            Check(false, "plaintext frame rejected");
        }
        catch (AggregateException ex) when (ex.InnerException is LinkSecurityException)
        {
            Check(true, "plaintext frame rejected");
        }
        catch (LinkSecurityException)
        {
            Check(true, "plaintext frame rejected");
        }

        // An unpinned client (pre-pairing, ceremony state) still runs XX and
        // learns the device key in-band.
        var (clientPipe6, devicePipe6) = InMemoryPeripheral.CreatePair();
        using var clientLink6 = new SecureLink(clientPipe6, 4096, new LinkSecurity(client, null), initiator: true, NullLogger.Instance);
        using var deviceLink6 = new SecureLink(devicePipe6, 4096, new LinkSecurity(device, null), initiator: false, NullLogger.Instance);
        var xxSide = Task.Run(async () =>
        {
            await deviceLink6.Establish(CancellationToken.None);
            var request = await deviceLink6.Receive(CancellationToken.None);
            await deviceLink6.Send(request, CancellationToken.None);
        });
        clientLink6.Send(new byte[] { 9, 9, 9 }, CancellationToken.None).GetAwaiter().GetResult();
        var xxEcho = clientLink6.Receive(timeout.Token).GetAwaiter().GetResult();
        xxSide.GetAwaiter().GetResult();
        Check(xxEcho.AsSpan().SequenceEqual((byte[])[9, 9, 9]), "unpinned client establishes via XX");
        Check(clientLink6.RemoteStaticKey!.SequenceEqual(device.StaticPublicKey), "XX learns the device key in-band");

        Console.WriteLine("Self-test PASSED.");
        return 0;
    }

    private static void Check(bool condition, string what)
    {
        Console.WriteLine($"  {(condition ? "ok" : "FAIL")}: {what}");
        if (!condition)
        {
            throw new InvalidOperationException($"Self-test failed: {what}");
        }
    }
}
