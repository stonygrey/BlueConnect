// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import 'package:blueconnect/blueconnect.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('pack places seq in the high nibble', () {
    const cases = [
      (0, SegmentHeader.start, 0x01),
      (0, SegmentHeader.end, 0x02),
      (0, SegmentHeader.start | SegmentHeader.end, 0x03),
      (0, SegmentHeader.abort, 0x04),
      (3, SegmentHeader.start, 0x31),
      (4, SegmentHeader.end, 0x42),
      (15, SegmentHeader.start | SegmentHeader.end, 0xF3),
    ];
    for (final (seq, flags, expected) in cases) {
      expect(SegmentHeader.pack(seq, flags), expected);
    }
  });

  test('pack masks seq to four bits', () {
    expect(SegmentHeader.pack(1, SegmentHeader.start), SegmentHeader.pack(17, SegmentHeader.start));
  });

  test('pack does not emit the reserved bit', () {
    expect(SegmentHeader.pack(0, SegmentHeader.reserved) & SegmentHeader.reserved, 0);
  });

  test('seq and flags round trip', () {
    const cases = [
      (0x31, 3, SegmentHeader.start),
      (0x42, 4, SegmentHeader.end),
      (0xF3, 15, SegmentHeader.start | SegmentHeader.end),
    ];
    for (final (header, seq, flags) in cases) {
      expect(SegmentHeader.seq(header), seq);
      expect(SegmentHeader.flags(header), flags);
      expect(SegmentHeader.has(header, flags), isTrue);
    }
  });
}
