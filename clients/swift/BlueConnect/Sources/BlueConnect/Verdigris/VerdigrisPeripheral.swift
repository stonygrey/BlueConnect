// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation
import SwiftProtobuf

/// The privilege tier a pairing grants.
public enum Privilege: Sendable, Equatable {
    case low
    case high

    var proto: Verdigris_Privilege {
        switch self {
        case .low: .low
        case .high: .high
        }
    }
}

/// `VerdigrisException`: the device answered with a status other than OK.
public struct VerdigrisError: Error, Sendable, CustomStringConvertible {
    public let status: Verdigris_Status
    public let detail: String?

    public init(status: Verdigris_Status, detail: String?) {
        self.status = status
        self.detail = detail.flatMap { $0.isEmpty ? nil : $0 }
    }

    public var description: String {
        let detail = self.detail.map { " (\($0))" } ?? ""
        return "The device rejected the request: \(status)\(detail)"
    }

    public var localizedDescription: String { description }
}

/// Typed Verdigris commands over a secure link. One command at a time; each
/// request carries a fresh id and the matching response is awaited under
/// `BlueConnectOptions.commandTimeout`.
public actor VerdigrisPeripheral {
    /// Cap on a reassembled GATT message. The value is arbitrary and specific
    /// to this implementation; it is not negotiated, so it must equal the
    /// firmware's `SEG_MAX_MESSAGE` and the two should be kept in sync.
    public static let maxFrame = 2304
    public static var maxEncodedRequest: Int { maxFrame - 1 - LinkProtocol.tagLength }
    /// Longer than the firmware's 30 s pairing window (`PAIR_WINDOW_SECONDS`),
    /// an arbitrary implementation value. The two should be kept in sync, so
    /// that the app is still waiting when the device answers at the end of the
    /// window.
    static let pairTimeout: Duration = .seconds(40)

    public let peripheral: any BlePeripheral
    private let link: SecureLink
    private let commandTimeout: Duration
    private let gate = AsyncGate()
    private var nextId: UInt32 = 0
    private var closed = false

    public init(peripheral: any BlePeripheral, options: BlueConnectOptions, security: LinkSecurity) {
        self.peripheral = peripheral
        commandTimeout = options.commandTimeout
        link = SecureLink(peripheral: peripheral, maxFrame: Self.maxFrame, security: security)
    }

    /// The device's proven static key once a session has been established.
    public var remoteStaticKey: Data? {
        get async { await link.remoteStaticKey }
    }

    public func echo(_ payload: Data) async throws -> Data {
        var request = Verdigris_Request()
        request.echo.payload = payload
        return try await invoke(request, expecting: .echo).echo.payload
    }

    public func squareRoot(_ value: Double) async throws -> Double {
        var request = Verdigris_Request()
        request.sqrt.value = value
        return try await invoke(request, expecting: .sqrt).sqrt.value
    }

    public func fibonacci(_ n: UInt32) async throws -> UInt64 {
        var request = Verdigris_Request()
        request.fib.n = n
        return try await invoke(request, expecting: .fib).fib.value
    }

    public func getTime() async throws -> Date {
        var request = Verdigris_Request()
        request.getTime = Verdigris_GetTimeRequest()
        let millis = try await invoke(request, expecting: .time).time.unixMillis
        return Date(timeIntervalSince1970: TimeInterval(millis) / 1000)
    }

    public func setTime(_ time: Date) async throws {
        var request = Verdigris_Request()
        request.setTime.unixMillis = Int64((time.timeIntervalSince1970 * 1000).rounded())
        _ = try await invoke(request, expecting: .time)
    }

    public func getLed() async throws -> Bool {
        var request = Verdigris_Request()
        request.getLed = Verdigris_GetLedRequest()
        return try await invoke(request, expecting: .led).led.on
    }

    public func setLed(_ on: Bool) async throws -> Bool {
        var request = Verdigris_Request()
        request.setLed.on = on
        return try await invoke(request, expecting: .led).led.on
    }

    public func setSerialNumber(_ serialNumber: UInt16) async throws -> String {
        var request = Verdigris_Request()
        request.setSerialNumber.serialNumber = UInt32(serialNumber)
        return try await invoke(request, expecting: .name).name.name
    }

    public func getSerialNumber() async throws -> String {
        var request = Verdigris_Request()
        request.getSerialNumber = Verdigris_GetSerialNumberRequest()
        return try await invoke(request, expecting: .name).name.name
    }

    /// Arms the device's 30 s pairing window for `privilege` and waits for the
    /// button. Returns the device key to pin.
    public func pair(_ privilege: Privilege) async throws -> Data {
        var request = Verdigris_Request()
        request.pair.privilege = privilege.proto
        _ = try await invoke(request, expecting: .ack, timeout: Self.pairTimeout)
        return await link.remoteStaticKey!
    }

    public func unpair() async throws {
        var request = Verdigris_Request()
        request.unpair = Verdigris_UnpairRequest()
        _ = try await invoke(request, expecting: .ack)
    }

    public func enroll(clientPublicKey: Data) async throws {
        var request = Verdigris_Request()
        request.enroll.clientPublicKey = clientPublicKey
        _ = try await invoke(request, expecting: .ack)
    }

    /// Closes the link and the peripheral, disconnecting first if connected.
    public func close() async {
        if closed {
            return
        }
        closed = true
        await link.close()
        await peripheral.close()
    }

    private func invoke(
        _ request: Verdigris_Request, expecting expected: Verdigris_Response.OneOf_Result.Case, timeout: Duration? = nil
    ) async throws -> Verdigris_Response {
        if closed {
            throw BlueConnectError.invalidOperation("The Verdigris peripheral has been closed.")
        }
        let commandTimeout = timeout ?? self.commandTimeout
        await gate.acquire()
        defer { Task { await gate.release() } }
        nextId &+= 1
        var request = request
        request.id = nextId
        let id = nextId
        let body = try request.serializedData()
        if body.count > Self.maxEncodedRequest {
            throw BlueConnectError.invalidOperation(
                "Encoded request is \(body.count) bytes; the maximum is \(Self.maxEncodedRequest) (type + AEAD tag must fit under \(Self.maxFrame)).")
        }
        let link = self.link
        do {
            return try await withTimeout(commandTimeout) {
                try await link.send(body)
                while true {
                    let raw = try await link.receive()
                    let response: Verdigris_Response
                    do {
                        response = try Verdigris_Response(serializedBytes: raw)
                    } catch {
                        throw BlueConnectError.transport("Undecodable Verdigris frame; the stream is desynchronized.")
                    }
                    if response.id != id {
                        IoLog.logger.debug("Dropping stale Verdigris response \(response.id) while awaiting \(id).")
                        continue
                    }
                    if response.status != .ok {
                        throw VerdigrisError(status: response.status, detail: response.detail)
                    }
                    if response.result?.caseValue != expected {
                        throw BlueConnectError.transport(
                            "Verdigris response carried \(response.result.map { String(describing: $0.caseValue) } ?? "nothing") where \(expected) was expected.")
                    }
                    return response
                }
            }
        } catch is DeadlineExceeded {
            throw BlueConnectError.timeout("Verdigris command timed out after \(Int(commandTimeout.seconds))s.")
        }
    }
}

extension Verdigris_Response.OneOf_Result {
    /// The case without its payload, for comparison with the expected result.
    enum Case: Equatable {
        case echo, sqrt, fib, time, name, ack, led
    }

    var caseValue: Case {
        switch self {
        case .echo: .echo
        case .sqrt: .sqrt
        case .fib: .fib
        case .time: .time
        case .name: .name
        case .ack: .ack
        case .led: .led
        }
    }
}
