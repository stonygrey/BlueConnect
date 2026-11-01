// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect

import com.stonygrey.blueconnect.core.SegmentLink
import com.stonygrey.blueconnect.secure.FrameType
import com.stonygrey.blueconnect.secure.LinkIdentity
import com.stonygrey.blueconnect.secure.LinkProtocol
import com.stonygrey.blueconnect.secure.SecureLink
import com.stonygrey.blueconnect.verdigris.VerdigrisPeripheral
import com.stonygrey.torann.Transport
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import verdigris.Verdigris

/**
 * An in-process Verdigris responder on the far end of a [LoopbackPipe]: it
 * runs Torann's responder side of the handshake (XX or IK, selected by the
 * length of message 1 as the firmware does), then answers each sealed request
 * through [handler]. It checks the lengths and the 0x5A payload the firmware
 * checks, so the initiator is tested against the wire rules before it meets
 * the board.
 */
class FakeDevice(
    private val pipe: LoopbackPipe,
    val identity: LinkIdentity = LinkIdentity.generate(),
    private val handler: (Verdigris.Request) -> List<Verdigris.Response> = ::echoHandler,
) {
    private val scope = CoroutineScope(Dispatchers.Default + SupervisorJob())
    private val lock = Any()
    private val lengths = ArrayList<Int>()
    private var pattern = ""

    /** The lengths of the handshake messages, in order, as the device saw them. */
    val observedHandshakeLengths: List<Int> get() = synchronized(lock) { lengths.toList() }

    val observedPattern: String get() = synchronized(lock) { pattern }

    fun start() {
        scope.launch { serve() }
    }

    fun stop() {
        scope.cancel()
    }

    companion object {
        fun echoHandler(request: Verdigris.Request): List<Verdigris.Response> {
            val response = Verdigris.Response.newBuilder().setId(request.id).setStatus(Verdigris.Status.STATUS_OK)
            when (request.opCase) {
                Verdigris.Request.OpCase.ECHO -> response.setEcho(Verdigris.EchoResponse.newBuilder().setPayload(request.echo.payload))
                Verdigris.Request.OpCase.GET_SERIAL_NUMBER -> response.setName(Verdigris.NameResponse.newBuilder().setName("Verdigris 0042"))
                Verdigris.Request.OpCase.PAIR, Verdigris.Request.OpCase.UNPAIR, Verdigris.Request.OpCase.ENROLL ->
                    response.setAck(Verdigris.AckResponse.getDefaultInstance())
                Verdigris.Request.OpCase.GET_LED -> response.setLed(Verdigris.LedResponse.newBuilder().setOn(true))
                Verdigris.Request.OpCase.SET_LED -> response.setLed(Verdigris.LedResponse.newBuilder().setOn(request.setLed.on))
                else -> response.setStatus(Verdigris.Status.STATUS_UNKNOWN_OP)
            }
            return listOf(response.build())
        }
    }

    private suspend fun serve() {
        val segments = SegmentLink(pipe, VerdigrisPeripheral.MAX_FRAME)
        LinkProtocol.xx.createKeyPair(identity.privateKey).use { keyPair ->
            while (scope.isActive) {
                val (type, message1) = FrameType.split(segments.read())
                if (type != FrameType.HANDSHAKE) continue
                record(message1.size)
                val transport: Transport
                if (message1.size == SecureLink.IK_HANDSHAKE_MESSAGE_1_LENGTH) {
                    setPattern("IK")
                    val handshake = LinkProtocol.ik.createHandshake(initiator = false, prologue = SecureLink.PROLOGUE, localStatic = keyPair)
                    try {
                        val read1 = handshake.readMessage(message1)
                        check(read1.payload.contentEquals(SecureLink.HANDSHAKE_PAYLOAD)) { "IK message 1 payload" }
                        val write2 = handshake.writeMessage(SecureLink.HANDSHAKE_PAYLOAD)
                        record(write2.message.size)
                        segments.write(FrameType.bundle(FrameType.HANDSHAKE, write2.message))
                        transport = write2.transport!!
                    } finally {
                        handshake.close()
                    }
                } else {
                    setPattern("XX")
                    check(message1.size == SecureLink.HANDSHAKE_MESSAGE_1_LENGTH) { "XX message 1 length" }
                    val handshake = LinkProtocol.xx.createHandshake(initiator = false, prologue = SecureLink.PROLOGUE, localStatic = keyPair)
                    try {
                        handshake.readMessage(message1)
                        val write2 = handshake.writeMessage(SecureLink.HANDSHAKE_PAYLOAD)
                        record(write2.message.size)
                        segments.write(FrameType.bundle(FrameType.HANDSHAKE, write2.message))
                        val (type3, message3) = FrameType.split(segments.read())
                        check(type3 == FrameType.HANDSHAKE) { "XX message 3 type" }
                        record(message3.size)
                        val read3 = handshake.readMessage(message3)
                        check(read3.payload.contentEquals(SecureLink.HANDSHAKE_PAYLOAD)) { "XX message 3 payload" }
                        transport = read3.transport!!
                    } finally {
                        handshake.close()
                    }
                }
                try {
                    // Sealed traffic until the next handshake message 1 supersedes the session.
                    while (scope.isActive) {
                        val (sealedType, sealed) = FrameType.split(segments.read())
                        if (sealedType == FrameType.HANDSHAKE) break
                        if (sealedType != FrameType.ENCRYPTED) continue
                        val request = Verdigris.Request.parseFrom(transport.readMessage(sealed))
                        for (response in handler(request)) {
                            segments.write(FrameType.bundle(FrameType.ENCRYPTED, transport.writeMessage(response.toByteArray())))
                        }
                    }
                } finally {
                    transport.close()
                }
            }
        }
    }

    private fun record(length: Int) = synchronized(lock) { lengths.add(length) }

    private fun setPattern(value: String) = synchronized(lock) { pattern = value }
}
