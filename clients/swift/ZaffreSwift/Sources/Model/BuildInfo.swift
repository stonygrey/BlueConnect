// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation

/// The version shown in the corner of the scan list, so that a phone or Mac
/// running the app shows which build it is running: `CFBundleShortVersionString`,
/// which project.yml ties to the target's `MARKETING_VERSION`.
enum BuildInfo {
    static let label = "v\(Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "?")"
}
