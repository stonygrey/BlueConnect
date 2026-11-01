// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import Foundation
import Security

/// Where identities and pins are stored: the Keychain on iOS, and
/// `UserDefaults` on Mac Catalyst, where a Keychain access group needs a
/// provisioning profile the local build does not have.
protocol CredentialStore: Sendable {
    func data(forKey key: String) -> Data?
    /// Stores `data`, or removes the entry when `data` is nil.
    func set(_ data: Data?, forKey key: String)
}

enum CredentialStores {
    static func platformDefault() -> any CredentialStore {
        #if targetEnvironment(macCatalyst)
            UserDefaultsCredentialStore()
        #else
            KeychainCredentialStore()
        #endif
    }
}

final class UserDefaultsCredentialStore: CredentialStore {
    func data(forKey key: String) -> Data? {
        UserDefaults.standard.data(forKey: key)
    }

    func set(_ data: Data?, forKey key: String) {
        if let data {
            UserDefaults.standard.set(data, forKey: key)
        } else {
            UserDefaults.standard.removeObject(forKey: key)
        }
    }
}

/// Generic-password Keychain items, this device only, available after the
/// first unlock.
final class KeychainCredentialStore: CredentialStore {
    private let service = "com.stonygrey.zaffre.swift"

    func data(forKey key: String) -> Data? {
        var query = baseQuery(key)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        guard status == errSecSuccess else { return nil }
        return result as? Data
    }

    func set(_ data: Data?, forKey key: String) {
        let query = baseQuery(key)
        SecItemDelete(query as CFDictionary)
        guard let data else { return }
        var item = query
        item[kSecValueData as String] = data
        item[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        SecItemAdd(item as CFDictionary, nil)
    }

    private func baseQuery(_ key: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
        ]
    }
}
