// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

using StonyGrey.BlueConnect.Provision.Reference;
using StonyGrey.BlueConnect.Secure;

namespace StonyGrey.BlueConnect.Provision;

/// <summary>
/// Byte-level interop between Torann (the production Noise implementation)
/// and the frozen hand-rolled reference oracle, in both roles, using the
/// exact wire dialect the Verdigris firmware speaks: suite
/// Noise_XX_25519_AESGCM_SHA256, prologue "BlueConnectLink/1", 0x5A
/// handshake payloads, message sizes 32/97/65. The reference oracle was
/// verified on-air against the firmware before production moved to Torann,
/// so passing both directions here pins Torann to the firmware's dialect.
/// </summary>
internal static class InteropTest
{
    private const string Suite = "Noise_XX_25519_AESGCM_SHA256";

    public static int Run()
    {
        TorannInitiates();
        OursInitiates();
        Console.WriteLine("Interop PASSED (both roles, exact wire dialect).");
        return 0;
    }

    /// <summary>Torann as the app (initiator), our implementation as the device.</summary>
    private static void TorannInitiates()
    {
        var protocol = Torann.Protocol.Parse(Suite);
        using var torannStatic = protocol.GenerateKeyPair();
        var ours = LinkIdentity.Generate();

        using var torann = protocol.CreateHandshake(initiator: true, SecureLink.Prologue, s: torannStatic);
        using var mine = new HandshakeState(initiator: false, (byte[])ours.StaticPrivateKey.Clone(), SecureLink.Prologue);

        var buffer = new byte[Torann.Protocol.MaxMessageLength];

        // msg 1: Torann -> ours
        var (m1Len, _, _) = torann.WriteMessage(default, buffer);
        Check(m1Len == 32, "message 1 length (Torann write)");
        mine.ReadMessage1(buffer.AsSpan(0, m1Len));

        // msg 2: ours -> Torann
        var m2 = mine.WriteMessage2(SecureLink.HandshakePayload);
        Check(m2.Length == 97, "message 2 length (ours write)");
        var payload = new byte[Torann.Protocol.MaxMessageLength];
        var (p2Len, _, _) = torann.ReadMessage(m2, payload);
        Check(p2Len == 1 && payload[0] == 0x5A, "message 2 payload via Torann");

        // msg 3: Torann -> ours (returns the transport on the final message)
        var (m3Len, _, torannTransport) = torann.WriteMessage(SecureLink.HandshakePayload, buffer);
        Check(m3Len == 65, "message 3 length (Torann write)");
        Check(torannTransport is not null, "Torann transport after split");
        var p3 = mine.ReadMessage3(buffer.AsSpan(0, m3Len));
        Check(p3.Length == 1 && p3[0] == 0x5A, "message 3 payload via ours");

        // Identity agreement.
        Check(torann.RemoteStaticPublicKey!.SequenceEqual(ours.StaticPublicKey), "device key seen by Torann");
        Check(mine.RemoteStatic!.SequenceEqual(torannStatic.PublicKey), "app key seen by ours");

        // Transport both directions: Torann seals -> ours opens; ours seals -> Torann opens.
        var (ourSend, ourReceive) = mine.Split();
        using (ourSend)
        using (ourReceive)
        using (torannTransport)
        {
            var message = new byte[1536];
            RandomNumberGenerator.Fill(message);

            var sealedLen = torannTransport!.WriteMessage(message, buffer);
            var opened = ourReceive.DecryptWithAd([], buffer.AsSpan(0, sealedLen));
            Check(opened.SequenceEqual(message), "Torann->ours sealed frame");

            var sealedByOurs = ourSend.EncryptWithAd([], message);
            var openedLen = torannTransport.ReadMessage(sealedByOurs, payload);
            Check(payload.AsSpan(0, openedLen).SequenceEqual(message), "ours->Torann sealed frame");
        }
    }

    /// <summary>Our implementation as the app (initiator), Torann as the device.</summary>
    private static void OursInitiates()
    {
        var protocol = Torann.Protocol.Parse(Suite);
        using var torannStatic = protocol.GenerateKeyPair();
        var ours = LinkIdentity.Generate();

        using var mine = new HandshakeState(initiator: true, (byte[])ours.StaticPrivateKey.Clone(), SecureLink.Prologue);
        using var torann = protocol.CreateHandshake(initiator: false, SecureLink.Prologue, s: torannStatic);

        var buffer = new byte[Torann.Protocol.MaxMessageLength];
        var payload = new byte[Torann.Protocol.MaxMessageLength];

        // msg 1: ours -> Torann
        var m1 = mine.WriteMessage1();
        Check(m1.Length == 32, "message 1 length (ours write)");
        torann.ReadMessage(m1, payload);

        // msg 2: Torann -> ours
        var (m2Len, _, _) = torann.WriteMessage(SecureLink.HandshakePayload, buffer);
        Check(m2Len == 97, "message 2 length (Torann write)");
        var p2 = mine.ReadMessage2(buffer.AsSpan(0, m2Len));
        Check(p2.Length == 1 && p2[0] == 0x5A, "message 2 payload via ours");

        // msg 3: ours -> Torann
        var m3 = mine.WriteMessage3(SecureLink.HandshakePayload);
        Check(m3.Length == 65, "message 3 length (ours write)");
        var (p3Len, _, torannTransport) = torann.ReadMessage(m3, payload);
        Check(p3Len == 1 && payload[0] == 0x5A, "message 3 payload via Torann");
        Check(torannTransport is not null, "Torann transport after split");

        Check(mine.RemoteStatic!.SequenceEqual(torannStatic.PublicKey), "device key seen by ours");
        Check(torann.RemoteStaticPublicKey!.SequenceEqual(ours.StaticPublicKey), "app key seen by Torann");

        var (ourSend, ourReceive) = mine.Split();
        using (ourSend)
        using (ourReceive)
        using (torannTransport)
        {
            var message = new byte[1536];
            RandomNumberGenerator.Fill(message);

            var sealedByOurs = ourSend.EncryptWithAd([], message);
            var openedLen = torannTransport!.ReadMessage(sealedByOurs, payload);
            Check(payload.AsSpan(0, openedLen).SequenceEqual(message), "ours->Torann sealed frame");

            var sealedLen = torannTransport.WriteMessage(message, buffer);
            var opened = ourReceive.DecryptWithAd([], buffer.AsSpan(0, sealedLen));
            Check(opened.SequenceEqual(message), "Torann->ours sealed frame");
        }
    }

    private static void Check(bool condition, string what)
    {
        Console.WriteLine($"  {(condition ? "ok" : "FAIL")}: {what}");
        if (!condition)
        {
            throw new InvalidOperationException($"Interop failed: {what}");
        }
    }
}
