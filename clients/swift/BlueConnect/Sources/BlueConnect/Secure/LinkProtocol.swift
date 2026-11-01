// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation
import Torann

/// The Noise suites the link runs. The names are constants that Torann is
/// known to parse, so the parse cannot fail at runtime.
public enum LinkProtocol {
    public static let name = "Noise_XX_25519_AESGCM_SHA256"
    public static let pinnedName = "Noise_IK_25519_AESGCM_SHA256"
    public static let keyLength = 32
    public static let tagLength = 16
    public static let xx = try! NoiseProtocol.parse(name)
    public static let ik = try! NoiseProtocol.parse(pinnedName)
}

/// The app's long-term X25519 identity for one tier.
public struct LinkIdentity: Sendable, Equatable {
    public let privateKey: Data
    public let publicKey: Data

    /// Derives the public key with Torann. Throws for a key that is not 32 bytes.
    public init(privateKey: Data) throws {
        guard privateKey.count == LinkProtocol.keyLength else {
            throw BlueConnectError.invalidOperation("A link identity private key is \(LinkProtocol.keyLength) bytes.")
        }
        let keyPair = try LinkProtocol.xx.createKeyPair(privateKey: privateKey)
        defer { keyPair.close() }
        self.privateKey = privateKey
        self.publicKey = try keyPair.publicKey
    }

    public static func generate() throws -> LinkIdentity {
        let keyPair = try LinkProtocol.xx.generateKeyPair()
        defer { keyPair.close() }
        return try LinkIdentity(privateKey: try keyPair.privateKey)
    }
}

/// What a session is opened with: this side's identity and, once paired, the
/// device key it must present. No endorsement: this library does not carry
/// client certificates.
public struct LinkSecurity: Sendable {
    public let identity: LinkIdentity
    public let pinnedPeerKey: Data?

    public init(identity: LinkIdentity, pinnedPeerKey: Data?) {
        self.identity = identity
        self.pinnedPeerKey = pinnedPeerKey
    }
}
