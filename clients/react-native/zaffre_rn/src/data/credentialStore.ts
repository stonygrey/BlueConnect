// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

import { base64Decode, base64Encode } from '@stonygrey/blueconnect';
import * as SecureStore from 'expo-secure-store';

/** Where identities and pins are stored. */
export interface CredentialStore {
  get(key: string): Promise<Uint8Array | null>;
  /** Stores `value`, or removes the entry when `value` is null. */
  set(key: string, value: Uint8Array | null): Promise<void>;
}

/**
 * expo-secure-store: the Keychain on iOS and a Keystore-encrypted
 * SharedPreferences file on Android. Values are base64 because the store
 * holds strings.
 */
export class SecureCredentialStore implements CredentialStore {
  async get(key: string): Promise<Uint8Array | null> {
    const value = await SecureStore.getItemAsync(key);
    return value === null ? null : base64Decode(value);
  }

  async set(key: string, value: Uint8Array | null): Promise<void> {
    if (value === null) {
      await SecureStore.deleteItemAsync(key);
    } else {
      await SecureStore.setItemAsync(key, base64Encode(value));
    }
  }
}
