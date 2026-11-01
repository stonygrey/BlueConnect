// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import Foundation
import os

/// The two privilege tiers, each with its own app identity and pin, so that
/// both can be demonstrated from a single install.
enum Tier: String, CaseIterable, Identifiable, Sendable {
    case low
    case high

    var id: String { rawValue }

    var privilege: Privilege {
        self == .low ? .low : .high
    }

    var title: String {
        self == .low ? "Low privilege" : "High privilege"
    }

    /// The nRF54L15-DK silkscreen label of the confirming button.
    var button: String {
        self == .low ? "Button 2" : "Button 3"
    }

    var logName: String {
        self == .low ? "Low" : "High"
    }
}

/// The source of a pin: the button ceremony, a USB-provisioned QR, or
/// enrollment.
enum CredentialOrigin: String, Sendable {
    case paired
    case provisioned
    case enrolled

    var label: String {
        switch self {
        case .paired: "Paired"
        case .provisioned: "Provisioned"
        case .enrolled: "Enrolled"
        }
    }
}

struct TierCredential: Sendable {
    var identity: LinkIdentity
    var deviceKey: Data?
    var deviceName: String?
    var origin: CredentialOrigin?

    var isPaired: Bool { deviceKey != nil }
}

/// Identity and pin storage per tier, base64 in the platform store. A missing
/// identity is generated on first use.
@MainActor
@Observable
final class Pairings {
    private static let logger = Logger(subsystem: "com.stonygrey.zaffre.swift", category: "credentials")
    private let store: any CredentialStore
    private(set) var credentials: [Tier: TierCredential] = [:]

    init(store: any CredentialStore) {
        self.store = store
        for tier in Tier.allCases {
            let identity = loadOrGenerateIdentity(tier)
            let deviceKey = store.data(forKey: "zaffre.pair.device.key.\(tier.rawValue)")
            let deviceName = store.data(forKey: "zaffre.pair.device.name.\(tier.rawValue)").flatMap { String(data: $0, encoding: .utf8) }
            let origin = store.data(forKey: "zaffre.pair.origin.\(tier.rawValue)")
                .flatMap { String(data: $0, encoding: .utf8) }
                .flatMap(CredentialOrigin.init(rawValue:))
            credentials[tier] = TierCredential(identity: identity, deviceKey: deviceKey, deviceName: deviceName, origin: deviceKey == nil ? nil : origin ?? .paired)
            Self.logger.info("ZAFFRE-NOISE-PUB: \(tier.logName, privacy: .public) \(IoLog.hex(identity.publicKey), privacy: .public) cert=none.")
        }
    }

    func credential(_ tier: Tier) -> TierCredential {
        credentials[tier]!
    }

    /// The security for a normal session: IK against the pin when paired.
    func security(for tier: Tier) -> LinkSecurity {
        let credential = credential(tier)
        return LinkSecurity(identity: credential.identity, pinnedPeerKey: credential.deviceKey)
    }

    /// The security for the pairing ceremony: XX, any device key accepted.
    func pairingSecurity(for tier: Tier) -> LinkSecurity {
        LinkSecurity(identity: credential(tier).identity, pinnedPeerKey: nil)
    }

    /// Pins the device key for the tier: after the button ceremony (`paired`)
    /// or after `Enroll` (`enrolled`).
    func record(tier: Tier, deviceKey: Data, deviceName: String?, origin: CredentialOrigin = .paired) {
        store.set(deviceKey, forKey: "zaffre.pair.device.key.\(tier.rawValue)")
        store.set(deviceName.map { Data($0.utf8) }, forKey: "zaffre.pair.device.name.\(tier.rawValue)")
        store.set(Data(origin.rawValue.utf8), forKey: "zaffre.pair.origin.\(tier.rawValue)")
        credentials[tier]?.deviceKey = deviceKey
        credentials[tier]?.deviceName = deviceName
        credentials[tier]?.origin = origin
        Self.logger.info("\(origin.label, privacy: .public) (\(tier.logName, privacy: .public)) with '\(deviceName ?? "", privacy: .public)' (\(IoLog.hex(deviceKey), privacy: .public)).")
    }

    /// Installs a USB-provisioned credential: the identity from the QR
    /// replaces the tier's own, and the device key is pinned. No BLE traffic.
    func storeProvisioned(tier: Tier, identity: LinkIdentity, deviceKey: Data, deviceName: String?) {
        store.set(identity.privateKey, forKey: "zaffre.link.identity.\(tier.rawValue)")
        credentials[tier]?.identity = identity
        record(tier: tier, deviceKey: deviceKey, deviceName: deviceName, origin: .provisioned)
    }

    /// Deletes the pin. A provisioned tier also gets a fresh identity, because
    /// the one from the QR stays locked into the device's slot until USB
    /// possession clears it.
    func clear(tier: Tier) {
        let wasProvisioned = credentials[tier]?.origin == .provisioned
        store.set(nil, forKey: "zaffre.pair.device.key.\(tier.rawValue)")
        store.set(nil, forKey: "zaffre.pair.device.name.\(tier.rawValue)")
        store.set(nil, forKey: "zaffre.pair.origin.\(tier.rawValue)")
        credentials[tier]?.deviceKey = nil
        credentials[tier]?.deviceName = nil
        credentials[tier]?.origin = nil
        if wasProvisioned {
            store.set(nil, forKey: "zaffre.link.identity.\(tier.rawValue)")
            credentials[tier]?.identity = loadOrGenerateIdentity(tier)
        }
    }

    private func loadOrGenerateIdentity(_ tier: Tier) -> LinkIdentity {
        let key = "zaffre.link.identity.\(tier.rawValue)"
        if let stored = store.data(forKey: key), let identity = try? LinkIdentity(privateKey: stored) {
            return identity
        }
        // Key generation is CryptoKit random bytes; a failure there has no
        // recovery path in an app that exists to open secure sessions.
        let identity = try! LinkIdentity.generate()
        store.set(identity.privateKey, forKey: key)
        Self.logger.info("Generated \(tier.logName, privacy: .public) app link identity \(IoLog.hex(identity.publicKey), privacy: .public).")
        return identity
    }
}
