// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import XCTest

/// The initiator against Torann's responder over the loopback pipe. Checks
/// the pattern selection and the message lengths the firmware enforces.
final class SecureLinkTests: XCTestCase {
    func testUnpinnedInitiatorRunsXxAndLearnsTheDeviceKey() async throws {
        let (app, dev) = LoopbackPipe.pair(mtu: 20)
        let device = try FakeDevice(pipe: dev)
        device.start()
        defer { device.stop() }
        let link = SecureLink(
            peripheral: app, maxFrame: VerdigrisPeripheral.maxFrame,
            security: LinkSecurity(identity: try LinkIdentity.generate(), pinnedPeerKey: nil))
        try await link.establish()
        let remote = await link.remoteStaticKey
        XCTAssertEqual(remote, device.identity.publicKey)
        // One sealed round trip so that the device has consumed message 3
        // before its record of the handshake is read.
        var request = Verdigris_Request()
        request.id = 1
        request.getSerialNumber = Verdigris_GetSerialNumberRequest()
        try await link.send(try request.serializedData())
        _ = try await link.receive()
        XCTAssertEqual(device.observedPattern, "XX")
        XCTAssertEqual(device.observedHandshakeLengths, [32, 97, 65])
    }

    func testPinnedInitiatorRunsIk() async throws {
        let (app, dev) = LoopbackPipe.pair(mtu: 20)
        let device = try FakeDevice(pipe: dev)
        device.start()
        defer { device.stop() }
        let link = SecureLink(
            peripheral: app, maxFrame: VerdigrisPeripheral.maxFrame,
            security: LinkSecurity(identity: try LinkIdentity.generate(), pinnedPeerKey: device.identity.publicKey))
        try await link.establish()
        let remote = await link.remoteStaticKey
        XCTAssertEqual(remote, device.identity.publicKey)
        XCTAssertEqual(device.observedPattern, "IK")
        XCTAssertEqual(device.observedHandshakeLengths, [97, 49])
    }

    func testSealedMessagesRoundTrip() async throws {
        let (app, dev) = LoopbackPipe.pair(mtu: 20)
        let device = try FakeDevice(pipe: dev)
        device.start()
        defer { device.stop() }
        let link = SecureLink(
            peripheral: app, maxFrame: VerdigrisPeripheral.maxFrame,
            security: LinkSecurity(identity: try LinkIdentity.generate(), pinnedPeerKey: nil))
        var request = Verdigris_Request()
        request.id = 7
        request.echo.payload = payload(300)
        try await link.send(try request.serializedData())
        let reply = try Verdigris_Response(serializedBytes: try await link.receive())
        XCTAssertEqual(reply.id, 7)
        XCTAssertEqual(reply.echo.payload, payload(300))
    }

    func testSendAfterCloseIsRejected() async throws {
        let (app, _) = LoopbackPipe.pair(mtu: 20)
        let link = SecureLink(
            peripheral: app, maxFrame: VerdigrisPeripheral.maxFrame,
            security: LinkSecurity(identity: try LinkIdentity.generate(), pinnedPeerKey: nil))
        await link.close()
        await assertThrows(try await link.send(Data([1]))) { error in
            if case .invalidOperation = error { return true }
            return false
        }
    }

    func testIdentityDerivesThePublicKey() throws {
        let identity = try LinkIdentity.generate()
        let again = try LinkIdentity(privateKey: identity.privateKey)
        XCTAssertEqual(again.publicKey, identity.publicKey)
        XCTAssertEqual(identity.publicKey.count, 32)
        XCTAssertThrowsError(try LinkIdentity(privateKey: Data(repeating: 1, count: 31)))
    }
}
