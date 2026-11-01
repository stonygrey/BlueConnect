// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import SwiftUI

/// The trust tab: one pairing card per tier, High first.
struct NoiseView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        List {
            Section {
                Text("Noise XX link security; each tier is enabled independently.")
                Label("Pair: Mutual key pinning, confirmed with the board's button.", systemImage: "link")
                    .font(.footnote)
            }
            Section {
                PairingCard(tier: .high)
            }
            Section {
                PairingCard(tier: .low)
            }
        }
    }
}

struct PairingCard: View {
    @Environment(AppModel.self) private var model
    let tier: Tier
    @State private var scanning = false

    var body: some View {
        let credential = model.pairings.credential(tier)
        VStack(alignment: .leading, spacing: 8) {
            Label {
                Text("\(tier.title): \(stateLabel(credential))")
                    .font(.headline)
                    .accessibilityIdentifier("title-\(tier.rawValue)")
            } icon: {
                Image(systemName: credential.isPaired ? "iphone.radiowaves.left.and.right" : "iphone.slash")
                    .foregroundStyle(iconColor(credential))
            }
            Text(status(credential))
                .font(.footnote)
                .foregroundStyle(.secondary)
                .accessibilityIdentifier("status-\(tier.rawValue)")
            if credential.isPaired {
                Button("Unpair") {
                    Task { await model.commands.unpair(model.selected, tier: tier) }
                }
                .accessibilityIdentifier("unpair-\(tier.rawValue)")
                .disabled(!model.canRun)
            } else {
                HStack(alignment: .firstTextBaseline) {
                    Button("Pair") {
                        Task { await model.commands.pair(model.selected, tier: tier) }
                    }
                    .accessibilityIdentifier("pair-\(tier.rawValue)")
                    .disabled(!model.canRun)
                    Text("Confirm with \(tier.button)")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Divider()
                HStack(alignment: .firstTextBaseline) {
                    Button("Scan QR", systemImage: "qrcode.viewfinder") {
                        scanning = true
                    }
                    .accessibilityIdentifier("scan-\(tier.rawValue)")
                    .disabled(!model.canRun)
                    Text("Provision from the QR created over USB, or an enrollment card.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            let result = model.commands.pairResult[tier] ?? ""
            if !result.isEmpty {
                Text(result)
                    .font(.caption)
                    .accessibilityIdentifier("result-\(tier.rawValue)")
            }
        }
        .padding(.vertical, 4)
        // A List row that holds several buttons triggers them all on a click
        // anywhere in the row unless the buttons are borderless.
        .buttonStyle(.borderless)
        .sheet(isPresented: $scanning) {
            QrScannerSheet { text in
                scanning = false
                Task { await model.commands.provision(model.selected, tier: tier, text: text) }
            }
        }
    }

    private func stateLabel(_ credential: TierCredential) -> String {
        credential.isPaired ? (credential.origin ?? .paired).label : "Unsecured"
    }

    private func iconColor(_ credential: TierCredential) -> Color {
        guard credential.isPaired else { return .secondary }
        return model.selected?.isSecured == true ? .green : .orange
    }

    private func status(_ credential: TierCredential) -> String {
        guard credential.isPaired else {
            return "Not paired (\(tier.rawValue)). Pair to run this page's commands"
        }
        if model.selected?.isSecured == true {
            return "Sessions are encrypted."
        }
        let with = credential.deviceName.map { " with '\($0)'" } ?? ""
        return "Paired (\(tier.rawValue))\(with), but the device reports no pairings. It may have been reset; unpair to start over."
    }
}
