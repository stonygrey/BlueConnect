// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

using Org.BouncyCastle.Math;

using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;

namespace StonyGrey.Spake2Plus;

/// <summary>
/// The verifier (device) side of one SPAKE2+ P256-SHA256 handshake: the
/// party that stores only w0 and L, never the code. In production this role
/// runs on the firmware via PSA; this implementation exists for the
/// loopback self-test and interop validation. Single-use: Respond once,
/// then VerifyConfirm once.
/// </summary>
public sealed class Spake2PlusVerifier
{
    private readonly BigInteger _w0;
    private readonly byte[] _w0Bytes;
    private readonly ECPoint _l;
    private readonly byte[] _context;
    private readonly byte[] _idProver;
    private readonly byte[] _idVerifier;
    private readonly BigInteger _y;

    private Spake2PlusKeys? _keys;
    private bool _confirmed;

    public Spake2PlusVerifier(byte[] w0, byte[] l, byte[] context, byte[] idProver, byte[] idVerifier)
        : this(w0, l, context, idProver, idVerifier,
               new BigInteger(1, Spake2PlusP256.RandomScalar()))
    {
    }

    /// <summary>Fixed-ephemeral constructor for RFC 9383 vector tests.</summary>
    internal Spake2PlusVerifier(
        byte[] w0, byte[] l, byte[] context, byte[] idProver, byte[] idVerifier, BigInteger y)
    {
        _w0 = new BigInteger(1, w0);
        _w0Bytes = Spake2PlusP256.EncodeScalar(_w0);
        _l = Spake2PlusP256.DecodeShare(l);
        _context = context;
        _idProver = idProver;
        _idVerifier = idVerifier;
        _y = y;
    }

    /// <summary>
    /// Consumes the prover's share; returns shareV = y·G + w0·N and
    /// confirmV. Throws <see cref="Spake2PlusException"/> on a malformed
    /// share. A confirmation mismatch is not detectable until
    /// <see cref="VerifyConfirm"/>.
    /// </summary>
    public (byte[] ShareV, byte[] ConfirmV) Respond(ReadOnlySpan<byte> shareP)
    {
        if (_keys is not null)
        {
            throw new Spake2PlusException("Respond may only be called once.");
        }

        var x = Spake2PlusP256.DecodeShare(shareP);

        var shareV = Spake2PlusP256.Encode(
            Spake2PlusP256.G.Multiply(_y).Add(Spake2PlusP256.NPoint.Multiply(_w0)));

        // T = shareP − w0·M; Z = y·T, V = y·L (cofactor 1, RFC 9383 §3.3).
        ECPoint t = x.Subtract(Spake2PlusP256.M.Multiply(_w0));
        var z = Spake2PlusP256.Encode(t.Multiply(_y));
        var v = Spake2PlusP256.Encode(_l.Multiply(_y));

        var sharePBytes = shareP.ToArray();
        var transcript = Spake2PlusP256.Transcript(
            _context, _idProver, _idVerifier, sharePBytes, shareV, z, v, _w0Bytes);
        _keys = Spake2PlusP256.DeriveKeys(transcript, sharePBytes, shareV);

        return (shareV, _keys.ConfirmV);
    }

    /// <summary>
    /// Checks the prover's confirmation; returns the shared secret or throws
    /// <see cref="Spake2PlusException"/> — the prover's code did not match.
    /// </summary>
    public byte[] VerifyConfirm(ReadOnlySpan<byte> confirmP)
    {
        if (_keys is null || _confirmed)
        {
            throw new Spake2PlusException("VerifyConfirm must follow Respond and may only be called once.");
        }

        _confirmed = true;

        if (!CryptographicOperations.FixedTimeEquals(_keys.ConfirmP, confirmP))
        {
            throw new Spake2PlusException("Prover confirmation failed; the setup code did not match.");
        }

        return _keys.SharedKey;
    }
}
