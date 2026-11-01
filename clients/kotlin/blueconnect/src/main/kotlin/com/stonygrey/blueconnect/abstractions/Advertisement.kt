// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.abstractions

/** The current RSSI sample and the extremes seen so far, in dBm. */
data class Rssi(val valueDbm: Int, val minDbm: Int, val maxDbm: Int) {
    constructor(dbm: Int) : this(dbm, dbm, dbm)
}

/**
 * One scanned device as last seen. A value snapshot: the scanner publishes a
 * new one on every packet, and the app keeps whichever it selected.
 * [id] is the platform identifier, the MAC address on Android.
 */
data class Advertisement(
    val id: String,
    val localName: String,
    val isConnectable: Boolean,
    /** Bit 0 or 1 of the status byte: at least one tier is pinned. */
    val isSecured: Boolean,
    val rssi: Rssi,
    val timestampMillis: Long,
)
