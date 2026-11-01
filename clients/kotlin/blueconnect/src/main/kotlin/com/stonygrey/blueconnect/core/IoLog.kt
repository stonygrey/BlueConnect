// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.core

/**
 * The device I/O log lines, in the exact format `lib/smalt` parses. This
 * module has no Logcat, so the app installs a [Sink] that forwards to
 * `android.util.Log`; the default sink drops everything, which is what the
 * JVM tests need.
 */
object IoLog {
    const val PREFIX = "_BLE_IO"
    const val TAG = "BlueConnect"

    interface Sink {
        fun info(tag: String, line: String)
        fun warn(tag: String, line: String)
        fun debug(tag: String, line: String)
    }

    @Volatile
    var sink: Sink = object : Sink {
        override fun info(tag: String, line: String) {}
        override fun warn(tag: String, line: String) {}
        override fun debug(tag: String, line: String) {}
    }

    fun info(line: String) = sink.info(TAG, line)

    fun warn(line: String) = sink.warn(TAG, line)

    fun debug(line: String) = sink.debug(TAG, line)

    fun message(direction: String, type: Int, bytes: ByteArray): String =
        "$PREFIX message $direction type=${"%02x".format(type and 0xFF)} len=${bytes.size} hex=${hex(bytes)}"

    fun segment(direction: String, seq: Int, flags: Int, bytes: ByteArray): String =
        "$PREFIX segment $direction seq=$seq flags=${formatFlags(flags)} len=${bytes.size} hex=${hex(bytes)}"

    fun formatFlags(flags: Int): String {
        val parts = ArrayList<String>(3)
        if (flags and SegmentHeader.START != 0) parts.add("start")
        if (flags and SegmentHeader.END != 0) parts.add("end")
        if (flags and SegmentHeader.ABORT != 0) parts.add("abort")
        return if (parts.isEmpty()) "none" else parts.joinToString("+")
    }

    private val digits = "0123456789ABCDEF".toCharArray()

    /** Uppercase hex with no separators, as `Convert.ToHexString` produces. */
    fun hex(bytes: ByteArray): String {
        val out = CharArray(bytes.size * 2)
        for ((i, b) in bytes.withIndex()) {
            val v = b.toInt() and 0xFF
            out[i * 2] = digits[v ushr 4]
            out[i * 2 + 1] = digits[v and 0x0F]
        }
        return String(out)
    }
}
