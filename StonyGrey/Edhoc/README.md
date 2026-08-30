# EDHOC.NET (`StonyGrey.Edhoc`)

A C# implementation of **EDHOC** — Ephemeral Diffie-Hellman Over COSE,
[RFC 9528](https://www.rfc-editor.org/rfc/rfc9528) — the IETF LAKE working
group's lightweight authenticated key exchange for constrained devices.

## Scope (v0.1)

- **Method 3** (static-static DH): mutual authentication from static
  Diffie-Hellman keys; no signature algorithm required on either side.
- **Cipher suite 2**: P-256 ECDH, SHA-256, AES-CCM-16-64-128 (8-byte MAC).
- **Credentials**: raw public keys in a CWT Claims Set (CCS), identified by
  COSE `kid` (RFC 9528 §3.5.2). Credential bytes are treated as opaque and
  verbatim — they participate byte-exactly in the transcript hashes.
- messages 1–4, EDHOC error messages (ERR_CODE 1 and 2), opaque EAD
  passthrough (unknown critical items rejected), and the EDHOC_Exporter.

Verified byte-exactly against the [RFC 9529](https://www.rfc-editor.org/rfc/rfc9529)
§3 test vectors (trace 2), including every key-schedule intermediate.
Signature methods (0–2) and further cipher suites (e.g. suite 0:
X25519/EdDSA, RFC 9529 trace 1) are designed-for, non-breaking additions.

All crypto is pure managed (`BouncyCastle.Cryptography` for P-256 and
AES-CCM, BCL for SHA-256/HKDF/HMAC), so the library runs on every MAUI
target. CBOR uses `System.Formats.Cbor` with deterministic (canonical)
encoding as RFC 9528 §3.1 requires.

## Usage

```csharp
using StonyGrey.Edhoc;

// Provisioning time: issue credentials (each side keeps its private key and
// the peer's CCS bytes).
var deviceCred = EdhocCredential.Generate(kid: [0x0a], subject: "device");
var hostCred = EdhocCredential.Generate(kid: [0x01], subject: null);

// Handshake: the initiator pins the responder's credential up front.
var initiator = new EdhocInitiator(
    hostCred, EdhocCredential.FromCcs(deviceCred.CredentialBytes.Span));
var responder = new EdhocResponder(
    deviceCred, EdhocCredential.FromCcs(hostCred.CredentialBytes.Span));

responder.ProcessMessage1(initiator.CreateMessage1());
initiator.ProcessMessage2(responder.CreateMessage2());   // authenticates responder
responder.ProcessMessage3(initiator.CreateMessage3());   // authenticates initiator

// Both sides now export identical keying material (RFC 9528 §4.2.1).
byte[] keys = initiator.Export(label: 32768, context: [], length: 64);
```

State machines are single-use; misuse and every protocol failure throw
`EdhocException` (or `EdhocErrorReceivedException` when the peer sent an
EDHOC error message). When a failure warrants telling the peer, the thrown
exception's `ErrorMessageToSend` carries the ready-encoded error — the
library itself never transmits anything.

## Caveats

- .NET offers no key-zeroization guarantees; private keys and session
  secrets live in managed memory.
- Side-channel posture is that of pure-managed BouncyCastle arithmetic.
