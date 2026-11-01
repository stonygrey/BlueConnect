// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.abstractions

/**
 * The failures the library reports. The messages are written for the app's
 * toasts. A call the current state does not allow throws Kotlin's own
 * [IllegalStateException], and coroutine cancellation is never wrapped.
 */
open class BlueConnectException(message: String, cause: Throwable? = null) : Exception(message, cause)

/** The GATT layer or the segment layer failed. */
class BleTransportException(message: String, cause: Throwable? = null) : BlueConnectException(message, cause)

/** The handshake or an AEAD check failed. */
class LinkSecurityException(message: String, cause: Throwable? = null) : BlueConnectException(message, cause)

/** A connect attempt, retry window, or command ran out of time. */
class BleTimeoutException(message: String, cause: Throwable? = null) : BlueConnectException(message, cause)
