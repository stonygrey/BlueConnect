<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# BlueConnect / Zaffre

This repository is a workbench for BLE connectivity and link authentication:
a host library, a MAUI test app, and three firmware images that
speak the same command protocol (Verdigris) using three different ways
of establishing trust.

This code is not production ready. It has not been independently audited. Development used Fable 5 and Grok 4.6.

**Zaffre** (`Zaffre/`) is the app (iOS, Android, Mac Catalyst, Windows).
It scans, connects, runs a command, disconnects. The trust tab (Noise,
PAKE, or EDHOC — whichever the device advertises) pairs or provisions
each privilege tier. The Low and High tabs
drive the board's serial number (read / write), LED 1 (tap the LED to
read it, tap again to toggle it) and Echo; the device enforces the tier,
so on Low the writes come back `PERMISSION_DENIED`. Command responses and
errors arrive as toasts. The Loop tab soaks connect → echo → disconnect.
The host libraries are under `StonyGrey/`. Flash one of three images
onto a Nordic DK (the nRF54L15-DK by default); they share GATT,
segmentation, and the nanopb dispatcher (`firmware/common/`) and differ
only in how a session is authenticated:

- **Noise** (`firmware/noise/`): `Noise_XX_25519_AESGCM_SHA256` for the
  ceremony, `Noise_IK` once both keys are pinned. `Pair` opens a 30 s
  window; Button 2 / LED 2 is low privilege, Button 3 / LED 3 is high
  (nRF54L15-DK silkscreen). Each side pins the other's X25519 public
  key; a tier can instead be provisioned over USB (`lib/device
  noise-provision`), which locks it against re-pairing. A client in no
  slot is served exactly one command: `Pair`.
- **PAKE** (`firmware/pake/`): SPAKE2+ (RFC 9383, P256-SHA256) from a
  printed setup code per tier. The device stores only the verifier
  `w0 ‖ L`, written over USB by `lib/device pake-provision`. Knowing the
  code is the pairing. There is no Confirm button.
- **EDHOC** (`firmware/edhoc/`): RFC 9528 method 3 (static-static DH),
  cipher suite 2 (P-256, SHA-256, AES-CCM-16-64-128), credentials as a
  CCS with a COSE `kid`. Same hybrid lifecycle as Noise — a button
  ceremony enrolls a tier over the air, or `lib/device edhoc-provision`
  installs and locks it over USB — with P-256 key material instead of
  X25519.

| Find | Pair | Run |
|:----:|:-----:|:----:|
| ![Find](documents/scan.png) | ![Pair](documents/noise.png) | ![Run](documents/high.png) |

The IDL is `proto/verdigris.proto`. C# (protoc) and C (nanopb) are both
generated at build. The Noise handshake on the host is
[Torann](https://www.nuget.org/packages/Torann) (NuGet); SPAKE2+ and
EDHOC are the in-repo `StonyGrey/Spake2Plus` and `StonyGrey/Edhoc`
libraries.

| Project | What it is |
|---|---|
| `StonyGrey/BlueConnect/Abstractions` | `IScanner`, `IPeripheral`, `IAdvertisement` |
| `StonyGrey/BlueConnect/Core` | `ScannerBase`, `PeripheralBase`, `SegmentLink` |
| `StonyGrey/BlueConnect/Secure` | `SecureLink` (Noise XX/IK), `PakeLink` (SPAKE2+), `EdhocLink` (EDHOC) |
| `StonyGrey/Spake2Plus` | RFC 9383 P256-SHA256 on BouncyCastle |
| `StonyGrey/Edhoc` | RFC 9528 method 3, cipher suite 2; conforms to the RFC 9529 trace-2 vectors |
| `StonyGrey/BlueConnect/CoreBluetooth` | iOS and Mac Catalyst |
| `StonyGrey/BlueConnect/Droid` | Android |
| `StonyGrey/BlueConnect/WinRT` | Windows (`EnableWindowsTargeting` so it compiles on macOS) |
| `StonyGrey/BlueConnect/Verdigris` | `IVerdigrisPeripheral` — typed commands over the secure link |

## Run it

Toolchain and platform steps: [documents/build-deploy.md](documents/build-deploy.md).
In short, after .NET 10 / MAUI, nRF Connect SDK v3.4.0, and J-Link are
installed:

```bash
# firmware (default board nrf54l15dk/nrf54l15/cpuapp; BOARD=nrf52840dk/nrf52840 for the older kit)
lib/flash-firmware            # Noise image
# lib/flash-firmware pake     # SPAKE2+ image; then lib/device pake-provision …
# lib/flash-firmware edhoc    # EDHOC image

# iPhone — copy Zaffre/Directory.Build.local.props.example
#          → Directory.Build.local.props and fill in your team
#          device id: xcrun devicectl list devices
dotnet build Zaffre/Zaffre.csproj -f net10.0-ios -t:Rebuild -p:RuntimeIdentifier=ios-arm64
xcrun devicectl device install app --device <DEVICE-ID> Zaffre/bin/Debug/net10.0-ios/ios-arm64/Zaffre.app
xcrun devicectl device process launch --device <DEVICE-ID> <ApplicationId>

# Android
dotnet build Zaffre/Zaffre.csproj -f net10.0-android -t:Install \
  "-p:JavaSdkDirectory=/Applications/Android Studio.app/Contents/jbr/Contents/Home" \
  "-p:AndroidSdkDirectory=$HOME/Library/Android/sdk"

# Windows
dotnet build Zaffre/Zaffre.csproj -f net10.0-windows10.0.19041.0

# Mac Catalyst — launch with `open`, not the bundle binary (TCC). Use -t:Rebuild
# after library changes: incremental app builds have shipped stale DLLs in the bundle.
dotnet build Zaffre/Zaffre.csproj -f net10.0-maccatalyst -t:Rebuild
open Zaffre/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/Zaffre.app
```

Zaffre logs device I/O as `_BLE_IO` lines (MEL,
`Log - yyyyMMdd.log`; `ZAFFRE-LOG-DIR:` on stdout). `lib/smalt <file.log>`
turns that file into HTML.

## Documents

- [Build & deploy](documents/build-deploy.md) — SDK, Zaffre builds,
  `lib/flash-firmware`, `lib/device`
- [Noise link security](documents/noise-security.md) — Noise XX, pinning,
  the pairing ceremony
- [PAKE link security](documents/pake-security.md) — SPAKE2+, setup codes,
  verifiers
- [EDHOC link security](documents/edhoc-security.md) — RFC 9528, CCS
  credentials, ceremony vs USB provisioning
- [GATT framing](documents/ble-framing.md) — one-byte segment header,
  notify peripheral→central, fail closed on a gap

## License

MIT — see [LICENSE](LICENSE). Exception: `firmware/common/src/app_main.c`
(the shared peripheral_uart chassis) and the `Kconfig` and `prj.conf`
under `firmware/noise/`, `firmware/pake/`, and `firmware/edhoc/` are
Nordic's peripheral_uart sample,
[LicenseRef-Nordic-5-Clause](LICENSES/LicenseRef-Nordic-5-Clause.txt).
Those files may only be used with a Nordic Semiconductor ASA chip.
