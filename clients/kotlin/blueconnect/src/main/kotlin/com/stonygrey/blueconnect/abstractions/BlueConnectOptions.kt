// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.blueconnect.abstractions

import kotlin.time.Duration
import kotlin.time.Duration.Companion.seconds

/** Values shared with the firmware. */
object BlueConnectConstants {
    val SCANNER_EXPIRATION: Duration = 60.seconds
    const val VERDIGRIS_SERVICE_UUID = "2B200001-465F-4625-94D8-37EC5C99BFB3"
    const val VERDIGRIS_RX_CHARACTERISTIC_UUID = "2B200002-465F-4625-94D8-37EC5C99BFB3"
    const val VERDIGRIS_TX_CHARACTERISTIC_UUID = "2B200003-465F-4625-94D8-37EC5C99BFB3"
    const val MANUFACTURER_COMPANY_ID = 0xFFFF
    const val LOW_PRIVILEGE_PAIRED_FLAG = 0x01
    const val HIGH_PRIVILEGE_PAIRED_FLAG = 0x02
    const val SECURED_STATUS_MASK = LOW_PRIVILEGE_PAIRED_FLAG or HIGH_PRIVILEGE_PAIRED_FLAG
}

/**
 * Connection and command policy. The app uses the defaults as they stand;
 * there is no configuration file.
 */
data class BlueConnectOptions(
    val connectTimeout: Duration = 15.seconds,
    val commandTimeout: Duration = 10.seconds,
    /** The whole retry window. Zero retries until cancelled. */
    val connectRetryTimeout: Duration = 30.seconds,
    val retryOnlyWhileAdvertising: Boolean = true,
    val minCycleDelay: Duration = 1.seconds,
    val maxCycleDelay: Duration = 5.seconds,
    val isEchoVerificationEnabled: Boolean = true,
    val serviceUuid: String = BlueConnectConstants.VERDIGRIS_SERVICE_UUID,
    val rxCharacteristicUuid: String = BlueConnectConstants.VERDIGRIS_RX_CHARACTERISTIC_UUID,
    val txCharacteristicUuid: String = BlueConnectConstants.VERDIGRIS_TX_CHARACTERISTIC_UUID,
    val manufacturerCompanyId: Int = BlueConnectConstants.MANUFACTURER_COMPANY_ID,
)
