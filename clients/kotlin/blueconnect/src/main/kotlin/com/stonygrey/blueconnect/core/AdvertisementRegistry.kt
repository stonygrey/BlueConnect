// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.core

import com.stonygrey.blueconnect.abstractions.Advertisement
import com.stonygrey.blueconnect.abstractions.BlueConnectConstants
import com.stonygrey.blueconnect.abstractions.Rssi
import com.stonygrey.blueconnect.abstractions.ScanEvent
import com.stonygrey.blueconnect.abstractions.ScanEventType
import kotlin.time.Duration.Companion.seconds
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/**
 * The scan cache that every backend shares: the last advertisement per
 * device, the hot event flow, the set of devices with a connection in
 * progress (which never expire), and the 5 s expiry sweep. Guarded by a
 * plain lock because backends call [observe] from the platform's scan
 * callback thread.
 */
class AdvertisementRegistry(
    private val minDbm: Int,
    private val maxDbm: Int,
    scope: CoroutineScope,
) {
    private val lock = Any()
    private val cache = HashMap<String, Advertisement>()
    private val connections = HashSet<String>()
    private val _events = MutableSharedFlow<ScanEvent>(extraBufferCapacity = 256, onBufferOverflow = BufferOverflow.DROP_OLDEST)
    private val expiry: Job = scope.launch {
        while (isActive) {
            delay(5.seconds)
            expireStaleEntries()
        }
    }

    val events: SharedFlow<ScanEvent> = _events

    val peripherals: List<Advertisement>
        get() = synchronized(lock) { cache.values.toList() }

    fun cached(id: String): Advertisement? = synchronized(lock) { cache[id] }

    fun lastSeen(id: String): Long? = cached(id)?.timestampMillis

    /**
     * Merges one packet. [secured] is null for packets without the Stony Grey
     * manufacturer data, which keep the previous value.
     */
    fun observe(id: String, localName: String, isConnectable: Boolean, secured: Boolean?, dbm: Int) {
        val now = System.currentTimeMillis()
        val event = synchronized(lock) {
            val previous = cache[id]
            val advertisement =
                if (previous != null) {
                    Advertisement(id, localName, isConnectable, secured ?: previous.isSecured, mergeRssi(previous.rssi, dbm), now)
                } else {
                    Advertisement(id, localName, isConnectable, secured ?: false, Rssi(dbm), now)
                }
            cache[id] = advertisement
            ScanEvent(if (previous == null) ScanEventType.ADDED else ScanEventType.UPDATED, advertisement)
        }
        _events.tryEmit(event)
    }

    /** Marks a device as connecting or connected so that it is not expired while it has stopped advertising. */
    fun register(id: String) = synchronized(lock) { connections.add(id) }

    fun unregister(id: String) = synchronized(lock) { connections.remove(id) }

    fun close() {
        expiry.cancel()
    }

    private fun mergeRssi(previous: Rssi, dbm: Int): Rssi =
        if (dbm > minDbm && dbm < maxDbm) Rssi(dbm, minOf(previous.minDbm, dbm), maxOf(previous.maxDbm, dbm)) else previous

    private fun expireStaleEntries() {
        val cutoff = System.currentTimeMillis() - BlueConnectConstants.SCANNER_EXPIRATION.inWholeMilliseconds
        val expired = synchronized(lock) {
            val stale = cache.values.filter { it.timestampMillis < cutoff && it.id !in connections }
            for (advertisement in stale) cache.remove(advertisement.id)
            stale
        }
        for (advertisement in expired) {
            _events.tryEmit(ScanEvent(ScanEventType.EXPIRED, advertisement))
        }
    }
}
