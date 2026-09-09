<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# firmware/common — the shared Verdigris service

These files compile into all three firmware apps (noise, pake, edhoc).
They do not know which transport security the image uses.

| File | Concern |
|------|---------|
| `src/segment.c/.h` | GATT segmentation ([ble-framing.md](../../documents/ble-framing.md)). Payload-blind. Cap is `SEG_MAX_MESSAGE` (2304), kept in sync with `VerdigrisPeripheral.MaxFrame`. |
| `src/verdigris.c/.h` | The product GATT UUIDs, the nanopb dispatcher, and the per-command minimum-privilege table. |
| `src/link_iface.h` | What a link implementation must provide: frame-type constants, privilege tiers, `link_handshake` / `encrypt` / `decrypt` / `privilege` / `pair` / `unpair_slot` / `reset`. |
| `src/app_main.c` | The Nordic peripheral_uart chassis (`LicenseRef-Nordic-5-Clause`): advertising with the status byte, the NUS UART. Shared by all three apps; each app's `src/main.c` is a small init shim over it. |

Each image links exactly one implementation of that header:
`firmware/noise/src/link.c` (`0x01` handshake frames),
`firmware/pake/src/pake.c` (`0x03`), or `firmware/edhoc/src/edhoc.c`
(`0x04`). The dispatcher forwards every handshake type to
`link_handshake()`. An implementation answers only its own dialect and
drops the others (logging `Frame type 0x.. is not this link's handshake
dialect`), so pointing the wrong Zaffre tab at an image shows up as a
timeout, not a misparse.

Not here: the ceremony / provisioning UI (`pairing_ui.c`, `prov_ui.c`,
`cred_ui.c`) or the link implementations. The proto is copied from the
repo root into each app's `src/` at configure time.
