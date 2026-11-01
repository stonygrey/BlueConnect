// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation

/// Message pipe over a peripheral: one `write` is one buffer, one `read`
/// returns one complete buffer. Each ATT value is a self-describing segment
/// (`documents/ble-framing.md`). The caller owns the peripheral.
public actor SegmentLink {
    public static let defaultIncompleteTimeout: Duration = .seconds(5)
    /// Bound on the best-effort `ABORT` after a failed `write`, which cannot
    /// run under the caller's task because that task may already be cancelled.
    public static let abortWriteTimeout: Duration = .seconds(2)

    private let peripheral: any BlePeripheral
    private let maxMessage: Int
    private let incompleteTimeout: Duration
    private let send = AsyncGate()
    private var acc = Data()
    private var assembling = false
    private var seqIn = 0

    public init(peripheral: any BlePeripheral, maxMessage: Int, incompleteTimeout: Duration = SegmentLink.defaultIncompleteTimeout) {
        precondition(maxMessage > 0, "maxMessage must be positive")
        self.peripheral = peripheral
        self.maxMessage = maxMessage
        self.incompleteTimeout = incompleteTimeout
    }

    public func write(_ message: Data) async throws {
        if message.count > maxMessage {
            throw BlueConnectError.invalidOperation("Message is \(message.count) bytes; the maximum is \(maxMessage).")
        }
        await send.acquire()
        defer { Task { await send.release() } }
        let payloadMtu = max(1, peripheral.mtu)
        let cap = max(0, payloadMtu - 1)
        if cap < 1 && !message.isEmpty {
            throw BlueConnectError.transport("ATT payload is too small to carry a segment.")
        }
        var seq = Int.random(in: 0..<SegmentHeader.seqModulus)
        var offset = 0
        var first = true
        do {
            repeat {
                let n = min(cap, message.count - offset)
                let last = offset + n == message.count
                var flags: UInt8 = 0
                if first { flags |= SegmentHeader.start }
                if last { flags |= SegmentHeader.end }
                var segment = Data(capacity: 1 + n)
                segment.append(SegmentHeader.pack(seq: seq, flags: flags))
                if n > 0 {
                    segment.append(message.subdata(in: (message.startIndex + offset)..<(message.startIndex + offset + n)))
                }
                IoLog.logger.info("\(IoLog.segment("write", seq: seq, flags: flags, bytes: segment), privacy: .public)")
                try await peripheral.write(segment)
                offset += n
                first = false
                seq = (seq + 1) % SegmentHeader.seqModulus
            } while offset < message.count
        } catch {
            // The caller's task may already be cancelled; the abort runs in a
            // task of its own so that it is attempted regardless, bounded in time.
            let abortSeq = (seq + 1) % SegmentHeader.seqModulus
            let peripheral = self.peripheral
            await Task {
                try? await withTimeout(SegmentLink.abortWriteTimeout) {
                    try await SegmentLink.sendAbort(peripheral, seq: abortSeq)
                }
            }.value
            throw error
        }
    }

    public func read() async throws -> Data {
        resetRx()
        while true {
            let pdu: Data
            if assembling {
                let peripheral = self.peripheral
                do {
                    pdu = try await withTimeout(incompleteTimeout) { try await peripheral.read() }
                } catch is DeadlineExceeded {
                    resetRx()
                    await trySendAbort(seq: 0)
                    throw BlueConnectError.transport("Incomplete message: timed out waiting for the next segment.")
                }
            } else {
                pdu = try await peripheral.read()
            }
            logInbound(pdu)
            guard let header = pdu.first, !SegmentHeader.has(header, SegmentHeader.reserved) else {
                resetRx()
                await trySendAbort(seq: 0)
                throw BlueConnectError.transport("Malformed segment header.")
            }
            if SegmentHeader.has(header, SegmentHeader.abort) {
                let wasAssembling = assembling
                resetRx()
                if wasAssembling {
                    throw BlueConnectError.transport("Peer aborted the message.")
                }
                continue
            }
            let seq = SegmentHeader.seq(header)
            let data = pdu.dropFirst()
            if SegmentHeader.has(header, SegmentHeader.start) {
                assembling = true
                seqIn = seq
                acc = Data(data)
            } else {
                if !assembling {
                    await trySendAbort(seq: 0)
                    throw BlueConnectError.transport("Orphan continuation segment.")
                }
                if seq != (seqIn + 1) % SegmentHeader.seqModulus {
                    resetRx()
                    await trySendAbort(seq: 0)
                    throw BlueConnectError.transport("Segment sequence gap.")
                }
                seqIn = seq
                acc.append(contentsOf: data)
            }
            if acc.count > maxMessage {
                resetRx()
                await trySendAbort(seq: 0)
                throw BlueConnectError.transport("Message exceeds the \(maxMessage)-byte cap.")
            }
            if SegmentHeader.has(header, SegmentHeader.end) {
                let complete = acc
                resetRx()
                return complete
            }
        }
    }

    private func logInbound(_ pdu: Data) {
        guard let header = pdu.first else {
            IoLog.logger.info("\(IoLog.segment("read", seq: 0, flags: 0, bytes: pdu), privacy: .public)")
            return
        }
        IoLog.logger.info("\(IoLog.segment("read", seq: SegmentHeader.seq(header), flags: SegmentHeader.flags(header), bytes: pdu), privacy: .public)")
    }

    private func resetRx() {
        assembling = false
        acc = Data()
    }

    /// Best effort: the read that triggered this already failed.
    private func trySendAbort(seq: Int) async {
        await send.acquire()
        defer { Task { await send.release() } }
        try? await SegmentLink.sendAbort(peripheral, seq: seq)
    }

    private static func sendAbort(_ peripheral: any BlePeripheral, seq: Int) async throws {
        let abort = Data([SegmentHeader.pack(seq: seq, flags: SegmentHeader.abort)])
        IoLog.logger.info("\(IoLog.segment("write", seq: seq, flags: SegmentHeader.abort, bytes: abort), privacy: .public)")
        try await peripheral.write(abort)
    }
}
