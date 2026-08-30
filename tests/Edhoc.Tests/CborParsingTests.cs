// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Edhoc.Tests;

using V = Rfc9529Trace2Vectors;

/// <summary>
/// Malformed-input tests, largely drawn from the RFC 9529 §4 invalid traces:
/// every case must throw <see cref="EdhocException"/> — never crash, never
/// partially succeed.
/// </summary>
public class CborParsingTests
{
    private static EdhocResponder NewResponder()
    {
        var credI = EdhocCredential.FromCcs(V.CredI);
        return new EdhocResponder(V.ResponderCredentialWithKey(), credI, V.CR);
    }

    [Theory]
    // RFC 9529 §4.1.1: message_1 wrapped in a surplus CBOR array.
    [InlineData("8403025820741a13d7ba048fbb615e94386aa3b61bea5b3d8f65f32620b749bee8d278efa90e")]
    // §4.1.2: C_I as a surplus bstr (0x410e) instead of the int 0x0e.
    [InlineData("03025820741a13d7ba048fbb615e94386aa3b61bea5b3d8f65f32620b749bee8d278efa9410e")]
    // §4.1.3: SUITES_I = 2 as a surplus one-element array.
    [InlineData("0381025820741a13d7ba048fbb615e94386aa3b61bea5b3d8f65f32620b749bee8d278efa90e")]
    // §4.1.4: G_X as a text string.
    [InlineData("0302782020616972207370656564206f66206120756e6c6164656e207377616c6c6f77200e")]
    // §4.2.2: G_X x-coordinate ≥ p.
    [InlineData("03025820ffffffff00000001000000000000000000000000ffffffffffffffffffffffff0e")]
    // §4.2.3: G_X not on the P-256 curve.
    [InlineData("03025820a04e73601df544a70ba7ea1e57030f7d4b4eb7f673924e58d54ca77a5e7d4d4a0e")]
    // §4.2.6: G_X missing its leading zero octet (31 bytes).
    [InlineData("0302581fd9697725d23a688b12d1c7e0108a08c9f71a85a09c20814976ab21122248fc0e")]
    // §4.3.1: METHOD in non-shortest form (19 0003).
    [InlineData("190003025820741a13d7ba048fbb615e94386aa3b61bea5b3d8f65f32620b749bee8d278efa90e")]
    // §4.3.2: SUITES_I as an indefinite-length array.
    [InlineData("039f0602ff5820741a13d7ba048fbb615e94386aa3b61bea5b3d8f65f32620b749bee8d278efa90e")]
    public void InvalidMessage1IsRejected(string hex)
    {
        var responder = NewResponder();
        Assert.Throws<EdhocException>(() => responder.ProcessMessage1(Convert.FromHexString(hex)));
    }

    [Fact]
    public void TruncatedMessage1IsRejectedAtEveryLength()
    {
        for (var length = 0; length < V.Message1.Length; length++)
        {
            var responder = NewResponder();
            var truncated = V.Message1[..length];
            Assert.Throws<EdhocException>(() => responder.ProcessMessage1(truncated));
        }
    }

    [Fact]
    public void Message1WithTrailingGarbageIsRejected()
    {
        // A trailing non-EAD item (a bstr where an int label must be).
        var padded = new byte[V.Message1.Length + 2];
        V.Message1.CopyTo(padded, 0);
        padded[^2] = 0x41;
        padded[^1] = 0xaa;

        var responder = NewResponder();
        Assert.Throws<EdhocException>(() => responder.ProcessMessage1(padded));
    }

    [Theory]
    // RFC 9529 §4.1.5: message_2 with two sequence elements instead of one.
    [InlineData("5820419701d7f00a26c2dc587a36dd752549f33763c893422c8ea0f955a13a4ff5d54b9862a11de42a95d785386a")]
    // A text string where the single bstr must be.
    [InlineData("6161")]
    // An empty message.
    [InlineData("")]
    // Too short to hold G_Y plus any ciphertext.
    [InlineData("5820419701d7f00a26c2dc587a36dd752549f33763c893422c8ea0f955a13a4ff5d5")]
    public void InvalidMessage2IsRejected(string hex)
    {
        Assert.Throws<EdhocException>(
            () => EdhocMessageCodec.ParseMessage2(Convert.FromHexString(hex)));
    }

    [Theory]
    // RFC 9529 §4.1.6: ID_CRED_R as a surplus map in the plaintext.
    [InlineData("27a10442321048fa5efa2ebf920bf3")]
    // §4.1.7: ID_CRED_R kid as a surplus bstr (0x4132) instead of the int 0x32.
    [InlineData("27413248fa5efa2ebf920bf3")]
    // §4.2.5: MAC_2 shorter than the suite's 8 bytes.
    [InlineData("273244fa5efa2e")]
    // Missing MAC entirely.
    [InlineData("2732")]
    public void InvalidPlaintext2IsRejected(string hex)
    {
        Assert.Throws<EdhocException>(
            () => EdhocMessageCodec.ParsePlaintext2(Convert.FromHexString(hex)));
    }

    [Fact]
    public void IdentifierRoundTripsBothForms()
    {
        // Identity-encoded single bytes and true bstr identifiers.
        foreach (var raw in new[] { new byte[] { 0x00 }, [0x17], [0x20], [0x37] })
        {
            Assert.Equal(raw, EdhocCbor.EncodeIdentifier(raw));
            Assert.Equal(raw, EdhocCbor.ReadIdentifier(EdhocCbor.Reader(raw)));
        }

        byte[] wide = [0x18];                    // one byte, but not int-encodable
        Assert.Equal(new byte[] { 0x41, 0x18 }, EdhocCbor.EncodeIdentifier(wide));
        byte[] multi = [0xde, 0xad];
        var encoded = EdhocCbor.EncodeIdentifier(multi);
        Assert.Equal(multi, EdhocCbor.ReadIdentifier(EdhocCbor.Reader(encoded)));
    }
}
