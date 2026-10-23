import { Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';
import * as LocalAuthentication from 'expo-local-authentication';

// Biometric re-login: after a password login the driver can opt in to sign in
// with fingerprint/face next time. Credentials live ONLY in the device's
// secure enclave (Keychain / Android Keystore via expo-secure-store) — same
// approach banking apps use — and are read back only after a successful
// biometric prompt (authenticate() below).
//
// NOTE: device biometrics are device-scoped: ANY finger enrolled on the phone
// unlocks them. The Login screen shows WHOSE session the button restores, and
// a password login by a different user replaces/clears the stored credentials.

const KEY = 'colavora.biometric-login';

export interface BiometricCredentials {
  userName: string;
  password: string;
}

// Hardware present AND at least one fingerprint/face enrolled.
export async function isBiometricAvailable(): Promise<boolean> {
  if (Platform.OS === 'web') return false;
  try {
    const hasHardware = await LocalAuthentication.hasHardwareAsync();
    if (!hasHardware) return false;
    return await LocalAuthentication.isEnrolledAsync();
  } catch {
    return false;
  }
}

export async function getStoredCredentials(): Promise<BiometricCredentials | null> {
  if (Platform.OS === 'web') return null;
  try {
    const raw = await SecureStore.getItemAsync(KEY);
    return raw ? (JSON.parse(raw) as BiometricCredentials) : null;
  } catch {
    return null;
  }
}

export async function saveCredentials(creds: BiometricCredentials): Promise<void> {
  if (Platform.OS === 'web') return;
  await SecureStore.setItemAsync(KEY, JSON.stringify(creds));
}

export async function clearCredentials(): Promise<void> {
  if (Platform.OS === 'web') return;
  await SecureStore.deleteItemAsync(KEY);
}

// Native biometric prompt. true only on a positive match.
export async function authenticate(promptMessage: string, cancelLabel: string): Promise<boolean> {
  try {
    const result = await LocalAuthentication.authenticateAsync({
      promptMessage,
      cancelLabel,
      disableDeviceFallback: false, // allow PIN fallback, like banking apps
    });
    return result.success;
  } catch {
    return false;
  }
}
