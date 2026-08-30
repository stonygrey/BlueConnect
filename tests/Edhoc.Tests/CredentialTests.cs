// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.Edhoc.Tests;

using V = Rfc9529Trace2Vectors;

public class CredentialTests
{
    [Fact]
    public void ParsesTraceCredentials()
    {
        var credR = EdhocCredential.FromCcs(V.CredR);
        Assert.Equal(V.KidR, credR.Kid.ToArray());
        Assert.Equal(V.CredR, credR.CredentialBytes.ToArray());
        Assert.False(credR.HasPrivateKey);

        var credI = EdhocCredential.FromCcs(V.CredI);
        Assert.Equal(V.KidI, credI.Kid.ToArray());
        Assert.Equal(V.CredI, credI.CredentialBytes.ToArray());
    }

    [Fact]
    public void CreateCcsReproducesTraceBytes()
    {
        // The deterministic writer must reproduce the RFC's CCS encodings
        // exactly — CRED_x participates byte-for-byte in the MACs.
        var credR = EdhocCredential.CreateCcs(V.KidR, V.SubjectR, V.PkRx, V.PkRy);
        Assert.Equal(V.CredR, credR.CredentialBytes.ToArray());

        var credI = EdhocCredential.CreateCcs(V.KidI, V.SubjectI, V.PkIx, V.PkIy);
        Assert.Equal(V.CredI, credI.CredentialBytes.ToArray());
    }

    [Fact]
    public void WithPrivateKeyValidatesTheKey()
    {
        var credR = EdhocCredential.FromCcs(V.CredR).WithPrivateKey(V.SkR);
        Assert.True(credR.HasPrivateKey);
        Assert.Equal(V.SkR, credR.PrivateKey!.Value.ToArray());

        Assert.Throws<EdhocException>(
            () => EdhocCredential.FromCcs(V.CredR).WithPrivateKey(V.SkI));
        Assert.Throws<EdhocException>(
            () => EdhocCredential.FromCcs(V.CredR).WithPrivateKey(new byte[32]));
    }

    [Fact]
    public void GenerateIssuesAWorkingCredential()
    {
        var credential = EdhocCredential.Generate([0x0a], "device");
        Assert.True(credential.HasPrivateKey);
        Assert.Equal([0x0a], credential.Kid.ToArray());

        var reparsed = EdhocCredential.FromCcs(credential.CredentialBytes.Span);
        Assert.False(reparsed.HasPrivateKey);
        var rearmed = reparsed.WithPrivateKey(credential.PrivateKey!.Value.Span);
        Assert.True(rearmed.HasPrivateKey);
    }

    [Fact]
    public void RejectsMalformedCcs()
    {
        // Truncations anywhere in the encoding must throw, never crash.
        for (var length = 0; length < V.CredR.Length; length++)
        {
            var truncated = V.CredR[..length];
            Assert.Throws<EdhocException>(() => EdhocCredential.FromCcs(truncated));
        }

        var trailing = new byte[V.CredR.Length + 1];
        V.CredR.CopyTo(trailing, 0);
        Assert.Throws<EdhocException>(() => EdhocCredential.FromCcs(trailing));
    }

    [Fact]
    public void RejectsWrongKeyTypeAndCurve()
    {
        var wrongKty = V.CredR.ToArray();
        Assert.Equal(0x02, wrongKty[19]);        // kty value
        wrongKty[19] = 0x03;
        Assert.Throws<EdhocException>(() => EdhocCredential.FromCcs(wrongKty));

        var wrongCurve = V.CredR.ToArray();
        Assert.Equal(0x01, wrongCurve[24]);      // crv value
        wrongCurve[24] = 0x02;
        Assert.Throws<EdhocException>(() => EdhocCredential.FromCcs(wrongCurve));
    }

    [Fact]
    public void RejectsAnEmptyKid()
    {
        Assert.Throws<EdhocException>(
            () => EdhocCredential.CreateCcs([], "x", V.PkRx, V.PkRy));
    }
}
