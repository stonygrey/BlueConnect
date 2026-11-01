// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation

/// A USB-provisioned Noise credential as the QR carries it:
/// `SGNK1:<L|H>:<base64url 64 bytes>[:<NAME>]`, the blob being the app's
/// X25519 private key followed by the device's X25519 public key. The QR is
/// the credential itself.
public struct NoiseQrPayload: Sendable, Equatable {
    public static let prefix = "SGNK1"

    public let tier: Privilege
    public let appPrivateKey: Data
    public let devicePublicKey: Data
    public let deviceName: String?

    public init(tier: Privilege, appPrivateKey: Data, devicePublicKey: Data, deviceName: String?) {
        self.tier = tier
        self.appPrivateKey = appPrivateKey
        self.devicePublicKey = devicePublicKey
        self.deviceName = deviceName
    }

    public func encode() throws -> String {
        guard appPrivateKey.count == LinkProtocol.keyLength, devicePublicKey.count == LinkProtocol.keyLength,
            !Base64Url.isZero(appPrivateKey), !Base64Url.isZero(devicePublicKey)
        else {
            throw BlueConnectError.linkSecurity("Noise QR payload fields are out of range.")
        }
        let encoded = Base64Url.encode(appPrivateKey + devicePublicKey)
        let tierLetter = tier == .high ? "H" : "L"
        if let deviceName, !deviceName.isEmpty {
            return "\(Self.prefix):\(tierLetter):\(encoded):\(deviceName)"
        }
        return "\(Self.prefix):\(tierLetter):\(encoded)"
    }

    /// Nil for anything that is not a well-formed SGNK1 payload with non-zero keys.
    public static func parse(_ text: String) -> NoiseQrPayload? {
        let parts = text.trimmingCharacters(in: .whitespacesAndNewlines).split(separator: ":", maxSplits: 3, omittingEmptySubsequences: false).map(String.init)
        guard parts.count >= 3, parts[0] == prefix else { return nil }
        let tier: Privilege
        switch parts[1] {
        case "L": tier = .low
        case "H": tier = .high
        default: return nil
        }
        guard let blob = Base64Url.decode(parts[2]), blob.count == 2 * LinkProtocol.keyLength else { return nil }
        let appPrivate = blob.prefix(LinkProtocol.keyLength)
        let devicePublic = blob.suffix(LinkProtocol.keyLength)
        if Base64Url.isZero(appPrivate) || Base64Url.isZero(devicePublic) { return nil }
        let name = parts.count == 4 && !parts[3].isEmpty ? parts[3] : nil
        return NoiseQrPayload(tier: tier, appPrivateKey: Data(appPrivate), devicePublicKey: Data(devicePublic), deviceName: name)
    }
}

/// An enrollment card: `SGNE1:<base64url 64 bytes>[:<NAME>]`, the blob being
/// the device's enrollment private key followed by the device's public key.
/// Used once, to enroll the phone's own low-tier key, then discarded.
public struct NoiseEnrollmentPayload: Sendable, Equatable {
    public static let prefix = "SGNE1"

    public let enrollmentPrivateKey: Data
    public let devicePublicKey: Data
    public let deviceName: String?

    public init(enrollmentPrivateKey: Data, devicePublicKey: Data, deviceName: String?) {
        self.enrollmentPrivateKey = enrollmentPrivateKey
        self.devicePublicKey = devicePublicKey
        self.deviceName = deviceName
    }

    public func encode() throws -> String {
        guard enrollmentPrivateKey.count == LinkProtocol.keyLength, devicePublicKey.count == LinkProtocol.keyLength,
            !Base64Url.isZero(enrollmentPrivateKey), !Base64Url.isZero(devicePublicKey)
        else {
            throw BlueConnectError.linkSecurity("Noise enrollment payload fields are out of range.")
        }
        let encoded = Base64Url.encode(enrollmentPrivateKey + devicePublicKey)
        if let deviceName, !deviceName.isEmpty {
            return "\(Self.prefix):\(encoded):\(deviceName)"
        }
        return "\(Self.prefix):\(encoded)"
    }

    public static func parse(_ text: String) -> NoiseEnrollmentPayload? {
        let parts = text.trimmingCharacters(in: .whitespacesAndNewlines).split(separator: ":", maxSplits: 2, omittingEmptySubsequences: false).map(String.init)
        guard parts.count >= 2, parts[0] == prefix else { return nil }
        guard let blob = Base64Url.decode(parts[1]), blob.count == 2 * LinkProtocol.keyLength else { return nil }
        let enrollmentPrivate = blob.prefix(LinkProtocol.keyLength)
        let devicePublic = blob.suffix(LinkProtocol.keyLength)
        if Base64Url.isZero(enrollmentPrivate) || Base64Url.isZero(devicePublic) { return nil }
        let name = parts.count == 3 && !parts[2].isEmpty ? parts[2] : nil
        return NoiseEnrollmentPayload(enrollmentPrivateKey: Data(enrollmentPrivate), devicePublicKey: Data(devicePublic), deviceName: name)
    }
}

/// Base64url without padding, as the QR payloads carry it; the decoder also
/// accepts padded input.
enum Base64Url {
    static func encode(_ data: Data) -> String {
        data.base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }

    static func decode(_ text: String) -> Data? {
        var base64 = text.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        while base64.count % 4 != 0 {
            base64.append("=")
        }
        return Data(base64Encoded: base64)
    }

    static func isZero(_ bytes: Data) -> Bool {
        !bytes.contains { $0 != 0 }
    }
}
