// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace Zaffre.Services;

/// <summary>
/// Pairing identity/pin persistence. iOS and Android use Keychain /
/// EncryptedSharedPreferences. Mac Catalyst's iOS Keychain stack requires
/// <c>keychain-access-groups</c> backed by a Mac Catalyst provisioning
/// profile; without one launchd refuses the app (POSIX 163). Preferences
/// is the local-dev stand-in until that profile exists.
///
/// On iOS every item is stored "after first unlock, this device only".
/// MAUI's default accessibility lets Keychain items travel in an encrypted
/// backup or a phone-to-phone transfer, which would hand a paired identity
/// (private key and pin) to a new phone without the pairing ceremony. Items
/// written before this rule are re-saved with it the first time they are read.
///
/// The rule covers the Noise identities and pins, which the credential
/// service stores through this class.
/// </summary>
internal static class PairingStore
{
#if IOS && !MACCATALYST
    /// <summary>Preferences flag per key: the Keychain item already carries the this-device-only rule.</summary>
    private const string LocalOnlyFlagPrefix = "zaffre.keychain.local.";

    static PairingStore()
        => SecureStorage.DefaultAccessible = Security.SecAccessible.AfterFirstUnlockThisDeviceOnly;
#endif

    public static async Task<string?> GetAsync(string key)
    {
#if MACCATALYST
        return await Task.FromResult(Preferences.Default.Get<string?>(key, null)).ConfigureAwait(false);
#else
        var value = await SecureStorage.Default.GetAsync(key).ConfigureAwait(false);
#if IOS
        if (value is not null && !Preferences.Default.Get(LocalOnlyFlagPrefix + key, false))
        {
            // SetAsync removes the existing record and adds a new one with
            // the current DefaultAccessible, which converts a migratable item.
            await SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false);
            Preferences.Default.Set(LocalOnlyFlagPrefix + key, true);
#if DEBUG
            Console.WriteLine($"ZAFFRE-KEYCHAIN: re-saved '{key}' as this-device-only");
#endif
        }
#endif
        return value;
#endif
    }

    public static async Task SetAsync(string key, string value)
    {
#if MACCATALYST
        Preferences.Default.Set(key, value);
        await Task.CompletedTask.ConfigureAwait(false);
#else
        await SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false);
#if IOS
        Preferences.Default.Set(LocalOnlyFlagPrefix + key, true);
#endif
#endif
    }

    public static void Remove(string key)
    {
#if MACCATALYST
        Preferences.Default.Remove(key);
#else
        SecureStorage.Default.Remove(key);
#if IOS
        Preferences.Default.Remove(LocalOnlyFlagPrefix + key);
#endif
#endif
    }
}
