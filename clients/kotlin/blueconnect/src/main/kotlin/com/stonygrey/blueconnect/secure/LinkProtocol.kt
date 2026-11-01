// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.secure

import com.stonygrey.torann.NoiseProtocol

/** The Noise suites the link runs. The names are constants Torann is known to parse. */
object LinkProtocol {
    const val NAME = "Noise_XX_25519_AESGCM_SHA256"
    const val PINNED_NAME = "Noise_IK_25519_AESGCM_SHA256"
    const val KEY_LENGTH = 32
    const val TAG_LENGTH = 16
    val xx: NoiseProtocol = NoiseProtocol.parse(NAME)
    val ik: NoiseProtocol = NoiseProtocol.parse(PINNED_NAME)
}

/** The app's long-term X25519 identity for one tier. */
class LinkIdentity(val privateKey: ByteArray) {
    val publicKey: ByteArray

    init {
        require(privateKey.size == LinkProtocol.KEY_LENGTH) { "A link identity private key is ${LinkProtocol.KEY_LENGTH} bytes." }
        publicKey = LinkProtocol.xx.createKeyPair(privateKey).use { it.publicKey.copyOf() }
    }

    companion object {
        fun generate(): LinkIdentity = LinkProtocol.xx.generateKeyPair().use { LinkIdentity(it.privateKey.copyOf()) }
    }
}

/**
 * What a session is opened with: this side's identity and, once paired, the
 * device key it must present. No endorsement: this library does not carry
 * client certificates.
 */
data class LinkSecurity(val identity: LinkIdentity, val pinnedPeerKey: ByteArray?)
