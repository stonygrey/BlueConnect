// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect

import com.stonygrey.blueconnect.abstractions.Advertisement
import com.stonygrey.blueconnect.abstractions.BlePeripheral
import com.stonygrey.blueconnect.abstractions.BleTransportException
import com.stonygrey.blueconnect.abstractions.ConnectionState
import com.stonygrey.blueconnect.abstractions.Rssi
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.emptyFlow

val pipeAdvertisement = Advertisement("pipe", "pipe", true, false, Rssi(-50), System.currentTimeMillis())

fun payload(length: Int): ByteArray = ByteArray(length) { it.toByte() }

/**
 * Test double: inbound ATT values are queued by the test, outbound writes are
 * recorded and can be scripted to fail.
 */
class ScriptedPipe : BlePeripheral {
    private val inbound = Channel<ByteArray>(Channel.UNLIMITED)
    private val writtenItems = ArrayList<ByteArray>()
    private var failAt: Int? = null

    override var mtu: Int = 20
    override val advertisement = pipeAdvertisement
    override val isConnected = true
    override val states: Flow<ConnectionState> = emptyFlow()

    val written: List<ByteArray> get() = synchronized(writtenItems) { writtenItems.toList() }

    fun enqueue(vararg bytes: Int) {
        inbound.trySend(ByteArray(bytes.size) { bytes[it].toByte() })
    }

    fun failOnWrite(index: Int) {
        failAt = index
    }

    override suspend fun connect() {}

    override suspend fun disconnect() {}

    override suspend fun read(): ByteArray = inbound.receive()

    override suspend fun write(bytes: ByteArray) {
        synchronized(writtenItems) {
            if (failAt == writtenItems.size) {
                failAt = null
                throw BleTransportException("scripted write failure")
            }
            writtenItems.add(bytes)
        }
    }

    override suspend fun close() {
        inbound.close()
    }
}

/** Two cross-wired in-memory peripherals. Each [write] is one ATT value. */
class LoopbackPipe private constructor(override var mtu: Int) : BlePeripheral {
    private val inbound = Channel<ByteArray>(Channel.UNLIMITED)
    private lateinit var peer: LoopbackPipe

    override val advertisement = pipeAdvertisement
    override val isConnected = true
    override val states: Flow<ConnectionState> = emptyFlow()

    companion object {
        fun pair(mtu: Int = 20): Pair<LoopbackPipe, LoopbackPipe> {
            val a = LoopbackPipe(mtu)
            val b = LoopbackPipe(mtu)
            a.peer = b
            b.peer = a
            return a to b
        }
    }

    override suspend fun connect() {}

    override suspend fun disconnect() {}

    override suspend fun read(): ByteArray = inbound.receive()

    override suspend fun write(bytes: ByteArray) {
        peer.inbound.trySend(bytes)
    }

    override suspend fun close() {
        inbound.close()
    }
}
