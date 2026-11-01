// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.zaffre.data

import android.util.Log
import com.stonygrey.blueconnect.abstractions.Advertisement
import com.stonygrey.blueconnect.core.IoLog
import com.stonygrey.blueconnect.secure.LinkIdentity
import com.stonygrey.blueconnect.secure.LinkSecurity
import com.stonygrey.blueconnect.verdigris.Privilege
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** The two privilege tiers, each with its own app identity and pin. */
enum class Tier(val privilege: Privilege, val title: String, val button: String, val logName: String) {
    LOW(Privilege.LOW, "Low privilege", "Button 2", "Low"),
    HIGH(Privilege.HIGH, "High privilege", "Button 3", "High");

    val key: String get() = name.lowercase()
}

/** The source of a pin: the button ceremony, a USB-provisioned QR, or enrollment. */
enum class CredentialOrigin(val label: String) {
    PAIRED("Paired"),
    PROVISIONED("Provisioned"),
    ENROLLED("Enrolled");

    companion object {
        fun parse(value: String?): CredentialOrigin? = entries.firstOrNull { it.name.equals(value, ignoreCase = true) }
    }
}

data class TierCredential(
    val identity: LinkIdentity,
    val deviceKey: ByteArray? = null,
    val deviceName: String? = null,
    val origin: CredentialOrigin? = null,
) {
    val isPaired: Boolean get() = deviceKey != null
}

/**
 * Identity and pin storage per tier. A missing identity is generated on
 * first use.
 */
class PairingRepository(private val store: CredentialStore) {
    private val _credentials = MutableStateFlow<Map<Tier, TierCredential>>(emptyMap())
    val credentials: StateFlow<Map<Tier, TierCredential>> = _credentials.asStateFlow()

    init {
        _credentials.value = Tier.entries.associateWith { tier ->
            val identity = loadOrGenerateIdentity(tier)
            val deviceKey = store.get("zaffre.pair.device.key.${tier.key}")
            val deviceName = store.get("zaffre.pair.device.name.${tier.key}")?.toString(Charsets.UTF_8)
            val origin = CredentialOrigin.parse(store.get("zaffre.pair.origin.${tier.key}")?.toString(Charsets.UTF_8))
            Log.i(TAG, "ZAFFRE-NOISE-PUB: ${tier.logName} ${IoLog.hex(identity.publicKey)} cert=none.")
            TierCredential(identity, deviceKey, deviceName, if (deviceKey == null) null else origin ?: CredentialOrigin.PAIRED)
        }
    }

    fun credential(tier: Tier): TierCredential = _credentials.value.getValue(tier)

    /** The security for a normal session: IK against the pin when paired. */
    fun security(tier: Tier): LinkSecurity = credential(tier).let { LinkSecurity(it.identity, it.deviceKey) }

    /** The security for the pairing ceremony: XX, any device key accepted. */
    fun pairingSecurity(tier: Tier): LinkSecurity = LinkSecurity(credential(tier).identity, null)

    /** Pins the device key for the tier: after the button ceremony (`PAIRED`) or after `Enroll` (`ENROLLED`). */
    fun record(tier: Tier, deviceKey: ByteArray, deviceName: String?, origin: CredentialOrigin = CredentialOrigin.PAIRED) {
        store.set("zaffre.pair.device.key.${tier.key}", deviceKey)
        store.set("zaffre.pair.device.name.${tier.key}", deviceName?.toByteArray(Charsets.UTF_8))
        store.set("zaffre.pair.origin.${tier.key}", origin.name.lowercase().toByteArray(Charsets.UTF_8))
        update(tier) { it.copy(deviceKey = deviceKey, deviceName = deviceName, origin = origin) }
        Log.i(TAG, "${origin.label} (${tier.logName}) with '${deviceName ?: ""}' (${IoLog.hex(deviceKey)}).")
    }

    /** Installs a USB-provisioned credential: the identity from the QR replaces the tier's own, and the device key is pinned. No BLE traffic. */
    fun storeProvisioned(tier: Tier, identity: LinkIdentity, deviceKey: ByteArray, deviceName: String?) {
        store.set("zaffre.link.identity.${tier.key}", identity.privateKey)
        update(tier) { it.copy(identity = identity) }
        record(tier, deviceKey, deviceName, CredentialOrigin.PROVISIONED)
    }

    /**
     * Deletes the pin. A provisioned tier also gets a fresh identity, because the
     * one from the QR stays locked into the device's slot until USB possession clears it.
     */
    fun clear(tier: Tier) {
        val wasProvisioned = credential(tier).origin == CredentialOrigin.PROVISIONED
        store.set("zaffre.pair.device.key.${tier.key}", null)
        store.set("zaffre.pair.device.name.${tier.key}", null)
        store.set("zaffre.pair.origin.${tier.key}", null)
        update(tier) { it.copy(deviceKey = null, deviceName = null, origin = null) }
        if (wasProvisioned) {
            store.set("zaffre.link.identity.${tier.key}", null)
            val identity = loadOrGenerateIdentity(tier)
            update(tier) { it.copy(identity = identity) }
        }
    }

    private fun update(tier: Tier, change: (TierCredential) -> TierCredential) {
        _credentials.value = _credentials.value + (tier to change(credential(tier)))
    }

    private fun loadOrGenerateIdentity(tier: Tier): LinkIdentity {
        val key = "zaffre.link.identity.${tier.key}"
        store.get(key)?.let { stored -> runCatching { LinkIdentity(stored) }.getOrNull()?.let { return it } }
        val identity = LinkIdentity.generate()
        store.set(key, identity.privateKey)
        Log.i(TAG, "Generated ${tier.logName} app link identity ${IoLog.hex(identity.publicKey)}.")
        return identity
    }

    companion object {
        const val TAG = "Zaffre"
    }
}

/** The device the user tapped on the scan list, shared by every screen. */
class DeviceSelection {
    private val _selected = MutableStateFlow<Advertisement?>(null)
    val selected: StateFlow<Advertisement?> = _selected.asStateFlow()

    fun select(advertisement: Advertisement?) {
        _selected.value = advertisement
    }

    /** Replaces the selection with a fresher advertisement of the same device. */
    fun refresh(advertisements: List<Advertisement>) {
        val current = _selected.value ?: return
        advertisements.firstOrNull { it.id == current.id }?.let { _selected.value = it }
    }
}
