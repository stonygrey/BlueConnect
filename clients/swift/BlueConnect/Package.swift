// swift-tools-version: 6.0
// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import PackageDescription

// The Swift client library for Verdigris devices. `BlueConnect` holds the
// protocol code (segments, frames, the Noise link, Verdigris commands) and
// depends on nothing platform-specific, so its tests run under `swift test`
// on macOS. `BlueConnectCoreBluetooth` is the iOS and Mac Catalyst backend.
// Torann is pinned to a tag of https://github.com/stonygrey/torann, so
// building this package needs no sibling checkout.
let package = Package(
    name: "BlueConnect",
    platforms: [.iOS("26.0"), .macOS("26.0")],
    products: [
        .library(name: "BlueConnect", targets: ["BlueConnect"]),
        .library(name: "BlueConnectCoreBluetooth", targets: ["BlueConnectCoreBluetooth"]),
    ],
    dependencies: [
        .package(url: "https://github.com/stonygrey/torann.git", exact: "0.3.1"),
        .package(url: "https://github.com/apple/swift-protobuf.git", exact: "1.38.1"),
    ],
    targets: [
        .target(
            name: "BlueConnect",
            dependencies: [
                .product(name: "Torann", package: "torann"),
                .product(name: "SwiftProtobuf", package: "swift-protobuf"),
            ]),
        .target(name: "BlueConnectCoreBluetooth", dependencies: ["BlueConnect"]),
        .testTarget(
            name: "BlueConnectTests",
            dependencies: ["BlueConnect", .product(name: "Torann", package: "torann")]),
    ],
    swiftLanguageModes: [.v6]
)
