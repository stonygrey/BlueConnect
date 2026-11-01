// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import Foundation
import Torann

/// An in-process Verdigris responder on the far end of a `LoopbackPipe`: it
/// runs Torann's responder side of the handshake (XX or IK, selected by the
/// length of message 1 as the firmware does), then answers each sealed
/// request through `handler`. It checks the lengths and the 0x5A payload the
/// firmware checks, so the initiator is tested against the wire rules before
/// it meets the board.
final class FakeDevice: @unchecked Sendable {
    typealias Handler = @Sendable (Verdigris_Request) -> [Verdigris_Response]

    let identity: LinkIdentity
    private let pipe: LoopbackPipe
    private let handler: Handler
    private let lock = NSLock()
    private var handshakeLengths: [Int] = []
    private var pattern = ""
    private var task: Task<Void, any Error>?

    init(pipe: LoopbackPipe, identity: LinkIdentity? = nil, handler: @escaping Handler = FakeDevice.echoHandler) throws {
        self.pipe = pipe
        self.identity = try identity ?? LinkIdentity.generate()
        self.handler = handler
    }

    /// The lengths of the handshake messages, in order, as the device saw them.
    var observedHandshakeLengths: [Int] {
        lock.lock()
        defer { lock.unlock() }
        return handshakeLengths
    }

    var observedPattern: String {
        lock.lock()
        defer { lock.unlock() }
        return pattern
    }

    func start() {
        task = Task { try await self.serve() }
    }

    func stop() {
        task?.cancel()
    }

    static let echoHandler: Handler = { request in
        var response = Verdigris_Response()
        response.id = request.id
        response.status = .ok
        switch request.op {
        case .echo(let echo):
            response.echo.payload = echo.payload
        case .getSerialNumber:
            response.name.name = "Verdigris 0042"
        case .pair, .unpair, .enroll:
            response.ack = Verdigris_AckResponse()
        case .getLed:
            response.led.on = true
        case .setLed(let led):
            response.led.on = led.on
        default:
            response.status = .unknownOp
        }
        return [response]
    }

    private func serve() async throws {
        let segments = SegmentLink(peripheral: pipe, maxMessage: VerdigrisPeripheral.maxFrame)
        let keyPair = try LinkProtocol.xx.createKeyPair(privateKey: identity.privateKey)
        defer { keyPair.close() }
        while !Task.isCancelled {
            let frame = try await segments.read()
            let (type, message1) = try FrameType.split(frame)
            guard type == FrameType.handshake else { continue }
            record(message1.count)
            let transport: Transport
            if message1.count == SecureLink.ikHandshakeMessage1Length {
                setPattern("IK")
                let handshake = try LinkProtocol.ik.createHandshake(
                    initiator: false, prologue: SecureLink.prologue, localStatic: keyPair)
                defer { handshake.close() }
                let read1 = try handshake.readMessage(message1)
                precondition(read1.payload == SecureLink.handshakePayload, "IK message 1 payload")
                let write2 = try handshake.writeMessage(payload: SecureLink.handshakePayload)
                record(write2.message.count)
                try await segments.write(FrameType.bundle(FrameType.handshake, write2.message))
                transport = write2.transport!
            } else {
                setPattern("XX")
                precondition(message1.count == SecureLink.handshakeMessage1Length, "XX message 1 length")
                let handshake = try LinkProtocol.xx.createHandshake(
                    initiator: false, prologue: SecureLink.prologue, localStatic: keyPair)
                defer { handshake.close() }
                _ = try handshake.readMessage(message1)
                let write2 = try handshake.writeMessage(payload: SecureLink.handshakePayload)
                record(write2.message.count)
                try await segments.write(FrameType.bundle(FrameType.handshake, write2.message))
                let frame3 = try await segments.read()
                let (type3, message3) = try FrameType.split(frame3)
                precondition(type3 == FrameType.handshake, "XX message 3 type")
                record(message3.count)
                let read3 = try handshake.readMessage(message3)
                precondition(read3.payload == SecureLink.handshakePayload, "XX message 3 payload")
                transport = read3.transport!
            }
            defer { transport.close() }
            // Sealed traffic until the next handshake message 1 supersedes the session.
            while !Task.isCancelled {
                let sealedFrame = try await segments.read()
                let (sealedType, sealed) = try FrameType.split(sealedFrame)
                if sealedType == FrameType.handshake {
                    break
                }
                guard sealedType == FrameType.encrypted else { continue }
                let plaintext = try transport.readMessage(sealed)
                let request = try Verdigris_Request(serializedBytes: plaintext)
                for response in handler(request) {
                    let body = try response.serializedData()
                    let out = try transport.writeMessage(payload: body)
                    try await segments.write(FrameType.bundle(FrameType.encrypted, out))
                }
            }
        }
    }

    private func record(_ length: Int) {
        lock.lock()
        handshakeLengths.append(length)
        lock.unlock()
    }

    private func setPattern(_ value: String) {
        lock.lock()
        pattern = value
        lock.unlock()
    }
}
