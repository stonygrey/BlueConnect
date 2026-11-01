// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import Foundation

/// Runs one Verdigris command at a time: each command opens its own
/// connection, runs, disconnects, and releases the peripheral.
@MainActor
@Observable
final class CommandRunner {
    private let scanner: any BleScanner
    private let options: BlueConnectOptions
    private let pairings: Pairings
    private let toasts: Toasts

    private(set) var isBusy = false
    /// LED 1 as last reported by the device, per tier page; nil until read.
    private(set) var ledOn: [Tier: Bool] = [:]
    /// The pairing card's result line.
    private(set) var pairResult: [Tier: String] = [:]

    init(scanner: any BleScanner, options: BlueConnectOptions, pairings: Pairings, toasts: Toasts) {
        self.scanner = scanner
        self.options = options
        self.pairings = pairings
        self.toasts = toasts
    }

    /// Connect, run `body`, disconnect, close. The connection is never kept
    /// between commands.
    private func session<T: Sendable>(
        _ device: Advertisement, security: LinkSecurity,
        _ body: @Sendable (VerdigrisPeripheral) async throws -> T
    ) async throws -> T {
        let peripheral = try scanner.createPeripheral(device)
        let verdigris = VerdigrisPeripheral(peripheral: peripheral, options: options, security: security)
        do {
            try await peripheral.connect()
            let result = try await body(verdigris)
            try await peripheral.disconnect()
            await verdigris.close()
            return result
        } catch {
            await verdigris.close()
            throw error
        }
    }

    /// A command on the tier's pinned session. A failure becomes an error
    /// toast with the message, and nil is returned.
    func run<T: Sendable>(
        _ device: Advertisement?, tier: Tier, _ body: @Sendable (VerdigrisPeripheral) async throws -> T
    ) async -> T? {
        guard let device, !isBusy else { return nil }
        isBusy = true
        defer { isBusy = false }
        do {
            return try await session(device, security: pairings.security(for: tier), body)
        } catch {
            appLogger.warning("Verdigris command failed against '\(device.localName, privacy: .public)': \(describe(error), privacy: .public)")
            toasts.error(describe(error))
            return nil
        }
    }

    func readLed(_ device: Advertisement?, tier: Tier) async {
        if let on = await run(device, tier: tier, { try await $0.getLed() }) {
            ledOn[tier] = on
        }
    }

    /// Tap on the LED: read it when unknown, otherwise switch it.
    func tapLed(_ device: Advertisement?, tier: Tier) async {
        guard let current = ledOn[tier] else {
            await readLed(device, tier: tier)
            return
        }
        if let on = await run(device, tier: tier, { try await $0.setLed(!current) }) {
            ledOn[tier] = on
        }
    }

    /// The ceremony: an XX session that accepts any device key, `Pair`, and
    /// the button on the board within 30 s. On Ack the proven device key is
    /// pinned for the tier.
    func pair(_ device: Advertisement?, tier: Tier) async {
        guard let device, !isBusy else { return }
        isBusy = true
        defer { isBusy = false }
        pairResult[tier] = "Press \(tier.button) on the device to confirm…"
        do {
            let key = try await session(device, security: pairings.pairingSecurity(for: tier)) {
                // The marker a hardware test watches for to confirm over USB.
                appLogger.info("ZAFFRE-PAIR: armed \(tier.rawValue, privacy: .public)")
                return try await $0.pair(tier.privilege)
            }
            pairings.record(tier: tier, deviceKey: key, deviceName: device.localName)
            pairResult[tier] = ""
        } catch let error as VerdigrisError where error.status == .unauthenticated {
            pairResult[tier] = error.detail.map { "Pairing not confirmed: \($0)" }
                ?? "Pairing not confirmed. Press \(tier.button) on the device within 30 seconds and try again."
        } catch {
            pairResult[tier] = ""
            toasts.error(describe(error))
        }
    }

    /// A pasted or scanned credential for the tier's card: an `SGNE1` card
    /// enrolls the low tier over the air, an `SGNK1` credential is installed
    /// without any traffic, and anything else is refused.
    func provision(_ device: Advertisement?, tier: Tier, text: String) async {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            toasts.error("Paste the SGNK1 payload first.")
            return
        }
        if let enrollment = NoiseEnrollmentPayload.parse(trimmed) {
            guard tier == .low else {
                toasts.warning("An enrollment QR enrolls the low tier — scan it on the low card.")
                return
            }
            guard let device else {
                toasts.error("Select the device first.")
                return
            }
            await enroll(device, enrollment)
            return
        }
        guard let credential = NoiseQrPayload.parse(trimmed) else {
            toasts.error("That is not a BlueConnect Noise credential (SGNK1) or enrollment (SGNE1) payload.")
            return
        }
        let payloadTier: Tier = credential.tier == .high ? .high : .low
        guard payloadTier == tier else {
            toasts.warning("That QR is the \(payloadTier.rawValue) Noise credential — scan it on the \(payloadTier.rawValue) card.")
            return
        }
        do {
            let identity = try LinkIdentity(privateKey: credential.appPrivateKey)
            pairings.storeProvisioned(tier: tier, identity: identity, deviceKey: credential.devicePublicKey, deviceName: credential.deviceName)
        } catch {
            toasts.error(describe(error))
        }
    }

    /// One IK session as the enrollment key, with the device key from the
    /// card pinned, carrying `Enroll` with the low identity's public key. The
    /// enrollment key is used for this call and never stored.
    private func enroll(_ device: Advertisement, _ card: NoiseEnrollmentPayload) async {
        guard !isBusy else { return }
        isBusy = true
        defer { isBusy = false }
        do {
            let enrollmentIdentity = try LinkIdentity(privateKey: card.enrollmentPrivateKey)
            let security = LinkSecurity(identity: enrollmentIdentity, pinnedPeerKey: card.devicePublicKey)
            let lowPublicKey = pairings.credential(.low).identity.publicKey
            try await session(device, security: security) { try await $0.enroll(clientPublicKey: lowPublicKey) }
            pairings.record(tier: .low, deviceKey: card.devicePublicKey, deviceName: card.deviceName ?? device.localName, origin: .enrolled)
        } catch {
            appLogger.warning("Enrollment failed against '\(device.localName, privacy: .public)': \(describe(error), privacy: .public)")
            toasts.error(describe(error))
        }
    }

    /// `Unpair` on the pinned session, then the local pin is cleared even if
    /// the device call failed: the board can be cleared by its own button or
    /// over USB, and a stale local pin would only block the next session.
    func unpair(_ device: Advertisement?, tier: Tier) async {
        guard let device, !isBusy else { return }
        guard pairings.credential(tier).isPaired else {
            toasts.error("Not paired.")
            return
        }
        isBusy = true
        defer { isBusy = false }
        do {
            try await session(device, security: pairings.security(for: tier)) { try await $0.unpair() }
        } catch is CancellationError {
            return
        } catch {
            appLogger.warning("Device-side unpair (\(tier.logName, privacy: .public)) failed; clearing the local pairing anyway: \(describe(error), privacy: .public)")
        }
        pairings.clear(tier: tier)
        pairResult[tier] = ""
    }
}
