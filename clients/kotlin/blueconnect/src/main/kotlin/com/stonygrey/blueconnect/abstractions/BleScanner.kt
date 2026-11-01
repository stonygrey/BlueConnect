// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.abstractions

import kotlinx.coroutines.flow.Flow

enum class ScanEventType { ADDED, UPDATED, EXPIRED }

data class ScanEvent(val type: ScanEventType, val advertisement: Advertisement)

/**
 * A BLE central that scans for Verdigris devices and creates peripherals for
 * them. Backends implement it over the shared
 * [com.stonygrey.blueconnect.core.AdvertisementRegistry].
 */
interface BleScanner {
    /** Every device seen within the last [BlueConnectConstants.SCANNER_EXPIRATION]. */
    val peripherals: List<Advertisement>
    val isScanning: Boolean

    /** Starts scanning. Returns once the radio is on and the scan is running. */
    suspend fun start()

    /** Scan events, hot; a late collector sees only what happens after it subscribes. */
    val events: Flow<ScanEvent>

    /** When [id] was last seen, in epoch milliseconds, or null once it has expired. */
    fun lastSeen(id: String): Long?

    /**
     * A peripheral for a device still in the cache. Throws [IllegalStateException]
     * once the entry has expired.
     */
    fun createPeripheral(advertisement: Advertisement): BlePeripheral
}
