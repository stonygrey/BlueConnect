// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import BlueConnectCoreBluetooth
import Foundation
import os

let appLogger = Logger(subsystem: "com.stonygrey.zaffre.swift", category: "app")

/// The root model: the scanner, the device list sampled from it every 500 ms,
/// the selected device, and the objects that own pairing, commands, the soak
/// loop, and toasts.
@MainActor
@Observable
final class AppModel {
    let options = BlueConnectOptions()
    let scanner: CoreBluetoothScanner
    let toasts = Toasts()
    let pairings: Pairings
    let commands: CommandRunner
    let loop: SoakLoop

    /// Devices seen within the expiry window, strongest signal first.
    private(set) var devices: [Advertisement] = []
    /// The device the user tapped, refreshed from the scan so that a renamed
    /// device or a changed status byte shows on every tab.
    var selected: Advertisement?
    private(set) var scanFailure: String?
    private var sampling: Task<Void, Never>?

    init() {
        scanner = CoreBluetoothScanner(options: options)
        pairings = Pairings(store: CredentialStores.platformDefault())
        commands = CommandRunner(scanner: scanner, options: options, pairings: pairings, toasts: toasts)
        loop = SoakLoop(scanner: scanner, options: options, pairings: pairings)
    }

    /// Commands and pairing are allowed when a device is selected, nothing
    /// else is running, and the loop is stopped.
    var canRun: Bool {
        selected != nil && !commands.isBusy && !loop.isRunning
    }

    func start() {
        guard sampling == nil else { return }
        sampling = Task { [scanner] in
            do {
                try await scanner.start()
            } catch {
                scanFailure = describe(error)
                return
            }
            while !Task.isCancelled {
                sample()
                try? await Task.sleep(for: .milliseconds(500))
            }
        }
    }

    private func sample() {
        devices = scanner.peripherals.sorted { $0.rssi.valueDbm > $1.rssi.valueDbm }
        if let selected, let fresh = devices.first(where: { $0.id == selected.id }) {
            self.selected = fresh
        }
    }
}

/// The toast text for `error`: the library's own message for its errors,
/// the device's status for a rejected request.
func describe(_ error: any Error) -> String {
    switch error {
    case let error as BlueConnectError: error.description
    case let error as VerdigrisError: error.description
    case is CancellationError: "Cancelled."
    default: String(describing: error)
    }
}
