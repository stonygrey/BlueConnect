// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.zaffre

import android.Manifest
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.performClick
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import com.stonygrey.zaffre.ui.MainActivity
import org.junit.Assert.assertEquals
import org.junit.BeforeClass
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

/**
 * Drives the app against the Verdigris DK over the real radio: pair both
 * tiers, run the High and Low commands, soak three loop cycles, and unpair.
 * It needs the board advertising, BLUETOOTH_SCAN and BLUETOOTH_CONNECT
 * granted to the app (`adb shell pm grant`), and a watcher on logcat that
 * runs `lib/device confirm` over the board's USB shell when the app logs
 * `ZAFFRE-PAIR: armed <tier>` (see `documents/clients.md`).
 */
@RunWith(AndroidJUnit4::class)
class HardwareFlowTest {
    companion object {
        /**
         * Gradle reinstalls the app for every connected test run, which resets its
         * runtime permissions, and the activity launches before any `@Before`
         * method runs. Granting here, before the rule, keeps the permission
         * dialog from covering the scan page.
         */
        @JvmStatic
        @BeforeClass
        fun grantBluetoothPermissions() {
            val instrumentation = InstrumentationRegistry.getInstrumentation()
            val target = instrumentation.targetContext.packageName
            for (permission in listOf(Manifest.permission.BLUETOOTH_SCAN, Manifest.permission.BLUETOOTH_CONNECT)) {
                instrumentation.uiAutomation.grantRuntimePermission(target, permission)
            }
        }
    }

    @get:Rule
    val rule = createAndroidComposeRule<MainActivity>()

    @Test
    fun pairCommandsLoopAndUnpair() {
        // Scan: the board is the only Verdigris device in range.
        waitForTag("device-row", 60_000)
        rule.onNodeWithTag("device-row").performClick()

        // Pair High, then Low; the confirm comes over USB from the watcher. A
        // tier the app still holds from earlier use is unpaired first.
        rule.onNodeWithTag("tab-noise").performClick()
        for (tier in listOf("high", "low")) {
            if (nodes(hasTestTag("unpair-$tier")).isNotEmpty()) {
                waitEnabled("unpair-$tier")
                rule.onNodeWithTag("unpair-$tier").performClick()
                waitForText("status-$tier", "Not paired", 45_000)
            }
            waitEnabled("pair-$tier")
            rule.onNodeWithTag("pair-$tier").performClick()
            waitForText("status-$tier", "Sessions are encrypted", 45_000)
        }

        // High: serial read and write, LED read and toggle, echo.
        rule.onNodeWithTag("tab-high").performClick()
        clickAndExpectToast("serial-read-high", "Advertising as")
        clickAndExpectToast("serial-write-high", "Now advertising as")
        waitEnabled("led-high")
        rule.onNodeWithTag("led-high").performClick()
        waitForText("led-label-high", "Tap to switch", 45_000)
        val before = textOf("led-label-high")
        waitEnabled("led-high")
        rule.onNodeWithTag("led-high").performClick()
        rule.waitUntil(45_000) { textOf("led-label-high").let { it != before && it.contains("Tap to switch") } }
        clickAndExpectToast("echo-write-high", "Hello, Verdigris!")

        // Low: reads work, echo is refused by the device.
        rule.onNodeWithTag("tab-low").performClick()
        clickAndExpectToast("serial-read-low", "Advertising as")
        clickAndExpectToast("echo-write-low", "STATUS_PERMISSION_DENIED")

        // Loop: three cycles with no failures.
        rule.onNodeWithTag("tab-loop").performClick()
        waitEnabled("loop-start")
        rule.onNodeWithTag("loop-start").performClick()
        rule.waitUntil(180_000) { (textOf("loop-tests").toIntOrNull() ?: 0) >= 3 }
        rule.onNodeWithTag("loop-stop").performClick()
        rule.waitUntil(60_000) { runCatching { rule.onNodeWithTag("loop-start").assertIsEnabled() }.isSuccess }
        assertEquals("0", textOf("loop-connection-failures"))
        assertEquals("0", textOf("loop-command-failures"))

        // Unpair both tiers.
        rule.onNodeWithTag("tab-noise").performClick()
        for (tier in listOf("high", "low")) {
            waitEnabled("unpair-$tier")
            rule.onNodeWithTag("unpair-$tier").performClick()
            waitForText("status-$tier", "Not paired", 45_000)
        }
    }

    /** `fetchSemanticsNodes` throws while the first frame is not yet composed; before then nothing is found. */
    private fun nodes(matcher: androidx.compose.ui.test.SemanticsMatcher) =
        runCatching { rule.onAllNodes(matcher).fetchSemanticsNodes() }.getOrDefault(emptyList())

    private fun waitForTag(tag: String, timeoutMillis: Long) {
        rule.waitUntil(timeoutMillis) { nodes(hasTestTag(tag)).isNotEmpty() }
    }

    private fun waitEnabled(tag: String, timeoutMillis: Long = 60_000) {
        rule.waitUntil(timeoutMillis) { runCatching { rule.onNodeWithTag(tag).assertIsEnabled() }.isSuccess }
    }

    private fun waitForText(tag: String, text: String, timeoutMillis: Long) {
        rule.waitUntil(timeoutMillis) { nodes(hasTestTag(tag) and hasText(text, substring = true)).isNotEmpty() }
    }

    private fun clickAndExpectToast(tag: String, text: String) {
        waitEnabled(tag)
        rule.onNodeWithTag(tag).performClick()
        rule.waitUntil(45_000) { nodes(hasText(text, substring = true)).isNotEmpty() }
    }

    private fun textOf(tag: String): String {
        val node = nodes(hasTestTag(tag)).firstOrNull() ?: return ""
        val texts = node.config.getOrNull(SemanticsProperties.Text) ?: return ""
        return texts.joinToString("") { it.text }
    }
}

