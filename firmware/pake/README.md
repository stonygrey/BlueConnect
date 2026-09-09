<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# PAKE firmware

The SPAKE2+ image of the Verdigris device. Same GATT service, framing, and
nanopb command API as `firmware/noise` and `firmware/edhoc` (shared from
`firmware/common`).
Transport security is RFC 9383 P256-SHA256 from a printed setup code, not
Noise XX with a button ceremony.

| File | Concern | Counterpart |
|------|---------|-------------|
| `../common/src/segment.c` | GATT segmentation; payload-blind | `Core/SegmentLink.cs` |
| `../common/src/verdigris.c` | GATT table, dispatcher, ACL | `Verdigris/VerdigrisPeripheral.cs` |
| `src/pake.c` | SPAKE2+ verifier, slots, rate limiting, sealed transport | `Secure/PakeLink.cs` + `StonyGrey.Spake2Plus` |
| `src/prov_ui.c` | Slot LEDs, clear-hold button, `vg info\|set\|clear\|wipe` shell | `lib/device pake-provision` |
| `../common/src/app_main.c` | Nordic peripheral_uart chassis (`LicenseRef-Nordic-5-Clause`) | — |
| `src/main.c` | App-init shim over the shared chassis | — |

`pake.c` contains no hand-written protocol crypto. The exchange is
`psa_pake_*` (`PSA_ALG_SPAKE2P_HMAC(SHA-256)`, secp256r1). The C# side of the same profile is checked against the RFC
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

SPAKE2P key imports, public keys included, sit behind
`CONFIG_PSA_WANT_KEY_TYPE_SPAKE2P_KEY_PAIR_IMPORT` (see `prj.conf`).
Interop was run on an nRF52840-DK (NCS v3.4.0): PSA accepts the
profile's empty idProver/idVerifier, and handshake plus sealed
transport round-trip against the C# side. On the nRF54L15-DK (the
default board since 2026-08-30) the image builds and provisions; the
on-air SPAKE2+ exchange there has not yet been exercised.

Build and flash with `lib/flash-firmware pake`. Framing and the proto
are the same as the Noise and EDHOC images. The `0x03` handshake dialect is in
`src/pake.c` and `tools/Provision/PakeSelfTest.cs`.
