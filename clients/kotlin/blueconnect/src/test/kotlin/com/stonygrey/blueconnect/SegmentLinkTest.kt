// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect

import com.stonygrey.blueconnect.abstractions.BleTransportException
import com.stonygrey.blueconnect.core.SegmentHeader
import com.stonygrey.blueconnect.core.SegmentLink
import kotlin.time.Duration.Companion.milliseconds
import kotlinx.coroutines.async
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test

class SegmentLinkTest {
    private val cap = 2304

    private fun header(pipe: ScriptedPipe, index: Int): Int = pipe.written[index][0].toInt() and 0xFF

    private inline fun <reified T : Throwable> assertThrows(message: String? = null, body: () -> Unit): T {
        try {
            body()
        } catch (e: Throwable) {
            if (e is T) {
                if (message != null) assertTrue("'${e.message}' lacks '$message'", e.message!!.contains(message, ignoreCase = true))
                return e
            }
            throw e
        }
        fail("expected ${T::class.simpleName}")
        throw IllegalStateException()
    }

    @Test
    fun roundTripAtDefaultMtu() = runBlocking {
        for (length in listOf(0, 1, 19, 20, 38, 323)) {
            assertArrayEquals("length $length", payload(length), echo(payload(length), 20))
        }
    }

    @Test
    fun roundTripExactlyOneSegment() = runBlocking { assertArrayEquals(payload(19), echo(payload(19), 20)) }

    @Test
    fun roundTripOneByteIntoASecondSegment() = runBlocking { assertArrayEquals(payload(20), echo(payload(20), 20)) }

    @Test
    fun roundTripWrapsSeqModulo16() = runBlocking {
        // 17 segments at P=20 (cap 19) forces seq to wrap.
        assertArrayEquals(payload(17 * 19), echo(payload(17 * 19), 20))
    }

    @Test
    fun twoMessagesOnTheSameLink() = runBlocking {
        val (a, b) = LoopbackPipe.pair(20)
        val send = SegmentLink(a, cap)
        val recv = SegmentLink(b, cap)
        assertArrayEquals(payload(40), sendAndReceive(send, recv, payload(40)))
        assertArrayEquals(payload(7), sendAndReceive(send, recv, payload(7)))
    }

    @Test
    fun writeEmitsStartEndAndConsecutiveSeq() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, cap)
        link.write(payload(40))
        assertEquals(3, pipe.written.size)
        assertTrue(SegmentHeader.has(header(pipe, 0), SegmentHeader.START))
        assertFalse(SegmentHeader.has(header(pipe, 0), SegmentHeader.END))
        assertFalse(SegmentHeader.has(header(pipe, 1), SegmentHeader.START))
        assertFalse(SegmentHeader.has(header(pipe, 1), SegmentHeader.END))
        assertTrue(SegmentHeader.has(header(pipe, 2), SegmentHeader.END))
        assertFalse(SegmentHeader.has(header(pipe, 2), SegmentHeader.START))
        val seq0 = SegmentHeader.seq(header(pipe, 0))
        assertEquals((seq0 + 1) % 16, SegmentHeader.seq(header(pipe, 1)))
        assertEquals((seq0 + 2) % 16, SegmentHeader.seq(header(pipe, 2)))
        assertEquals(19, pipe.written[0].size - 1)
        assertEquals(19, pipe.written[1].size - 1)
        assertEquals(2, pipe.written[2].size - 1)
    }

    @Test
    fun singleSegmentSetsStartAndEnd() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, cap)
        link.write(payload(3))
        assertEquals(1, pipe.written.size)
        assertEquals(SegmentHeader.START or SegmentHeader.END, SegmentHeader.flags(header(pipe, 0)))
    }

    @Test
    fun emptyMessageIsStartEndWithNoData() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, cap)
        link.write(ByteArray(0))
        assertEquals(1, pipe.written.size)
        assertEquals(1, pipe.written[0].size)
        assertEquals(SegmentHeader.START or SegmentHeader.END, SegmentHeader.flags(header(pipe, 0)))
    }

    @Test
    fun writeRejectsOversizeMessage() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, 8)
        assertThrows<IllegalStateException> { link.write(payload(9)) }
        assertTrue(pipe.written.isEmpty())
    }

    @Test
    fun writeRejectsNonEmptyMessageWhenMtuIs1() = runBlocking {
        val pipe = ScriptedPipe().apply { mtu = 1 }
        val link = SegmentLink(pipe, cap)
        assertThrows<BleTransportException> { link.write(payload(1)) }
        Unit
    }

    @Test
    fun writeSendsAbortIfALaterSegmentFails() = runBlocking {
        val pipe = ScriptedPipe()
        pipe.failOnWrite(1)
        val link = SegmentLink(pipe, cap)
        assertThrows<BleTransportException> { link.write(payload(40)) }
        assertEquals(2, pipe.written.size)
        assertTrue(SegmentHeader.has(header(pipe, 0), SegmentHeader.START))
        assertTrue(SegmentHeader.has(header(pipe, 1), SegmentHeader.ABORT))
        val startSeq = SegmentHeader.seq(header(pipe, 0))
        // The failed segment used startSeq+1; the abort occupies the next slot.
        assertEquals((startSeq + 2) % SegmentHeader.SEQ_MODULUS, SegmentHeader.seq(header(pipe, 1)))
    }

    @Test
    fun readEmptyPduIsMalformed() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, cap)
        pipe.enqueue()
        assertThrows<BleTransportException>("Malformed") { link.read() }
        assertEquals(1, pipe.written.size)
        assertTrue(SegmentHeader.has(header(pipe, 0), SegmentHeader.ABORT))
    }

    @Test
    fun readReservedBitIsMalformed() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, cap)
        pipe.enqueue(SegmentHeader.pack(0, SegmentHeader.START or SegmentHeader.END) or SegmentHeader.RESERVED, 0xAA)
        assertThrows<BleTransportException>("Malformed") { link.read() }
        assertEquals(1, pipe.written.size)
        assertTrue(SegmentHeader.has(header(pipe, 0), SegmentHeader.ABORT))
    }

    @Test
    fun readOrphanContinuationFails() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, cap)
        pipe.enqueue(SegmentHeader.pack(1, SegmentHeader.END), 0xAA)
        assertThrows<BleTransportException>("Orphan") { link.read() }
        assertEquals(1, pipe.written.size)
        assertTrue(SegmentHeader.has(header(pipe, 0), SegmentHeader.ABORT))
    }

    @Test
    fun readSequenceGapFails() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, cap)
        pipe.enqueue(SegmentHeader.pack(3, SegmentHeader.START), 0xAA)
        pipe.enqueue(SegmentHeader.pack(5, SegmentHeader.END), 0xBB)
        assertThrows<BleTransportException>("gap") { link.read() }
        assertEquals(1, pipe.written.size)
        assertTrue(SegmentHeader.has(header(pipe, 0), SegmentHeader.ABORT))
    }

    @Test
    fun readAbortWhileAssemblingFails() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, cap)
        pipe.enqueue(SegmentHeader.pack(0, SegmentHeader.START), 0xAA)
        pipe.enqueue(SegmentHeader.pack(1, SegmentHeader.ABORT))
        assertThrows<BleTransportException>("aborted") { link.read() }
        Unit
    }

    @Test
    fun readAbortWhileIdleIsIgnored() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, cap)
        pipe.enqueue(SegmentHeader.pack(0, SegmentHeader.ABORT))
        pipe.enqueue(SegmentHeader.pack(2, SegmentHeader.START or SegmentHeader.END), 0x42)
        assertArrayEquals(byteArrayOf(0x42), link.read())
        assertTrue(pipe.written.isEmpty())
    }

    @Test
    fun readStartWhileAssemblingDiscardsThePartial() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, cap)
        pipe.enqueue(SegmentHeader.pack(0, SegmentHeader.START), 0xAA, 0xBB)
        pipe.enqueue(SegmentHeader.pack(9, SegmentHeader.START or SegmentHeader.END), 0xCC)
        assertArrayEquals(byteArrayOf(0xCC.toByte()), link.read())
    }

    @Test
    fun readRejectsMessageOverTheCap() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, 2)
        pipe.enqueue(SegmentHeader.pack(0, SegmentHeader.START or SegmentHeader.END), 0x01, 0x02, 0x03)
        assertThrows<BleTransportException>("exceeds") { link.read() }
        assertEquals(1, pipe.written.size)
        assertTrue(SegmentHeader.has(header(pipe, 0), SegmentHeader.ABORT))
    }

    @Test
    fun readTimesOutAnIncompleteMessage() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, cap, incompleteTimeout = 40.milliseconds)
        pipe.enqueue(SegmentHeader.pack(0, SegmentHeader.START), 0xAA)
        assertThrows<BleTransportException>("timed out") { link.read() }
        assertEquals(1, pipe.written.size)
        assertTrue(SegmentHeader.has(header(pipe, 0), SegmentHeader.ABORT))
    }

    @Test
    fun afterAGapTheNextReadAcceptsAFreshStart() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, cap)
        pipe.enqueue(SegmentHeader.pack(0, SegmentHeader.START), 0xAA)
        pipe.enqueue(SegmentHeader.pack(2, SegmentHeader.END), 0xBB)
        assertThrows<BleTransportException> { link.read() }
        pipe.enqueue(SegmentHeader.pack(7, SegmentHeader.START or SegmentHeader.END), 0xCC)
        assertArrayEquals(byteArrayOf(0xCC.toByte()), link.read())
    }

    @Test
    fun callerCancellationIsNotAnIncompleteTimeout() = runBlocking {
        val pipe = ScriptedPipe()
        val link = SegmentLink(pipe, cap)
        val read = launch { link.read() }
        delay(20)
        read.cancel()
        read.join()
        assertTrue(read.isCancelled)
        assertTrue(pipe.written.isEmpty())
    }

    private suspend fun echo(message: ByteArray, mtu: Int): ByteArray {
        val (a, b) = LoopbackPipe.pair(mtu)
        return sendAndReceive(SegmentLink(a, cap), SegmentLink(b, cap), message)
    }

    private suspend fun sendAndReceive(send: SegmentLink, recv: SegmentLink, message: ByteArray): ByteArray = runBlocking {
        val receive = async { recv.read() }
        send.write(message)
        receive.await()
    }
}
