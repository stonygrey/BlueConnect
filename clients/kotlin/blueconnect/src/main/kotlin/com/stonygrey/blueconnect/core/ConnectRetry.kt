// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.core

import com.stonygrey.blueconnect.abstractions.BleTimeoutException
import com.stonygrey.blueconnect.abstractions.BleTransportException
import com.stonygrey.blueconnect.abstractions.BlueConnectOptions
import kotlin.coroutines.cancellation.CancellationException
import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds
import kotlin.time.DurationUnit
import kotlin.time.TimeSource
import kotlinx.coroutines.delay

/**
 * The connect policy: attempts bounded by `connectTimeout`, repeated after
 * a 1 s delay until `connectRetryTimeout` runs out, and abandoned early when
 * the device has stopped advertising and `retryOnlyWhileAdvertising` is set.
 * Backends call it from `connect()` with their single-attempt connect as
 * [attempt].
 */
object ConnectRetry {
    val RETRY_DELAY: Duration = 1.seconds

    /** [attempt] receives the time it may take; [isPresent] reports whether the scanner has seen the device within the expiry window. */
    suspend fun run(
        options: BlueConnectOptions,
        name: String,
        isPresent: () -> Boolean,
        attempt: suspend (Duration) -> Unit,
    ) {
        val unbounded = options.connectRetryTimeout <= Duration.ZERO
        val window = options.connectRetryTimeout
        val attemptTimeout = options.connectTimeout
        val started = TimeSource.Monotonic.markNow()
        var attemptNumber = 1
        while (true) {
            val remaining = if (unbounded) attemptTimeout else window - started.elapsedNow()
            try {
                attempt(minOf(attemptTimeout, remaining))
                return
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                val elapsed = started.elapsedNow()
                if (!unbounded && elapsed + RETRY_DELAY >= window) {
                    throw BleTimeoutException(
                        "Connecting to '$name' did not succeed within ${window.inWholeSeconds}s " +
                            "($attemptNumber attempt(s); last failure: ${e.message})",
                        e,
                    )
                }
                IoLog.warn(
                    "Connect attempt $attemptNumber to '$name' failed after " +
                        "${"%.1f".format(elapsed.toDouble(DurationUnit.SECONDS))}s (${e.message}); retrying.",
                )
                delay(RETRY_DELAY)
                if (options.retryOnlyWhileAdvertising && !isPresent()) {
                    throw BleTransportException(
                        "'$name' is no longer advertising; giving up after $attemptNumber attempt(s) (last failure: ${e.message})",
                        e,
                    )
                }
            }
            attemptNumber++
        }
    }
}
