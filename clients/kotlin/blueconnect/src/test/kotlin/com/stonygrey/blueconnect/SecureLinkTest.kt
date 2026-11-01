// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect

import com.stonygrey.blueconnect.secure.LinkIdentity
import com.stonygrey.blueconnect.secure.LinkSecurity
import com.stonygrey.blueconnect.secure.SecureLink
import com.stonygrey.blueconnect.verdigris.VerdigrisPeripheral
import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test
import verdigris.Verdigris

/** The initiator against Torann's responder over the loopback pipe. */
class SecureLinkTest {
    @Test
    fun unpinnedInitiatorRunsXxAndLearnsTheDeviceKey() = runBlocking {
        val (app, dev) = LoopbackPipe.pair(20)
        val device = FakeDevice(dev)
        device.start()
        try {
            val link = SecureLink(app, VerdigrisPeripheral.MAX_FRAME, LinkSecurity(LinkIdentity.generate(), null))
            link.establish()
            assertArrayEquals(device.identity.publicKey, link.remoteStaticKey)
            // One sealed round trip so that the device has consumed message 3.
            val request = Verdigris.Request.newBuilder().setId(1).setGetSerialNumber(Verdigris.GetSerialNumberRequest.getDefaultInstance()).build()
            link.send(request.toByteArray())
            link.receive()
            assertEquals("XX", device.observedPattern)
            assertEquals(listOf(32, 97, 65), device.observedHandshakeLengths)
        } finally {
            device.stop()
        }
    }

    @Test
    fun pinnedInitiatorRunsIk() = runBlocking {
        val (app, dev) = LoopbackPipe.pair(20)
        val device = FakeDevice(dev)
        device.start()
        try {
            val link = SecureLink(app, VerdigrisPeripheral.MAX_FRAME, LinkSecurity(LinkIdentity.generate(), device.identity.publicKey))
            link.establish()
            assertArrayEquals(device.identity.publicKey, link.remoteStaticKey)
            assertEquals("IK", device.observedPattern)
            assertEquals(listOf(97, 49), device.observedHandshakeLengths)
        } finally {
            device.stop()
        }
    }

    @Test
    fun sealedMessagesRoundTrip() = runBlocking {
        val (app, dev) = LoopbackPipe.pair(20)
        val device = FakeDevice(dev)
        device.start()
        try {
            val link = SecureLink(app, VerdigrisPeripheral.MAX_FRAME, LinkSecurity(LinkIdentity.generate(), null))
            val request = Verdigris.Request.newBuilder().setId(7)
                .setEcho(Verdigris.EchoRequest.newBuilder().setPayload(com.google.protobuf.ByteString.copyFrom(payload(300)))).build()
            link.send(request.toByteArray())
            val reply = Verdigris.Response.parseFrom(link.receive())
            assertEquals(7, reply.id)
            assertArrayEquals(payload(300), reply.echo.payload.toByteArray())
        } finally {
            device.stop()
        }
    }

    @Test
    fun sendAfterCloseIsRejected() = runBlocking {
        val (app, _) = LoopbackPipe.pair(20)
        val link = SecureLink(app, VerdigrisPeripheral.MAX_FRAME, LinkSecurity(LinkIdentity.generate(), null))
        link.close()
        assertThrows(IllegalStateException::class.java) { runBlocking { link.send(byteArrayOf(1)) } }
        Unit
    }

    @Test
    fun identityDerivesThePublicKey() {
        val identity = LinkIdentity.generate()
        val again = LinkIdentity(identity.privateKey)
        assertArrayEquals(identity.publicKey, again.publicKey)
        assertEquals(32, identity.publicKey.size)
        assertThrows(IllegalArgumentException::class.java) { LinkIdentity(ByteArray(31)) }
    }
}
