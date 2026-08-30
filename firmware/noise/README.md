<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# Noise firmware

The Noise XX image of the Verdigris device. The SPAKE2+ sibling is
[firmware/pake](../pake/README.md). Both expose the same GATT service.
Framing, security, and the command API are separate files; the
protocol-agnostic pieces come from `firmware/common/` behind
`link_iface.h`.

| File | Concern | Host counterpart |
|------|---------|------------------|
| `../common/src/segment.c` | GATT segmentation; payload-blind | `Core/SegmentLink.cs` |
| `src/link.c` | Noise XX responder: handshake, pin slots, pairing window, sealed transport | `Secure/Links.cs` + [Torann](https://www.nuget.org/packages/Torann) |
| `../common/src/verdigris.c` | nanopb dispatcher and the per-command privilege table | `Verdigris/VerdigrisPeripheral.cs` |
| `src/pairing_ui.c` | LEDs, buttons, and the `vg info\|keygen\|cred\|set\|ca-root\|confirm\|clear\|wipe` shell | pairing card UI |
| `src/main.c` | Nordic peripheral_uart scaffolding (`LicenseRef-Nordic-5-Clause`) | — |

`link.c` is the one hand-written protocol state machine in this image —
which DH result is mixed when, what is transcript-hashed. Every primitive
it calls (X25519, AES-256-GCM, SHA-256, HMAC, and Ed25519 verify for the
optional client endorsement) goes through PSA Crypto to Nordic's Oberon
implementations, with entropy from the CryptoCell TRNG.

The IDL is `proto/verdigris.proto` at the repo root. CMake copies it into
`src/` at configure time; those copies are build artifacts. Build and
flash with `lib/flash-firmware`. Protocol: [noise-security.md](../../documents/noise-security.md).
Toolchain: [build-deploy.md](../../documents/build-deploy.md).
