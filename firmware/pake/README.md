<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# PAKE firmware

The SPAKE2+ image of the Verdigris device. Same GATT service, framing, and
nanopb command API as `firmware/noise` (shared from `firmware/common`).
Transport security is RFC 9383 P256-SHA256 from a printed setup code, not
Noise XX with a button ceremony.

| File | Concern | Counterpart |
|------|---------|-------------|
| `../common/src/segment.c` | GATT segmentation; payload-blind | `Core/SegmentLink.cs` |
| `../common/src/verdigris.c` | GATT table, dispatcher, ACL | `Verdigris/VerdigrisPeripheral.cs` |
| `src/pake.c` | SPAKE2+ verifier, slots, rate limiting, sealed transport | `Secure/PakeLink.cs` + `StonyGrey.Spake2Plus` |
| `src/prov_ui.c` | Slot LEDs, clear-hold button, `vg info\|set\|clear\|wipe` shell | `lib/device pake-provision` |
| `src/main.c` | Nordic peripheral_uart scaffolding (`LicenseRef-Nordic-5-Clause`) | — |

`pake.c` contains no hand-written protocol crypto. The exchange is
`psa_pake_*` into Nordic's Oberon driver (`PSA_ALG_SPAKE2P_HMAC(SHA-256)`,
secp256r1). The C# side of the same profile is checked against the RFC
test vectors. The device stores only the verifier `w0 ‖ L` per tier
(`pk/ver_lo`, `pk/ver_hi`) — never the setup code. A flash dump is
material for an offline dictionary attack; it is not enough to
impersonate a client.

There is no pairing ceremony. `Pair` and `Unpair` both answer
`UNKNOWN_OP`. Verifiers are factory credentials, written and cleared
only on the USB shell (`lib/device pake-provision <low|high>`,
`vg clear`). Every Start that runs against a verifier counts as one
code guess, abandoned attempts included. After 3 consecutive unconfirmed
attempts, further Starts are refused with exponential backoff. The
failure count lives in `pk/fails` (a reboot does not wipe it); the
lockout deadline is uptime-relative.

Oberon compiles SPAKE2P key imports, public keys included, behind
`CONFIG_PSA_WANT_KEY_TYPE_SPAKE2P_KEY_PAIR_IMPORT` (see `prj.conf`).
Interop was run on an nRF52840-DK (NCS v3.4.0): Oberon accepts the
profile's empty idProver/idVerifier, and handshake plus sealed
transport round-trip against the C# side.

Build and flash with `lib/flash-firmware pake`. Framing and the proto
are the same as the Noise image. The `0x03` handshake dialect is in
`src/pake.c` and `tools/Provision/PakeSelfTest.cs`.
