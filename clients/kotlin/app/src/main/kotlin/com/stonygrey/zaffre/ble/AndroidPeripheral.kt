// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.zaffre.ble

import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothGatt
import android.bluetooth.BluetoothGattCallback
import android.bluetooth.BluetoothGattCharacteristic
import android.bluetooth.BluetoothGattDescriptor
import android.bluetooth.BluetoothProfile
import android.bluetooth.BluetoothStatusCodes
import android.content.Context
import com.stonygrey.blueconnect.abstractions.Advertisement
import com.stonygrey.blueconnect.abstractions.BlePeripheral
import com.stonygrey.blueconnect.abstractions.BleTimeoutException
import com.stonygrey.blueconnect.abstractions.BleTransportException
import com.stonygrey.blueconnect.abstractions.BlueConnectConstants
import com.stonygrey.blueconnect.abstractions.BlueConnectOptions
import com.stonygrey.blueconnect.abstractions.ConnectionState
import com.stonygrey.blueconnect.core.ConnectRetry
import com.stonygrey.blueconnect.core.IoLog
import java.util.UUID
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.coroutines.cancellation.CancellationException
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.channels.BufferOverflow
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.withTimeoutOrNull

/**
 * One GATT connection through `BluetoothGatt`. Each asynchronous step parks a
 * [CompletableDeferred] that the matching callback completes. Only the API 33
 * overloads are used, because the app supports Android 14 and later. No
 * bonding, no connection priority request, no cache refresh.
 */
class AndroidPeripheral(
    private val context: Context,
    private val scanner: AndroidScanner,
    private val adapter: BluetoothAdapter,
    override val advertisement: Advertisement,
    private val options: BlueConnectOptions,
) : BlePeripheral {
    private enum class Slot { CONNECT, DISCONNECT, MTU, SERVICES, SUBSCRIBE, WRITE }

    private val serviceUuid = UUID.fromString(options.serviceUuid)
    private val rxUuid = UUID.fromString(options.rxCharacteristicUuid)
    private val txUuid = UUID.fromString(options.txCharacteristicUuid)
    private val cccdUuid = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb")
    private val lock = Any()
    private val waiters = HashMap<Slot, CompletableDeferred<Unit>>()
    private val received = Channel<ByteArray>(Channel.UNLIMITED)
    private val _states = MutableSharedFlow<ConnectionState>(extraBufferCapacity = 16, onBufferOverflow = BufferOverflow.DROP_OLDEST)
    private val gattClosed = AtomicBoolean(false)
    private var gatt: BluetoothGatt? = null
    private var rx: BluetoothGattCharacteristic? = null

    @Volatile
    private var mtuValue = 23

    @Volatile
    private var connected = false

    @Volatile
    private var closed = false

    override val isConnected: Boolean get() = connected

    /** The usable ATT payload, `MTU - 3`, never below the 20 of the default MTU. */
    override val mtu: Int get() = if (connected) maxOf(20, mtuValue - 3) else 0

    override val states: Flow<ConnectionState> = _states

    override suspend fun connect() {
        check(!closed) { "The peripheral has been closed." }
        ConnectRetry.run(
            options,
            advertisement.localName,
            isPresent = {
                val seen = scanner.lastSeen(advertisement.id)
                seen != null && System.currentTimeMillis() - seen <= BlueConnectConstants.SCANNER_EXPIRATION.inWholeMilliseconds
            },
            attempt = ::connectOnce,
        )
    }

    private suspend fun connectOnce(timeout: Duration) {
        scanner.register(advertisement.id)
        try {
            withTimeoutOrNull(timeout) { connectCore() }
                ?: throw BleTimeoutException("Connecting to '${advertisement.localName}' timed out after ${timeout.inWholeSeconds}s.")
            connected = true
            _states.tryEmit(ConnectionState.CONNECTED)
        } catch (e: CancellationException) {
            abortConnect()
            throw e
        } catch (e: Exception) {
            abortConnect()
            throw e
        }
    }

    private suspend fun connectCore() {
        val device = adapter.getRemoteDevice(advertisement.id)
        val connect = newWaiter(Slot.CONNECT)
        gattClosed.set(false)
        gatt = device.connectGatt(context, false, callback, BluetoothDevice.TRANSPORT_LE)
        connect.await()
        val g = gatt ?: throw BleTransportException("GATT was closed during connect.")
        // A refused or failed MTU request is not fatal: the link stays at 23.
        val mtuWaiter = newWaiter(Slot.MTU)
        if (g.requestMtu(247)) mtuWaiter.await() else clearWaiter(Slot.MTU)
        val services = newWaiter(Slot.SERVICES)
        if (!g.discoverServices()) {
            throw BleTransportException("Service discovery could not be started.")
        }
        services.await()
        val service = g.getService(serviceUuid)
            ?: throw BleTransportException("Peripheral does not expose GATT service ${options.serviceUuid}.")
        rx = service.getCharacteristic(rxUuid)
            ?: throw BleTransportException("RX characteristic ${options.rxCharacteristicUuid} not found.")
        val tx = service.getCharacteristic(txUuid)
            ?: throw BleTransportException("TX characteristic ${options.txCharacteristicUuid} not found.")
        g.setCharacteristicNotification(tx, true)
        val cccd = tx.getDescriptor(cccdUuid) ?: throw BleTransportException("TX characteristic has no CCCD.")
        val subscribe = newWaiter(Slot.SUBSCRIBE)
        val status = g.writeDescriptor(cccd, BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE)
        if (status != BluetoothStatusCodes.SUCCESS) {
            throw BleTransportException("Enabling notifications failed: $status")
        }
        subscribe.await()
    }

    override suspend fun disconnect() {
        if (!connected) return
        val g = gatt ?: return
        val waiter = newWaiter(Slot.DISCONNECT)
        g.disconnect()
        try {
            waiter.await()
        } finally {
            closeGatt()
            connected = false
            _states.tryEmit(ConnectionState.DISCONNECTED)
        }
    }

    override suspend fun read(): ByteArray = received.receive()

    override suspend fun write(bytes: ByteArray) {
        check(connected) { "Peripheral is not connected." }
        val g = gatt ?: throw IllegalStateException("Peripheral is not connected.")
        val rx = rx ?: throw IllegalStateException("Peripheral is not connected.")
        val mtu = maxOf(1, this.mtu)
        var offset = 0
        while (offset < bytes.size) {
            val end = minOf(offset + mtu, bytes.size)
            val waiter = newWaiter(Slot.WRITE)
            val status = g.writeCharacteristic(rx, bytes.copyOfRange(offset, end), BluetoothGattCharacteristic.WRITE_TYPE_DEFAULT)
            if (status != BluetoothStatusCodes.SUCCESS) {
                clearWaiter(Slot.WRITE)
                throw BleTransportException("Characteristic write failed: $status")
            }
            waiter.await()
            offset = end
        }
    }

    override suspend fun close() {
        if (closed) return
        closed = true
        if (connected) {
            withTimeoutOrNull(5.seconds) { runCatching { disconnect() } }
        } else {
            abortConnect()
        }
        closeGatt()
        scanner.unregister(advertisement.id)
        received.close()
    }

    private fun abortConnect() {
        gatt?.disconnect()
        closeGatt()
    }

    private fun closeGatt() {
        if (gattClosed.compareAndSet(false, true)) {
            gatt?.close()
            gatt = null
        }
    }

    private fun newWaiter(slot: Slot): CompletableDeferred<Unit> {
        val waiter = CompletableDeferred<Unit>()
        synchronized(lock) { waiters[slot] = waiter }
        return waiter
    }

    private fun clearWaiter(slot: Slot) {
        synchronized(lock) { waiters.remove(slot) }
    }

    private fun resume(slot: Slot, error: Throwable?) {
        val waiter = synchronized(lock) { waiters.remove(slot) } ?: return
        if (error == null) waiter.complete(Unit) else waiter.completeExceptionally(error)
    }

    private val callback = object : BluetoothGattCallback() {
        override fun onConnectionStateChange(gatt: BluetoothGatt, status: Int, newState: Int) {
            if (newState == BluetoothProfile.STATE_CONNECTED && status == BluetoothGatt.GATT_SUCCESS) {
                resume(Slot.CONNECT, null)
                return
            }
            if (newState != BluetoothProfile.STATE_DISCONNECTED) return
            val expected = synchronized(lock) { waiters.remove(Slot.DISCONNECT) }
            if (expected != null) {
                expected.complete(Unit)
                return
            }
            // Unexpected drop, or a connect that never reached CONNECTED: fail whatever is in flight.
            val failure = BleTransportException("Peripheral disconnected: GATT status $status")
            for (slot in listOf(Slot.CONNECT, Slot.MTU, Slot.SERVICES, Slot.SUBSCRIBE, Slot.WRITE)) {
                resume(slot, failure)
            }
            if (connected) {
                connected = false
                _states.tryEmit(ConnectionState.DISCONNECTED)
            }
        }

        override fun onMtuChanged(gatt: BluetoothGatt, mtu: Int, status: Int) {
            if (status == BluetoothGatt.GATT_SUCCESS) mtuValue = mtu
            resume(Slot.MTU, null)
        }

        override fun onServicesDiscovered(gatt: BluetoothGatt, status: Int) {
            resume(Slot.SERVICES, if (status == BluetoothGatt.GATT_SUCCESS) null else BleTransportException("Service discovery failed: $status"))
        }

        override fun onDescriptorWrite(gatt: BluetoothGatt, descriptor: BluetoothGattDescriptor, status: Int) {
            resume(Slot.SUBSCRIBE, if (status == BluetoothGatt.GATT_SUCCESS) null else BleTransportException("Descriptor write failed: $status"))
        }

        override fun onCharacteristicWrite(gatt: BluetoothGatt, characteristic: BluetoothGattCharacteristic, status: Int) {
            resume(Slot.WRITE, if (status == BluetoothGatt.GATT_SUCCESS) null else BleTransportException("Characteristic write failed: $status"))
        }

        override fun onCharacteristicChanged(gatt: BluetoothGatt, characteristic: BluetoothGattCharacteristic, value: ByteArray) {
            received.trySend(value)
        }
    }
}
