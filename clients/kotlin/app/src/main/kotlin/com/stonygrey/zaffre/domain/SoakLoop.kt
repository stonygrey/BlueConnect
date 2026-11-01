// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.zaffre.domain

import com.stonygrey.blueconnect.abstractions.Advertisement
import com.stonygrey.blueconnect.abstractions.BleScanner
import com.stonygrey.blueconnect.abstractions.BleTransportException
import com.stonygrey.blueconnect.abstractions.BlueConnectOptions
import com.stonygrey.blueconnect.verdigris.VerdigrisPeripheral
import com.stonygrey.zaffre.data.PairingRepository
import com.stonygrey.zaffre.data.Tier
import kotlin.coroutines.cancellation.CancellationException
import kotlin.random.Random
import kotlin.time.Duration.Companion.seconds
import kotlin.time.TimeSource
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

data class LoopState(
    val isRunning: Boolean = false,
    val deviceName: String = "",
    val tests: Int = 0,
    val connectionFailures: Int = 0,
    val commandFailures: Int = 0,
    val minConnectMs: Double? = null,
    val avgConnectMs: Double? = null,
    val maxConnectMs: Double? = null,
    val startedMillis: Long? = null,
    val elapsedSeconds: Long = 0,
)

/** The Loop tab: connect, echo, disconnect, over and over, with the high-tier pairing. */
class SoakLoop(
    private val scanner: BleScanner,
    private val options: BlueConnectOptions,
    private val pairings: PairingRepository,
    private val scope: CoroutineScope,
) {
    private val _state = MutableStateFlow(LoopState())
    val state: StateFlow<LoopState> = _state.asStateFlow()
    private var job: Job? = null
    private var ticker: Job? = null

    suspend fun start(device: Advertisement) {
        stop()
        _state.value = LoopState(isRunning = true, deviceName = device.localName, startedMillis = System.currentTimeMillis())
        ticker = scope.launch {
            while (isActive) {
                delay(1.seconds)
                tick()
            }
        }
        job = scope.launch { loop(device) }
    }

    suspend fun stop() {
        job?.cancel()
        ticker?.cancel()
        job?.join()
        job = null
        ticker = null
        _state.update { it.copy(isRunning = false) }
    }

    private fun tick() {
        _state.update { s -> s.startedMillis?.let { s.copy(elapsedSeconds = (System.currentTimeMillis() - it) / 1000) } ?: s }
    }

    private suspend fun loop(device: Advertisement) {
        while (scope.isActive && job?.isActive == true) {
            val security = pairings.security(Tier.HIGH)
            try {
                val peripheral = scanner.createPeripheral(device)
                val verdigris = VerdigrisPeripheral(peripheral, options, security)
                try {
                    val before = TimeSource.Monotonic.markNow()
                    peripheral.connect()
                    recordConnect(before.elapsedNow().inWholeMicroseconds / 1000.0)
                    try {
                        val payload = Random.nextBytes(if (options.isEchoVerificationEnabled) 1536 else 16)
                        val reply = verdigris.echo(payload)
                        if (!reply.contentEquals(payload)) {
                            throw BleTransportException("Echoed payload does not match the command.")
                        }
                    } catch (e: CancellationException) {
                        throw e
                    } catch (e: Exception) {
                        _state.update { it.copy(commandFailures = it.commandFailures + 1) }
                    }
                    runCatching { peripheral.disconnect() }
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    _state.update { it.copy(connectionFailures = it.connectionFailures + 1) }
                } finally {
                    verdigris.close()
                }
            } catch (e: CancellationException) {
                break
            } catch (e: Exception) {
                _state.update { it.copy(connectionFailures = it.connectionFailures + 1) }
            }
            _state.update { it.copy(tests = it.tests + 1) }
            tick()
            val delaySeconds = Random.nextInt(options.minCycleDelay.inWholeSeconds.toInt(), options.maxCycleDelay.inWholeSeconds.toInt() + 1)
            try {
                delay(delaySeconds.seconds)
            } catch (e: CancellationException) {
                break
            }
        }
        _state.update { it.copy(isRunning = false) }
    }

    private fun recordConnect(ms: Double) {
        _state.update {
            val tests = it.tests
            it.copy(
                minConnectMs = minOf(it.minConnectMs ?: ms, ms),
                maxConnectMs = maxOf(it.maxConnectMs ?: ms, ms),
                avgConnectMs = ((it.avgConnectMs ?: 0.0) * tests + ms) / (tests + 1),
            )
        }
    }
}
