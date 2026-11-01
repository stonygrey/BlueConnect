// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.zaffre.ui

import android.Manifest
import android.content.pm.PackageManager
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarDuration
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.core.content.ContextCompat
import androidx.lifecycle.ViewModelProvider
import com.stonygrey.zaffre.container
import com.stonygrey.zaffre.data.Tier
import com.stonygrey.zaffre.domain.Toast
import kotlinx.coroutines.flow.Flow

/** The single activity: the permission request, then the scan list and the selected device's pages. */
class MainActivity : ComponentActivity() {
    private val requestPermissions = registerForActivityResult(ActivityResultContracts.RequestMultiplePermissions()) { startScanning() }

    private lateinit var scan: ScanViewModel
    private lateinit var noise: NoiseViewModel
    private lateinit var loop: LoopViewModel

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val factory = ZaffreViewModelFactory(container)
        scan = ViewModelProvider(this, factory)[ScanViewModel::class.java]
        noise = ViewModelProvider(this, factory)[NoiseViewModel::class.java]
        loop = ViewModelProvider(this, factory)[LoopViewModel::class.java]
        setContent {
            MaterialTheme(colorScheme = if (isSystemInDarkTheme()) darkColorScheme() else lightColorScheme()) {
                ZaffreApp(scan, noise, loop, container.toasts.messages)
            }
        }
        val needed = listOf(Manifest.permission.BLUETOOTH_SCAN, Manifest.permission.BLUETOOTH_CONNECT)
            .filter { ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED }
        if (needed.isEmpty()) startScanning() else requestPermissions.launch(needed.toTypedArray())
    }

    private fun startScanning() = scan.start()
}

/**
 * Navigation: the scan list is the only screen until a device is
 * selected; then the device's pages (Noise, Low, High, Loop) show under its
 * name, and the back arrow or the system back gesture returns to the list.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ZaffreApp(scan: ScanViewModel, noise: NoiseViewModel, loop: LoopViewModel, toasts: Flow<Toast>) {
    val device by scan.selected.collectAsState()
    var tab by remember { mutableIntStateOf(0) }
    val snackbar = remember { SnackbarHostState() }
    LaunchedEffect(toasts) {
        toasts.collect { toast ->
            snackbar.currentSnackbarData?.dismiss()
            snackbar.showSnackbar(toast.text, withDismissAction = true, duration = SnackbarDuration.Short)
        }
    }
    LaunchedEffect(device?.id) { tab = 0 }
    if (device != null) {
        BackHandler { scan.clearSelection() }
    }
    Scaffold(
        snackbarHost = { SnackbarHost(snackbar, modifier = Modifier.testTag("toast")) },
        topBar = {
            val selected = device ?: return@Scaffold
            TopAppBar(
                title = { Text(selected.localName, Modifier.testTag("device-heading")) },
                navigationIcon = {
                    IconButton(onClick = { scan.clearSelection() }, modifier = Modifier.testTag("back")) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back to the device list")
                    }
                },
            )
        },
        bottomBar = {
            if (device == null) return@Scaffold
            NavigationBar {
                val tabs = listOf(
                    Triple("Noise", Icons.Filled.Lock, "tab-noise"),
                    Triple("Low", Icons.Filled.KeyboardArrowDown, "tab-low"),
                    Triple("High", Icons.Filled.KeyboardArrowUp, "tab-high"),
                    Triple("Loop", Icons.Filled.Refresh, "tab-loop"),
                )
                tabs.forEachIndexed { index, (label, icon, tag) ->
                    NavigationBarItem(
                        selected = tab == index,
                        onClick = { tab = index },
                        icon = { Icon(icon, contentDescription = label) },
                        label = { Text(label) },
                        modifier = Modifier.testTag(tag),
                    )
                }
            }
        },
    ) { padding ->
        val content = Modifier.padding(padding)
        if (device == null) {
            ScanScreen(scan, content)
        } else {
            when (tab) {
                0 -> NoiseScreen(noise, content)
                1 -> CommandsScreen(noise, Tier.LOW, content)
                2 -> CommandsScreen(noise, Tier.HIGH, content)
                else -> LoopScreen(loop, content)
            }
        }
    }
}
