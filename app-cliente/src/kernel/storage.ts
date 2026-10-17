// Almacenamiento clave-valor de la app. En el teléfono usa el almacén seguro del sistema (Keychain / Keystore, cifrado por el aparato); en la web usa el almacenamiento del navegador (localStorage), y si el navegador no deja
// (modo privado, datos bloqueados) vive en memoria mientras la pestaña esté abierta. Lo que se guarda aquí es lo estrictamente necesario (sesión, marca de la computadora, idioma y preferencias): ver «Cookies y
// almacenamiento del navegador» en la política de privacidad.
import * as SecureStore from 'expo-secure-store'
import { Platform } from 'react-native'

const memory = new Map<string, string>()

export async function storeGet(key: string): Promise<string | null> {
  if (Platform.OS === 'web') {
    try {
      return globalThis.localStorage?.getItem(key) ?? memory.get(key) ?? null
    } catch {
      return memory.get(key) ?? null
    }
  }
  try {
    return (await SecureStore.getItemAsync(key)) ?? memory.get(key) ?? null
  } catch {
    return memory.get(key) ?? null
  }
}

export async function storeSet(key: string, value: string): Promise<void> {
  memory.set(key, value)
  if (Platform.OS === 'web') {
    try {
      globalThis.localStorage?.setItem(key, value)
    } catch {
      // queda en memoria
    }
    return
  }
  try {
    await SecureStore.setItemAsync(key, value)
  } catch {
    // queda en memoria
  }
}

export async function storeDelete(key: string): Promise<void> {
  memory.delete(key)
  if (Platform.OS === 'web') {
    try {
      globalThis.localStorage?.removeItem(key)
    } catch {
      // nada
    }
    return
  }
  try {
    await SecureStore.deleteItemAsync(key)
  } catch {
    // nada
  }
}

export const StoreKeys = {
  session: 'teikem.cliente.session',
  browser: 'teikem.browser',
  lang: 'teikem.cliente.lang',
  noTrust: 'teikem.cliente.noTrust',
  biometric: 'teikem.cliente.biometric',
} as const
