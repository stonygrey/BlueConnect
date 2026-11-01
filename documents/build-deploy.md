<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# Build & deploy

This document describes the toolchain installation, the Zaffre build
commands, and the firmware and device tooling. The link design is in
[noise-security.md](noise-security.md). GATT segmentation is in
[ble-framing.md](ble-framing.md). The four native apps have their own
build steps in [native-clients.md](native-clients.md).

Versions in parentheses are the ones this repository was built and
verified with.

## .NET and MAUI

Install the .NET SDK (10.0.301) from
<https://dotnet.microsoft.com/download>, then:

```bash
dotnet workload install maui ios
```

On an Apple silicon Mac the build also needs Rosetta 2. The Verdigris
project compiles `proto/verdigris.proto` with the `protoc` that ships in
the Grpc.Tools package, and that package carries only an Intel (x64)
`protoc` for macOS. Rosetta is not part of a default macOS install (it
was absent on the development Mac after the upgrade to macOS 27), and
without it the build stops with `Bad CPU type in executable` from
`Google.Protobuf.Tools.targets`. Install it once:

```bash
softwareupdate --install-rosetta --agree-to-license
```

## Android

Install Android Studio. It provides the SDK manager and a bundled JDK,
so no separate JDK install is needed. The SDK is at
`~/Library/Android/sdk`.

The Android platform the .NET SDK targets is API 36
(`net10.0-android`). The build can fetch it, including the license
acceptance:

```bash
dotnet build clients/maui/StonyGrey/BlueConnect/Droid/BlueConnect.Droid.csproj \
  -t:InstallAndroidDependencies -f net10.0-android \
  "-p:AndroidSdkDirectory=$HOME/Library/Android/sdk" \
  -p:AcceptAndroidSDKLicenses=true
```

Every Android build needs the SDK and JDK paths passed explicitly. The
JDK is Android Studio's bundled JBR:

```bash
-p:JavaSdkDirectory="/Applications/Android Studio.app/Contents/jbr/Contents/Home"
-p:AndroidSdkDirectory="$HOME/Library/Android/sdk"
```

On the phone, enable Developer Options and USB debugging, plug it in,
and accept the RSA prompt. The BLE runtime permissions can be granted
from the shell instead of through the permission dialogs:

```bash
adb shell pm grant <ApplicationId> android.permission.BLUETOOTH_SCAN
adb shell pm grant <ApplicationId> android.permission.BLUETOOTH_CONNECT
```

`<ApplicationId>` is the value in `Zaffre.csproj`, or the override in
the local props file.

## Apple signing

Team IDs and certificate names are not in the repository. Copy the
example and fill in the Apple Developer account:

```bash
cp clients/maui/Zaffre/Directory.Build.local.props.example clients/maui/Zaffre/Directory.Build.local.props
```

Edit `DevelopmentTeam` (the Membership details page at
<https://developer.apple.com/account>). `CodesignKey` may also be set
when the keychain has more than one Apple Development identity. A
stable identity keeps the macOS Bluetooth TCC grant across rebuilds;
ad-hoc signing prompts for it on every build.

Android, Windows, and the iOS simulator do not need this file.

iOS device builds need a provisioning profile for the bundle id in
`ApplicationId`. Automatic signing can create it: build any signable
Xcode stub with `PRODUCT_BUNDLE_IDENTIFIER` matching that id and
`DEVELOPMENT_TEAM` matching the team, and Xcode registers the App ID
and downloads the profile.

The bundle id must belong to the team that signs it. Forks set
`ApplicationId` in the local props file to a reverse-DNS name they
control.

`-p:DevelopmentTeam=` and `-p:CodesignKey=` on the command line
override the local file.

## Nordic firmware toolchain

SEGGER J-Link is installed from the "J-Link Software and Documentation
Pack" (the macOS Universal .pkg) at
<https://www.segger.com/downloads/jlink/>. This is the one manual
download with a EULA; everything else is scripted. Flashing goes over
the kit's onboard J-Link USB.

nrfutil is Nordic's command-line multiplexer, a single static binary:

```bash
curl -L https://files.nordicsemi.com/artifactory/swtools/external/nrfutil/executables/aarch64-apple-darwin/nrfutil -o ~/bin/nrfutil
chmod +x ~/bin/nrfutil
nrfutil install sdk-manager device
```

The nRF Connect SDK (v3.4.0) installs the toolchain and the SDK to
`/opt/nordic/ncs`. The download is several gigabytes:

```bash
sudo mkdir -p /opt/nordic && sudo chown "$(whoami)" /opt/nordic
nrfutil sdk-manager search
nrfutil sdk-manager install v3.4.0
```

The same `lib/` scripts run from WSL (Ubuntu). Install the Linux nrfutil
and keep the SDK on the WSL filesystem, where no sudo is needed. The
scripts use `~/ncs` when `/opt/nordic/ncs` is absent, and `NCS_ROOT`
overrides both:

```bash
# inside WSL
mkdir -p ~/bin
curl -L https://files.nordicsemi.com/artifactory/swtools/external/nrfutil/executables/x86_64-unknown-linux-gnu/nrfutil -o ~/bin/nrfutil
chmod +x ~/bin/nrfutil
nrfutil install sdk-manager device
nrfutil sdk-manager config install-dir set ~/ncs
nrfutil sdk-manager install v3.4.0

# then, from the repo
lib/build-firmware                # or from PowerShell: wsl lib/build-firmware
```

Building from `/mnt/*` works, but the 9P filesystem makes it slower than
a native checkout. Flashing from WSL also needs the DK's J-Link
attached into WSL with `usbipd` (`usbipd attach --busid <id> --wsl`) and
the Linux J-Link tools. When that attach fails, flash from Windows
instead: copy `firmware/noise/build/noise/zephyr/zephyr.hex` onto the
DK's `JLINK` mass-storage drive (a failed program leaves `FAIL.TXT` on
the drive). MSD programming chip-erases the part and so wipes the
settings partition; the pins must be re-provisioned after every MSD
reflash. `west flash` does a sector erase and preserves them.

The image supports both the button ceremony and locked USB
provisioning. `lib/flash-firmware` builds and flashes it:

```bash
lib/flash-firmware                    # incremental build + flash + reset
lib/flash-firmware --pristine         # after Kconfig/prj.conf changes
```

The scripts target the [nRF54L15-DK](https://www.digikey.com/en/products/detail/nordic-semiconductor-asa/NRF54L15-DK/25601516) (`nrf54l15dk/nrf54l15/cpuapp`) by
default; `BOARD=nrf52840dk/nrf52840` selects the nRF52840-DK. A build
directory keeps the board it was configured for, so pass `--pristine`
when switching kits. On the nRF54L15-DK the shell is on the second of
the kit's two VCOM ports, and `lib/device` probes both to find it.

Button and LED numbers in the app and in these documents follow the
nRF54L15-DK silkscreen (Button 0-3, LED0-3): Button 2 confirms a
low-privilege pairing, Button 3 a high one, and holding Buttons 2 and 3
together for 5 s is the factory wipe, which the firmware signals by
flashing LED2 and LED3 together three times. The nRF52840-DK prints the
same physical controls one higher (Buttons 1-4, LED1-4), so add one to
each number on that kit.

The firmware does its cryptography through the PSA Crypto API
(Arm's specification, `#include <psa/crypto.h>`) as shipped in Nordic's
nrf_security: key agreement, AEAD, hashing, and signature verification
are all `psa_*` calls. Which driver runs is decided at build time by the
board: the nRF54L15 build uses its CRACEN hardware engine, the nRF52840
build the Oberon software driver with the CryptoCell TRNG. The firmware
never names a driver and neither do the other documents; "PSA" there
means whichever driver the board selected.

nanopb ships inside the nRF Connect SDK as a Zephyr module, so it needs
no separate install. The firmware layout is described in
[firmware/noise/README.md](../firmware/noise/README.md) and
`firmware/common/`.

## Building and running Zaffre

```bash
# iPhone (find the device id with: xcrun devicectl list devices).
# Signing comes from clients/maui/Zaffre/Directory.Build.local.props.
dotnet build clients/maui/Zaffre/Zaffre.csproj -f net10.0-ios -t:Rebuild -p:RuntimeIdentifier=ios-arm64
xcrun devicectl device install app --device <DEVICE-ID> clients/maui/Zaffre/bin/Debug/net10.0-ios/ios-arm64/Zaffre.app
xcrun devicectl device process launch --device <DEVICE-ID> <ApplicationId>

# On a Mac whose Xcode is newer than the installed .NET for iOS workload
# expects (e.g. Xcode 27.0 with workload 26.5, which requires Xcode 26.6), add
# -p:ValidateXcodeVersion=false to the iOS and Mac Catalyst builds until the
# workload catches up.
# A phone added after the provisioning profile was created needs the profile
# regenerated: rebuild the Xcode stub (see Apple signing) with
# -destination 'id=<DEVICE-UDID>' -allowProvisioningUpdates.

# iOS simulator. There is no Bluetooth on the simulator; a simulated scanner
# and peripheral pair is registered on virtual devices so the UI flow runs.
dotnet build clients/maui/Zaffre/Zaffre.csproj -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64

# Android (attached phone)
dotnet build clients/maui/Zaffre/Zaffre.csproj -f net10.0-android -t:Install \
  "-p:JavaSdkDirectory=/Applications/Android Studio.app/Contents/jbr/Contents/Home" \
  "-p:AndroidSdkDirectory=$HOME/Library/Android/sdk"

# Windows (on a Windows machine; the TFM activates automatically there)
dotnet build clients/maui/Zaffre/Zaffre.csproj -f net10.0-windows10.0.26100.0

# Mac Catalyst: launch with `open`. Launching the bundle binary from a shell
# makes the shell the TCC-responsible process, and the app then crashes with a
# Bluetooth privacy violation despite the correct usage description.
# Local Catalyst is unsandboxed (no Mac Catalyst provisioning profile).
dotnet build clients/maui/Zaffre/Zaffre.csproj -f net10.0-maccatalyst
open clients/maui/Zaffre/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/Zaffre.app
```

DEBUG builds log `ZAFFRE-SCAN:` and `ZAFFRE-TEST:` diagnostics to
stdout. On Catalyst, launch with `open Zaffre.app --stdout <log>
--stderr <log>` and tail the files.

The configuration (timeouts, cycle delays, echo payload size, log
levels) is `clients/maui/Zaffre/appsettings.json`, embedded at build.

## Device tooling

`lib/device info` shows the board's identity, pairing state, and
session state. `clear`, `confirm`, and `wipe` are the other possession
commands: they run over the USB shell, so they need the kit in hand.
Provisioning is `lib/device noise-provision low|high`, which locks the
slot against ceremonies and prints the credential QR.
`lib/device noise-provision enroll --out kit.svg` instead writes an
enrollment key and prints the kit QR. A phone that scans it enrolls its
own key into the low slot, one phone at a time; the Enrollment section
of [noise-security.md](noise-security.md) has the detail.

The Noise client endorsement is optional and grants no privilege (see
[noise-security.md](noise-security.md)):

```bash
lib/device noise-ca-gen                 # once; writes lib/noise-ca.key (gitignored) + lib/noise-ca.pub
lib/device noise-ca-root                # USB-write the root into Noise firmware (vg ca-root)
lib/device noise-ca-sign <pubkey hex>   # sign Zaffre's existing Noise static; prints the compact blob
```

Zaffre writes MetroLog rolling files (`Log - yyyyMMdd.log`) and prints
the folder on stdout as `ZAFFRE-LOG-DIR:`; on the Mac it is
`~/Library/Application Support/Zaffre/logs`. Device reads and writes
are tagged `_BLE_IO` at message and segment granularity, with hex
payloads. `lib/smalt <logfile>` rewrites a log as HTML.

`lib/device self-test` runs a loopback validation of the Noise
handshake, pinning, and tamper rejection. It needs no hardware.

## License

See the [README](../README.md#license).
