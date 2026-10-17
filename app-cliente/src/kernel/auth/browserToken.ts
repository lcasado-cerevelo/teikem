// Marca secreta de ESTE aparato / navegador para las «computadoras de confianza»: 64 caracteres hexadecimales aleatorios, creados la primera vez y guardados (almacén seguro en el teléfono, localStorage en la web).
// El servidor solo guarda su resumen. Si no se puede guardar, vive en memoria mientras la app esté abierta (cada arranque pedirá confirmar).
import * as Crypto from 'expo-crypto'

import { StoreKeys, storeGet, storeSet } from '../storage'

let cached: string | null = null

export function newBrowserToken(): string {
  return Array.from(Crypto.getRandomBytes(32), (b) => b.toString(16).padStart(2, '0')).join('')
}

export async function getBrowserToken(): Promise<string> {
  if (cached) return cached
  const saved = await storeGet(StoreKeys.browser)
  if (saved && saved.length >= 32) {
    cached = saved
    return saved
  }
  const fresh = newBrowserToken()
  cached = fresh
  await storeSet(StoreKeys.browser, fresh)
  return fresh
}

/** Solo para pruebas. */
export function resetBrowserTokenCache(): void {
  cached = null
}
