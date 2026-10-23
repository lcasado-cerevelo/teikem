import { Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';
import type { AuthUser } from './auth0';

// Cross-platform session storage.
//  - Native: expo-secure-store (Keychain / Keystore).
//  - Web: localStorage (SecureStore has no web implementation).
//
// We persist ONLY the minimum (access + refresh token + expiry). The user
// profile is re-fetched from /userinfo on restore. Keeping the blob small
// matters because SecureStore on Android caps values around 2KB.

const KEY = 'colavora.session';

export interface StoredSession {
  accessToken: string;
  refreshToken?: string;
  expiresAt: number; // epoch ms
  // Cached so the driver's name/initials still show while offline, before a
  // fresh /userinfo call (which needs a network) can update it.
  user?: AuthUser;
}

async function setItem(value: string): Promise<void> {
  if (Platform.OS === 'web') {
    localStorage.setItem(KEY, value);
    return;
  }
  await SecureStore.setItemAsync(KEY, value);
}

async function getItem(): Promise<string | null> {
  if (Platform.OS === 'web') {
    return localStorage.getItem(KEY);
  }
  return SecureStore.getItemAsync(KEY);
}

async function removeItem(): Promise<void> {
  if (Platform.OS === 'web') {
    localStorage.removeItem(KEY);
    return;
  }
  await SecureStore.deleteItemAsync(KEY);
}

export const sessionStorage = {
  async save(session: StoredSession): Promise<void> {
    await setItem(JSON.stringify(session));
  },
  async load(): Promise<StoredSession | null> {
    const raw = await getItem();
    if (!raw) return null;
    try {
      return JSON.parse(raw) as StoredSession;
    } catch {
      return null;
    }
  },
  async clear(): Promise<void> {
    await removeItem();
  },
};
