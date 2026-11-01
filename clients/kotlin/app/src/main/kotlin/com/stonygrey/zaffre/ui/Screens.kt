// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.zaffre.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import android.content.Context
import android.util.Log
import androidx.compose.material3.HorizontalDivider
import androidx.compose.ui.platform.LocalContext
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Lock
import androidx.compose.material.icons.filled.Refresh
import androidx.compose.material.icons.outlined.Lock
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.unit.dp
import com.stonygrey.blueconnect.abstractions.Advertisement
import com.stonygrey.zaffre.data.CredentialOrigin
import com.stonygrey.zaffre.data.Tier
import com.stonygrey.zaffre.data.TierCredential

/** The scan list: name, address, padlock, and RSSI, strongest first. Tapping a row selects it. */
@Composable
fun ScanScreen(scan: ScanViewModel, modifier: Modifier = Modifier) {
    val devices by scan.devices.collectAsState()
    val selected by scan.selected.collectAsState()
    val failure by scan.scanFailure.collectAsState()
    val context = LocalContext.current
    val build = remember(context) { buildLabel(context) }
    Box(modifier.fillMaxSize()) {
        Column(Modifier.fillMaxSize().padding(16.dp)) {
            Text("Select a device", style = MaterialTheme.typography.headlineSmall)
            if (devices.isEmpty()) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    if (failure != null) {
                        Text(failure!!)
                    } else {
                        Column(horizontalAlignment = Alignment.CenterHorizontally) {
                            CircularProgressIndicator()
                            Text("Scanning for Verdigris devices…", Modifier.padding(top = 8.dp))
                        }
                    }
                }
            } else {
                LazyColumn {
                    items(devices, key = { it.id }) { device ->
                        DeviceRow(device, device.id == selected?.id) { scan.select(device) }
                    }
                }
            }
        }
        Text(
            build,
            Modifier.align(Alignment.BottomStart).padding(start = 12.dp, bottom = 6.dp).testTag("build-label"),
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
    }
}

@Composable
private fun DeviceRow(device: Advertisement, isSelected: Boolean, onClick: () -> Unit) {
    Card(Modifier.fillMaxWidth().padding(vertical = 4.dp).testTag("device-row").clickable(onClick = onClick)) {
        Row(Modifier.padding(12.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(device.localName, style = MaterialTheme.typography.titleMedium)
                Text(device.id, style = MaterialTheme.typography.bodySmall)
            }
            Icon(
                if (device.isSecured) Icons.Filled.Lock else Icons.Outlined.Lock,
                contentDescription = if (device.isSecured) "Paired. Only the pinned client is accepted" else "Not paired. Any client can open an encrypted session",
                tint = if (device.isSecured) Color(0xFF2E7D32) else MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (!device.isConnectable) Text("not connectable", style = MaterialTheme.typography.labelSmall, color = Color(0xFFEF6C00))
            Text("${device.rssi.valueDbm} dBm", Modifier.padding(start = 8.dp), style = MaterialTheme.typography.labelMedium)
            if (isSelected) Icon(Icons.Filled.Check, contentDescription = "selected", Modifier.padding(start = 4.dp))
        }
    }
}

/** The trust tab: one pairing card per tier, High first. */
@Composable
fun NoiseScreen(noise: NoiseViewModel, modifier: Modifier = Modifier) {
    Column(modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text("Noise XX link security; each tier is enabled independently.")
        Text("Pair: Mutual key pinning, confirmed with the board's button.", style = MaterialTheme.typography.bodySmall)
        PairingCard(noise, Tier.HIGH)
        PairingCard(noise, Tier.LOW)
    }
}

@Composable
private fun PairingCard(noise: NoiseViewModel, tier: Tier) {
    val selected by noise.selected.collectAsState()
    val credentials by noise.credentials.collectAsState()
    val pairResult by noise.pairResult.collectAsState()
    val canRun by noise.canRun.collectAsState()
    val credential = credentials[tier] ?: return
    Card(Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            val state = if (credential.isPaired) (credential.origin ?: CredentialOrigin.PAIRED).label else "Unsecured"
            Text("${tier.title}: $state", Modifier.testTag("title-${tier.key}"), style = MaterialTheme.typography.titleMedium)
            Text(status(credential, tier, selected), Modifier.testTag("status-${tier.key}"), style = MaterialTheme.typography.bodySmall)
            if (credential.isPaired) {
                OutlinedButton(onClick = { noise.unpair(tier) }, enabled = canRun, modifier = Modifier.testTag("unpair-${tier.key}")) { Text("Unpair") }
            } else {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Button(onClick = { noise.pair(tier) }, enabled = canRun, modifier = Modifier.testTag("pair-${tier.key}")) { Text("Pair") }
                    Text("Confirm with ${tier.button}", Modifier.padding(start = 12.dp), style = MaterialTheme.typography.bodySmall)
                }
                HorizontalDivider()
                val context = LocalContext.current
                Row(verticalAlignment = Alignment.CenterVertically) {
                    OutlinedButton(
                        onClick = { scanQr(context) { noise.provision(tier, it) } },
                        enabled = canRun,
                        modifier = Modifier.testTag("scan-${tier.key}"),
                    ) { Text("Scan QR") }
                    Text("Provision from the QR created over USB, or an enrollment card.", Modifier.padding(start = 12.dp), style = MaterialTheme.typography.bodySmall)
                }
            }
            val result = pairResult[tier] ?: ""
            if (result.isNotEmpty()) Text(result, Modifier.testTag("result-${tier.key}"), style = MaterialTheme.typography.bodySmall)
        }
    }
}

/**
 * Google's code scanner: Play services supply the camera UI and the decoding,
 * so the app needs neither a camera permission nor a scanning library.
 */
private fun scanQr(context: Context, onScanned: (String) -> Unit) {
    val options = GmsBarcodeScannerOptions.Builder().setBarcodeFormats(Barcode.FORMAT_QR_CODE).build()
    GmsBarcodeScanning.getClient(context, options).startScan()
        .addOnSuccessListener { barcode -> barcode.rawValue?.let(onScanned) }
        .addOnFailureListener { e -> Log.w("Zaffre", "QR scan failed.", e) }
}

private fun status(credential: TierCredential, tier: Tier, selected: Advertisement?): String {
    if (!credential.isPaired) return "Not paired (${tier.key}). Pair to run this page's commands"
    if (selected?.isSecured == true) return "Sessions are encrypted."
    val with = credential.deviceName?.let { " with '$it'" } ?: ""
    return "Paired (${tier.key})$with, but the device reports no pairings. It may have been reset; unpair to start over."
}

/** The Low and High tabs: the serial number, LED 1, and Echo cards. */
@Composable
fun CommandsScreen(noise: NoiseViewModel, tier: Tier, modifier: Modifier = Modifier) {
    val selected by noise.selected.collectAsState()
    val canRun by noise.canRun.collectAsState()
    val serialInput by noise.serialInput.collectAsState()
    val echoInput by noise.echoInput.collectAsState()
    val ledOn by noise.ledOn.collectAsState()
    Column(modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text(tier.title, style = MaterialTheme.typography.titleLarge)
        Card(Modifier.fillMaxWidth()) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Serial Number", style = MaterialTheme.typography.titleMedium)
                OutlinedTextField(
                    value = serialInput[tier] ?: "",
                    onValueChange = { noise.setSerialInput(tier, it) },
                    label = { Text("Serial number") },
                    modifier = Modifier.fillMaxWidth().testTag("serial-input-${tier.key}"),
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    OutlinedButton(onClick = { noise.readSerial(tier) }, enabled = canRun, modifier = Modifier.testTag("serial-read-${tier.key}")) { Text("Read") }
                    OutlinedButton(onClick = { noise.writeSerial(tier) }, enabled = canRun, modifier = Modifier.testTag("serial-write-${tier.key}")) { Text("Write") }
                }
            }
        }
        Card(Modifier.fillMaxWidth()) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("LED 1", style = MaterialTheme.typography.titleMedium)
                val on = ledOn[tier]
                val label = when (on) {
                    true -> "LED 1 is on. Tap to switch it off"
                    false -> "LED 1 is off. Tap to switch it on"
                    null -> "LED 1 state not read yet. Tap to read it"
                }
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(
                        Modifier
                            .size(44.dp)
                            .background(
                                when (on) {
                                    true -> Color(0xFFFBC02D)
                                    false -> Color.Gray
                                    null -> MaterialTheme.colorScheme.outline
                                },
                                CircleShape,
                            )
                            .testTag("led-${tier.key}")
                            .clickable(enabled = canRun) { noise.tapLed(tier) },
                        contentAlignment = Alignment.Center,
                    ) {
                        if (on == null) Icon(Icons.Filled.Refresh, contentDescription = null, tint = Color.White)
                    }
                    Spacer(Modifier.width(12.dp))
                    Text(label, Modifier.weight(1f).testTag("led-label-${tier.key}"), style = MaterialTheme.typography.bodySmall)
                    IconButton(onClick = { noise.readLed(tier) }, enabled = canRun, modifier = Modifier.testTag("led-read-${tier.key}")) {
                        Icon(Icons.Filled.Refresh, contentDescription = "Read LED state")
                    }
                }
            }
        }
        Card(Modifier.fillMaxWidth()) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text("Echo", style = MaterialTheme.typography.titleMedium, color = if (tier == Tier.LOW) Color(0xFFC62828) else MaterialTheme.colorScheme.onSurface)
                OutlinedTextField(
                    value = echoInput[tier] ?: "",
                    onValueChange = { noise.setEchoInput(tier, it) },
                    label = { Text("Payload") },
                    modifier = Modifier.fillMaxWidth().testTag("echo-input-${tier.key}"),
                )
                OutlinedButton(onClick = { noise.echo(tier) }, enabled = canRun, modifier = Modifier.testTag("echo-write-${tier.key}")) { Text("Write") }
            }
        }
    }
}

/** The Loop tab: start and stop the soak loop and watch its counters. */
@Composable
fun LoopScreen(loop: LoopViewModel, modifier: Modifier = Modifier) {
    val state by loop.state.collectAsState()
    val selected by loop.selected.collectAsState()
    Column(modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Text(if (state.isRunning) "Loop (running)" else "Loop", style = MaterialTheme.typography.titleLarge)
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Button(onClick = { loop.start() }, enabled = !state.isRunning && selected != null, modifier = Modifier.testTag("loop-start")) { Text("Start") }
            OutlinedButton(onClick = { loop.stop() }, enabled = state.isRunning, modifier = Modifier.testTag("loop-stop")) { Text("Stop") }
        }
        Text("The loop echoes each cycle, which requires the high-privilege pairing.", style = MaterialTheme.typography.bodySmall)
        val started = state.startedMillis != null
        fun ms(value: Double?) = value?.let { "%.0f ms".format(it) } ?: "—"
        StatRow("Device", state.deviceName.ifEmpty { "—" }, "loop-device")
        StatRow("Tests", if (started) state.tests.toString() else "—", "loop-tests")
        StatRow("Connection failures", if (started) state.connectionFailures.toString() else "—", "loop-connection-failures")
        StatRow("Command failures", if (started) state.commandFailures.toString() else "—", "loop-command-failures")
        StatRow("Min connect", ms(state.minConnectMs), "loop-min")
        StatRow("Average connect", ms(state.avgConnectMs), "loop-avg")
        StatRow("Max connect", ms(state.maxConnectMs), "loop-max")
        StatRow("Elapsed", if (started) "%02d:%02d:%02d".format(state.elapsedSeconds / 3600, state.elapsedSeconds % 3600 / 60, state.elapsedSeconds % 60) else "—", "loop-elapsed")
    }
}

@Composable
private fun StatRow(label: String, value: String, tag: String) {
    Row(Modifier.fillMaxWidth()) {
        Text(label, Modifier.weight(1f))
        Text(value, Modifier.testTag(tag), fontFamily = FontFamily.Monospace)
    }
}
