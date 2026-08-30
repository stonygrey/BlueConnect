// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;

using Org.BouncyCastle.Math.EC.Rfc7748;

using StonyGrey.BlueConnect.Secure;

namespace StonyGrey.BlueConnect.Provision.Reference;

// FROZEN REFERENCE ORACLE — do not modify. This is the hand-rolled Noise
// implementation that was verified on-air against the Verdigris firmware
// before production moved to Torann. `lib/device interop` runs Torann against
// it, both roles, so any dialect drift fails here instead of on a board.

/// <summary>
/// Minimal implementation of the Noise Protocol Framework (revision 34) for the
/// single suite Noise_XX_25519_AESGCM_SHA256, structured after the spec's
/// CipherState/SymmetricState/HandshakeState objects. XX gives mutual
/// authentication (both static keys are exchanged and proven inside the
/// handshake) and forward secrecy (fresh ephemerals per session).
/// </summary>
public static class Noise
{
    public const string ProtocolName = "Noise_XX_25519_AESGCM_SHA256";
    public const int DhLen = 32;
    public const int TagLen = 16;
    public const int HashLen = 32;

    /// <summary>HKDF as specified by Noise §4.3 (HMAC-SHA256 extract-and-expand, two outputs).</summary>
    public static (byte[] Output1, byte[] Output2) Hkdf(byte[] chainingKey, byte[] inputKeyMaterial)
    {
        var tempKey = HMACSHA256.HashData(chainingKey, inputKeyMaterial);
        var output1 = HMACSHA256.HashData(tempKey, (byte[])[0x01]);
        var output2 = HMACSHA256.HashData(tempKey, (byte[])[.. output1, 0x02]);
        return (output1, output2);
    }

    public static (byte[] PrivateKey, byte[] PublicKey) GenerateKeyPair()
    {
        var privateKey = new byte[DhLen];
        RandomNumberGenerator.Fill(privateKey);
        var publicKey = new byte[DhLen];
        X25519.GeneratePublicKey(privateKey, 0, publicKey, 0);
        return (privateKey, publicKey);
    }

    public static byte[] PublicKeyOf(byte[] privateKey)
    {
        var publicKey = new byte[DhLen];
        X25519.GeneratePublicKey(privateKey, 0, publicKey, 0);
        return publicKey;
    }

    public static byte[] Dh(byte[] privateKey, byte[] peerPublicKey)
    {
        var shared = new byte[DhLen];
        if (!X25519.CalculateAgreement(privateKey, 0, peerPublicKey, 0, shared, 0))
        {
            throw new LinkSecurityException("X25519 agreement produced an all-zero shared secret (invalid peer public key).");
        }

        return shared;
    }
}

/// <summary>Noise §5.1: an AEAD key plus a strictly increasing nonce.</summary>
public sealed class CipherState : IDisposable
{
    private AesGcm? _aead;
    private ulong _nonce;

    public bool HasKey => _aead != null;

    public void InitializeKey(byte[] key)
    {
        _aead?.Dispose();
        _aead = new AesGcm(key, Noise.TagLen);
        _nonce = 0;
        CryptographicOperations.ZeroMemory(key);
    }

    public byte[] EncryptWithAd(ReadOnlySpan<byte> ad, ReadOnlySpan<byte> plaintext)
    {
        if (_aead is null)
        {
            return plaintext.ToArray();
        }

        var output = new byte[plaintext.Length + Noise.TagLen];
        _aead.Encrypt(NextNonce(), plaintext, output.AsSpan(0, plaintext.Length), output.AsSpan(plaintext.Length), ad);
        return output;
    }

    public byte[] DecryptWithAd(ReadOnlySpan<byte> ad, ReadOnlySpan<byte> ciphertext)
    {
        if (_aead is null)
        {
            return ciphertext.ToArray();
        }

        if (ciphertext.Length < Noise.TagLen)
        {
            throw new LinkSecurityException("Ciphertext shorter than the authentication tag.");
        }

        var output = new byte[ciphertext.Length - Noise.TagLen];
        try
        {
            _aead.Decrypt(NextNonce(), ciphertext[..output.Length], ciphertext[output.Length..], output, ad);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new LinkSecurityException("AEAD authentication failed; the frame was forged, corrupted, or replayed.", ex);
        }

        return output;
    }

    public void Dispose()
    {
        _aead?.Dispose();
        _aead = null;
    }

    /// <summary>Noise §5.1 AESGCM nonce: 4 zero bytes then the 64-bit counter, big-endian.</summary>
    private byte[] NextNonce()
    {
        if (_nonce == ulong.MaxValue)
        {
            throw new LinkSecurityException("Session nonce exhausted; reconnect to rekey.");
        }

        var nonce = new byte[12];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), _nonce);
        _nonce++;
        return nonce;
    }
}

/// <summary>Noise §5.2: chaining key + handshake hash over a CipherState.</summary>
public sealed class SymmetricState : IDisposable
{
    private byte[] _chainingKey;
    private byte[] _hash;

    public CipherState Cipher { get; } = new();

    public SymmetricState(string protocolName)
    {
        var name = System.Text.Encoding.ASCII.GetBytes(protocolName);
        if (name.Length <= Noise.HashLen)
        {
            _hash = new byte[Noise.HashLen];
            name.CopyTo(_hash, 0);
        }
        else
        {
            _hash = SHA256.HashData(name);
        }

        _chainingKey = (byte[])_hash.Clone();
    }

    public byte[] HandshakeHash => _hash;

    public void MixKey(byte[] inputKeyMaterial)
    {
        var (chainingKey, tempKey) = Noise.Hkdf(_chainingKey, inputKeyMaterial);
        CryptographicOperations.ZeroMemory(_chainingKey);
        CryptographicOperations.ZeroMemory(inputKeyMaterial);
        _chainingKey = chainingKey;
        Cipher.InitializeKey(tempKey);
    }

    public void MixHash(ReadOnlySpan<byte> data)
    {
        _hash = SHA256.HashData([.. _hash, .. data]);
    }

    public byte[] EncryptAndHash(ReadOnlySpan<byte> plaintext)
    {
        var ciphertext = Cipher.EncryptWithAd(_hash, plaintext);
        MixHash(ciphertext);
        return ciphertext;
    }

    public byte[] DecryptAndHash(ReadOnlySpan<byte> ciphertext)
    {
        var plaintext = Cipher.DecryptWithAd(_hash, ciphertext);
        MixHash(ciphertext);
        return plaintext;
    }

    /// <summary>Noise §5.2 Split: derive the two transport cipher states.</summary>
    public (CipherState First, CipherState Second) Split()
    {
        var (key1, key2) = Noise.Hkdf(_chainingKey, []);
        var first = new CipherState();
        first.InitializeKey(key1);
        var second = new CipherState();
        second.InitializeKey(key2);
        return (first, second);
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_chainingKey);
        Cipher.Dispose();
    }
}

/// <summary>
/// Noise §5.3 HandshakeState for the XX pattern only:
///   -&gt; e
///   &lt;- e, ee, s, es
///   -&gt; s, se
/// Messages 2 and 3 carry a one-byte payload (see <see cref="SecureLink.HandshakePayload"/>).
/// </summary>
public sealed class HandshakeState : IDisposable
{
    private readonly SymmetricState _symmetric;
    private readonly bool _initiator;
    private readonly byte[] _staticPrivate;
    private byte[]? _ephemeralPrivate;
    private byte[]? _remoteEphemeral;

    public byte[] StaticPublic { get; }

    /// <summary>The peer's static public key, available after it has been received (message 2 for the initiator, message 3 for the responder).</summary>
    public byte[]? RemoteStatic { get; private set; }

    public HandshakeState(bool initiator, byte[] staticPrivateKey, ReadOnlySpan<byte> prologue)
    {
        ArgumentNullException.ThrowIfNull(staticPrivateKey);

        _initiator = initiator;
        _staticPrivate = (byte[])staticPrivateKey.Clone();
        StaticPublic = Noise.PublicKeyOf(_staticPrivate);
        _symmetric = new SymmetricState(Noise.ProtocolName);
        _symmetric.MixHash(prologue);
    }

    /// <summary>Initiator: -&gt; e</summary>
    public byte[] WriteMessage1()
    {
        (_ephemeralPrivate, var ephemeralPublic) = Noise.GenerateKeyPair();
        _symmetric.MixHash(ephemeralPublic);
        var payload = _symmetric.EncryptAndHash([]);
        return [.. ephemeralPublic, .. payload];
    }

    /// <summary>Responder: -&gt; e</summary>
    public void ReadMessage1(ReadOnlySpan<byte> message)
    {
        if (message.Length != Noise.DhLen)
        {
            throw new LinkSecurityException("Handshake message 1 has an unexpected length.");
        }

        _remoteEphemeral = message[..Noise.DhLen].ToArray();
        _symmetric.MixHash(_remoteEphemeral);
        _symmetric.DecryptAndHash([]);
    }

    /// <summary>Responder: &lt;- e, ee, s, es</summary>
    public byte[] WriteMessage2(ReadOnlySpan<byte> payload)
    {
        (_ephemeralPrivate, var ephemeralPublic) = Noise.GenerateKeyPair();
        _symmetric.MixHash(ephemeralPublic);
        _symmetric.MixKey(Noise.Dh(_ephemeralPrivate, _remoteEphemeral!));
        var encryptedStatic = _symmetric.EncryptAndHash(StaticPublic);
        _symmetric.MixKey(Noise.Dh(_staticPrivate, _remoteEphemeral!));
        var encryptedPayload = _symmetric.EncryptAndHash(payload);
        return [.. ephemeralPublic, .. encryptedStatic, .. encryptedPayload];
    }

    /// <summary>Initiator: &lt;- e, ee, s, es. Returns the responder's payload (its certificate).</summary>
    public byte[] ReadMessage2(ReadOnlySpan<byte> message)
    {
        if (message.Length < Noise.DhLen + Noise.DhLen + Noise.TagLen + Noise.TagLen)
        {
            throw new LinkSecurityException("Handshake message 2 is truncated.");
        }

        _remoteEphemeral = message[..Noise.DhLen].ToArray();
        _symmetric.MixHash(_remoteEphemeral);
        _symmetric.MixKey(Noise.Dh(_ephemeralPrivate!, _remoteEphemeral));
        RemoteStatic = _symmetric.DecryptAndHash(message.Slice(Noise.DhLen, Noise.DhLen + Noise.TagLen));
        _symmetric.MixKey(Noise.Dh(_ephemeralPrivate!, RemoteStatic));
        return _symmetric.DecryptAndHash(message[(Noise.DhLen + Noise.DhLen + Noise.TagLen)..]);
    }

    /// <summary>Initiator: -&gt; s, se</summary>
    public byte[] WriteMessage3(ReadOnlySpan<byte> payload)
    {
        var encryptedStatic = _symmetric.EncryptAndHash(StaticPublic);
        _symmetric.MixKey(Noise.Dh(_staticPrivate, _remoteEphemeral!));
        var encryptedPayload = _symmetric.EncryptAndHash(payload);
        return [.. encryptedStatic, .. encryptedPayload];
    }

    /// <summary>Responder: -&gt; s, se. Returns the initiator's payload (its certificate).</summary>
    public byte[] ReadMessage3(ReadOnlySpan<byte> message)
    {
        if (message.Length < Noise.DhLen + Noise.TagLen + Noise.TagLen)
        {
            throw new LinkSecurityException("Handshake message 3 is truncated.");
        }

        RemoteStatic = _symmetric.DecryptAndHash(message[..(Noise.DhLen + Noise.TagLen)]);
        _symmetric.MixKey(Noise.Dh(_ephemeralPrivate!, RemoteStatic));
        return _symmetric.DecryptAndHash(message[(Noise.DhLen + Noise.TagLen)..]);
    }

    /// <summary>Transport keys; (send, receive) from this party's perspective.</summary>
    public (CipherState Send, CipherState Receive) Split()
    {
        var (first, second) = _symmetric.Split();
        return _initiator ? (first, second) : (second, first);
    }

    public void Dispose()
    {
        if (_ephemeralPrivate != null)
        {
            CryptographicOperations.ZeroMemory(_ephemeralPrivate);
        }

        CryptographicOperations.ZeroMemory(_staticPrivate);
        _symmetric.Dispose();
    }
}
