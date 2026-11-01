// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import SwiftUI

/// The Loop tab: start and stop the soak loop and watch its counters.
struct LoopView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        let loop = model.loop
        List {
            Section {
                Text(loop.isRunning ? "Loop (running)" : "Loop")
                    .font(.title3)
                HStack {
                    Button("Start") {
                        if let device = model.selected {
                            Task { await loop.start(device) }
                        }
                    }
                    .accessibilityIdentifier("loop-start")
                    .disabled(loop.isRunning || model.selected == nil)
                    Button("Stop") {
                        Task { await loop.stop() }
                    }
                    .accessibilityIdentifier("loop-stop")
                    .disabled(!loop.isRunning)
                }
                .buttonStyle(.bordered)
                Text("The loop echoes each cycle, which requires the high-privilege pairing.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }
            Section("Results") {
                row("Device", loop.deviceName.isEmpty ? "—" : loop.deviceName, id: "loop-device")
                row("Tests", loop.started == nil ? "—" : String(loop.tests), id: "loop-tests")
                row("Connection failures", loop.started == nil ? "—" : String(loop.connectionFailures), id: "loop-connection-failures")
                row("Command failures", loop.started == nil ? "—" : String(loop.commandFailures), id: "loop-command-failures")
                row("Min connect", ms(loop.minConnectMs), id: "loop-min")
                row("Average connect", ms(loop.avgConnectMs), id: "loop-avg")
                row("Max connect", ms(loop.maxConnectMs), id: "loop-max")
                row("Started", loop.started.map { $0.formatted(date: .omitted, time: .standard) } ?? "—", id: "loop-started")
                row("Elapsed", loop.started == nil ? "—" : elapsed(loop.elapsed), id: "loop-elapsed")
            }
        }
    }

    private func row(_ label: String, _ value: String, id: String) -> some View {
        HStack {
            Text(label)
            Spacer()
            Text(value)
                .monospacedDigit()
                .foregroundStyle(.secondary)
                .accessibilityIdentifier(id)
        }
    }

    private func ms(_ value: Double?) -> String {
        value.map { String(format: "%.0f ms", $0) } ?? "—"
    }

    private func elapsed(_ duration: Duration) -> String {
        let total = Int(duration.seconds)
        return String(format: "%02d:%02d:%02d", total / 3600, total % 3600 / 60, total % 60)
    }
}
