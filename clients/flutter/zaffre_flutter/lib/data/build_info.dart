// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'package:package_info_plus/package_info_plus.dart';

/// The version shown in the corner of the scan list, so that a phone running
/// the app shows which build it is running: the `version` of pubspec.yaml, as
/// `package_info_plus` reads it back from the platform.
class BuildInfo {
  BuildInfo._();

  /// Resolved once per process; the scan list renders it when it arrives.
  static final Future<String> label = PackageInfo.fromPlatform().then((info) => 'v${info.version}');
}
