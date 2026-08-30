// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using Org.BouncyCastle.Math;

namespace StonyGrey.Edhoc.Tests;

/// <summary>
/// The RFC 9529 §3 test vectors: method 3 (static-static DH), cipher suite 2,
/// CCS credentials identified by kid. Shared by the vector and key-schedule
/// tests; every value is byte-exact from the RFC.
/// </summary>
internal static class Rfc9529Trace2Vectors
{
    // Initiator ephemeral key pair (message_1, second time — §3.3).
    public static readonly BigInteger X = new(
        "368ec1f69aeb659ba37d5a8d45b21bdc0299dceaa8ef235f3ca42ce3530f9525", 16);

    public static readonly byte[] GX =
        Convert.FromHexString("8af6f430ebe18d34184017a9a11bf511c8dff8f834730b96c1b7c8dbca2fc3b6");

    // Responder ephemeral key pair (§3.4).
    public static readonly BigInteger Y = new(
        "e2f4126777205e853b437d6eaca1e1f753cdcc3e2c69fa884b0a1a640977e418", 16);

    public static readonly byte[] GY =
        Convert.FromHexString("419701d7f00a26c2dc587a36dd752549f33763c893422c8ea0f955a13a4ff5d5");

    // Connection identifiers (raw one-byte values).
    public static readonly byte[] CI = [0x37];
    public static readonly byte[] CR = [0x27];

    // Static authentication keys.
    public static readonly byte[] SkI =
        Convert.FromHexString("fb13adeb6518cee5f88417660841142e830a81fe334380a953406a1305e8706b");

    public static readonly byte[] SkR =
        Convert.FromHexString("72cc4761dbd4c78f758931aa589d348d1ef874a7e303ede2f140dcf3e6aa4aac");

    public static readonly byte[] PkIx =
        Convert.FromHexString("ac75e9ece3e50bfc8ed60399889522405c47bf16df96660a41298cb4307f7eb6");

    public static readonly byte[] PkIy =
        Convert.FromHexString("6e5de611388a4b8a8211334ac7d37ecb52a387d257e6db3c2a93df21ff3affc8");

    public static readonly byte[] PkRx =
        Convert.FromHexString("bbc34960526ea4d32e940cad2a234148ddc21791a12afbcbac93622046dd44f0");

    public static readonly byte[] PkRy =
        Convert.FromHexString("4519e257236b2a0ce2023f0931f1f386ca7afda64fcde0108c224c51eabf6072");

    // Credentials (CCS bytes, verbatim) and their kids.
    public static readonly byte[] KidI = [0x2b];
    public static readonly byte[] KidR = [0x32];
    public const string SubjectI = "42-50-31-FF-EF-37-32-39";
    public const string SubjectR = "example.edu";

    public static readonly byte[] CredI = Convert.FromHexString(
        "a2027734322d35302d33312d46462d45462d33372d33322d333908a101a5010202" +
        "412b2001215820ac75e9ece3e50bfc8ed60399889522405c47bf16df96660a4129" +
        "8cb4307f7eb62258206e5de611388a4b8a8211334ac7d37ecb52a387d257e6db3c" +
        "2a93df21ff3affc8");

    public static readonly byte[] CredR = Convert.FromHexString(
        "a2026b6578616d706c652e65647508a101a5010202413220012158" +
        "20bbc34960526ea4d32e940cad2a234148ddc21791a12afbcbac93622046dd44f0" +
        "2258204519e257236b2a0ce2023f0931f1f386ca7afda64fcde0108c224c51eabf" +
        "6072");

    // On-wire messages.
    public static readonly byte[] Message1 = Convert.FromHexString(
        "038206025820" +
        "8af6f430ebe18d34184017a9a11bf511c8dff8f834730b96c1b7c8dbca2fc3b6" +
        "37");

    public static readonly byte[] Message2 = Convert.FromHexString(
        "582b419701d7f00a26c2dc587a36dd752549f33763c893422c8ea0f955a13a4ff5" +
        "d59862a1eef9e0e7e1886fcd");

    public static readonly byte[] Message3 =
        Convert.FromHexString("52e562097bc417dd5919485ac7891ffd90a9fc");

    public static readonly byte[] Message4 = Convert.FromHexString("4828c966b7ca304f83");

    // The suite-negotiation prologue (§3.1–3.2): message_1 offering only
    // suite 6, and the responder's ERR_CODE 2 answer proposing suite 2.
    public static readonly byte[] Message1FirstTime = Convert.FromHexString(
        "03065820741a13d7ba048fbb615e94386aa3b61bea5b3d8f65f32620b749bee8d2" +
        "78efa90e");

    public static readonly byte[] WrongSuiteError = Convert.FromHexString("0202");

    // Key schedule intermediates.
    public static readonly byte[] Th2 =
        Convert.FromHexString("356efd53771425e008f3fe3a86c83ff4c6b16e57028ff39d5236c182b202084b");

    public static readonly byte[] GXy =
        Convert.FromHexString("2f0cb7e860ba538fbf5c8bded009f6259b4b628fe1eb7dbe9378e5ecf7a824ba");

    public static readonly byte[] Prk2e =
        Convert.FromHexString("5aa0d69f3e3d1e0c479f0b8a486690c9802630c3466b1dc92371c982563170b5");

    public static readonly byte[] Salt3e2m =
        Convert.FromHexString("af4e103a47cb3cf32570d5c25ad27732bd8d8178e9a69d061c31a27f8e3ca926");

    public static readonly byte[] GRx =
        Convert.FromHexString("f2b6eea02220b95eee5a0bc701f074e00a843ea02422f60825fb269b3e161423");

    public static readonly byte[] Prk3e2m =
        Convert.FromHexString("0ca3d3398296b3c03900987620c11f6fce70781c1d1219720f9ec08c122d8434");

    public static readonly byte[] Mac2 = Convert.FromHexString("0943305c899f5c54");

    public static readonly byte[] Plaintext2 = Convert.FromHexString("2732480943305c899f5c54");

    public static readonly byte[] Keystream2 = Convert.FromHexString("bf50e9e7bad0bb68173399");

    public static readonly byte[] Th3 =
        Convert.FromHexString("adaf67a78a4bcc91e018f8882762a722000b2507039df0bc1bbf0c161bb3155c");

    public static readonly byte[] Salt4e3m =
        Convert.FromHexString("cfddf9515a7e46e7b4dbff31cbd56cd04ba332250de9ea5de1caf9f6d13914a7");

    public static readonly byte[] GIy =
        Convert.FromHexString("080f425085bc6249089eac8f108ea62326857e12ab07d72028ca1b5f36e004b3");

    public static readonly byte[] Prk4e3m =
        Convert.FromHexString("81cc8a298e357044e3c466bb5c0a1e507e01d49238aeba138df94635407c0ff7");

    public static readonly byte[] Mac3 = Convert.FromHexString("623c91df41e34c2f");

    public static readonly byte[] Plaintext3 = Convert.FromHexString("2b48623c91df41e34c2f");

    public static readonly byte[] K3 = Convert.FromHexString("8e7a30042000f7900e8174131f75f3ed");

    public static readonly byte[] Iv3 = Convert.FromHexString("6d8300c1e23b56153ae70ee457");

    public static readonly byte[] Th4 =
        Convert.FromHexString("c902b1e3a4326c93c5551f5f3aa6c5ecc0246806765612e52b5d99e6059d6b6e");

    public static readonly byte[] K4 = Convert.FromHexString("d3c77872b6eeb508911bdbd308b2e6a0");

    public static readonly byte[] Iv4 = Convert.FromHexString("04ff0f44456e96e217853c3601");

    public static readonly byte[] PrkOut =
        Convert.FromHexString("2c71afc1a9338a940bb3529ca734b886f30d1aba0b4dc51beeaeabdfea9ecbf8");

    public static readonly byte[] PrkExporter =
        Convert.FromHexString("e14d06699cee248c5a04bf9227bbcd4ce394de7dcb56db43555474171e6446db");

    // EDHOC_Exporter outputs (§3.8): OSCORE Master Secret and Master Salt.
    public static readonly byte[] OscoreMasterSecret =
        Convert.FromHexString("f9868f6a3aca78a05d1485b35030b162");

    public static readonly byte[] OscoreMasterSalt = Convert.FromHexString("ada24c7dbfc85eeb");

    public static EdhocCredential InitiatorCredentialWithKey() =>
        EdhocCredential.FromCcs(CredI).WithPrivateKey(SkI);

    public static EdhocCredential ResponderCredentialWithKey() =>
        EdhocCredential.FromCcs(CredR).WithPrivateKey(SkR);
}
