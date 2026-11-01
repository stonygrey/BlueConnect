// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

/// The Dart client library for Verdigris devices: segments, frames, the Noise
/// link over Torann, Verdigris commands, and the universal_ble backend. The
/// protocol code depends on no platform API; the backend is the only part
/// that communicates with a radio.
library;

export 'src/abstractions/advertisement.dart';
export 'src/abstractions/ble_peripheral.dart';
export 'src/abstractions/ble_scanner.dart';
export 'src/abstractions/blueconnect_options.dart';
export 'src/abstractions/exceptions.dart';
export 'src/ble/universal_ble_peripheral.dart';
export 'src/ble/universal_ble_scanner.dart';
export 'src/core/advertisement_registry.dart';
export 'src/core/async_gate.dart';
export 'src/core/connect_retry.dart';
export 'src/core/io_log.dart';
export 'src/core/receive_queue.dart';
export 'src/core/segment_header.dart';
export 'src/core/segment_link.dart';
export 'src/generated/verdigris.pb.dart';
export 'src/generated/verdigris.pbenum.dart';
export 'src/secure/frame_type.dart';
export 'src/secure/link_protocol.dart';
export 'src/secure/noise_payloads.dart';
export 'src/secure/secure_link.dart';
export 'src/verdigris/verdigris_peripheral.dart';
