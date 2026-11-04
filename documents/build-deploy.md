<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# Build & deploy

This document describes the Nordic toolchain installation, the firmware
build, and the device tooling. The link design is in
[noise-security.md](noise-security.md). GATT segmentation is in
[ble-framing.md](ble-framing.md). The five clients have their build
steps in [clients.md](clients.md).

Versions in parentheses are the ones this repository was built and
verified with.

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
lib/device noise-ca-sign <pubkey hex>   # sign the MAUI client's Noise static; prints the compact blob
```

`lib/device self-test` runs a loopback validation of the Noise
handshake, pinning, and tamper rejection. It needs no hardware.

## License

See the [README](../README.md#license).
