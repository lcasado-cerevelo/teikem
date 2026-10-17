// Huella / rostro del aparato (expo-local-authentication). «Abrir con huella» NO reemplaza el primer ingreso ni el de una computadora nueva: solo abre la sesión que ya está guardada en el aparato, y el
// sistema deja el PIN o patrón del teléfono como respaldo. En la web no existe (devuelve 'unavailable').
import * as LocalAuthentication from 'expo-local-authentication'
import { Platform } from 'react-native'

import { t } from '../i18n/i18n'
import { biometricSupport, type BiometricSupport } from './lockRules'

export async function getBiometricSupport(): Promise<BiometricSupport> {
  if (Platform.OS === 'web') return 'unavailable'
  try {
    return biometricSupport(await LocalAuthentication.hasHardwareAsync(), await LocalAuthentication.isEnrolledAsync())
  } catch {
    return 'unavailable'
  }
}

/** Pide la huella (o el PIN del teléfono). true = el dueño del aparato se identificó. */
export async function promptBiometric(): Promise<boolean> {
  try {
    const result = await LocalAuthentication.authenticateAsync({
      promptMessage: t('lock.prompt'),
      cancelLabel: t('lock.cancel'),
      fallbackLabel: t('lock.fallback'),
      disableDeviceFallback: false,
    })
    return result.success
  } catch {
    return false
  }
}
