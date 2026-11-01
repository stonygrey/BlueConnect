// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { SegmentHeader } from '../core/segmentHeader';

test('pack places seq in the high nibble', () => {
  const cases: [number, number, number][] = [
    [0, SegmentHeader.start, 0x01],
    [0, SegmentHeader.end, 0x02],
    [0, SegmentHeader.start | SegmentHeader.end, 0x03],
    [0, SegmentHeader.abort, 0x04],
    [3, SegmentHeader.start, 0x31],
    [4, SegmentHeader.end, 0x42],
    [15, SegmentHeader.start | SegmentHeader.end, 0xf3],
  ];
  for (const [seq, flags, expected] of cases) {
    expect(SegmentHeader.pack(seq, flags)).toBe(expected);
  }
});

test('pack masks seq to four bits', () => {
  expect(SegmentHeader.pack(1, SegmentHeader.start)).toBe(SegmentHeader.pack(17, SegmentHeader.start));
});

test('pack does not emit the reserved bit', () => {
  expect(SegmentHeader.pack(0, SegmentHeader.reserved) & SegmentHeader.reserved).toBe(0);
});

test('seq and flags round trip', () => {
  const cases: [number, number, number][] = [
    [0x31, 3, SegmentHeader.start],
    [0x42, 4, SegmentHeader.end],
    [0xf3, 15, SegmentHeader.start | SegmentHeader.end],
  ];
  for (const [header, seq, flags] of cases) {
    expect(SegmentHeader.seq(header)).toBe(seq);
    expect(SegmentHeader.flags(header)).toBe(flags);
    expect(SegmentHeader.has(header, flags)).toBe(true);
  }
});
