// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.verdigris

import com.google.protobuf.ByteString
import com.google.protobuf.InvalidProtocolBufferException
import com.stonygrey.blueconnect.abstractions.BlePeripheral
import com.stonygrey.blueconnect.abstractions.BleTimeoutException
import com.stonygrey.blueconnect.abstractions.BleTransportException
import com.stonygrey.blueconnect.abstractions.BlueConnectException
import com.stonygrey.blueconnect.abstractions.BlueConnectOptions
import com.stonygrey.blueconnect.core.IoLog
import com.stonygrey.blueconnect.secure.LinkProtocol
import com.stonygrey.blueconnect.secure.LinkSecurity
import com.stonygrey.blueconnect.secure.SecureLink
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull
import verdigris.Verdigris

/** The privilege tier a pairing grants. */
enum class Privilege(internal val proto: Verdigris.Privilege) {
    LOW(Verdigris.Privilege.PRIVILEGE_LOW),
    HIGH(Verdigris.Privilege.PRIVILEGE_HIGH),
}

/** `VerdigrisException`: the device answered with a status other than OK. */
class VerdigrisException(val status: Verdigris.Status, detail: String?) :
    BlueConnectException("The device rejected the request: ${status.name}${if (detail.isNullOrEmpty()) "" else " ($detail)"}") {
    val detail: String? = detail?.takeIf { it.isNotEmpty() }
}

/**
 * Typed Verdigris commands over a secure link. One command at a time; each
 * request carries a fresh id and the matching response is awaited under
 * [BlueConnectOptions.commandTimeout].
 */
class VerdigrisPeripheral(
    val peripheral: BlePeripheral,
    options: BlueConnectOptions,
    security: LinkSecurity,
) {
    companion object {
        /**
         * Cap on a reassembled GATT message. The value is arbitrary and specific to this
         * implementation; it is not negotiated, so it must equal the firmware's `SEG_MAX_MESSAGE`
         * and the two should be kept in sync.
         */
        const val MAX_FRAME = 2304
        const val MAX_ENCODED_REQUEST = MAX_FRAME - 1 - LinkProtocol.TAG_LENGTH

        /**
         * Longer than the firmware's 30 s pairing window (`PAIR_WINDOW_SECONDS`), an arbitrary
         * implementation value. The two should be kept in sync, so that the app is still waiting
         * when the device answers at the end of the window.
         */
        val PAIR_TIMEOUT: Duration = 40.seconds
    }

    private val link = SecureLink(peripheral, MAX_FRAME, security)
    private val commandTimeout = options.commandTimeout
    private val gate = Mutex()
    private var nextId = 0
    private var closed = false

    /** The device's proven static key once a session has been established. */
    val remoteStaticKey: ByteArray? get() = link.remoteStaticKey

    suspend fun echo(payload: ByteArray): ByteArray =
        invoke(request { echo = Verdigris.EchoRequest.newBuilder().setPayload(ByteString.copyFrom(payload)).build() }, Verdigris.Response.ResultCase.ECHO)
            .echo.payload.toByteArray()

    suspend fun squareRoot(value: Double): Double =
        invoke(request { sqrt = Verdigris.SqrtRequest.newBuilder().setValue(value).build() }, Verdigris.Response.ResultCase.SQRT).sqrt.value

    suspend fun fibonacci(n: Int): Long =
        invoke(request { fib = Verdigris.FibRequest.newBuilder().setN(n).build() }, Verdigris.Response.ResultCase.FIB).fib.value

    suspend fun getTimeMillis(): Long =
        invoke(request { getTime = Verdigris.GetTimeRequest.getDefaultInstance() }, Verdigris.Response.ResultCase.TIME).time.unixMillis

    suspend fun setTimeMillis(unixMillis: Long) {
        invoke(request { setTime = Verdigris.SetTimeRequest.newBuilder().setUnixMillis(unixMillis).build() }, Verdigris.Response.ResultCase.TIME)
    }

    suspend fun getLed(): Boolean =
        invoke(request { getLed = Verdigris.GetLedRequest.getDefaultInstance() }, Verdigris.Response.ResultCase.LED).led.on

    suspend fun setLed(on: Boolean): Boolean =
        invoke(request { setLed = Verdigris.SetLedRequest.newBuilder().setOn(on).build() }, Verdigris.Response.ResultCase.LED).led.on

    suspend fun setSerialNumber(serialNumber: Int): String {
        require(serialNumber in 1..65535) { "Serial number must be a whole number from 1 to 65535." }
        return invoke(request { setSerialNumber = Verdigris.SetSerialNumberRequest.newBuilder().setSerialNumber(serialNumber).build() }, Verdigris.Response.ResultCase.NAME)
            .name.name
    }

    suspend fun getSerialNumber(): String =
        invoke(request { getSerialNumber = Verdigris.GetSerialNumberRequest.getDefaultInstance() }, Verdigris.Response.ResultCase.NAME).name.name

    /** Arms the device's 30 s pairing window for [privilege] and waits for the button. Returns the device key to pin. */
    suspend fun pair(privilege: Privilege): ByteArray {
        invoke(request { pair = Verdigris.PairRequest.newBuilder().setPrivilege(privilege.proto).build() }, Verdigris.Response.ResultCase.ACK, PAIR_TIMEOUT)
        return link.remoteStaticKey!!
    }

    suspend fun unpair() {
        invoke(request { unpair = Verdigris.UnpairRequest.getDefaultInstance() }, Verdigris.Response.ResultCase.ACK)
    }

    suspend fun enroll(clientPublicKey: ByteArray) {
        invoke(request { enroll = Verdigris.EnrollRequest.newBuilder().setClientPublicKey(ByteString.copyFrom(clientPublicKey)).build() }, Verdigris.Response.ResultCase.ACK)
    }

    /** Closes the link and the peripheral, disconnecting first if connected. */
    suspend fun close() {
        if (closed) return
        closed = true
        link.close()
        peripheral.close()
    }

    private inline fun request(block: Verdigris.Request.Builder.() -> Unit): Verdigris.Request =
        Verdigris.Request.newBuilder().apply(block).build()

    private suspend fun invoke(request: Verdigris.Request, expected: Verdigris.Response.ResultCase, timeout: Duration = commandTimeout): Verdigris.Response {
        check(!closed) { "The Verdigris peripheral has been closed." }
        gate.withLock {
            val id = ++nextId
            val body = request.toBuilder().setId(id).build().toByteArray()
            check(body.size <= MAX_ENCODED_REQUEST) {
                "Encoded request is ${body.size} bytes; the maximum is $MAX_ENCODED_REQUEST (type + AEAD tag must fit under $MAX_FRAME)."
            }
            return withTimeoutOrNull(timeout) { exchange(id, body, expected) }
                ?: throw BleTimeoutException("Verdigris command timed out after ${timeout.inWholeSeconds}s.")
        }
    }

    private suspend fun exchange(id: Int, body: ByteArray, expected: Verdigris.Response.ResultCase): Verdigris.Response {
        link.send(body)
        while (true) {
            val raw = link.receive()
            val response =
                try {
                    Verdigris.Response.parseFrom(raw)
                } catch (e: InvalidProtocolBufferException) {
                    throw BleTransportException("Undecodable Verdigris frame; the stream is desynchronized.", e)
                }
            if (response.id != id) {
                IoLog.debug("Dropping stale Verdigris response ${response.id} while awaiting $id.")
                continue
            }
            if (response.status != Verdigris.Status.STATUS_OK) {
                throw VerdigrisException(response.status, response.detail)
            }
            if (response.resultCase != expected) {
                throw BleTransportException("Verdigris response carried ${response.resultCase} where $expected was expected.")
            }
            return response
        }
    }
}
