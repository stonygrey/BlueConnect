// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import XCTest

final class NoisePayloadTests: XCTestCase {
    private func key(_ fill: UInt8) -> Data { Data(repeating: fill, count: 32) }

    func testCredentialRoundTripPreservesKeysAndTier() throws {
        var app = Data((0..<32).map { _ in UInt8.random(in: 0...255) })
        app[0] |= 1
        var device = Data((0..<32).map { _ in UInt8.random(in: 0...255) })
        device[0] |= 1
        let original = NoiseQrPayload(tier: .high, appPrivateKey: app, devicePublicKey: device, deviceName: "Verdigris 1")
        let parsed = try XCTUnwrap(NoiseQrPayload.parse(try original.encode()))
        XCTAssertEqual(parsed.tier, .high)
        XCTAssertEqual(parsed.appPrivateKey, app)
        XCTAssertEqual(parsed.devicePublicKey, device)
        XCTAssertEqual(parsed.deviceName, "Verdigris 1")
    }

    func testCredentialParseRejectsAllZeroKeys() throws {
        let zeros = Data(count: 64).base64EncodedString().replacingOccurrences(of: "=", with: "")
        XCTAssertNil(NoiseQrPayload.parse("SGNK1:L:\(zeros)"))
    }

    func testEnrollmentRoundTripsWithAndWithoutAName() throws {
        let named = NoiseEnrollmentPayload(enrollmentPrivateKey: key(1), devicePublicKey: key(2), deviceName: "Verdigris 0001")
        let parsed = try XCTUnwrap(NoiseEnrollmentPayload.parse(try named.encode()))
        XCTAssertEqual(parsed, named)
        XCTAssertTrue(try named.encode().hasPrefix("SGNE1:"))
        let anonymous = NoiseEnrollmentPayload(enrollmentPrivateKey: key(3), devicePublicKey: key(4), deviceName: nil)
        let parsed2 = try XCTUnwrap(NoiseEnrollmentPayload.parse(try anonymous.encode()))
        XCTAssertNil(parsed2.deviceName)
        XCTAssertEqual(parsed2.enrollmentPrivateKey, key(3))
        XCTAssertEqual(parsed2.devicePublicKey, key(4))
    }

    func testEachParserRejectsTheOtherPayloadAndBadBlobs() throws {
        let credential = try NoiseQrPayload(tier: .low, appPrivateKey: key(1), devicePublicKey: key(2), deviceName: nil).encode()
        XCTAssertNil(NoiseEnrollmentPayload.parse(credential))
        let enrollment = try NoiseEnrollmentPayload(enrollmentPrivateKey: key(1), devicePublicKey: key(2), deviceName: nil).encode()
        XCTAssertNil(NoiseQrPayload.parse(enrollment))
        XCTAssertNil(NoiseEnrollmentPayload.parse("SGNE1:not-base64!"))
        let short = Data(count: 63).base64EncodedString().replacingOccurrences(of: "=", with: "")
        XCTAssertNil(NoiseEnrollmentPayload.parse("SGNE1:\(short)"))
        XCTAssertThrowsError(try NoiseEnrollmentPayload(enrollmentPrivateKey: Data(count: 32), devicePublicKey: key(2), deviceName: nil).encode())
    }
}
