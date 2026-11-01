// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.core

import com.stonygrey.blueconnect.abstractions.BlePeripheral
import com.stonygrey.blueconnect.abstractions.BleTransportException
import java.io.ByteArrayOutputStream
import kotlin.random.Random
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

/**
 * Message pipe over a peripheral: one [write] is one buffer, one [read]
 * returns one complete buffer. Each ATT value is a self-describing segment
 * (`documents/ble-framing.md`). The caller owns the peripheral and reads from
 * one coroutine at a time.
 */
class SegmentLink(
    private val peripheral: BlePeripheral,
    private val maxMessage: Int,
    private val incompleteTimeout: Duration = DEFAULT_INCOMPLETE_TIMEOUT,
) {
    companion object {
        val DEFAULT_INCOMPLETE_TIMEOUT: Duration = 5.seconds

        /**
         * Bound on the best-effort `ABORT` after a failed [write], which runs
         * under [NonCancellable] because the caller may already be cancelled.
         */
        val ABORT_WRITE_TIMEOUT: Duration = 2.seconds
    }

    private val send = Mutex()
    private val acc = ByteArrayOutputStream()
    private var assembling = false
    private var seqIn = 0

    init {
        require(maxMessage > 0) { "maxMessage must be positive" }
    }

    suspend fun write(message: ByteArray) {
        check(message.size <= maxMessage) { "Message is ${message.size} bytes; the maximum is $maxMessage." }
        send.withLock {
            val payloadMtu = maxOf(1, peripheral.mtu)
            val cap = maxOf(0, payloadMtu - 1)
            if (cap < 1 && message.isNotEmpty()) {
                throw BleTransportException("ATT payload is too small to carry a segment.")
            }
            var seq = Random.nextInt(SegmentHeader.SEQ_MODULUS)
            var offset = 0
            var first = true
            try {
                do {
                    val n = minOf(cap, message.size - offset)
                    val last = offset + n == message.size
                    var flags = 0
                    if (first) flags = flags or SegmentHeader.START
                    if (last) flags = flags or SegmentHeader.END
                    val segment = ByteArray(1 + n)
                    segment[0] = SegmentHeader.pack(seq, flags).toByte()
                    message.copyInto(segment, 1, offset, offset + n)
                    IoLog.info(IoLog.segment("write", seq, flags, segment))
                    peripheral.write(segment)
                    offset += n
                    first = false
                    seq = (seq + 1) % SegmentHeader.SEQ_MODULUS
                } while (offset < message.size)
            } catch (e: Throwable) {
                val abortSeq = (seq + 1) % SegmentHeader.SEQ_MODULUS
                withContext(NonCancellable) {
                    withTimeoutOrNull(ABORT_WRITE_TIMEOUT) { runCatching { sendAbort(abortSeq) } }
                }
                throw e
            }
        }
    }

    suspend fun read(): ByteArray {
        resetRx()
        while (true) {
            val pdu: ByteArray =
                if (assembling) {
                    withTimeoutOrNull(incompleteTimeout) { peripheral.read() } ?: run {
                        resetRx()
                        trySendAbort(0)
                        throw BleTransportException("Incomplete message: timed out waiting for the next segment.")
                    }
                } else {
                    peripheral.read()
                }
            logInbound(pdu)
            if (pdu.isEmpty() || SegmentHeader.has(pdu[0].toInt(), SegmentHeader.RESERVED)) {
                resetRx()
                trySendAbort(0)
                throw BleTransportException("Malformed segment header.")
            }
            val header = pdu[0].toInt() and 0xFF
            if (SegmentHeader.has(header, SegmentHeader.ABORT)) {
                val wasAssembling = assembling
                resetRx()
                if (wasAssembling) {
                    throw BleTransportException("Peer aborted the message.")
                }
                continue
            }
            val seq = SegmentHeader.seq(header)
            if (SegmentHeader.has(header, SegmentHeader.START)) {
                assembling = true
                seqIn = seq
                acc.reset()
                acc.write(pdu, 1, pdu.size - 1)
            } else {
                if (!assembling) {
                    trySendAbort(0)
                    throw BleTransportException("Orphan continuation segment.")
                }
                if (seq != (seqIn + 1) % SegmentHeader.SEQ_MODULUS) {
                    resetRx()
                    trySendAbort(0)
                    throw BleTransportException("Segment sequence gap.")
                }
                seqIn = seq
                acc.write(pdu, 1, pdu.size - 1)
            }
            if (acc.size() > maxMessage) {
                resetRx()
                trySendAbort(0)
                throw BleTransportException("Message exceeds the $maxMessage-byte cap.")
            }
            if (SegmentHeader.has(header, SegmentHeader.END)) {
                val complete = acc.toByteArray()
                resetRx()
                return complete
            }
        }
    }

    private fun logInbound(pdu: ByteArray) {
        if (pdu.isEmpty()) {
            IoLog.info(IoLog.segment("read", 0, 0, pdu))
            return
        }
        val header = pdu[0].toInt() and 0xFF
        IoLog.info(IoLog.segment("read", SegmentHeader.seq(header), SegmentHeader.flags(header), pdu))
    }

    private fun resetRx() {
        assembling = false
        acc.reset()
    }

    /** Best effort: the read that triggered this already failed. */
    private suspend fun trySendAbort(seq: Int) {
        runCatching { send.withLock { sendAbort(seq) } }
    }

    private suspend fun sendAbort(seq: Int) {
        val abort = byteArrayOf(SegmentHeader.pack(seq, SegmentHeader.ABORT).toByte())
        IoLog.info(IoLog.segment("write", seq, SegmentHeader.ABORT, abort))
        peripheral.write(abort)
    }
}
