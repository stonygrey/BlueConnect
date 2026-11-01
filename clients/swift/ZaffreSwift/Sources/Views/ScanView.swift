// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import SwiftUI

/// The scan list: name, identifier, the padlock for an advertised pairing,
/// and RSSI, strongest first. Tapping a row hands the device to `onSelect`,
/// which opens its pages.
struct ScanView: View {
    @Environment(AppModel.self) private var model
    let onSelect: (Advertisement) -> Void

    var body: some View {
        List(model.devices) { device in
            Button {
                onSelect(device)
            } label: {
                DeviceRow(device: device, isSelected: device.id == model.selected?.id)
            }
            .buttonStyle(.plain)
            .accessibilityIdentifier("device-row")
        }
        .overlay {
            if model.devices.isEmpty {
                ContentUnavailableView {
                    if let failure = model.scanFailure {
                        Label(failure, systemImage: "antenna.radiowaves.left.and.right.slash")
                    } else {
                        ProgressView()
                        Text("Scanning for Verdigris devices…")
                    }
                }
            }
        }
        .overlay(alignment: .bottomLeading) {
            Text(BuildInfo.label)
                .font(.caption2)
                .foregroundStyle(.secondary)
                .padding(.leading, 12)
                .padding(.bottom, 6)
                .allowsHitTesting(false)
                .accessibilityIdentifier("build-label")
        }
        .navigationTitle("Select a device")
    }
}

struct DeviceRow: View {
    let device: Advertisement
    let isSelected: Bool

    var body: some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading) {
                Text(device.localName)
                    .font(.headline)
                Text(device.id)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Image(systemName: device.isSecured ? "lock.fill" : "lock.open")
                .foregroundStyle(device.isSecured ? .green : .secondary)
                .help(device.isSecured ? "Paired. Only the pinned client is accepted" : "Not paired. Any client can open an encrypted session")
            if !device.isConnectable {
                Text("not connectable")
                    .font(.caption)
                    .foregroundStyle(.orange)
            }
            Text("\(device.rssi.valueDbm) dBm")
                .font(.caption)
                .monospacedDigit()
            if isSelected {
                Image(systemName: "checkmark")
                    .foregroundStyle(.tint)
            }
        }
        .contentShape(Rectangle())
    }
}
