// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import SwiftUI

/// Navigation: the scan list is the root, and a device's pages
/// exist only once a device has been selected, with the system back button
/// returning to the list. Toasts overlay everything.
struct ContentView: View {
    @Environment(AppModel.self) private var model
    @State private var path: [String] = []

    var body: some View {
        NavigationStack(path: $path) {
            ScanView { device in
                model.selected = device
                path = [device.id]
            }
            .navigationDestination(for: String.self) { _ in
                DeviceView()
            }
        }
        .overlay(alignment: .bottom) {
            ToastOverlay()
        }
    }
}

/// The selected device's pages: the Noise trust tab, the Low and High
/// command tabs, and the Loop tab, under the device's name.
struct DeviceView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        TabView {
            Tab("Noise", systemImage: "lock.shield") {
                NoiseView()
            }
            Tab("Low", systemImage: "arrow.down.circle") {
                CommandsView(tier: .low)
            }
            Tab("High", systemImage: "arrow.up.circle") {
                CommandsView(tier: .high)
            }
            Tab("Loop", systemImage: "repeat") {
                LoopView()
            }
        }
        .navigationTitle(model.selected?.localName ?? "")
    }
}
