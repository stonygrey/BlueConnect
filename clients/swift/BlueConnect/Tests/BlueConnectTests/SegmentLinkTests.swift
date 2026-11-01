// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import XCTest

final class SegmentLinkTests: XCTestCase {
    private let cap = 2304

    func testRoundTripAtDefaultMtu() async throws {
        for length in [0, 1, 19, 20, 38, 323] {
            let message = payload(length)
            let echoed = try await echo(message, mtu: 20)
            XCTAssertEqual(echoed, message, "length \(length)")
        }
    }

    func testRoundTripExactlyOneSegment() async throws {
        let echoed = try await echo(payload(19), mtu: 20)
        XCTAssertEqual(echoed, payload(19))
    }

    func testRoundTripOneByteIntoASecondSegment() async throws {
        let echoed = try await echo(payload(20), mtu: 20)
        XCTAssertEqual(echoed, payload(20))
    }

    func testRoundTripWrapsSeqModulo16() async throws {
        // 17 segments at P=20 (cap 19) forces seq to wrap.
        let message = payload(17 * 19)
        let echoed = try await echo(message, mtu: 20)
        XCTAssertEqual(echoed, message)
    }

    func testTwoMessagesOnTheSameLink() async throws {
        let (a, b) = LoopbackPipe.pair(mtu: 20)
        let send = SegmentLink(peripheral: a, maxMessage: cap)
        let recv = SegmentLink(peripheral: b, maxMessage: cap)
        let first = try await sendAndReceive(send, recv, payload(40))
        let second = try await sendAndReceive(send, recv, payload(7))
        XCTAssertEqual(first, payload(40))
        XCTAssertEqual(second, payload(7))
    }

    func testWriteEmitsStartEndAndConsecutiveSeq() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        try await link.write(payload(40))
        let written = pipe.written
        XCTAssertEqual(written.count, 3)
        XCTAssertTrue(SegmentHeader.has(written[0][0], SegmentHeader.start))
        XCTAssertFalse(SegmentHeader.has(written[0][0], SegmentHeader.end))
        XCTAssertFalse(SegmentHeader.has(written[1][0], SegmentHeader.start))
        XCTAssertFalse(SegmentHeader.has(written[1][0], SegmentHeader.end))
        XCTAssertTrue(SegmentHeader.has(written[2][0], SegmentHeader.end))
        XCTAssertFalse(SegmentHeader.has(written[2][0], SegmentHeader.start))
        let seq0 = SegmentHeader.seq(written[0][0])
        XCTAssertEqual(SegmentHeader.seq(written[1][0]), (seq0 + 1) % 16)
        XCTAssertEqual(SegmentHeader.seq(written[2][0]), (seq0 + 2) % 16)
        XCTAssertEqual(written[0].count - 1, 19)
        XCTAssertEqual(written[1].count - 1, 19)
        XCTAssertEqual(written[2].count - 1, 2)
    }

    func testSingleSegmentSetsStartAndEnd() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        try await link.write(payload(3))
        XCTAssertEqual(pipe.written.count, 1)
        XCTAssertEqual(SegmentHeader.flags(pipe.written[0][0]), SegmentHeader.start | SegmentHeader.end)
    }

    func testEmptyMessageIsStartEndWithNoData() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        try await link.write(Data())
        XCTAssertEqual(pipe.written.count, 1)
        XCTAssertEqual(pipe.written[0].count, 1)
        XCTAssertEqual(SegmentHeader.flags(pipe.written[0][0]), SegmentHeader.start | SegmentHeader.end)
    }

    func testWriteRejectsOversizeMessage() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: 8)
        await assertThrows(try await link.write(payload(9))) { error in
            if case .invalidOperation = error { return true }
            return false
        }
        XCTAssertTrue(pipe.written.isEmpty)
    }

    func testWriteRejectsNonEmptyMessageWhenMtuIs1() async throws {
        let pipe = ScriptedPipe()
        pipe.mtu = 1
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        await assertThrows(try await link.write(payload(1))) { error in
            if case .transport = error { return true }
            return false
        }
    }

    func testWriteSendsAbortIfALaterSegmentFails() async throws {
        let pipe = ScriptedPipe()
        pipe.failOnWrite(1)
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        await assertThrows(try await link.write(payload(40))) { error in
            if case .transport = error { return true }
            return false
        }
        let written = pipe.written
        XCTAssertEqual(written.count, 2)
        XCTAssertTrue(SegmentHeader.has(written[0][0], SegmentHeader.start))
        XCTAssertTrue(SegmentHeader.has(written[1][0], SegmentHeader.abort))
        let startSeq = SegmentHeader.seq(written[0][0])
        // The failed segment used startSeq+1; the abort occupies the next slot.
        XCTAssertEqual(SegmentHeader.seq(written[1][0]), (startSeq + 2) % SegmentHeader.seqModulus)
    }

    func testReadEmptyPduIsMalformed() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        pipe.enqueue()
        await assertThrows(try await link.read()) { $0.description.contains("Malformed") }
        XCTAssertEqual(pipe.written.count, 1)
        XCTAssertTrue(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort))
    }

    func testReadReservedBitIsMalformed() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        pipe.enqueue(SegmentHeader.pack(seq: 0, flags: SegmentHeader.start | SegmentHeader.end) | SegmentHeader.reserved, 0xAA)
        await assertThrows(try await link.read()) { $0.description.contains("Malformed") }
        XCTAssertEqual(pipe.written.count, 1)
        XCTAssertTrue(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort))
    }

    func testReadOrphanContinuationFails() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        pipe.enqueue(SegmentHeader.pack(seq: 1, flags: SegmentHeader.end), 0xAA)
        await assertThrows(try await link.read()) { $0.description.contains("Orphan") }
        XCTAssertEqual(pipe.written.count, 1)
        XCTAssertTrue(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort))
    }

    func testReadSequenceGapFails() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        pipe.enqueue(SegmentHeader.pack(seq: 3, flags: SegmentHeader.start), 0xAA)
        pipe.enqueue(SegmentHeader.pack(seq: 5, flags: SegmentHeader.end), 0xBB)
        await assertThrows(try await link.read()) { $0.description.lowercased().contains("gap") }
        XCTAssertEqual(pipe.written.count, 1)
        XCTAssertTrue(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort))
    }

    func testReadAbortWhileAssemblingFails() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        pipe.enqueue(SegmentHeader.pack(seq: 0, flags: SegmentHeader.start), 0xAA)
        pipe.enqueue(SegmentHeader.pack(seq: 1, flags: SegmentHeader.abort))
        await assertThrows(try await link.read()) { $0.description.lowercased().contains("aborted") }
    }

    func testReadAbortWhileIdleIsIgnored() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        pipe.enqueue(SegmentHeader.pack(seq: 0, flags: SegmentHeader.abort))
        pipe.enqueue(SegmentHeader.pack(seq: 2, flags: SegmentHeader.start | SegmentHeader.end), 0x42)
        let message = try await link.read()
        XCTAssertEqual(message, Data([0x42]))
        XCTAssertTrue(pipe.written.isEmpty)
    }

    func testReadStartWhileAssemblingDiscardsThePartial() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        pipe.enqueue(SegmentHeader.pack(seq: 0, flags: SegmentHeader.start), 0xAA, 0xBB)
        pipe.enqueue(SegmentHeader.pack(seq: 9, flags: SegmentHeader.start | SegmentHeader.end), 0xCC)
        let message = try await link.read()
        XCTAssertEqual(message, Data([0xCC]))
    }

    func testReadRejectsMessageOverTheCap() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: 2)
        pipe.enqueue(SegmentHeader.pack(seq: 0, flags: SegmentHeader.start | SegmentHeader.end), 0x01, 0x02, 0x03)
        await assertThrows(try await link.read()) { $0.description.contains("exceeds") }
        XCTAssertEqual(pipe.written.count, 1)
        XCTAssertTrue(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort))
    }

    func testReadTimesOutAnIncompleteMessage() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: cap, incompleteTimeout: .milliseconds(40))
        pipe.enqueue(SegmentHeader.pack(seq: 0, flags: SegmentHeader.start), 0xAA)
        await assertThrows(try await link.read()) { $0.description.contains("timed out") }
        XCTAssertEqual(pipe.written.count, 1)
        XCTAssertTrue(SegmentHeader.has(pipe.written[0][0], SegmentHeader.abort))
    }

    func testAfterAGapTheNextReadAcceptsAFreshStart() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        pipe.enqueue(SegmentHeader.pack(seq: 0, flags: SegmentHeader.start), 0xAA)
        pipe.enqueue(SegmentHeader.pack(seq: 2, flags: SegmentHeader.end), 0xBB)
        await assertThrows(try await link.read()) { _ in true }
        pipe.enqueue(SegmentHeader.pack(seq: 7, flags: SegmentHeader.start | SegmentHeader.end), 0xCC)
        let message = try await link.read()
        XCTAssertEqual(message, Data([0xCC]))
    }

    func testCallerCancellationIsNotAnIncompleteTimeout() async throws {
        let pipe = ScriptedPipe()
        let link = SegmentLink(peripheral: pipe, maxMessage: cap)
        let read = Task { try await link.read() }
        try await Task.sleep(for: .milliseconds(20))
        read.cancel()
        do {
            _ = try await read.value
            XCTFail("expected cancellation")
        } catch is CancellationError {
            // expected
        }
        XCTAssertTrue(pipe.written.isEmpty)
    }

    private func echo(_ message: Data, mtu: Int) async throws -> Data {
        let (a, b) = LoopbackPipe.pair(mtu: mtu)
        let send = SegmentLink(peripheral: a, maxMessage: cap)
        let recv = SegmentLink(peripheral: b, maxMessage: cap)
        return try await sendAndReceive(send, recv, message)
    }

    private func sendAndReceive(_ send: SegmentLink, _ recv: SegmentLink, _ message: Data) async throws -> Data {
        let receive = Task { try await recv.read() }
        try await send.write(message)
        return try await receive.value
    }
}

/// Asserts that `body` throws a `BlueConnectError` accepted by `check`.
func assertThrows(
    _ body: @autoclosure () async throws -> some Any,
    file: StaticString = #filePath, line: UInt = #line,
    _ check: (BlueConnectError) -> Bool
) async {
    do {
        _ = try await body()
        XCTFail("expected a BlueConnectError", file: file, line: line)
    } catch let error as BlueConnectError {
        XCTAssertTrue(check(error), "unexpected error: \(error)", file: file, line: line)
    } catch {
        XCTFail("expected a BlueConnectError, got \(error)", file: file, line: line)
    }
}
