// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.secure

import com.stonygrey.blueconnect.abstractions.BlePeripheral
import com.stonygrey.blueconnect.abstractions.LinkSecurityException
import com.stonygrey.blueconnect.core.IoLog
import com.stonygrey.blueconnect.core.SegmentLink
import com.stonygrey.torann.HandshakeState
import com.stonygrey.torann.TorannException
import com.stonygrey.torann.Transport

/**
 * Encrypted, mutually authenticated link over a peripheral, as initiator. An
 * initiator holding a pinned peer key opens with Noise IK; otherwise Noise XX
 * runs. After the handshake every message is AEAD-sealed as a `0x02` frame.
 * One coroutine at a time. This is the initiator half only; the responder
 * half is the firmware, and the tests use Torann's responder.
 */
class SecureLink(peripheral: BlePeripheral, maxFrame: Int, private val security: LinkSecurity) {
    companion object {
        /** Bound into the handshake transcript. Framing is below this layer. */
        val PROLOGUE: ByteArray = "BlueConnectLink/1".toByteArray(Charsets.US_ASCII)

        /** Messages 2 and 3 carry this single ignored byte instead of an empty payload: Android's native AES-GCM rejects zero-length inputs. */
        val HANDSHAKE_PAYLOAD: ByteArray = byteArrayOf(0x5A)
        const val HANDSHAKE_MESSAGE_1_LENGTH = LinkProtocol.KEY_LENGTH
        const val HANDSHAKE_MESSAGE_2_LENGTH = LinkProtocol.KEY_LENGTH + LinkProtocol.KEY_LENGTH + LinkProtocol.TAG_LENGTH + 1 + LinkProtocol.TAG_LENGTH
        const val HANDSHAKE_MESSAGE_3_LENGTH = LinkProtocol.KEY_LENGTH + LinkProtocol.TAG_LENGTH + 1 + LinkProtocol.TAG_LENGTH
        const val IK_HANDSHAKE_MESSAGE_1_LENGTH = LinkProtocol.KEY_LENGTH + LinkProtocol.KEY_LENGTH + LinkProtocol.TAG_LENGTH + 1 + LinkProtocol.TAG_LENGTH
        const val IK_HANDSHAKE_MESSAGE_2_LENGTH = LinkProtocol.KEY_LENGTH + 1 + LinkProtocol.TAG_LENGTH
    }

    private val segments = SegmentLink(peripheral, maxFrame)
    private var transport: Transport? = null
    private var closed = false

    /** The peer's proven static key; available once the handshake has completed. */
    var remoteStaticKey: ByteArray? = null
        private set

    val isEstablished: Boolean get() = transport != null

    /** Runs the handshake, IK when a peer key is pinned and XX otherwise, before any traffic flows. */
    suspend fun establish() {
        check(!closed) { "The secure link has been closed." }
        if (isEstablished) return
        LinkProtocol.xx.createKeyPair(security.identity.privateKey).use { staticKeyPair ->
            var handshake: HandshakeState? = null
            try {
                val pin = security.pinnedPeerKey
                val split: Transport?
                if (pin != null) {
                    // IK: message 1 both proves this identity and shows we already
                    // hold the peer's key; the reply completes the session. A stale
                    // pin fails on the device and yields silence.
                    val state = step { LinkProtocol.ik.createHandshake(initiator = true, prologue = PROLOGUE, localStatic = staticKeyPair, remoteStatic = pin) }
                    handshake = state
                    val message1 = step { state.writeMessage(HANDSHAKE_PAYLOAD) }
                    writeTyped(FrameType.HANDSHAKE, message1.message)
                    val message2 = readHandshakeFrame()
                    expectHandshakeLength(message2, IK_HANDSHAKE_MESSAGE_2_LENGTH, 2)
                    val read2 = step { state.readMessage(message2) }
                    expectHandshakePayload(read2.payload)
                    split = read2.transport
                } else {
                    val state = step { LinkProtocol.xx.createHandshake(initiator = true, prologue = PROLOGUE, localStatic = staticKeyPair) }
                    handshake = state
                    val message1 = step { state.writeMessage() }
                    writeTyped(FrameType.HANDSHAKE, message1.message)
                    val message2 = readHandshakeFrame()
                    expectHandshakeLength(message2, HANDSHAKE_MESSAGE_2_LENGTH, 2)
                    val read2 = step { state.readMessage(message2) }
                    expectHandshakePayload(read2.payload)
                    verifyPeer(state.remoteStaticPublicKey)
                    val message3 = step { state.writeMessage(HANDSHAKE_PAYLOAD) }
                    writeTyped(FrameType.HANDSHAKE, message3.message)
                    split = message3.transport
                }
                remoteStaticKey = handshake.remoteStaticPublicKey?.copyOf()
                transport = split ?: throw LinkSecurityException("The handshake completed without producing a transport.")
            } finally {
                handshake?.close()
            }
        }
        IoLog.info("Secure link established; peer static key ${IoLog.hex(remoteStaticKey ?: ByteArray(0))}.")
    }

    suspend fun send(message: ByteArray) {
        establish()
        val sealed = step { transport!!.writeMessage(message) }
        writeTyped(FrameType.ENCRYPTED, sealed)
    }

    suspend fun receive(): ByteArray {
        establish()
        while (true) {
            val (type, payload) = readTyped()
            when (type) {
                FrameType.ENCRYPTED ->
                    return try {
                        transport!!.readMessage(payload)
                    } catch (e: TorannException) {
                        throw LinkSecurityException("AEAD authentication failed; the frame was forged, corrupted, or replayed.", e)
                    }
                FrameType.PLAINTEXT -> throw LinkSecurityException("Plaintext frame on a secure link.")
                else -> IoLog.debug("Ignoring frame type $type on an established secure link.")
            }
        }
    }

    fun close() {
        if (closed) return
        closed = true
        transport?.close()
        transport = null
    }

    private fun verifyPeer(remoteStatic: ByteArray?) {
        // Torann leaves non-contributory-key checks to the application (Noise
        // spec §12.1); an all-zero static is never a legitimate identity.
        if (remoteStatic == null || remoteStatic.all { it == 0.toByte() }) {
            throw LinkSecurityException("Peer presented an all-zero static key.")
        }
        val pin = security.pinnedPeerKey
        if (pin != null && !remoteStatic.contentEquals(pin)) {
            throw LinkSecurityException("Peer's static key does not match the paired key.")
        }
    }

    /** Runs one Torann step, mapping its failures into the link taxonomy. */
    private inline fun <T> step(body: () -> T): T =
        try {
            body()
        } catch (e: TorannException) {
            throw LinkSecurityException("The handshake failed: ${e.message}", e)
        }

    private fun expectHandshakeLength(message: ByteArray, expected: Int, number: Int) {
        if (message.size != expected) {
            throw LinkSecurityException("Handshake message $number is ${message.size} bytes; expected $expected.")
        }
    }

    private fun expectHandshakePayload(payload: ByteArray) {
        if (!payload.contentEquals(HANDSHAKE_PAYLOAD)) {
            throw LinkSecurityException("Handshake payload does not match the protocol constant.")
        }
    }

    private suspend fun writeTyped(type: Int, payload: ByteArray) {
        val bundled = FrameType.bundle(type, payload)
        IoLog.info(IoLog.message("write", type, bundled))
        segments.write(bundled)
    }

    private suspend fun readTyped(): Pair<Int, ByteArray> {
        val bundled = segments.read()
        val (type, payload) = FrameType.split(bundled)
        IoLog.info(IoLog.message("read", type, bundled))
        return type to payload
    }

    private suspend fun readHandshakeFrame(): ByteArray {
        val (type, payload) = readTyped()
        if (type != FrameType.HANDSHAKE) {
            throw LinkSecurityException("Expected a handshake frame but received type $type.")
        }
        return payload
    }
}
