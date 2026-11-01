// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import XCTest

/// Commands against the in-process device: id matching, status mapping,
/// stale responses, result-type checks, and the command timeout.
final class VerdigrisPeripheralTests: XCTestCase {
    private func makePair(
        handler: @escaping FakeDevice.Handler = FakeDevice.echoHandler, commandTimeout: Duration = .seconds(5)
    ) throws -> (VerdigrisPeripheral, FakeDevice) {
        let (app, dev) = LoopbackPipe.pair(mtu: 20)
        let device = try FakeDevice(pipe: dev, handler: handler)
        device.start()
        var options = BlueConnectOptions()
        options.commandTimeout = commandTimeout
        let verdigris = VerdigrisPeripheral(
            peripheral: app, options: options,
            security: LinkSecurity(identity: try LinkIdentity.generate(), pinnedPeerKey: nil))
        return (verdigris, device)
    }

    func testEchoRoundTripsThroughTheSecureLink() async throws {
        let (verdigris, device) = try makePair()
        defer { device.stop() }
        let reply = try await verdigris.echo(payload(1536))
        XCTAssertEqual(reply, payload(1536))
        let remote = await verdigris.remoteStaticKey
        XCTAssertEqual(remote, device.identity.publicKey)
    }

    func testRequestIdsIncrementAndTheReplyIsMatched() async throws {
        let seen = IdRecorder()
        let (verdigris, device) = try makePair(handler: { request in
            seen.record(request.id)
            return FakeDevice.echoHandler(request)
        })
        defer { device.stop() }
        _ = try await verdigris.getSerialNumber()
        _ = try await verdigris.getSerialNumber()
        XCTAssertEqual(seen.ids, [1, 2])
    }

    func testStatusOtherThanOkBecomesVerdigrisError() async throws {
        let (verdigris, device) = try makePair(handler: { request in
            var response = Verdigris_Response()
            response.id = request.id
            response.status = .permissionDenied
            response.detail = "high privilege required"
            return [response]
        })
        defer { device.stop() }
        do {
            _ = try await verdigris.echo(Data([1]))
            XCTFail("expected VerdigrisError")
        } catch let error as VerdigrisError {
            XCTAssertEqual(error.status, .permissionDenied)
            XCTAssertEqual(error.detail, "high privilege required")
            XCTAssertEqual(error.description, "The device rejected the request: permissionDenied (high privilege required)")
        }
    }

    func testStaleResponsesAreDropped() async throws {
        let (verdigris, device) = try makePair(handler: { request in
            var stale = Verdigris_Response()
            stale.id = request.id + 100
            stale.status = .ok
            stale.name.name = "stale"
            var real = Verdigris_Response()
            real.id = request.id
            real.status = .ok
            real.name.name = "Verdigris 0042"
            return [stale, real]
        })
        defer { device.stop() }
        let name = try await verdigris.getSerialNumber()
        XCTAssertEqual(name, "Verdigris 0042")
    }

    func testWrongResultTypeIsATransportError() async throws {
        let (verdigris, device) = try makePair(handler: { request in
            var response = Verdigris_Response()
            response.id = request.id
            response.status = .ok
            response.led.on = true
            return [response]
        })
        defer { device.stop() }
        await assertThrows(try await verdigris.getSerialNumber()) { error in
            if case .transport(let message) = error { return message.contains("where name was expected") }
            return false
        }
    }

    func testCommandTimesOutWhenTheDeviceIsSilent() async throws {
        let (verdigris, device) = try makePair(handler: { _ in [] }, commandTimeout: .milliseconds(200))
        defer { device.stop() }
        await assertThrows(try await verdigris.getLed()) { error in
            if case .timeout(let message) = error { return message.contains("timed out after 0s") }
            return false
        }
    }

    func testPairReturnsTheDeviceKey() async throws {
        let (verdigris, device) = try makePair()
        defer { device.stop() }
        let key = try await verdigris.pair(.high)
        XCTAssertEqual(key, device.identity.publicKey)
    }

    func testOversizeRequestIsRejectedBeforeSending() async throws {
        let (verdigris, device) = try makePair()
        defer { device.stop() }
        await assertThrows(try await verdigris.echo(payload(VerdigrisPeripheral.maxEncodedRequest))) { error in
            if case .invalidOperation(let message) = error { return message.contains("Encoded request") }
            return false
        }
    }
}

final class IdRecorder: @unchecked Sendable {
    private let lock = NSLock()
    private var recorded: [UInt32] = []

    func record(_ id: UInt32) {
        lock.lock()
        recorded.append(id)
        lock.unlock()
    }

    var ids: [UInt32] {
        lock.lock()
        defer { lock.unlock() }
        return recorded
    }
}
