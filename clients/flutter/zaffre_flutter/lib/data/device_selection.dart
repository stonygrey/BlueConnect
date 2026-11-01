// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'package:blueconnect/blueconnect.dart';
import 'package:flutter/foundation.dart';

/// The device the user tapped on the scan list, shared by every screen.
final class DeviceSelection extends ValueNotifier<Advertisement?> {
  DeviceSelection() : super(null);

  /// Replaces the selection with a fresher advertisement of the same device.
  void refresh(List<Advertisement> advertisements) {
    final current = value;
    if (current == null) return;
    for (final advertisement in advertisements) {
      if (advertisement.id == current.id) {
        value = advertisement;
        return;
      }
    }
  }
}
