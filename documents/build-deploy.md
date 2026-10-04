<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# Build & deploy

This document is the environment setup, the Zaffre build commands, and
the firmware / device tooling. Link design is
[noise-security.md](noise-security.md) and [pake-security.md](pake-security.md).
GATT segmentation is [ble-framing.md](ble-framing.md).

Versions in parentheses are what this repo was built and verified with.

## .NET + MAUI

Install the .NET SDK (10.0.301) from
<https://dotnet.microsoft.com/download>, then:

```bash
dotnet workload install maui ios
```

On an Apple silicon Mac the build also needs Rosetta 2. The Verdigris
project compiles `proto/verdigris.proto` with the `protoc` that ships in
the Grpc.Tools package, and that package carries only an Intel (x64)
`protoc` for macOS. Rosetta is not part of a default macOS install (it
was absent on the development Mac after the upgrade to macOS 27);
without it the build stops with `Bad CPU type in executable` from
`Google.Protobuf.Tools.targets`. Install it once:

```bash
softwareupdate --install-rosetta --agree-to-license
```

## Android

Install Android Studio. It provides the SDK manager and a bundled JDK;
no separate JDK install is needed. SDK location:
`~/Library/Android/sdk`.

The Android platform the .NET SDK targets is API 36
(`net10.0-android`). The build can fetch it, including license
acceptance:

```bash
dotnet build StonyGrey/BlueConnect/Droid/BlueConnect.Droid.csproj \
  -t:InstallAndroidDependencies -f net10.0-android \
  "-p:AndroidSdkDirectory=$HOME/Library/Android/sdk" \
  -p:AcceptAndroidSDKLicenses=true
```

Every Android build needs the SDK and JDK paths passed explicitly (the
JDK is Android Studio's bundled JBR):

```bash
-p:JavaSdkDirectory="/Applications/Android Studio.app/Contents/jbr/Contents/Home"
-p:AndroidSdkDirectory="$HOME/Library/Android/sdk"
```

On the phone: enable Developer Options + USB debugging, plug in, accept
the RSA prompt. BLE runtime permissions can be granted without touching
the UI:

```bash
adb shell pm grant <ApplicationId> android.permission.BLUETOOTH_SCAN
adb shell pm grant <ApplicationId> android.permission.BLUETOOTH_CONNECT
```

`<ApplicationId>` is the value in `Zaffre.csproj`, or the override in
local props.

## Apple signing

Team IDs and certificate names are not in the repo. Copy the example
and fill in your Apple Developer account:

```bash
cp Zaffre/Directory.Build.local.props.example Zaffre/Directory.Build.local.props
```

Edit `DevelopmentTeam` (Membership details at
<https://developer.apple.com/account>). Optionally pin `CodesignKey`
if the keychain has more than one Apple Development identity. A stable
identity keeps the macOS Bluetooth TCC grant across rebuilds; ad-hoc
signing re-prompts every build.

Android, Windows, and the iOS simulator do not need this file.

iOS device builds need a provisioning profile for the bundle id in
`ApplicationId`. Automatic signing can create it: build any signable
Xcode stub with `PRODUCT_BUNDLE_IDENTIFIER` matching that id and
`DEVELOPMENT_TEAM` matching your team — Xcode registers the App ID and
downloads the profile.

The bundle id must belong to the team that signs it. Forks set
`ApplicationId` in the local props to a reverse-DNS name they control.

Command-line `-p:DevelopmentTeam=` / `-p:CodesignKey=` still override
the local file.

## Nordic firmware toolchain

**SEGGER J-Link** — install the "J-Link Software and Documentation Pack"
(macOS Universal .pkg) from <https://www.segger.com/downloads/jlink/>.
This is the one manual/EULA download; everything else is scriptable.
Flash over the kit's onboard J-Link USB.

**nrfutil** — Nordic's CLI multiplexer (single static binary):

```bash
curl -L https://files.nordicsemi.com/artifactory/swtools/external/nrfutil/executables/aarch64-apple-darwin/nrfutil -o ~/bin/nrfutil
chmod +x ~/bin/nrfutil
nrfutil install sdk-manager device
```

**nRF Connect SDK** (v3.4.0, installs toolchain + SDK to
`/opt/nordic/ncs`; multi-GB download):

```bash
sudo mkdir -p /opt/nordic && sudo chown "$(whoami)" /opt/nordic
nrfutil sdk-manager search
nrfutil sdk-manager install v3.4.0
```

The same `lib/` scripts run from WSL (Ubuntu). Install the Linux nrfutil
and keep the SDK on the WSL filesystem — no sudo needed; the scripts
auto-detect `~/ncs` when `/opt/nordic/ncs` is absent (override with
`NCS_ROOT`):

```bash
# inside WSL
mkdir -p ~/bin
curl -L https://files.nordicsemi.com/artifactory/swtools/external/nrfutil/executables/x86_64-unknown-linux-gnu/nrfutil -o ~/bin/nrfutil
chmod +x ~/bin/nrfutil
nrfutil install sdk-manager device
nrfutil sdk-manager config install-dir set ~/ncs
nrfutil sdk-manager install v3.4.0

# then, from the repo
lib/build-firmware pake           # or from PowerShell: wsl lib/build-firmware pake
```

Building from `/mnt/*` works but the 9P filesystem makes it slower than
a native checkout. Flashing from WSL additionally needs the DK's J-Link
attached into WSL with `usbipd` (`usbipd attach --busid <id> --wsl`) and
the Linux J-Link tools. If that fights you, flash from Windows: copy
`firmware/<app>/build/<app>/zephyr/zephyr.hex` onto the DK's `JLINK`
mass-storage drive (a failed program leaves `FAIL.TXT` on the drive).
MSD programming chip-erases, wiping the settings partition — Noise pins
and PAKE verifiers must be re-provisioned after every MSD reflash
(`west flash` does a sector erase and preserves them).

Three apps expose the same Verdigris service and differ only in
transport security: `noise` (Noise XX, the default), `pake` (SPAKE2+
from a printed setup code), and `edhoc` (RFC 9528). Noise and EDHOC
each accept both a button-ceremony pairing and locked USB provisioning;
PAKE is code-only:

```bash
lib/flash-firmware                    # noise: incremental build + flash + reset
lib/flash-firmware --pristine noise   # after Kconfig/prj.conf changes
lib/flash-firmware pake               # the SPAKE2+ app
lib/flash-firmware edhoc              # the EDHOC app
```

The scripts target the nRF54L15-DK (`nrf54l15dk/nrf54l15/cpuapp`) by
default; set `BOARD=nrf52840dk/nrf52840` for the nRF52840-DK. A build
directory remembers its board — pass `--pristine` when switching kits.
On the nRF54L15-DK the shell is on the second of the kit's two VCOM
ports; `lib/device` probes for it automatically.

Button and LED numbers in the app and these documents follow the
nRF54L15-DK silkscreen (Button 0-3, LED0-3): Button 2 confirms a
low-privilege pairing, Button 3 a high one, and holding Buttons 2+3
for 5 s is the factory wipe (LED2+LED3 salute). The nRF52840-DK
prints the same physical controls one higher (Buttons 1-4, LED1-4),
so add one to each number on that kit.

All three images do their cryptography through the PSA Crypto API
(Arm's specification; `#include <psa/crypto.h>`) as shipped in Nordic's
nrf_security: key agreement, AEAD, hashing, PAKE, and signature
verification are all `psa_*` calls. Which driver answers is a
build-time property of the board — the nRF54L15 routes to its CRACEN
hardware engine, the nRF52840 to the Oberon software driver with the
CryptoCell TRNG. The firmware never names a driver and neither do the
other documents: "PSA" there means whichever driver the board selected.
The one exception is `ecdh_x` in the EDHOC image, which calls the
`ocrypto` P-256 point decompression directly because PSA imports only
uncompressed points; that library is linked explicitly
(`CONFIG_NRF_OBERON`, `nrfxlib_crypto`) so the call exists on every
board.

nanopb ships inside the nRF Connect SDK as a Zephyr module — no
separate install. Layout:
[firmware/noise/README.md](../firmware/noise/README.md),
[firmware/pake/README.md](../firmware/pake/README.md),
`firmware/common/`.

The pake app then needs verifiers before a phone can connect, over the
same J-Link USB serial that `lib/device` already uses:

```bash
lib/device pake-provision low --out low.svg
lib/device pake-provision high --out high.svg
lib/device info
```

Scan the QR (or type the code) on Zaffre's PAKE tab.

## Building and running Zaffre

```bash
# iPhone (find the device id with: xcrun devicectl list devices).
# Signing comes from Zaffre/Directory.Build.local.props.
dotnet build Zaffre/Zaffre.csproj -f net10.0-ios -t:Rebuild -p:RuntimeIdentifier=ios-arm64
xcrun devicectl device install app --device <DEVICE-ID> Zaffre/bin/Debug/net10.0-ios/ios-arm64/Zaffre.app
xcrun devicectl device process launch --device <DEVICE-ID> <ApplicationId>

# On a Mac whose Xcode is newer than the installed .NET for iOS workload
# expects (e.g. Xcode 27.0 with workload 26.5, which wants Xcode 26.6), add
# -p:ValidateXcodeVersion=false to the iOS build until the workload catches up.
# A phone added after the provisioning profile was created needs the profile
# regenerated: rebuild the Xcode stub (see Apple signing) with
# -destination 'id=<DEVICE-UDID>' -allowProvisioningUpdates.

# iOS simulator (no Bluetooth — a simulated scanner/peripheral pair is
# auto-registered on virtual devices so the full UI flow still works)
dotnet build Zaffre/Zaffre.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64

# Android (attached phone)
dotnet build Zaffre/Zaffre.csproj -f net10.0-android -t:Install \
  "-p:JavaSdkDirectory=/Applications/Android Studio.app/Contents/jbr/Contents/Home" \
  "-p:AndroidSdkDirectory=$HOME/Library/Android/sdk"

# Windows (on a Windows machine; the TFM activates automatically there)
dotnet build Zaffre/Zaffre.csproj -f net10.0-windows10.0.19041.0

# Mac Catalyst — MUST be launched via `open`: launching the bundle binary from
# a shell makes the shell the TCC-responsible process and the app crashes with
# a Bluetooth privacy violation despite the correct usage description.
# Local Catalyst is unsandboxed (no Mac Catalyst provisioning profile).
dotnet build Zaffre/Zaffre.csproj -f net10.0-maccatalyst
open Zaffre/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/Zaffre.app
```

DEBUG builds log `ZAFFRE-SCAN:` / `ZAFFRE-TEST:` diagnostics to stdout.
On Catalyst launch with `open Zaffre.app --stdout <log> --stderr <log>`
and tail the files.

Configuration (timeouts, cycle delays, echo payload size, log levels)
lives in `Zaffre/appsettings.json`, embedded at build.

## Device tooling

`lib/device info` shows the board's identity, pairing state, and
session state; `clear` / `confirm` / `wipe` are the other possession
commands. The `vg` shell verbs are the same on all three images, so
these work against whichever firmware is flashed. Provisioning is
`lib/device pake-provision`, `noise-provision`, or `edhoc-provision` —
each locks its slot against ceremonies and prints the credential QR.

Optional Noise client endorsement (does not grant privilege; see
[noise-security.md](noise-security.md)):

```bash
lib/device noise-ca-gen                 # once; writes lib/noise-ca.key (gitignored) + lib/noise-ca.pub
lib/device noise-ca-root                # USB-write the root into Noise firmware (vg ca-root)
lib/device noise-ca-sign <pubkey hex>   # sign Zaffre's existing Noise static; prints the compact blob
```

Zaffre writes MetroLog rolling files (`Log - yyyyMMdd.log`;
`ZAFFRE-LOG-DIR:` on stdout — on Mac,
`~/Library/Application Support/Zaffre/logs`). Device reads and writes
are tagged `_BLE_IO` at message and segment granularity with hex
payloads. `lib/smalt <logfile>` rewrites a log as HTML.

`lib/device self-test` runs a loopback validation of the Noise
handshake, pinning, and tamper rejection. `lib/device pake-self-test`
does the same for SPAKE2+. Neither needs hardware.

## License

See the [README](../README.md#license).
