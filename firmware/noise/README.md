<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# Noise firmware

The firmware image of the Verdigris device: Noise XX for the ceremony,
IK once both keys are pinned. Framing, security, and the command API are
separate files; the protocol-agnostic pieces come from `firmware/common/`
behind `link_iface.h`.

| File | Concern | Host counterpart |
|------|---------|------------------|
| `../common/src/segment.c` | GATT segmentation; payload-blind | `Core/SegmentLink.cs` |
| `src/link.c` | Noise XX responder: handshake, pin slots, pairing window, sealed transport | `Secure/Links.cs` + [Torann](https://www.nuget.org/packages/Torann) |
| `../common/src/verdigris.c` | nanopb dispatcher and the per-command privilege table | `Verdigris/VerdigrisPeripheral.cs` |
| `src/pairing_ui.c` | LEDs, buttons, and the `vg info\|keygen\|cred\|set\|ca-root\|confirm\|clear\|wipe` shell | pairing card UI |
| `../common/src/app_main.c` | Nordic peripheral_uart chassis (`LicenseRef-Nordic-5-Clause`) | none |
| `src/main.c` | App-init shim over the shared chassis | none |

`link.c` is the one hand-written protocol state machine in this image:
the order in which the DH results are mixed, and what goes into the
transcript hash, are written there.
Every primitive it calls (X25519, AES-256-GCM, SHA-256, HMAC, and
Ed25519 verify for the optional client endorsement) goes through PSA
Crypto (the board's driver; see
[build-deploy.md](../../documents/build-deploy.md)), with entropy from
the SoC's TRNG.

The IDL is `proto/verdigris.proto` at the repo root. CMake copies it into
`src/` at configure time; those copies are build artifacts. Build and
flash with `lib/flash-firmware`. Protocol: [noise-security.md](../../documents/noise-security.md).
Toolchain: [build-deploy.md](../../documents/build-deploy.md).
