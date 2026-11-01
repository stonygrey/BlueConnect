// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.secure

import com.stonygrey.blueconnect.abstractions.LinkSecurityException
import com.stonygrey.blueconnect.verdigris.Privilege
import java.util.Base64

/**
 * A USB-provisioned Noise credential as the QR carries it:
 * `SGNK1:<L|H>:<base64url 64 bytes>[:<NAME>]`, the blob being the app's
 * X25519 private key followed by the device's X25519 public key. The QR is
 * the credential itself.
 */
class NoiseQrPayload(val tier: Privilege, val appPrivateKey: ByteArray, val devicePublicKey: ByteArray, val deviceName: String?) {
    fun encode(): String {
        if (appPrivateKey.size != LinkProtocol.KEY_LENGTH || devicePublicKey.size != LinkProtocol.KEY_LENGTH ||
            isZero(appPrivateKey) || isZero(devicePublicKey)
        ) {
            throw LinkSecurityException("Noise QR payload fields are out of range.")
        }
        val encoded = base64Url.encodeToString(appPrivateKey + devicePublicKey)
        val letter = if (tier == Privilege.HIGH) "H" else "L"
        return if (deviceName.isNullOrEmpty()) "$PREFIX:$letter:$encoded" else "$PREFIX:$letter:$encoded:$deviceName"
    }

    companion object {
        const val PREFIX = "SGNK1"

        /** Null for anything that is not a well-formed SGNK1 payload with non-zero keys. */
        fun parse(text: String): NoiseQrPayload? {
            val parts = text.trim().split(':', limit = 4)
            if (parts.size < 3 || parts[0] != PREFIX) return null
            val tier = when (parts[1]) {
                "L" -> Privilege.LOW
                "H" -> Privilege.HIGH
                else -> return null
            }
            val blob = decodeBase64Url(parts[2]) ?: return null
            if (blob.size != 2 * LinkProtocol.KEY_LENGTH) return null
            val appPrivate = blob.copyOfRange(0, LinkProtocol.KEY_LENGTH)
            val devicePublic = blob.copyOfRange(LinkProtocol.KEY_LENGTH, blob.size)
            if (isZero(appPrivate) || isZero(devicePublic)) return null
            return NoiseQrPayload(tier, appPrivate, devicePublic, parts.getOrNull(3)?.takeIf { it.isNotEmpty() })
        }
    }
}

/**
 * An enrollment card: `SGNE1:<base64url 64 bytes>[:<NAME>]`, the blob being
 * the device's enrollment private key followed by the device's public key.
 * Used once, to enroll the phone's own low-tier key, then discarded.
 */
class NoiseEnrollmentPayload(val enrollmentPrivateKey: ByteArray, val devicePublicKey: ByteArray, val deviceName: String?) {
    fun encode(): String {
        if (enrollmentPrivateKey.size != LinkProtocol.KEY_LENGTH || devicePublicKey.size != LinkProtocol.KEY_LENGTH ||
            isZero(enrollmentPrivateKey) || isZero(devicePublicKey)
        ) {
            throw LinkSecurityException("Noise enrollment payload fields are out of range.")
        }
        val encoded = base64Url.encodeToString(enrollmentPrivateKey + devicePublicKey)
        return if (deviceName.isNullOrEmpty()) "$PREFIX:$encoded" else "$PREFIX:$encoded:$deviceName"
    }

    companion object {
        const val PREFIX = "SGNE1"

        fun parse(text: String): NoiseEnrollmentPayload? {
            val parts = text.trim().split(':', limit = 3)
            if (parts.size < 2 || parts[0] != PREFIX) return null
            val blob = decodeBase64Url(parts[1]) ?: return null
            if (blob.size != 2 * LinkProtocol.KEY_LENGTH) return null
            val enrollmentPrivate = blob.copyOfRange(0, LinkProtocol.KEY_LENGTH)
            val devicePublic = blob.copyOfRange(LinkProtocol.KEY_LENGTH, blob.size)
            if (isZero(enrollmentPrivate) || isZero(devicePublic)) return null
            return NoiseEnrollmentPayload(enrollmentPrivate, devicePublic, parts.getOrNull(2)?.takeIf { it.isNotEmpty() })
        }
    }
}

/** Base64url without padding, as the QR payloads carry it; the decoder also accepts padded input. */
private val base64Url: Base64.Encoder = Base64.getUrlEncoder().withoutPadding()

private fun decodeBase64Url(text: String): ByteArray? =
    try {
        Base64.getUrlDecoder().decode(text)
    } catch (e: IllegalArgumentException) {
        null
    }

private fun isZero(key: ByteArray): Boolean = key.all { it == 0.toByte() }
