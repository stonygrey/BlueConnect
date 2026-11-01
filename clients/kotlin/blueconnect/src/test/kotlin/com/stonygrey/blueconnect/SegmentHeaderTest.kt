// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect

import com.stonygrey.blueconnect.core.SegmentHeader
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class SegmentHeaderTest {
    @Test
    fun packPlacesSeqInHighNibble() {
        val cases = listOf(
            Triple(0, SegmentHeader.START, 0x01),
            Triple(0, SegmentHeader.END, 0x02),
            Triple(0, SegmentHeader.START or SegmentHeader.END, 0x03),
            Triple(0, SegmentHeader.ABORT, 0x04),
            Triple(3, SegmentHeader.START, 0x31),
            Triple(4, SegmentHeader.END, 0x42),
            Triple(15, SegmentHeader.START or SegmentHeader.END, 0xF3),
        )
        for ((seq, flags, expected) in cases) {
            assertEquals(expected, SegmentHeader.pack(seq, flags))
        }
    }

    @Test
    fun packMasksSeqToFourBits() {
        assertEquals(SegmentHeader.pack(1, SegmentHeader.START), SegmentHeader.pack(17, SegmentHeader.START))
    }

    @Test
    fun packDoesNotEmitTheReservedBit() {
        assertEquals(0, SegmentHeader.pack(0, SegmentHeader.RESERVED) and SegmentHeader.RESERVED)
    }

    @Test
    fun seqAndFlagsRoundTrip() {
        val cases = listOf(
            Triple(0x31, 3, SegmentHeader.START),
            Triple(0x42, 4, SegmentHeader.END),
            Triple(0xF3, 15, SegmentHeader.START or SegmentHeader.END),
        )
        for ((header, seq, flags) in cases) {
            assertEquals(seq, SegmentHeader.seq(header))
            assertEquals(flags, SegmentHeader.flags(header))
            assertTrue(SegmentHeader.has(header, flags))
        }
    }
}
