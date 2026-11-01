<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# zaffre_flutter

The Flutter client of the Verdigris device. It scans for the device,
pairs or provisions each privilege tier over the Noise link, runs the
serial number, LED 1, and Echo commands, and repeats connect, echo, and
disconnect on the Loop tab. The protocol code is the `blueconnect`
package next to this app; this app holds the screens, the stores, and
the command flow.

The object graph is built in `main.dart` and handed to the widget tree
through `provider`, in the layout Flutter's architecture guide gives.
`lib/data` holds the credential store (`flutter_secure_storage`: the
Keychain on iOS, `EncryptedSharedPreferences` on Android), the pairing
repository with one identity and pin per tier, the selected device, and
the build version that `package_info_plus` reads back for the corner of
the scan list. `lib/domain` holds the command runner, which opens one
connection per command and releases the peripheral afterwards, the soak
loop, and the toast texts. `lib/ui` holds the `ChangeNotifier` view
models and the screens: the scan list is the home page, and a device's
pages are pushed on top of it once a row is tapped. The QR page uses
the `mobile_scanner` plugin, which asks for the camera permission
itself.

The macOS target is not a supported configuration: `flutter_secure_storage`
needs the `keychain-access-groups` entitlement there, which the local
build does not carry.

```bash
flutter test integration_test/loopback_test.dart -d <device id>   # Torann on the device, no board
flutter test integration_test/hardware_flow_test.dart -d <device id>   # against the DK
flutter run -d <device id>
```

The loopback test runs the initiator against Torann's responder over an
in-memory pipe on the phone, where the Torann plugin is available. The
hardware test pairs both tiers against the board, runs the High and Low
commands, soaks three loop cycles, and unpairs; it needs the board
advertising, the Bluetooth permissions granted, and `lib/device confirm`
answering the pairing window. The iOS team is set in
`ios/Flutter/Signing.local.xcconfig`, copied from the `.example` next to
it. The full method, the permission grants, and the release build for
the iPhone are in `documents/native-clients.md`.
