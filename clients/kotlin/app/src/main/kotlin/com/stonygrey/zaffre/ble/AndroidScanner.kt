// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.zaffre.ble

import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothManager
import android.bluetooth.le.ScanCallback
import android.bluetooth.le.ScanFilter
import android.bluetooth.le.ScanResult
import android.bluetooth.le.ScanSettings
import android.content.Context
import android.os.ParcelUuid
import com.stonygrey.blueconnect.abstractions.Advertisement
import com.stonygrey.blueconnect.abstractions.BlePeripheral
import com.stonygrey.blueconnect.abstractions.BleScanner
import com.stonygrey.blueconnect.abstractions.BleTransportException
import com.stonygrey.blueconnect.abstractions.BlueConnectConstants
import com.stonygrey.blueconnect.abstractions.BlueConnectOptions
import com.stonygrey.blueconnect.abstractions.ScanEvent
import com.stonygrey.blueconnect.core.AdvertisementRegistry
import com.stonygrey.blueconnect.core.IoLog
import java.util.UUID
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.flow.Flow

/**
 * The Android scanner: a low-latency scan filtered on the Verdigris service
 * UUID. Android merges the scan response into the result, so the filter
 * matches the UUID the device sends there.
 */
class AndroidScanner(context: Context, private val options: BlueConnectOptions, scope: CoroutineScope) : BleScanner {
    private val appContext = context.applicationContext
    private val adapter: BluetoothAdapter? = appContext.getSystemService(BluetoothManager::class.java)?.adapter
    private val registry = AdvertisementRegistry(minDbm = -127, maxDbm = 20, scope = scope)
    private val serviceUuid = ParcelUuid(UUID.fromString(options.serviceUuid))

    @Volatile
    private var scanning = false

    private val callback = object : ScanCallback() {
        override fun onScanResult(callbackType: Int, result: ScanResult) = handle(result)

        override fun onBatchScanResults(results: MutableList<ScanResult>) = results.forEach(::handle)

        override fun onScanFailed(errorCode: Int) {
            IoLog.warn("Scan failed: $errorCode")
            scanning = errorCode == SCAN_FAILED_ALREADY_STARTED
        }
    }

    override val peripherals: List<Advertisement> get() = registry.peripherals

    override val isScanning: Boolean get() = scanning

    override val events: Flow<ScanEvent> = registry.events

    override fun lastSeen(id: String): Long? = registry.lastSeen(id)

    override suspend fun start() {
        val adapter = adapter ?: throw BleTransportException("Bluetooth is unavailable.")
        if (!adapter.isEnabled) {
            throw BleTransportException("Bluetooth is disabled.")
        }
        if (scanning) return
        val filter = ScanFilter.Builder().setServiceUuid(serviceUuid).build()
        val settings = ScanSettings.Builder().setScanMode(ScanSettings.SCAN_MODE_LOW_LATENCY).build()
        val scanner = adapter.bluetoothLeScanner ?: throw BleTransportException("Bluetooth is disabled.")
        try {
            scanner.startScan(listOf(filter), settings, callback)
            scanning = true
        } catch (e: SecurityException) {
            throw BleTransportException("BLUETOOTH_SCAN not granted", e)
        }
    }

    override fun createPeripheral(advertisement: Advertisement): BlePeripheral {
        val cached = registry.cached(advertisement.id)
            ?: throw IllegalStateException("'${advertisement.localName}' (${advertisement.id}) is no longer in the scan cache.")
        return AndroidPeripheral(appContext, this, adapter!!, cached, options)
    }

    /** Marks a device as connecting so that the registry does not expire it. */
    fun register(id: String) = registry.register(id)

    fun unregister(id: String) = registry.unregister(id)

    private fun handle(result: ScanResult) {
        val device = result.device
        val record = result.scanRecord
        // An empty name keeps the previous one.
        val name = record?.deviceName?.takeIf { it.isNotEmpty() } ?: registry.cached(device.address)?.localName ?: "(unnamed)"
        // Android strips the company id, so the first byte is the status byte.
        val data = record?.getManufacturerSpecificData(options.manufacturerCompanyId)
        var secured: Boolean? = null
        if (data != null && data.isNotEmpty()) {
            val status = data[0].toInt() and 0xFF
            secured = status and BlueConnectConstants.SECURED_STATUS_MASK != 0
        }
        registry.observe(device.address, name, result.isConnectable, secured, result.rssi)
    }
}
