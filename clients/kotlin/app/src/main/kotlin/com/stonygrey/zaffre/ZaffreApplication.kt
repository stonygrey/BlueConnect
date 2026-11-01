// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.zaffre

import android.app.Application
import android.content.Context
import android.util.Log
import com.stonygrey.blueconnect.abstractions.BleScanner
import com.stonygrey.blueconnect.abstractions.BlueConnectOptions
import com.stonygrey.blueconnect.core.IoLog
import com.stonygrey.zaffre.ble.AndroidScanner
import com.stonygrey.zaffre.data.DeviceSelection
import com.stonygrey.zaffre.data.EncryptedCredentialStore
import com.stonygrey.zaffre.data.PairingRepository
import com.stonygrey.zaffre.domain.CommandRunner
import com.stonygrey.zaffre.domain.SoakLoop
import com.stonygrey.zaffre.domain.Toasts
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob

/**
 * The Kotlin client of the Verdigris device: Noise XX/IK link security over
 * BLE through the `:blueconnect` module and Torann. The object graph is
 * built by hand in [AppContainer], the manual dependency injection Google's
 * architecture guide describes for an app this size.
 */
class ZaffreApplication : Application() {
    lateinit var container: AppContainer
        private set

    override fun onCreate() {
        super.onCreate()
        IoLog.sink = object : IoLog.Sink {
            override fun info(tag: String, line: String) {
                Log.i(tag, line)
            }

            override fun warn(tag: String, line: String) {
                Log.w(tag, line)
            }

            override fun debug(tag: String, line: String) {
                Log.d(tag, line)
            }
        }
        container = AppContainer(this)
    }
}

class AppContainer(context: Context) {
    val options = BlueConnectOptions()
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    val scanner: BleScanner = AndroidScanner(context, options, scope)
    val pairings = PairingRepository(EncryptedCredentialStore(context))
    val selection = DeviceSelection()
    val toasts = Toasts()
    val commands = CommandRunner(scanner, options, pairings, toasts)
    val loop = SoakLoop(scanner, options, pairings, scope)
}

val Context.container: AppContainer
    get() = (applicationContext as ZaffreApplication).container
