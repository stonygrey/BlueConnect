// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace StonyGrey.BlueConnect.Secure;

/// <summary>
/// Compact client endorsement: a CA-signed binding of a Noise XX
/// initiator's X25519 static public key. Not X.509. One-way — the
/// client presents this to the device; the device never presents a
/// cert. The blob is small enough to ride in Noise XX handshake message 3.
///
/// Layout (97 bytes):
///   version (1) || subject X25519 public key (32) || Ed25519 signature (64)
///
/// The signature is PURE_EDDSA over <c>version || subject</c>. Verify
/// requires the provisioned CA root and that <c>subject</c> equals the
/// initiator static just authenticated (<c>rs</c> after message 3).
/// An endorsement grants no privilege by itself; a slot is still earned
/// only through the button ceremony or USB provisioning.
/// </summary>
public static class ClientEndorsement
{
    public const byte Version = 1;

    public const int SubjectLength = 32;

    public const int SignatureLength = 64;

    public const int BlobLength = 1 + SubjectLength + SignatureLength;

    public const int CaKeyLength = 32;

    /// <summary>True when <paramref name="payload"/> is a well-formed v1 blob (layout only).</summary>
    public static bool IsCertBlob(ReadOnlySpan<byte> payload)
        => payload.Length == BlobLength && payload[0] == Version;

    public static (byte[] PrivateKey, byte[] PublicKey) GenerateCa()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        var privateKey = ((Ed25519PrivateKeyParameters)pair.Private).GetEncoded();
        var publicKey = ((Ed25519PublicKeyParameters)pair.Public).GetEncoded();
        return (privateKey, publicKey);
    }

    public static byte[] PublicKeyFromPrivate(ReadOnlySpan<byte> caPrivateKey)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(caPrivateKey.Length, CaKeyLength);
        var privateKey = new Ed25519PrivateKeyParameters(caPrivateKey.ToArray(), 0);
        return privateKey.GeneratePublicKey().GetEncoded();
    }

    /// <summary>Sign <paramref name="subjectPublicKey"/> (the app's existing Noise static) under the CA.</summary>
    public static byte[] Sign(ReadOnlySpan<byte> subjectPublicKey, ReadOnlySpan<byte> caPrivateKey)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(subjectPublicKey.Length, SubjectLength);
        ArgumentOutOfRangeException.ThrowIfNotEqual(caPrivateKey.Length, CaKeyLength);
        if (!subjectPublicKey.ContainsAnyExcept((byte)0))
        {
            throw new ArgumentException("Subject public key is all-zero.", nameof(subjectPublicKey));
        }

        var blob = new byte[BlobLength];
        blob[0] = Version;
        subjectPublicKey.CopyTo(blob.AsSpan(1, SubjectLength));

        var privateKey = new Ed25519PrivateKeyParameters(caPrivateKey.ToArray(), 0);
        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, privateKey);
        signer.BlockUpdate(blob, 0, 1 + SubjectLength);
        var signature = signer.GenerateSignature();
        if (signature.Length != SignatureLength)
        {
            throw new InvalidOperationException("Ed25519 signature was not 64 bytes.");
        }

        signature.CopyTo(blob.AsSpan(1 + SubjectLength));
        return blob;
    }

    /// <summary>
    /// Signature valid under <paramref name="caPublicKey"/> AND subject equals
    /// <paramref name="subjectPublicKey"/> (the Noise XX initiator static).
    /// </summary>
    public static bool TryVerify(ReadOnlySpan<byte> blob, ReadOnlySpan<byte> subjectPublicKey, ReadOnlySpan<byte> caPublicKey)
    {
        if (!IsCertBlob(blob) ||
            subjectPublicKey.Length != SubjectLength ||
            caPublicKey.Length != CaKeyLength ||
            !blob.Slice(1, SubjectLength).SequenceEqual(subjectPublicKey) ||
            !subjectPublicKey.ContainsAnyExcept((byte)0) ||
            !caPublicKey.ContainsAnyExcept((byte)0))
        {
            return false;
        }

        try
        {
            var publicKey = new Ed25519PublicKeyParameters(caPublicKey.ToArray(), 0);
            var verifier = new Ed25519Signer();
            verifier.Init(forSigning: false, publicKey);
            var tbs = blob.Slice(0, 1 + SubjectLength).ToArray();
            verifier.BlockUpdate(tbs, 0, tbs.Length);
            return verifier.VerifySignature(blob.Slice(1 + SubjectLength, SignatureLength).ToArray());
        }
        catch (Exception)
        {
            return false;
        }
    }
}
