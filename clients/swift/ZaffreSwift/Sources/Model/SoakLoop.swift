// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import Foundation

/// The Loop tab: connect, echo, disconnect, over and over, with the
/// high-tier pairing, keeping counts and connect-time statistics.
@MainActor
@Observable
final class SoakLoop {
    private let scanner: any BleScanner
    private let options: BlueConnectOptions
    private let pairings: Pairings

    private(set) var isRunning = false
    private(set) var deviceName = ""
    private(set) var tests = 0
    private(set) var connectionFailures = 0
    private(set) var commandFailures = 0
    private(set) var minConnectMs: Double?
    private(set) var avgConnectMs: Double?
    private(set) var maxConnectMs: Double?
    private(set) var started: Date?
    private(set) var elapsed: Duration = .zero
    private var task: Task<Void, Never>?
    private var ticker: Task<Void, Never>?

    init(scanner: any BleScanner, options: BlueConnectOptions, pairings: Pairings) {
        self.scanner = scanner
        self.options = options
        self.pairings = pairings
    }

    func start(_ device: Advertisement) async {
        await stop()
        tests = 0
        connectionFailures = 0
        commandFailures = 0
        minConnectMs = nil
        avgConnectMs = nil
        maxConnectMs = nil
        deviceName = device.localName
        started = Date()
        elapsed = .zero
        isRunning = true
        ticker = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(1))
                self?.tick()
            }
        }
        task = Task { await self.loop(device) }
    }

    func stop() async {
        task?.cancel()
        ticker?.cancel()
        await task?.value
        task = nil
        ticker = nil
        isRunning = false
    }

    private func tick() {
        if let started {
            elapsed = .seconds(Date().timeIntervalSince(started))
        }
    }

    private func loop(_ device: Advertisement) async {
        let clock = ContinuousClock()
        while !Task.isCancelled {
            let security = pairings.security(for: .high)
            do {
                let peripheral = try scanner.createPeripheral(device)
                let verdigris = VerdigrisPeripheral(peripheral: peripheral, options: options, security: security)
                let before = clock.now
                do {
                    try await peripheral.connect()
                    recordConnect(clock.now - before)
                    do {
                        let payload = Self.randomPayload(options.isEchoVerificationEnabled ? 1536 : 16)
                        let reply = try await verdigris.echo(payload)
                        if reply != payload {
                            throw BlueConnectError.transport("Echoed payload does not match the command.")
                        }
                    } catch is CancellationError {
                        await verdigris.close()
                        break
                    } catch {
                        commandFailures += 1
                    }
                    try? await peripheral.disconnect()
                } catch is CancellationError {
                    await verdigris.close()
                    break
                } catch {
                    connectionFailures += 1
                }
                await verdigris.close()
            } catch {
                connectionFailures += 1
            }
            tests += 1
            tick()
            let delay = Int.random(in: Int(options.minCycleDelay.seconds)...Int(options.maxCycleDelay.seconds))
            do {
                try await Task.sleep(for: .seconds(delay))
            } catch {
                break
            }
        }
        isRunning = false
    }

    private func recordConnect(_ duration: Duration) {
        let ms = duration.seconds * 1000
        minConnectMs = min(minConnectMs ?? ms, ms)
        maxConnectMs = max(maxConnectMs ?? ms, ms)
        avgConnectMs = ((avgConnectMs ?? 0) * Double(tests) + ms) / Double(tests + 1)
    }

    private static func randomPayload(_ count: Int) -> Data {
        Data((0..<count).map { _ in UInt8.random(in: 0...255) })
    }
}
