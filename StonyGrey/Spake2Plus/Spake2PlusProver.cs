// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

using Org.BouncyCastle.Math;

using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;

namespace StonyGrey.Spake2Plus;

/// <summary>
/// The prover (client) side of one SPAKE2+ P256-SHA256 handshake: the party
/// that knows the setup code (w0 and w1). Single-use: GenerateShare once,
/// then Finish once.
/// </summary>
public sealed class Spake2PlusProver
{
    private readonly BigInteger _w0;
    private readonly BigInteger _w1;
    private readonly byte[] _w0Bytes;
    private readonly byte[] _context;
    private readonly byte[] _idProver;
    private readonly byte[] _idVerifier;
    private readonly BigInteger _x;

    private byte[]? _shareP;
    private bool _finished;

    public Spake2PlusProver(byte[] w0, byte[] w1, byte[] context, byte[] idProver, byte[] idVerifier)
        : this(w0, w1, context, idProver, idVerifier,
               new BigInteger(1, Spake2PlusP256.RandomScalar()))
    {
    }

    /// <summary>Fixed-ephemeral constructor for RFC 9383 vector tests.</summary>
    internal Spake2PlusProver(
        byte[] w0, byte[] w1, byte[] context, byte[] idProver, byte[] idVerifier, BigInteger x)
    {
        _w0 = new BigInteger(1, w0);
        _w1 = new BigInteger(1, w1);
        _w0Bytes = Spake2PlusP256.EncodeScalar(_w0);
        _context = context;
        _idProver = idProver;
        _idVerifier = idVerifier;
        _x = x;
    }

    /// <summary>shareP = x·G + w0·M, 65 bytes. First message on the wire.</summary>
    public byte[] GenerateShare()
    {
        if (_shareP is not null)
        {
            throw new Spake2PlusException("GenerateShare may only be called once.");
        }

        var shareP = Spake2PlusP256.G.Multiply(_x).Add(Spake2PlusP256.M.Multiply(_w0));
        _shareP = Spake2PlusP256.Encode(shareP);
        return _shareP;
    }

    /// <summary>
    /// Consumes the verifier's share and confirmation. Throws
    /// <see cref="Spake2PlusException"/> if confirmV does not verify — the
    /// two sides do not hold the same code. Returns the prover confirmation
    /// to send and the shared secret.
    /// </summary>
    public (byte[] ConfirmP, byte[] SharedKey) Finish(ReadOnlySpan<byte> shareV, ReadOnlySpan<byte> confirmV)
    {
        if (_shareP is null || _finished)
        {
            throw new Spake2PlusException("Finish must follow GenerateShare and may only be called once.");
        }

        _finished = true;

        var y = Spake2PlusP256.DecodeShare(shareV);

        // T = shareV − w0·N; Z = x·T, V = w1·T (cofactor 1, RFC 9383 §3.3).
        ECPoint t = y.Subtract(Spake2PlusP256.NPoint.Multiply(_w0));
        var z = Spake2PlusP256.Encode(t.Multiply(_x));
        var v = Spake2PlusP256.Encode(t.Multiply(_w1));

        var shareVBytes = shareV.ToArray();
        var transcript = Spake2PlusP256.Transcript(
            _context, _idProver, _idVerifier, _shareP, shareVBytes, z, v, _w0Bytes);
        var keys = Spake2PlusP256.DeriveKeys(transcript, _shareP, shareVBytes);

        if (!CryptographicOperations.FixedTimeEquals(keys.ConfirmV, confirmV))
        {
            throw new Spake2PlusException("Verifier confirmation failed; the setup code did not match.");
        }

        return (keys.ConfirmP, keys.SharedKey);
    }
}
