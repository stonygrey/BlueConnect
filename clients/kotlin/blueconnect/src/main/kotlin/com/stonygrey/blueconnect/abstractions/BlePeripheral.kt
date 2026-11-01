// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.abstractions

import kotlinx.coroutines.flow.Flow

enum class ConnectionState { DISCONNECTED, CONNECTED }

/**
 * One GATT connection to a Verdigris device. [read] returns one ATT value
 * (one notification); [write] sends raw bytes, split at [mtu]. Segmentation
 * is above this in [com.stonygrey.blueconnect.core.SegmentLink].
 */
interface BlePeripheral {
    val advertisement: Advertisement
    val isConnected: Boolean

    /** The ATT payload size, already `ATT_MTU - 3`. Zero when not connected. */
    val mtu: Int

    /** Connects with the retry policy in [BlueConnectOptions]. */
    suspend fun connect()

    suspend fun disconnect()

    /** The next received ATT value. Cancellable. */
    suspend fun read(): ByteArray

    suspend fun write(bytes: ByteArray)

    /** Connection state changes, hot. */
    val states: Flow<ConnectionState>

    /** Disconnects if connected and releases the platform object. Idempotent. */
    suspend fun close()
}
