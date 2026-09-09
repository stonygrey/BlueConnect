<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# BlueConnect EDHOC Link Security

This document describes how a Zaffre app and a device running the EDHOC
firmware (`firmware/edhoc/`) establish an encrypted, mutually
authenticated link from static Diffie-Hellman credentials — either
enrolled over the air with a button ceremony, or installed at
provisioning time over USB.

It is the third sibling of [noise-security.md](noise-security.md) and
[pake-security.md](pake-security.md): the same Verdigris GATT service,
segmentation, and command protocol, with the transport bootstrapped by
**EDHOC** ([RFC 9528](https://www.rfc-editor.org/rfc/rfc9528)) —
**method 3** (static-static DH, no signatures) with **cipher suite 2**
(P-256 ECDH, SHA-256, AES-CCM-16-64-128 with an 8-byte MAC).

The profile was chosen for the constrained side: method 3 needs no
signature algorithm on the device, suite 2's messages are the smallest
of the P-256 suites (message_2 is 45 bytes), and every primitive maps
onto the PSA crypto the other firmware images already carry. The C#
implementation (`StonyGrey.Edhoc`, "EDHOC.NET") is executed against the
[RFC 9529](https://www.rfc-editor.org/rfc/rfc9529) trace-2 vectors,
which cover this exact profile. The firmware responder is a second
implementation of the same schedule, checked by interop with that
initiator and a boot-time probe of CCM-8 and compressed-point ECDH —
not by feeding the vectors through `link_handshake`.

Credentials arrive one of two ways, and each slot remembers which:

- **Enrollment (button ceremony).** When the phone holds nothing for a
  tier, Pair runs an *enrolling* handshake: message_1 carries the
  enrollment flag in EAD_1 (label 3737, value-less), both ends send
  their credential **by value** (`ID_CRED = {14: CCS}`, kccs) instead
  of by kid, and each verifies the other's MAC against the embedded
  credential — trust-on-first-use, so the session is only a *candidate*
  and runs at privilege NONE. The Pair command then arms the device's
  30 s confirmation window (LED 2 + Button 2 = low, LED 3 + Button 3 =
  high, or `vg confirm` on the USB shell); a matching press pins the
  candidate credential into the tier slot with **ceremony** provenance,
  and the phone stores the device credential it learned. The device
  key is generated at first boot, the phone's at first Pair for that
  tier; neither travels.
- **USB provisioning (factory).** `lib/device edhoc-provision
  <low|high>` issues a fresh P-256 key pair per tier, writes its
  credential to the device's slot over the USB serial shell (possession
  proof), reads the device's credential back, and prints a QR whose
  payload carries the tier's **private key** plus the device's pinned
  credential. The QR *is* the credential — the same custody model as
  the PAKE setup code, but with full-entropy keys. A USB-installed slot
  is **locked**: no ceremony may replace it and no remote unpair may
  clear it. `vg clear` over USB and holding Button 3 for 5 s, both
  possession-gated, clear pins and locks together — the same recovery
  as Noise (`vg clear` / the button hold). Physical possession is
  already out of scope (a holder can reflash the image); the hold is
  the path that works when the J-Link serial is not to hand. A
  ceremony-made pin is replaceable by a later matching ceremony and
  remotely unpairable.

This is the same hybrid lifecycle as the Noise image (ceremony keys vs
`vg set` locks); only the key material differs — X25519 there,
P-256 CCS here — so the two schemes cannot and do not share keys.

## Roles and materials

| Party | Holds | Derived how |
|---|---|---|
| Provisioning tool (`lib/device edhoc-provision`) | the initiator key pair, transiently | generates it; writes CRED_I to the device slot; reads CRED_R back; prints the QR; forgets everything |
| Phone (Zaffre) | initiator private key + kid, device CCS (per tier) | Pair (enrollment: generates the key, learns the device CCS at the ceremony) or scans the QR / pasted `SGEK1` payload; stored like the other credentials: Keychain / EncryptedSharedPreferences, `Preferences` on Mac Catalyst |
| Device | own P-256 key pair (`ek/key`, generated at first boot) and CCS (`ek/cred`), plus one pinned initiator CCS per tier (`ek/peer_lo`, `ek/peer_hi`) with a provenance lock per slot (`ek/lock_lo`, `ek/lock_hi`: set = USB) | keygen on device; peers pinned by ceremony or imported over the USB serial shell |

Credentials are CWT Claims Sets (CCS, RFC 9528 §3.5.2) carrying a raw
P-256 public key, identified by the COSE `kid`. The kid doubles as the
tier: `0x01` = low slot, `0x02` = high slot, `0x0A` = the device. CCS
bytes are stored and transported **verbatim** on both sides — they enter
the transcript hashes and MACs byte-exactly, so they are never
re-encoded after issuance.

## Wire dialect

Same segmentation as always; the type byte gains one value: `0x04` =
EDHOC handshake (the other images drop it, and vice versa — pointing a
tab at the wrong image fails cleanly). Inside an `0x04` frame, byte 0 is
the message kind:

| Kind | Direction | Body | Size |
|---|---|---|---|
| `0x01` Msg1 | phone → device | EDHOC message_1 (method, SUITES_I, G_X, C_I, EAD_1 when enrolling) | ~38–41 B |
| `0x02` Msg2 | device → phone | EDHOC message_2 (G_Y ‖ CIPHERTEXT_2) | 45 B (kid) / ~129 B (enrolling: the CCS travels inside) |
| `0x03` Msg3 | phone → device | EDHOC message_3 (CIPHERTEXT_3) | 19 B (kid) / ~105 B (enrolling) |
| `0x04` Status | device → phone | OK / AUTH_FAILED / NO_CREDENTIAL / RATE_LIMITED / PROTOCOL_ERROR | 2 B |

The phone (central) is the EDHOC initiator and talks first. A Msg1 in
any phase resets the device's session and starts over (the responder is
stateful; this is the resync rule). EDHOC's optional message_4 is not
used on this wire: a 2-byte plaintext Status after message_3 plays that
role, without message_4's AEAD, mirroring the PAKE dialect. A flipped
Status(OK) makes the phone install exporter keys the device has already
discarded; the first successful sealed frame is the actual confirmation,
and device privilege still requires MAC_3. G_X/G_Y travel as bare
32-byte x-coordinates per RFC 9528 §3.7; the y sign never matters
because ECDH uses only the shared x-coordinate (§9.5).

The manufacturer-data status byte advertises the scheme in bits 2–3
(`00` Noise, `01` PAKE, `10` EDHOC) plus the provisioned-slot bits — an
unauthenticated UX hint only, like the other schemes.

## Trust model

- **Mutual authentication.** The phone authenticates the device at
  message_2: MAC_2 is keyed through `G_RX = ECDH(eph_I, static_R)`, so
  only the holder of the pinned device credential's private key can
  produce it. The device authenticates the phone at message_3: the kid
  inside CIPHERTEXT_3 selects a pinned slot credential and MAC_3 is
  keyed through `G_IY = ECDH(eph_R, static_I)`. Privilege is granted
  only after MAC_3 verifies against that slot.
- **Forward secrecy.** Session keys mix both ephemerals (`G_XY`);
  compromise of the static keys later does not decrypt past sessions.
- **Identity protection.** Credential identifiers travel only inside
  CIPHERTEXT_2/3; a passive observer sees ephemeral keys and opaque
  ciphertext. (An active attacker can elicit the device's kid — inherent
  to the message_2 direction, RFC 9528 §9.1 — which here reveals the
  fixed device kid `0x0A`, or, when the initiator sent the enrollment
  EAD, the device's full public CCS by value.)
- **Downgrade resistance.** SUITES_I and the method are bound into
  TH_2..TH_4; tampering with either fails both MACs.
- **Enrollment is TOFU plus a ceremony.** An enrolling handshake
  authenticates only *itself*: MAC verification against a credential the
  peer just sent proves key possession, not identity. The candidate
  session therefore runs at privilege NONE — the only command it can
  usefully issue is Pair — and trust is created by the physical button
  press (or `vg confirm`, the same possession proof over USB). The
  confirmation must name the tier the credential's kid names; a
  mismatch refuses the pin.
- **Provenance locks.** A USB-provisioned slot refuses ceremonies
  (`PERMISSION_DENIED: slot is provisioned over USB`) and remote
  unpairs. `vg clear` and the 5 s Button 3 hold, both possession-gated,
  clear pins and locks together. Ceremony pins accept a later matching
  ceremony and remote unpair, because a matching press *is* the
  possession proof.
- **Rate limiting.** Every answered Msg1 counts as an attempt; a
  MAC_3-confirmed session against a *pinned* slot resets the counter,
  and so does a completed pairing ceremony — but a candidate session by
  itself does not (anyone can enroll-handshake; forgiveness would let
  an attacker reset the throttle at will). After 3 consecutive
  unconfirmed attempts the device refuses further Msg1 with exponential
  backoff (to 32 s), persisted across reboots (`ek/fails`). With
  full-entropy keys this throttles forgery probing and DoS, not
  guessing.

## Key schedule and transport

The RFC 9528 §4 schedule runs exactly as specified (Extract = HMAC-SHA-256,
Expand = HKDF-Expand with the CBOR `( label, context, length )` info) down
to `PRK_exporter`. The transport keys are then

    EDHOC_Exporter( 32768, h'', 64 )   — private-use label

split 0–31 initiator→device / 32–63 device→initiator, each an
AES-256-GCM key driving the Noise link's transport discipline byte for
byte, carried in `0x02` frames: nonce = 4 zero bytes ‖ 64-bit
big-endian counter from 0, no AAD, 16-byte tags, refuse-to-wrap, any
AEAD failure drops the session. The command layer above is identical
across all three schemes.

## Error handling

The device answers protocol violations with Status codes rather than
CBOR EDHOC error messages (the library emits/parses those for
interoperability, but this wire's Status plays that role). AUTH_FAILED
covers AEAD or MAC_3 failure; NO_CREDENTIAL an empty slot;
RATE_LIMITED the backoff window. The host maps each to a distinct
`LinkSecurityException` message.

## Residual risks

- The printed QR carries the tier's private key: custody of the label
  is the root of trust, exactly like the PAKE setup code label.
  (Enrollment avoids this entirely — its keys never leave the device
  they were generated on.)
- Status is a 2-byte plaintext frame. Flipping AUTH_FAILED to OK
  desynchronises the phone (it installs exporter keys, then fails at
  the first sealed frame). Device privilege is untouched: MAC_3 still
  has to verify. The first successful AEAD open is the confirmation
  message_4 would have been.
- During an armed pairing window, whoever is enrolling gets pinned by
  the press: the ceremony proves presence, not which radio peer is
  present — the same residual as the Noise ceremony. Provision over USB
  where that matters.
- Slots provisioned by firmware older than the provenance flags load as
  ceremony pins (unlocked) after an upgrade; re-run
  `lib/device edhoc-provision` to re-lock them.
- `vg keygen force` (and the Buttons 2+3 factory wipe) rotates the device key and orphans every printed
  QR; re-provision afterwards.
- No revocation beyond `vg clear` / `vg wipe` on the USB shell and the Button 3
  hold; no certificates (raw public keys by design).
- .NET offers no key-zeroization guarantees; the firmware zeroes
  session secrets but the device private key lives in flash
  (`ek/key`) unencrypted — APPROTECT hardening is the same open
  roadmap item as for the other images.
- Side channels: P-256 and CCM run in the board's PSA driver on-device; the host uses pure-managed BouncyCastle, whose
  posture is best-effort.

## References

RFC 9528 (EDHOC), RFC 9529 (traces), RFC 8949 (CBOR), RFC 8392 (CWT),
RFC 9052 (COSE).
