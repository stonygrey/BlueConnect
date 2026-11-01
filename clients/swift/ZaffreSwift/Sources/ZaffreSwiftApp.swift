// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import SwiftUI

/// The Swift client of the Verdigris device: Noise XX/IK link security over
/// BLE through the `BlueConnect` package and Torann. One `AppModel` is
/// created here and reaches every view through the environment.
@main
struct ZaffreSwiftApp: App {
    @State private var model = AppModel()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environment(model)
                .task { model.start() }
        }
    }
}
