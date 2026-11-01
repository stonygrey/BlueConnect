// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation
import Torann

/// Encrypted, mutually authenticated link over a peripheral, as initiator.
/// An initiator holding a pinned peer key opens with Noise IK; otherwise
/// Noise XX runs and the pin, if any, is enforced at message 2. After the
/// handshake every message is AEAD-sealed as a `0x02` frame. This is the
/// initiator half only; the responder half is the firmware, and the tests
/// use Torann's responder directly.
public actor SecureLink {
    /// Bound into the handshake transcript. Framing is below this layer.
    public static let prologue = Data("BlueConnectLink/1".utf8)
    /// Messages 2 and 3 carry this single ignored byte instead of an empty
    /// payload: Android's native AES-GCM rejects zero-length inputs.
    public static let handshakePayload = Data([0x5A])
    public static let handshakeMessage1Length = LinkProtocol.keyLength
    public static let handshakeMessage2Length =
        LinkProtocol.keyLength + LinkProtocol.keyLength + LinkProtocol.tagLength + handshakePayload.count + LinkProtocol.tagLength
    public static let handshakeMessage3Length =
        LinkProtocol.keyLength + LinkProtocol.tagLength + handshakePayload.count + LinkProtocol.tagLength
    public static let ikHandshakeMessage1Length =
        LinkProtocol.keyLength + LinkProtocol.keyLength + LinkProtocol.tagLength + handshakePayload.count + LinkProtocol.tagLength
    public static let ikHandshakeMessage2Length = LinkProtocol.keyLength + handshakePayload.count + LinkProtocol.tagLength

    private let segments: SegmentLink
    private let security: LinkSecurity
    private var transport: Transport?
    private var closed = false

    /// The peer's proven static key; available once the handshake has completed.
    public private(set) var remoteStaticKey: Data?

    public init(peripheral: any BlePeripheral, maxFrame: Int, security: LinkSecurity) {
        segments = SegmentLink(peripheral: peripheral, maxMessage: maxFrame)
        self.security = security
    }

    public var isEstablished: Bool { transport != nil }

    /// Runs the handshake, IK when a peer key is pinned and XX otherwise,
    /// before any traffic flows.
    public func establish() async throws {
        if closed {
            throw BlueConnectError.invalidOperation("The secure link has been closed.")
        }
        if isEstablished {
            return
        }
        let staticKeyPair = try LinkProtocol.xx.createKeyPair(privateKey: security.identity.privateKey)
        defer { staticKeyPair.close() }
        var handshake: HandshakeState?
        defer { handshake?.close() }
        let split: Transport?
        if let pin = security.pinnedPeerKey {
            // IK: message 1 both proves this identity and shows we already
            // hold the peer's key; the reply completes the session. A stale
            // pin fails on the device and yields silence; the caller's
            // timeout plus the advertised pairing state surface the reset.
            let state = try step {
                try LinkProtocol.ik.createHandshake(
                    initiator: true, prologue: Self.prologue, localStatic: staticKeyPair, remoteStatic: pin)
            }
            handshake = state
            let message1 = try step { try state.writeMessage(payload: Self.handshakePayload) }
            try await writeTyped(FrameType.handshake, message1.message)
            let message2 = try await readHandshakeFrame()
            try Self.expectHandshakeLength(message2, Self.ikHandshakeMessage2Length, number: 2)
            let read2 = try step { try state.readMessage(message2) }
            try Self.expectHandshakePayload(read2.payload)
            split = read2.transport
        } else {
            let state = try step {
                try LinkProtocol.xx.createHandshake(initiator: true, prologue: Self.prologue, localStatic: staticKeyPair)
            }
            handshake = state
            let message1 = try step { try state.writeMessage() }
            try await writeTyped(FrameType.handshake, message1.message)
            let message2 = try await readHandshakeFrame()
            try Self.expectHandshakeLength(message2, Self.handshakeMessage2Length, number: 2)
            let read2 = try step { try state.readMessage(message2) }
            try Self.expectHandshakePayload(read2.payload)
            try verifyPeer(try state.remoteStaticPublicKey)
            let message3 = try step { try state.writeMessage(payload: Self.handshakePayload) }
            try await writeTyped(FrameType.handshake, message3.message)
            split = message3.transport
        }
        guard let split else {
            throw BlueConnectError.linkSecurity("The handshake completed without producing a transport.")
        }
        remoteStaticKey = try handshake?.remoteStaticPublicKey
        transport = split
        IoLog.logger.info("Secure link established; peer static key \(IoLog.hex(self.remoteStaticKey ?? Data()), privacy: .public).")
    }

    public func send(_ message: Data) async throws {
        try await establish()
        let sealed = try step { try transport!.writeMessage(payload: message) }
        try await writeTyped(FrameType.encrypted, sealed)
    }

    public func receive() async throws -> Data {
        try await establish()
        while true {
            let (type, payload) = try await readTyped()
            if type == FrameType.encrypted {
                do {
                    return try transport!.readMessage(payload)
                } catch {
                    throw BlueConnectError.linkSecurity("AEAD authentication failed; the frame was forged, corrupted, or replayed.")
                }
            }
            if type == FrameType.plaintext {
                throw BlueConnectError.linkSecurity("Plaintext frame on a secure link.")
            }
            IoLog.logger.debug("Ignoring frame type \(type) on an established secure link.")
        }
    }

    public func close() {
        if closed {
            return
        }
        closed = true
        transport?.close()
        transport = nil
    }

    private func verifyPeer(_ remoteStatic: Data?) throws {
        // Torann leaves non-contributory-key checks to the application (Noise
        // spec §12.1); an all-zero static is never a legitimate identity.
        guard let remoteStatic, remoteStatic.contains(where: { $0 != 0 }) else {
            throw BlueConnectError.linkSecurity("Peer presented an all-zero static key.")
        }
        if let pin = security.pinnedPeerKey, remoteStatic != pin {
            throw BlueConnectError.linkSecurity("Peer's static key does not match the paired key.")
        }
    }

    /// Runs one Torann step, mapping its failures into the link taxonomy.
    private func step<T>(_ body: () throws -> T) throws -> T {
        do {
            return try body()
        } catch let error as TorannError {
            throw BlueConnectError.linkSecurity("The handshake failed: \(error.message)")
        }
    }

    private static func expectHandshakeLength(_ message: Data, _ expected: Int, number: Int) throws {
        if message.count != expected {
            throw BlueConnectError.linkSecurity("Handshake message \(number) is \(message.count) bytes; expected \(expected).")
        }
    }

    private static func expectHandshakePayload(_ payload: Data) throws {
        if payload != handshakePayload {
            throw BlueConnectError.linkSecurity("Handshake payload does not match the protocol constant.")
        }
    }

    private func writeTyped(_ type: UInt8, _ payload: Data) async throws {
        let bundled = FrameType.bundle(type, payload)
        IoLog.logger.info("\(IoLog.message("write", type: type, bytes: bundled), privacy: .public)")
        try await segments.write(bundled)
    }

    private func readTyped() async throws -> (type: UInt8, payload: Data) {
        let bundled = try await segments.read()
        let (type, payload) = try FrameType.split(bundled)
        IoLog.logger.info("\(IoLog.message("read", type: type, bytes: bundled), privacy: .public)")
        return (type, payload)
    }

    private func readHandshakeFrame() async throws -> Data {
        let (type, payload) = try await readTyped()
        if type != FrameType.handshake {
            throw BlueConnectError.linkSecurity("Expected a handshake frame but received type \(type).")
        }
        return payload
    }
}
