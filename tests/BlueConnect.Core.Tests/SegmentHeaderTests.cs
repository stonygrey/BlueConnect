// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace StonyGrey.BlueConnect.Core.Tests;

public sealed class SegmentHeaderTests
{
    [Theory]
    [InlineData(0, SegmentHeader.Start, 0x01)]
    [InlineData(0, SegmentHeader.End, 0x02)]
    [InlineData(0, SegmentHeader.Start | SegmentHeader.End, 0x03)]
    [InlineData(0, SegmentHeader.Abort, 0x04)]
    [InlineData(3, SegmentHeader.Start, 0x31)]
    [InlineData(4, SegmentHeader.End, 0x42)]
    [InlineData(15, SegmentHeader.Start | SegmentHeader.End, 0xF3)]
    public void Pack_places_seq_in_high_nibble(int seq, byte flags, byte expected)
        => Assert.Equal(expected, SegmentHeader.Pack(seq, flags));

    [Fact]
    public void Pack_masks_seq_to_four_bits()
        => Assert.Equal(SegmentHeader.Pack(1, SegmentHeader.Start), SegmentHeader.Pack(17, SegmentHeader.Start));

    [Fact]
    public void Pack_does_not_emit_the_reserved_bit()
        => Assert.Equal(0, SegmentHeader.Pack(0, SegmentHeader.Reserved) & SegmentHeader.Reserved);

    [Theory]
    [InlineData(0x31, 3, SegmentHeader.Start)]
    [InlineData(0x42, 4, SegmentHeader.End)]
    [InlineData(0xF3, 15, SegmentHeader.Start | SegmentHeader.End)]
    public void Seq_and_Flags_round_trip(byte header, int seq, byte flags)
    {
        Assert.Equal(seq, SegmentHeader.Seq(header));
        Assert.Equal(flags, SegmentHeader.Flags(header));
        Assert.True(SegmentHeader.Has(header, flags));
    }
}
