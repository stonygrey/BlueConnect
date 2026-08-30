<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# EDHOC firmware

The EDHOC image of the Verdigris device. Same GATT service, framing, and
nanopb command API as `firmware/noise` and `firmware/pake` (shared from
`firmware/common`). Transport security is RFC 9528 method 3 (static-static
DH) with cipher suite 2 from provisioned credentials, not a setup code or a
button ceremony.

| File | Concern | Counterpart |
|------|---------|-------------|
| `../common/src/segment.c` | GATT segmentation; payload-blind | `Core/SegmentLink.cs` |
| `../common/src/verdigris.c` | GATT table, dispatcher, ACL | `Verdigris/VerdigrisPeripheral.cs` |
| `../common/src/app_main.c` | Nordic peripheral_uart chassis (`LicenseRef-Nordic-5-Clause`) | — |
| `src/edhoc.c` | EDHOC responder, credential slots, rate limiting, sealed transport | `Secure/EdhocLink.cs` + `StonyGrey.Edhoc` |
| `src/cred_ui.c` | Slot LEDs, clear-hold button, `vg info\|keygen\|cred\|set\|confirm\|clear\|wipe` shell | `lib/device edhoc-provision` |
| `src/main.c` | App-init shim for the shared chassis | — |

`edhoc.c` drives PSA crypto (Oberon software driver): P-256 ECDH via
`psa_raw_key_agreement`, SHA-256/HMAC, AES-CCM with the suite's 8-byte
tag, and AES-256-GCM for the transport. HKDF-Expand (EDHOC_KDF) is the
plain RFC 5869 HMAC loop. CBOR is a small strict hand-rolled subset —
the message shapes are tiny and fixed. The C# library conforms to the
RFC 9529 trace-2 vectors; those vectors are the interop baseline.

Two Oberon behaviors are probed loudly at boot (watch RTT on first
flash): AES-CCM with `PSA_ALG_AEAD_WITH_SHORTENED_TAG(CCM, 8)`, and
compressed-point (`0x02 ‖ x`) peer keys in `psa_raw_key_agreement`
(EDHOC's G_X arrives x-only).

The device generates its own key pair at first boot (`ek/key`) and
persists its credential verbatim (`ek/cred`). Per-tier initiator
credentials (`ek/peer_lo`, `ek/peer_hi`) are written and cleared only
on the USB shell (`lib/device edhoc-provision <low|high>`, `vg clear`).
`Pair` and `Unpair` both answer `UNKNOWN_OP`. Every answered message_1
counts toward the rate limiter; 3 consecutive unconfirmed attempts arm
exponential backoff (to 32 s), persisted in `ek/fails`.

Build and flash from the repo root (WSL or Mac):

    lib/build-firmware edhoc
    lib/flash-firmware edhoc

See [../../documents/edhoc-security.md](../../documents/edhoc-security.md)
for the security design.
