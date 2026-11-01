// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.secure

import com.stonygrey.blueconnect.abstractions.BleTransportException

/** The one-byte frame type in front of every reassembled message (`documents/noise-security.md`, "Wire framing"). */
object FrameType {
    const val PLAINTEXT = 0x00
    const val HANDSHAKE = 0x01
    const val ENCRYPTED = 0x02

    fun bundle(type: Int, payload: ByteArray): ByteArray {
        val message = ByteArray(1 + payload.size)
        message[0] = type.toByte()
        payload.copyInto(message, 1)
        return message
    }

    fun split(message: ByteArray): Pair<Int, ByteArray> {
        if (message.isEmpty()) {
            throw BleTransportException("Empty message; missing type byte.")
        }
        return (message[0].toInt() and 0xFF) to message.copyOfRange(1, message.size)
    }
}
