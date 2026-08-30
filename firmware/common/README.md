<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# firmware/common — the shared Verdigris service

These files compile into both firmware apps. They do not know which
transport security the image uses.

| File | Concern |
|------|---------|
| `src/segment.c/.h` | GATT segmentation ([ble-framing.md](../../documents/ble-framing.md)). Payload-blind. Cap is `SEG_MAX_MESSAGE` (2304), kept in sync with `VerdigrisPeripheral.MaxFrame`. |
| `src/verdigris.c/.h` | The product GATT UUIDs, the nanopb dispatcher, and the per-command minimum-privilege table. |
| `src/link_iface.h` | What a link implementation must provide: frame-type constants, privilege tiers, `link_handshake` / `encrypt` / `decrypt` / `privilege` / `pair` / `unpair_slot` / `reset`. |

Each image links exactly one implementation of that header:
`firmware/noise/src/link.c` (`0x01` handshake frames) or
`firmware/pake/src/pake.c` (`0x03` frames). The dispatcher forwards both
handshake types to `link_handshake()`. An implementation answers only its
own dialect and drops the other, so pointing the wrong Zaffre tab at an
image shows up as a timeout, not a misparse.

Not here: `main.c` (the Nordic-licensed peripheral_uart fork, advertising),
the ceremony / provisioning UI, or the link implementations. The proto is
copied from the repo root into each app's `src/` at configure time.
