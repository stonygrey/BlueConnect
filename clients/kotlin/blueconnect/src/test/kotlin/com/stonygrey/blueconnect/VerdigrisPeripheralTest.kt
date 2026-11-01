// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect

import com.stonygrey.blueconnect.abstractions.BleTimeoutException
import com.stonygrey.blueconnect.abstractions.BleTransportException
import com.stonygrey.blueconnect.abstractions.BlueConnectOptions
import com.stonygrey.blueconnect.secure.LinkIdentity
import com.stonygrey.blueconnect.secure.LinkSecurity
import com.stonygrey.blueconnect.verdigris.Privilege
import com.stonygrey.blueconnect.verdigris.VerdigrisException
import com.stonygrey.blueconnect.verdigris.VerdigrisPeripheral
import java.util.concurrent.CopyOnWriteArrayList
import kotlin.time.Duration
import kotlin.time.Duration.Companion.milliseconds
import kotlin.time.Duration.Companion.seconds
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Test
import verdigris.Verdigris

/** Commands against the in-process device: id matching, status mapping, stale responses, result-type checks, and the command timeout. */
class VerdigrisPeripheralTest {
    private fun makePair(
        commandTimeout: Duration = 5.seconds,
        handler: (Verdigris.Request) -> List<Verdigris.Response> = FakeDevice::echoHandler,
    ): Pair<VerdigrisPeripheral, FakeDevice> {
        val (app, dev) = LoopbackPipe.pair(20)
        val device = FakeDevice(dev, handler = handler)
        device.start()
        val verdigris = VerdigrisPeripheral(app, BlueConnectOptions(commandTimeout = commandTimeout), LinkSecurity(LinkIdentity.generate(), null))
        return verdigris to device
    }

    @Test
    fun echoRoundTripsThroughTheSecureLink() = runBlocking {
        val (verdigris, device) = makePair()
        try {
            assertArrayEquals(payload(1536), verdigris.echo(payload(1536)))
            assertArrayEquals(device.identity.publicKey, verdigris.remoteStaticKey)
        } finally {
            device.stop()
        }
    }

    @Test
    fun requestIdsIncrementAndTheReplyIsMatched() = runBlocking {
        val seen = CopyOnWriteArrayList<Int>()
        val (verdigris, device) = makePair { request ->
            seen.add(request.id)
            FakeDevice.echoHandler(request)
        }
        try {
            verdigris.getSerialNumber()
            verdigris.getSerialNumber()
            assertEquals(listOf(1, 2), seen.toList())
        } finally {
            device.stop()
        }
    }

    @Test
    fun statusOtherThanOkBecomesVerdigrisException() = runBlocking {
        val (verdigris, device) = makePair { request ->
            listOf(Verdigris.Response.newBuilder().setId(request.id).setStatus(Verdigris.Status.STATUS_PERMISSION_DENIED).setDetail("high privilege required").build())
        }
        try {
            verdigris.echo(byteArrayOf(1))
            fail("expected VerdigrisException")
        } catch (e: VerdigrisException) {
            assertEquals(Verdigris.Status.STATUS_PERMISSION_DENIED, e.status)
            assertEquals("high privilege required", e.detail)
            assertEquals("The device rejected the request: STATUS_PERMISSION_DENIED (high privilege required)", e.message)
        } finally {
            device.stop()
        }
    }

    @Test
    fun staleResponsesAreDropped() = runBlocking {
        val (verdigris, device) = makePair { request ->
            listOf(
                Verdigris.Response.newBuilder().setId(request.id + 100).setStatus(Verdigris.Status.STATUS_OK).setName(Verdigris.NameResponse.newBuilder().setName("stale")).build(),
                Verdigris.Response.newBuilder().setId(request.id).setStatus(Verdigris.Status.STATUS_OK).setName(Verdigris.NameResponse.newBuilder().setName("Verdigris 0042")).build(),
            )
        }
        try {
            assertEquals("Verdigris 0042", verdigris.getSerialNumber())
        } finally {
            device.stop()
        }
    }

    @Test
    fun wrongResultTypeIsATransportError() = runBlocking {
        val (verdigris, device) = makePair { request ->
            listOf(Verdigris.Response.newBuilder().setId(request.id).setStatus(Verdigris.Status.STATUS_OK).setLed(Verdigris.LedResponse.newBuilder().setOn(true)).build())
        }
        try {
            verdigris.getSerialNumber()
            fail("expected BleTransportException")
        } catch (e: BleTransportException) {
            assertTrue(e.message!!.contains("where NAME was expected"))
        } finally {
            device.stop()
        }
    }

    @Test
    fun commandTimesOutWhenTheDeviceIsSilent() = runBlocking {
        val (verdigris, device) = makePair(commandTimeout = 200.milliseconds) { emptyList() }
        try {
            verdigris.getLed()
            fail("expected BleTimeoutException")
        } catch (e: BleTimeoutException) {
            assertTrue(e.message!!.contains("timed out after 0s"))
        } finally {
            device.stop()
        }
    }

    @Test
    fun pairReturnsTheDeviceKey() = runBlocking {
        val (verdigris, device) = makePair()
        try {
            assertArrayEquals(device.identity.publicKey, verdigris.pair(Privilege.HIGH))
        } finally {
            device.stop()
        }
    }

    @Test
    fun oversizeRequestIsRejectedBeforeSending() = runBlocking {
        val (verdigris, device) = makePair()
        try {
            verdigris.echo(payload(VerdigrisPeripheral.MAX_ENCODED_REQUEST))
            fail("expected IllegalStateException")
        } catch (e: IllegalStateException) {
            assertTrue(e.message!!.contains("Encoded request"))
        } finally {
            device.stop()
        }
    }
}
