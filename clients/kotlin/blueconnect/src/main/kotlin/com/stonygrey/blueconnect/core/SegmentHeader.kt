// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.core

/**
 * One-byte GATT segment header: `seq` in bits 7–4, reserved bit 3, and
 * `ABORT` / `END` / `START` in bits 2–0. See `documents/ble-framing.md`.
 * Headers are handled as `Int` in 0..255 so that bit arithmetic needs no casts.
 */
object SegmentHeader {
    const val START = 0x01
    const val END = 0x02
    const val ABORT = 0x04
    const val RESERVED = 0x08
    const val SEQ_MODULUS = 16

    fun pack(seq: Int, flags: Int): Int = ((seq and 0x0F) shl 4) or (flags and 0x07)

    fun seq(header: Int): Int = (header and 0xFF) shr 4

    fun flags(header: Int): Int = header and 0x0F

    fun has(header: Int, flag: Int): Boolean = header and flag != 0
}
