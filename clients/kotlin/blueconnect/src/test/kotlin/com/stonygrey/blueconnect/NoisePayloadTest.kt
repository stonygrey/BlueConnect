// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect

import com.stonygrey.blueconnect.abstractions.LinkSecurityException
import com.stonygrey.blueconnect.secure.NoiseEnrollmentPayload
import com.stonygrey.blueconnect.secure.NoiseQrPayload
import com.stonygrey.blueconnect.verdigris.Privilege
import java.util.Base64
import kotlin.random.Random
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertThrows
import org.junit.Assert.assertTrue
import org.junit.Test

class NoisePayloadTest {
    private fun key(fill: Int) = ByteArray(32) { fill.toByte() }

    @Test
    fun credentialRoundTripPreservesKeysAndTier() {
        val app = Random.nextBytes(32).also { it[0] = (it[0].toInt() or 1).toByte() }
        val device = Random.nextBytes(32).also { it[0] = (it[0].toInt() or 1).toByte() }
        val parsed = checkNotNull(NoiseQrPayload.parse(NoiseQrPayload(Privilege.HIGH, app, device, "Verdigris 1").encode()))
        assertEquals(Privilege.HIGH, parsed.tier)
        assertArrayEquals(app, parsed.appPrivateKey)
        assertArrayEquals(device, parsed.devicePublicKey)
        assertEquals("Verdigris 1", parsed.deviceName)
    }

    @Test
    fun credentialParseRejectsAllZeroKeys() {
        val zeros = Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(64))
        assertNull(NoiseQrPayload.parse("SGNK1:L:$zeros"))
    }

    @Test
    fun enrollmentRoundTripsWithAndWithoutAName() {
        val named = NoiseEnrollmentPayload(key(1), key(2), "Verdigris 0001")
        val parsed = checkNotNull(NoiseEnrollmentPayload.parse(named.encode()))
        assertArrayEquals(named.enrollmentPrivateKey, parsed.enrollmentPrivateKey)
        assertArrayEquals(named.devicePublicKey, parsed.devicePublicKey)
        assertEquals(named.deviceName, parsed.deviceName)
        assertTrue(named.encode().startsWith("SGNE1:"))
        val anonymous = NoiseEnrollmentPayload(key(3), key(4), null)
        val parsed2 = checkNotNull(NoiseEnrollmentPayload.parse(anonymous.encode()))
        assertNull(parsed2.deviceName)
        assertArrayEquals(key(3), parsed2.enrollmentPrivateKey)
        assertArrayEquals(key(4), parsed2.devicePublicKey)
    }

    @Test
    fun eachParserRejectsTheOtherPayloadAndBadBlobs() {
        assertNull(NoiseEnrollmentPayload.parse(NoiseQrPayload(Privilege.LOW, key(1), key(2), null).encode()))
        assertNull(NoiseQrPayload.parse(NoiseEnrollmentPayload(key(1), key(2), null).encode()))
        assertNull(NoiseEnrollmentPayload.parse("SGNE1:not-base64!"))
        assertNull(NoiseEnrollmentPayload.parse("SGNE1:" + Base64.getUrlEncoder().withoutPadding().encodeToString(ByteArray(63))))
        assertThrows(LinkSecurityException::class.java) { NoiseEnrollmentPayload(ByteArray(32), key(2), null).encode() }
    }
}
