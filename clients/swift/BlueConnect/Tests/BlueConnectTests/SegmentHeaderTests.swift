// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import BlueConnect
import XCTest

final class SegmentHeaderTests: XCTestCase {
    func testPackPlacesSeqInHighNibble() {
        let cases: [(Int, UInt8, UInt8)] = [
            (0, SegmentHeader.start, 0x01),
            (0, SegmentHeader.end, 0x02),
            (0, SegmentHeader.start | SegmentHeader.end, 0x03),
            (0, SegmentHeader.abort, 0x04),
            (3, SegmentHeader.start, 0x31),
            (4, SegmentHeader.end, 0x42),
            (15, SegmentHeader.start | SegmentHeader.end, 0xF3),
        ]
        for (seq, flags, expected) in cases {
            XCTAssertEqual(SegmentHeader.pack(seq: seq, flags: flags), expected)
        }
    }

    func testPackMasksSeqToFourBits() {
        XCTAssertEqual(SegmentHeader.pack(seq: 1, flags: SegmentHeader.start), SegmentHeader.pack(seq: 17, flags: SegmentHeader.start))
    }

    func testPackDoesNotEmitTheReservedBit() {
        XCTAssertEqual(SegmentHeader.pack(seq: 0, flags: SegmentHeader.reserved) & SegmentHeader.reserved, 0)
    }

    func testSeqAndFlagsRoundTrip() {
        let cases: [(UInt8, Int, UInt8)] = [
            (0x31, 3, SegmentHeader.start),
            (0x42, 4, SegmentHeader.end),
            (0xF3, 15, SegmentHeader.start | SegmentHeader.end),
        ]
        for (header, seq, flags) in cases {
            XCTAssertEqual(SegmentHeader.seq(header), seq)
            XCTAssertEqual(SegmentHeader.flags(header), flags)
            XCTAssertTrue(SegmentHeader.has(header, flags))
        }
    }
}
