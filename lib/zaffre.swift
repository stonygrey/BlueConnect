#!/bin/bash
# Copyright © 2026 Stony Grey LLC.
# SPDX-License-Identifier: MIT
#
# Build and launch the local Mac Catalyst build of the Swift client. No
# signing setting is passed: project.yml applies the team from
# clients/swift/ZaffreSwift/Signing.local.xcconfig (gitignored, copied from
# Signing.local.xcconfig.example) to every configuration, as the MAUI app
# takes its team from Directory.Build.local.props. The Xcode project is
# generated from project.yml when it is missing or older than project.yml.
# The app must be launched with `open`, not from the bundle binary, so that
# macOS attributes the Bluetooth and camera grants to the app.
set -euo pipefail
cd "$(dirname "$0")/../clients/swift/ZaffreSwift"

if [[ ! -f Signing.local.xcconfig ]]; then
    echo "clients/swift/ZaffreSwift/Signing.local.xcconfig is missing: copy Signing.local.xcconfig.example to it and set DEVELOPMENT_TEAM" >&2
    exit 1
fi
if [[ ! -f ZaffreSwift.xcodeproj/project.pbxproj || project.yml -nt ZaffreSwift.xcodeproj/project.pbxproj ]]; then
    xcodegen generate
fi
xcodebuild -project ZaffreSwift.xcodeproj -scheme ZaffreSwift \
    -destination 'platform=macOS,variant=Mac Catalyst' -configuration Debug -derivedDataPath build \
    -quiet build
open build/Build/Products/Debug-maccatalyst/ZaffreSwift.app
