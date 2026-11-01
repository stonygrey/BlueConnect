// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.zaffre.ui

import android.content.Context

/**
 * The version shown in the corner of the scan list, so that a phone running
 * the app shows which build it is running: `versionName` from app/build.gradle.kts.
 */
fun buildLabel(context: Context): String =
    "v${context.packageManager.getPackageInfo(context.packageName, 0).versionName}"
