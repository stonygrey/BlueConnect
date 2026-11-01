// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

package com.stonygrey.zaffre.data

import android.content.Context
import android.util.Base64
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey

/** Where identities and pins are stored. */
interface CredentialStore {
    fun get(key: String): ByteArray?

    /** Stores [value], or removes the entry when [value] is null. */
    fun set(key: String, value: ByteArray?)
}

/**
 * `EncryptedSharedPreferences`: values are AES-GCM under a key in the Android
 * Keystore. The library is in maintenance mode but is the platform-provided
 * option without writing Keystore code by hand.
 */
class EncryptedCredentialStore(context: Context) : CredentialStore {
    private val prefs = EncryptedSharedPreferences.create(
        context,
        "zaffre.credentials",
        MasterKey.Builder(context).setKeyScheme(MasterKey.KeyScheme.AES256_GCM).build(),
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
    )

    override fun get(key: String): ByteArray? = prefs.getString(key, null)?.let { Base64.decode(it, Base64.NO_WRAP) }

    override fun set(key: String, value: ByteArray?) {
        prefs.edit().apply {
            if (value == null) remove(key) else putString(key, Base64.encodeToString(value, Base64.NO_WRAP))
        }.apply()
    }
}
