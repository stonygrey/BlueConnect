<!-- Copyright © 2026 Stony Grey LLC. SPDX-License-Identifier: MIT -->

# blueconnect

The Dart protocol library for Verdigris devices. It is not published;
`zaffre_flutter` depends on it by path. The protocol code depends on no
platform API, so `flutter test` runs its 35 tests without a device. The
one part that communicates with a radio is the backend under
`lib/src/ble`, which is written over the `universal_ble` plugin.

`lib/src/abstractions` holds `BleScanner`, `BlePeripheral`,
`Advertisement`, `BlueConnectOptions`, and the exceptions. A
`BlueConnectException` message is written for the app's toasts; a call
the current state does not allow throws Dart's own `StateError`.

`lib/src/core` holds `SegmentLink`, which carries one buffer over ATT
writes and notifications as one-byte-header segments
(`documents/ble-framing.md`), the `AdvertisementRegistry` that every
backend shares, `ReceiveQueue` behind `BlePeripheral.read`, `AsyncGate`
so that two concurrent writes never interleave their segments on the
air, `ConnectRetry`, and `IoLog`, which prints device I/O lines in the
format `lib/smalt` parses.

`lib/src/secure` holds `SecureLink`, the initiator of the Noise XX and
IK link over [Torann](https://pub.dev/packages/torann)
(`documents/noise-security.md`). It is the initiator half only: the
responder is the firmware, and the tests use Torann's responder. Every
Torann call crosses to the host over Pigeon, so the parsed protocols are
futures, cached after the first use. `noise_payloads.dart` parses the
`SGNK1` and `SGNE1` QR payloads. The library does not carry client
endorsements.

`lib/src/verdigris` holds `VerdigrisPeripheral`, the typed commands over
the secure link, one at a time, each request with a fresh id and its
response awaited under `BlueConnectOptions.commandTimeout`. The protobuf
code under `lib/src/generated` is written by `lib/gen-proto` from
`proto/verdigris.proto` and is checked in.

`test/pipes.dart` is the test double: a peripheral whose inbound ATT
values the test queues and whose outbound writes it records or scripts
to fail. Dart has no task cancellation, so the segment-link tests have
no caller-cancellation case; the timed read of `ReceiveQueue` covers
abandoning a wait instead.
