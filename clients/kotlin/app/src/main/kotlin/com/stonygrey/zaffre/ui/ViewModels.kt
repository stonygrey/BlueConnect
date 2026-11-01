// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.zaffre.ui

import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.viewModelScope
import com.stonygrey.blueconnect.abstractions.Advertisement
import com.stonygrey.zaffre.AppContainer
import com.stonygrey.zaffre.data.Tier
import com.stonygrey.zaffre.data.TierCredential
import com.stonygrey.zaffre.domain.LoopState
import com.stonygrey.zaffre.domain.describe
import kotlin.random.Random
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.combine
import kotlinx.coroutines.flow.stateIn
import kotlinx.coroutines.flow.SharingStarted
import kotlinx.coroutines.launch

/** The UI state holders, one per screen, over the shared objects in [AppContainer]. */
class ZaffreViewModelFactory(private val container: AppContainer) : ViewModelProvider.Factory {
    @Suppress("UNCHECKED_CAST")
    override fun <T : ViewModel> create(modelClass: Class<T>): T =
        when (modelClass) {
            ScanViewModel::class.java -> ScanViewModel(container)
            NoiseViewModel::class.java -> NoiseViewModel(container)
            LoopViewModel::class.java -> LoopViewModel(container)
            else -> throw IllegalArgumentException("Unknown ViewModel ${modelClass.name}")
        } as T
}

/** The scan list, sampled from the scanner every 500 ms. */
class ScanViewModel(private val container: AppContainer) : ViewModel() {
    private val _devices = MutableStateFlow<List<Advertisement>>(emptyList())
    val devices: StateFlow<List<Advertisement>> = _devices.asStateFlow()
    private val _scanFailure = MutableStateFlow<String?>(null)
    val scanFailure: StateFlow<String?> = _scanFailure.asStateFlow()
    val selected: StateFlow<Advertisement?> = container.selection.selected

    fun start() {
        viewModelScope.launch {
            try {
                container.scanner.start()
            } catch (e: Exception) {
                _scanFailure.value = describe(e)
                return@launch
            }
            while (true) {
                val sorted = container.scanner.peripherals.sortedByDescending { it.rssi.valueDbm }
                _devices.value = sorted
                container.selection.refresh(sorted)
                delay(500)
            }
        }
    }

    fun select(device: Advertisement) = container.selection.select(device)

    /** Back to the scan list; the device's pages go away. */
    fun clearSelection() = container.selection.select(null)
}

/** The Noise tab and the Low and High tabs: pairing state and the command actions, per tier. */
class NoiseViewModel(private val container: AppContainer) : ViewModel() {
    val selected: StateFlow<Advertisement?> = container.selection.selected
    val credentials: StateFlow<Map<Tier, TierCredential>> = container.pairings.credentials
    val pairResult: StateFlow<Map<Tier, String>> = container.commands.pairResult
    val ledOn: StateFlow<Map<Tier, Boolean>> = container.commands.ledOn
    val isBusy: StateFlow<Boolean> = container.commands.isBusy

    /** Commands and pairing are allowed when a device is selected, nothing else is running, and the loop is stopped. */
    val canRun: StateFlow<Boolean> =
        combine(selected, isBusy, container.loop.state) { device, busy, loop -> device != null && !busy && !loop.isRunning }
            .stateIn(viewModelScope, SharingStarted.Eagerly, false)

    private val _serialInput = MutableStateFlow(Tier.entries.associateWith { Random.nextInt(1, 65536).toString() })
    val serialInput: StateFlow<Map<Tier, String>> = _serialInput.asStateFlow()
    private val _echoInput = MutableStateFlow(Tier.entries.associateWith { "Hello, Verdigris!" })
    val echoInput: StateFlow<Map<Tier, String>> = _echoInput.asStateFlow()

    fun setSerialInput(tier: Tier, value: String) {
        _serialInput.value = _serialInput.value + (tier to value)
    }

    fun setEchoInput(tier: Tier, value: String) {
        _echoInput.value = _echoInput.value + (tier to value)
    }

    /** A scanned SGNK1 or SGNE1 payload for the tier's card. */
    fun provision(tier: Tier, text: String) = viewModelScope.launch { container.commands.provision(selected.value, tier, text) }

    fun pair(tier: Tier) = viewModelScope.launch { container.commands.pair(selected.value, tier) }

    fun unpair(tier: Tier) = viewModelScope.launch { container.commands.unpair(selected.value, tier) }

    fun readSerial(tier: Tier) = viewModelScope.launch {
        container.commands.run(selected.value, tier) { it.getSerialNumber() }?.let { container.toasts.info("Advertising as '$it'") }
    }

    fun writeSerial(tier: Tier) = viewModelScope.launch {
        val serial = serialInput.value[tier]?.trim()?.toIntOrNull()
        if (serial == null || serial !in 1..65535) {
            container.toasts.error("Serial number must be a whole number from 1 to 65535.")
            return@launch
        }
        container.commands.run(selected.value, tier) { it.setSerialNumber(serial) }?.let {
            container.toasts.info("Now advertising as '$it'")
            setSerialInput(tier, Random.nextInt(1, 65536).toString())
        }
    }

    fun readLed(tier: Tier) = viewModelScope.launch { container.commands.readLed(selected.value, tier) }

    fun tapLed(tier: Tier) = viewModelScope.launch { container.commands.tapLed(selected.value, tier) }

    fun echo(tier: Tier) = viewModelScope.launch {
        val payload = (echoInput.value[tier] ?: "").toByteArray(Charsets.UTF_8)
        container.commands.run(selected.value, tier) { it.echo(payload) }?.let { reply ->
            val text = reply.toString(Charsets.UTF_8)
            if (text.isNotEmpty()) container.toasts.info(text)
        }
    }
}

/** The Loop tab. */
class LoopViewModel(private val container: AppContainer) : ViewModel() {
    val state: StateFlow<LoopState> = container.loop.state
    val selected: StateFlow<Advertisement?> = container.selection.selected

    fun start() = viewModelScope.launch { selected.value?.let { container.loop.start(it) } }

    fun stop() = viewModelScope.launch { container.loop.stop() }
}
