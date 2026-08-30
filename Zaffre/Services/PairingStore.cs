// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

namespace Zaffre.Services;

/// <summary>
/// Pairing identity/pin persistence. iOS and Android use Keychain /
/// EncryptedSharedPreferences. Mac Catalyst's iOS Keychain stack requires
/// <c>keychain-access-groups</c> backed by a Mac Catalyst provisioning
/// profile; without one launchd refuses the app (POSIX 163). Preferences
/// is the local-dev stand-in until that profile exists.
/// </summary>
internal static class PairingStore
{
    public static Task<string?> GetAsync(string key)
    {
#if MACCATALYST
        return Task.FromResult(Preferences.Default.Get<string?>(key, null));
#else
        return SecureStorage.Default.GetAsync(key);
#endif
    }

    public static Task SetAsync(string key, string value)
    {
#if MACCATALYST
        Preferences.Default.Set(key, value);
        return Task.CompletedTask;
#else
        return SecureStorage.Default.SetAsync(key, value);
#endif
    }

    public static void Remove(string key)
    {
#if MACCATALYST
        Preferences.Default.Remove(key);
#else
        SecureStorage.Default.Remove(key);
#endif
    }
}
