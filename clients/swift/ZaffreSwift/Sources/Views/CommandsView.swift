// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import SwiftUI

/// The Low and High tabs: the serial number, LED 1, and Echo cards. The
/// device enforces the tier, so on Low the writes and Echo come back
/// `PERMISSION_DENIED` as a toast.
struct CommandsView: View {
    @Environment(AppModel.self) private var model
    let tier: Tier
    @State private var serialInput = String(Int.random(in: 1...65535))
    @State private var echoInput = "Hello, Verdigris!"

    var body: some View {
        List {
            Section {
                Text(tier.title)
                    .font(.title3)
            }
        Section("Serial Number") {
                TextField("Serial number", text: $serialInput)
                    .accessibilityIdentifier("serial-input-\(tier.rawValue)")
                HStack {
                    Button("Read") { Task { await readSerial() } }
                        .accessibilityIdentifier("serial-read-\(tier.rawValue)")
                    Button("Write") { Task { await writeSerial() } }
                        .accessibilityIdentifier("serial-write-\(tier.rawValue)")
                }
                .buttonStyle(.bordered)
                .disabled(!model.canRun)
            }
            Section("LED 1") {
                HStack(spacing: 16) {
                    Button {
                        Task { await model.commands.tapLed(model.selected, tier: tier) }
                    } label: {
                        ZStack {
                            Circle()
                                .fill(ledColor)
                                .frame(width: 44, height: 44)
                            if model.commands.ledOn[tier] == nil {
                                Image(systemName: "arrow.clockwise")
                                    .foregroundStyle(.white)
                            }
                        }
                    }
                    .buttonStyle(.plain)
                    .accessibilityIdentifier("led-\(tier.rawValue)")
                    .accessibilityLabel(ledLabel)
                    .disabled(!model.canRun)
                    Text(ledLabel)
                        .font(.footnote)
                        .accessibilityIdentifier("led-label-\(tier.rawValue)")
                    Spacer()
                    Button {
                        Task { await model.commands.readLed(model.selected, tier: tier) }
                    } label: {
                        Image(systemName: "arrow.clockwise")
                    }
                    .accessibilityIdentifier("led-read-\(tier.rawValue)")
                    .help("Read LED state")
                    .disabled(!model.canRun)
                }
            }
            Section("Echo") {
                TextField("Payload", text: $echoInput)
                    .accessibilityIdentifier("echo-input-\(tier.rawValue)")
                Button("Write") { Task { await echo() } }
                    .buttonStyle(.bordered)
                    .tint(tier == .low ? .red : .accentColor)
                    .accessibilityIdentifier("echo-write-\(tier.rawValue)")
                    .disabled(!model.canRun)
            }
        }
    }

    private var ledColor: Color {
        switch model.commands.ledOn[tier] {
        case .some(true): .yellow
        case .some(false): .gray
        case .none: .secondary
        }
    }

    private var ledLabel: String {
        switch model.commands.ledOn[tier] {
        case .some(true): "LED 1 is on. Tap to switch it off"
        case .some(false): "LED 1 is off. Tap to switch it on"
        case .none: "LED 1 state not read yet. Tap to read it"
        }
    }

    private func readSerial() async {
        if let name = await model.commands.run(model.selected, tier: tier, { try await $0.getSerialNumber() }) {
            model.toasts.info("Advertising as '\(name)'")
        }
    }

    private func writeSerial() async {
        guard let serial = UInt16(serialInput.trimmingCharacters(in: .whitespaces)), serial != 0 else {
            model.toasts.error("Serial number must be a whole number from 1 to 65535.")
            return
        }
        if let name = await model.commands.run(model.selected, tier: tier, { try await $0.setSerialNumber(serial) }) {
            model.toasts.info("Now advertising as '\(name)'")
            serialInput = String(Int.random(in: 1...65535))
        }
    }

    private func echo() async {
        let payload = Data(echoInput.utf8)
        if let reply = await model.commands.run(model.selected, tier: tier, { try await $0.echo(payload) }) {
            let text = String(decoding: reply, as: UTF8.self)
            if !text.isEmpty {
                model.toasts.info(text)
            }
        }
    }
}
