<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# firmware/common: the shared Verdigris service

These files compile into the firmware image under `firmware/noise/`.
They do not depend on the link implementation.

| File | Concern |
|------|---------|
| `src/segment.c/.h` | GATT segmentation ([ble-framing.md](../../documents/ble-framing.md)). Payload-blind. The cap `SEG_MAX_MESSAGE` (2304) is an arbitrary implementation value that is not negotiated, so it must equal `VerdigrisPeripheral.MaxFrame` on the host. |
| `src/verdigris.c/.h` | The product GATT UUIDs, the nanopb dispatcher, and the per-command minimum-privilege table. |
| `src/link_iface.h` | What the link implementation must provide: frame-type constants, privilege tiers, `link_handshake` / `encrypt` / `decrypt` / `privilege` / `pair` / `unpair_slot` / `reset`. |
| `src/app_main.c` | The Nordic peripheral_uart chassis (`LicenseRef-Nordic-5-Clause`): advertising with the status byte, the NUS UART. `firmware/noise/src/main.c` is a small init shim over it. |

`firmware/noise/src/link.c` implements that header. The dispatcher
forwards the body of a `0x01` frame to `link_handshake()` and the body
of a `0x02` frame to `link_decrypt()`; a frame with any other type byte
is dropped and logged as `Unknown frame type`.

The ceremony and provisioning UI (`pairing_ui.c`) and the link
implementation are not here; both are under `firmware/noise/src/`. The
proto is copied from the repo root into the app's `src/` at configure
time.
