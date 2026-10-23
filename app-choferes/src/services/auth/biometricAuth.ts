import { Platform } from 'react-native';
import * as SecureStore from 'expo-secure-store';
import * as LocalAuthentication from 'expo-local-authentication';

// Huella o rostro del teléfono (expo-local-authentication) — «Abrir con huella».
//
// REGLA (igual que el portal del cliente): la huella NO reemplaza el primer ingreso ni el MFA. Solo ABRE la sesión que ya está guardada en el teléfono
// (la app se bloquea al arrancar y al volver de segundo plano pasados unos minutos). La contraseña NO se guarda en el teléfono: antes se guardaba y se
// reenviaba con la huella, lo que se saltaba el MFA de la compañía. Cuando la sesión vence (los días o minutos que fija la compañía) se pide todo otra vez.
//
// OJO: la huella la valida el teléfono, no el servidor: cualquier huella registrada en el teléfono lo abre. Por eso el MFA se sigue pidiendo en cada ingreso nuevo.

const KEY = 'teikem.choferes.biometric';

/** Minutos en segundo plano antes de volver a pedir la huella. */
export const LOCK_AFTER_MINUTES = 2;
/** Fallos seguidos de la huella antes de pedir la contraseña de nuevo (se cierra la sesión del teléfono). */
export const MAX_BIOMETRIC_FAILURES = 5;

/** ¿Hay que bloquear al volver a primer plano? `backgroundedAt` = cuándo salió de primer plano (null = nunca salió). */
export function shouldLock(enabled: boolean, backgroundedAt: number | null, now: number, afterMinutes = LOCK_AFTER_MINUTES): boolean {
  if (!enabled || backgroundedAt === null) return false;
  return now - backgroundedAt >= afterMinutes * 60_000;
}

// Hay lector Y al menos una huella o rostro registrado.
export async function isBiometricAvailable(): Promise<boolean> {
  if (Platform.OS === 'web') return false;
  try {
    if (!(await LocalAuthentication.hasHardwareAsync())) return false;
    return await LocalAuthentication.isEnrolledAsync();
  } catch {
    return false;
  }
}

export async function getBiometricEnabled(): Promise<boolean> {
  if (Platform.OS === 'web') return false;
  try {
    return (await SecureStore.getItemAsync(KEY)) === '1';
  } catch {
    return false;
  }
}

export async function setBiometricEnabled(value: boolean): Promise<void> {
  if (Platform.OS === 'web') return;
  if (value) await SecureStore.setItemAsync(KEY, '1');
  else await SecureStore.deleteItemAsync(KEY);
}

// Pide la huella (o el PIN del teléfono). true = el dueño del teléfono se identificó.
export async function authenticate(promptMessage: string, cancelLabel: string): Promise<boolean> {
  try {
    const result = await LocalAuthentication.authenticateAsync({
      promptMessage,
      cancelLabel,
      disableDeviceFallback: false, // el PIN del teléfono queda como respaldo
    });
    return result.success;
  } catch {
    return false;
  }
}
