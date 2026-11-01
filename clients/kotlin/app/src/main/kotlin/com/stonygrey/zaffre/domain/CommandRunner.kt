// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.zaffre.domain

import android.util.Log
import com.stonygrey.blueconnect.abstractions.Advertisement
import com.stonygrey.blueconnect.abstractions.BleScanner
import com.stonygrey.blueconnect.abstractions.BlueConnectOptions
import com.stonygrey.blueconnect.secure.LinkIdentity
import com.stonygrey.blueconnect.secure.LinkSecurity
import com.stonygrey.blueconnect.secure.NoiseEnrollmentPayload
import com.stonygrey.blueconnect.secure.NoiseQrPayload
import com.stonygrey.blueconnect.verdigris.VerdigrisException
import com.stonygrey.blueconnect.verdigris.VerdigrisPeripheral
import com.stonygrey.blueconnect.verdigris.Privilege
import com.stonygrey.zaffre.data.CredentialOrigin
import com.stonygrey.zaffre.data.PairingRepository
import com.stonygrey.zaffre.data.Tier
import kotlin.coroutines.cancellation.CancellationException
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import verdigris.Verdigris

enum class ToastSeverity { INFO, WARNING, ERROR }

data class Toast(val severity: ToastSeverity, val text: String)

/** The transient messages the app shows as snackbars. */
class Toasts {
    private val _messages = MutableSharedFlow<Toast>(extraBufferCapacity = 8)
    val messages: SharedFlow<Toast> = _messages

    fun info(text: String) = _messages.tryEmit(Toast(ToastSeverity.INFO, text))

    fun warning(text: String) = _messages.tryEmit(Toast(ToastSeverity.WARNING, text))

    fun error(text: String) = _messages.tryEmit(Toast(ToastSeverity.ERROR, text))
}

/** The toast text for [error]. */
fun describe(error: Throwable): String = error.message ?: error.javaClass.simpleName

/**
 * Runs one Verdigris command at a time: each command opens its own
 * connection, runs, disconnects, and releases the peripheral.
 */
class CommandRunner(
    private val scanner: BleScanner,
    private val options: BlueConnectOptions,
    private val pairings: PairingRepository,
    private val toasts: Toasts,
) {
    private val gate = Mutex()
    private val _isBusy = MutableStateFlow(false)
    val isBusy: StateFlow<Boolean> = _isBusy.asStateFlow()

    /** LED 1 as last reported by the device, per tier page; absent until read. */
    private val _ledOn = MutableStateFlow<Map<Tier, Boolean>>(emptyMap())
    val ledOn: StateFlow<Map<Tier, Boolean>> = _ledOn.asStateFlow()

    /** The pairing card's result line. */
    private val _pairResult = MutableStateFlow<Map<Tier, String>>(emptyMap())
    val pairResult: StateFlow<Map<Tier, String>> = _pairResult.asStateFlow()

    /** Connect, run [body], disconnect, close. The connection is never kept between commands. */
    private suspend fun <T> session(device: Advertisement, security: LinkSecurity, body: suspend (VerdigrisPeripheral) -> T): T {
        val peripheral = scanner.createPeripheral(device)
        val verdigris = VerdigrisPeripheral(peripheral, options, security)
        try {
            peripheral.connect()
            val result = body(verdigris)
            peripheral.disconnect()
            return result
        } finally {
            verdigris.close()
        }
    }

    /** A command on the tier's pinned session. A failure becomes an error toast, and null is returned. */
    suspend fun <T> run(device: Advertisement?, tier: Tier, body: suspend (VerdigrisPeripheral) -> T): T? {
        if (device == null || !gate.tryLock()) return null
        _isBusy.value = true
        try {
            return session(device, pairings.security(tier), body)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.w(TAG, "Verdigris command failed against '${device.localName}'.", e)
            toasts.error(describe(e))
            return null
        } finally {
            _isBusy.value = false
            gate.unlock()
        }
    }

    suspend fun readLed(device: Advertisement?, tier: Tier) {
        run(device, tier) { it.getLed() }?.let { on -> _ledOn.update { it + (tier to on) } }
    }

    /** Tap on the LED: read it when unknown, otherwise switch it. */
    suspend fun tapLed(device: Advertisement?, tier: Tier) {
        val current = _ledOn.value[tier]
        if (current == null) {
            readLed(device, tier)
            return
        }
        run(device, tier) { it.setLed(!current) }?.let { on -> _ledOn.update { it + (tier to on) } }
    }

    /** The ceremony: an XX session that accepts any device key, `Pair`, and the button within 30 s. */
    suspend fun pair(device: Advertisement?, tier: Tier) {
        if (device == null || !gate.tryLock()) return
        _isBusy.value = true
        _pairResult.update { it + (tier to "Press ${tier.button} on the device to confirm…") }
        try {
            val key = session(device, pairings.pairingSecurity(tier)) {
                // The marker a hardware test watches for to confirm over USB.
                Log.i(TAG, "ZAFFRE-PAIR: armed ${tier.key}")
                it.pair(tier.privilege)
            }
            pairings.record(tier, key, device.localName)
            _pairResult.update { it + (tier to "") }
        } catch (e: VerdigrisException) {
            if (e.status == Verdigris.Status.STATUS_UNAUTHENTICATED) {
                val detail = e.detail
                _pairResult.update {
                    it + (tier to if (detail != null) "Pairing not confirmed: $detail" else "Pairing not confirmed. Press ${tier.button} on the device within 30 seconds and try again.")
                }
            } else {
                _pairResult.update { it + (tier to "") }
                toasts.error(describe(e))
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            _pairResult.update { it + (tier to "") }
            toasts.error(describe(e))
        } finally {
            _isBusy.value = false
            gate.unlock()
        }
    }

    /**
     * A pasted or scanned credential for the tier's card: an `SGNE1` card enrolls
     * the low tier over the air, an `SGNK1` credential is installed without any
     * traffic, and anything else is refused.
     */
    suspend fun provision(device: Advertisement?, tier: Tier, text: String) {
        val trimmed = text.trim()
        if (trimmed.isEmpty()) {
            toasts.error("Paste the SGNK1 payload first.")
            return
        }
        NoiseEnrollmentPayload.parse(trimmed)?.let { card ->
            if (tier != Tier.LOW) {
                toasts.warning("An enrollment QR enrolls the low tier — scan it on the low card.")
                return
            }
            if (device == null) {
                toasts.error("Select the device first.")
                return
            }
            enroll(device, card)
            return
        }
        val credential = NoiseQrPayload.parse(trimmed)
        if (credential == null) {
            toasts.error("That is not a BlueConnect Noise credential (SGNK1) or enrollment (SGNE1) payload.")
            return
        }
        val payloadTier = if (credential.tier == Privilege.HIGH) Tier.HIGH else Tier.LOW
        if (payloadTier != tier) {
            toasts.warning("That QR is the ${payloadTier.key} Noise credential — scan it on the ${payloadTier.key} card.")
            return
        }
        try {
            pairings.storeProvisioned(tier, LinkIdentity(credential.appPrivateKey), credential.devicePublicKey, credential.deviceName)
        } catch (e: Exception) {
            toasts.error(describe(e))
        }
    }

    /**
     * One IK session as the enrollment key, with the device key from the card
     * pinned, carrying `Enroll` with the low identity's public key. The
     * enrollment key is used for this call and never stored.
     */
    private suspend fun enroll(device: Advertisement, card: NoiseEnrollmentPayload) {
        if (!gate.tryLock()) return
        _isBusy.value = true
        try {
            val security = LinkSecurity(LinkIdentity(card.enrollmentPrivateKey), card.devicePublicKey)
            val lowPublicKey = pairings.credential(Tier.LOW).identity.publicKey
            session(device, security) { it.enroll(lowPublicKey) }
            pairings.record(Tier.LOW, card.devicePublicKey, card.deviceName ?: device.localName, CredentialOrigin.ENROLLED)
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.w(TAG, "Enrollment failed against '${device.localName}'.", e)
            toasts.error(describe(e))
        } finally {
            _isBusy.value = false
            gate.unlock()
        }
    }

    /** `Unpair` on the pinned session, then the local pin is cleared even if the device call failed. */
    suspend fun unpair(device: Advertisement?, tier: Tier) {
        if (device == null) return
        if (!pairings.credential(tier).isPaired) {
            toasts.error("Not paired.")
            return
        }
        if (!gate.tryLock()) return
        _isBusy.value = true
        try {
            session(device, pairings.security(tier)) { it.unpair() }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            Log.w(TAG, "Device-side unpair (${tier.logName}) failed; clearing the local pairing anyway.", e)
        } finally {
            _isBusy.value = false
            gate.unlock()
        }
        pairings.clear(tier)
        _pairResult.update { it + (tier to "") }
    }

    companion object {
        const val TAG = "Zaffre"
    }
}
