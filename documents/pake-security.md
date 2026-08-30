<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# BlueConnect PAKE Link Security

This document describes how a Zaffre app and a device running the PAKE
firmware (`firmware/pake/`) establish an encrypted, mutually
authenticated link from a printed setup code. This is the sibling of
[noise-security.md](noise-security.md): the same Verdigris GATT service,
segmentation, and command protocol, with the Noise XX + button-ceremony
transport replaced by **SPAKE2+**
([RFC 9383](https://www.rfc-editor.org/rfc/rfc9383),
P256-SHA256-HKDF-SHA256-HMAC-SHA256).

At provisioning time a random setup code is generated per privilege
tier and printed as a QR. The device stores only a *verifier*
(`w0 ‖ L`) derived from the code — never the code itself. To connect,
the phone proves knowledge of the code through a SPAKE2+ handshake
against the tier's verifier. A passive observer learns nothing. An
active attacker gets exactly one code guess per rate-limited attempt.
A device flash dump yields only material for an offline dictionary
attack — never the code and never the ability to impersonate a client
(the augmented property). Success yields a fresh shared secret per
session (forward secrecy comes from the ephemeral scalars) from which
AES-256-GCM transport keys are derived.

There is no pairing ceremony and no Confirm button. Knowing the
tier's code is the pairing. The code is reusable for the device's
life; custody of the printed code is the root of trust.

## Roles and materials

| Party | Holds | Derived how |
|---|---|---|
| Provisioning tool (`lib/device pake-provision`) | the code, transiently | generates it; derives and writes `w0 ‖ L`; prints the QR; forgets everything |
| Phone (Zaffre) | the setup code (per tier) | scans the QR / typed; derives `w0`, `w1` via PBKDF2 (cached per app session). Stored the same way as the Noise identity: Keychain / EncryptedSharedPreferences, `Preferences` on Mac Catalyst. |
| Device | verifier `w0 ‖ L` per tier (`pk/ver_lo`, `pk/ver_hi` in settings) | imported over the USB serial shell; `L = w1·G` cannot be inverted to `w1` |

Code → scalars (the BlueConnect profile, `BlueConnectPake.cs` — every
deriving party must match exactly):

- Setup code: 80 bits from the RNG, 16 Crockford-base32 characters shown as
  `XXXX-XXXX-XXXX-XXXX`; normalization uppercases, strips separators, and
  folds I/L→1, O→0.
- `dk = PBKDF2-HMAC-SHA256(normalized code, salt, 100 000 iterations, 80 bytes)`
  with `salt = "BlueConnectPake/1|low"` or `"…|high"` — the tier lives in
  the salt, so one code can never unlock the other slot.
- `w0 = OS2IP(dk[0..40)) mod n`, `w1 = OS2IP(dk[40..80)) mod n`
  (RFC 9383 §3.2 for P-256); `L = w1·G`.
- RFC 9383 Context = `"BlueConnectPake/1"`; idProver = idVerifier = empty.

## Wire dialect

Same segmentation as always ([ble-framing.md](ble-framing.md)). The type
byte gains one value: `0x03` = PAKE handshake (the Noise firmware drops
it, and vice versa — pointing a tab at the wrong image times out).
Inside a `0x03` frame, byte 0 is the message kind:

| Msg | Dir | Layout | Size |
|---|---|---|---|
| Start `0x01` | phone → device | `ver=0x01 ‖ tier (0x01 low / 0x02 high) ‖ shareP (65 B)` | 68 |
| Respond `0x02` | device → phone | `shareV (65 B) ‖ confirmV (32 B)` | 98 |
| Confirm `0x03` | phone → device | `confirmP (32 B)` | 33 |
| Status `0x04` | device → phone | `0 OK · 1 AUTH_FAILED · 2 NO_VERIFIER · 3 RATE_LIMITED · 4 PROTOCOL_ERROR` | 2 |

The advertisement's manufacturer-data status byte carries bit 0/bit 1 =
verifier slot provisioned (low/high) and **bit 2 set = SPAKE2+** (clear =
Noise), surfaced in the app as `IAdvertisement.AuthenticationScheme`.
That is an unauthenticated UX hint, exactly like the Noise firmware's
pairing bits.

The tier is *declared* in Start (shareV depends on which slot's `w0` the
device uses) and *granted* only if key confirmation succeeds against that
slot's verifier. The phone (GATT central) is the SPAKE2+ **prover** and
talks first; a fresh Start supersedes an established session, mirroring the
Noise responder's rule. The phone detects a code mismatch at `confirmV`
(before revealing anything useful); the device detects it at `confirmP` and
answers AUTH_FAILED.

## Key schedule and transport

Per RFC 9383 §3.3–3.4 (both sides; the firmware gets this from PSA's
`PSA_ALG_SPAKE2P_HMAC(SHA-256)`, the phone from `StonyGrey.Spake2Plus`,
checked byte-exact against the RFC's test vectors):

```
TT       = len ‖ Context ‖ len ‖ idProver ‖ len ‖ idVerifier ‖ len ‖ M ‖ len ‖ N
           ‖ len ‖ shareP ‖ len ‖ shareV ‖ len ‖ Z ‖ len ‖ V ‖ len ‖ w0
K_main   = SHA-256(TT)
K_confirmP ‖ K_confirmV = HKDF(nil, K_main, "ConfirmationKeys", 64)
confirmP = HMAC(K_confirmP, shareV)   confirmV = HMAC(K_confirmV, shareP)
K_shared = HKDF(nil, K_main, "SharedKey", 32)
```

Transport (after Status OK): `HKDF-SHA256(salt=∅, IKM=K_shared,
info="BlueConnectPake/1 transport") → 64 bytes`; bytes 0–31 key
prover→verifier, 32–63 verifier→prover; AES-256-GCM, nonce = 4 zero bytes ‖
big-endian 64-bit counter per direction, strict ordering, no AAD — the Noise
link's transport discipline byte for byte, carried in `0x02` frames. The
command layer above (protobuf requests, the per-command ACL, the
UNAUTHENTICATED/PERMISSION_DENIED verdicts) is identical to the Noise
firmware; `Pair` and `Unpair` both answer `UNKNOWN_OP`. Verifiers are
factory credentials managed only over USB; erasing one over BLE would let
any code holder remotely lock out provisioned access, so rotation is
`vg clear` on the serial shell.

## Attacker outcomes

| Threat | Outcome |
|---|---|
| Passive sniffing of a handshake | Nothing: shares are uniformly distributed points; no offline attack on the transcript. |
| Active online guessing | Every Start that runs against a verifier counts as one guess — including attempts the prover abandons after checking confirmV locally. A confirmed session resets the counter. After 3 consecutive unconfirmed attempts, further Starts are refused with exponential backoff (min(2^n, 32) s) until the window expires, at which point one more guess is answered. The failure count is stored in `pk/fails` so a reboot does not wipe it; the lockout deadline is uptime-relative, so a reboot of an over-budget counter starts a fresh backoff window immediately. |
| Device flash dump | Yields `w0 ‖ L`: enough to impersonate the device to a phone and to run an offline dictionary attack on the 80-bit code, but not to impersonate a client to another device. Readout protection (APPROTECT) is the production answer, as with the Noise identity. |
| Phone compromise | Yields the stored code (SecureStorage) — full access at that tier. Same exposure class as the Noise identity key. |
| QR photographed | The code is the credential and stays valid for the device's life: custody of the label is the procedural control. Re-provision (`pake-provision` overwrites the slot) to rotate. |
| Wrong firmware for the tab | Handshake frame dropped → timeout; no downgrade path exists because neither image contains the other transport. |

There is no pairing window and no button that trusts whoever is
connected. Authentication is cryptographic on both ends of first
contact.

## Code

| Concern | Host | Firmware |
|---|---|---|
| SPAKE2+ (RFC 9383) | [`StonyGrey/Spake2Plus`](../StonyGrey/Spake2Plus) (BouncyCastle P-256; RFC vectors in `tests/Spake2Plus.Tests`) | PSA PAKE API (Oberon driver), driven by `firmware/pake/src/pake.c` |
| Link + transport | `Secure/PakeLink.cs` | `firmware/pake/src/pake.c` |
| Profile constants | `StonyGrey/Spake2Plus/BlueConnectPake.cs` | `CONTEXT`/`TRANSPORT_INFO` in `pake.c` |
| Provisioning | `tools/Provision/PakeProvision.cs` (`lib/device pake-provision`) | `pk` shell in `firmware/pake/src/prov_ui.c` |
| Loopback validation | `tools/Provision/PakeSelfTest.cs` (`lib/device pake-self-test`) | — |

The QR payload is `SGPK1:<L|H>:<CODE>[:<NAME>]` (`PakeQrPayload.cs`);
parsers reject unknown version prefixes.
