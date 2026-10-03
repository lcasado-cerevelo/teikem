// Lote 8A-app — pares clave/valor de configuración del aparato (idioma, almacén elegido, URL del API, tema). No guarda
// secretos: el secreto del aparato y el PIN cifrado local viven en expo-secure-store (kernel/auth/secureSession.ts).
import { getDb, getSharedDb } from './database'

/** Claves del teléfono (no de una compañía): viven en la base común aunque haya varias compañías registradas. */
const PHONE_KEYS = new Set<string>(['lang', 'apiBaseUrl', 'theme'])
function dbFor(key: string) {
  return PHONE_KEYS.has(key) ? getSharedDb() : getDb()
}

export function getKv(key: string): string | null {
  const row = dbFor(key).getFirstSync<{ value: string | null }>('SELECT value FROM kv WHERE key = ?', [key])
  return row?.value ?? null
}

export function setKv(key: string, value: string | null): void {
  if (value === null) {
    dbFor(key).runSync('DELETE FROM kv WHERE key = ?', [key])
    return
  }
  dbFor(key).runSync('INSERT INTO kv (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value', [
    key,
    value,
  ])
}

export const KvKeys = {
  lang: 'lang',
  apiBaseUrl: 'apiBaseUrl',
  selectedWarehousePublicId: 'selectedWarehousePublicId',
  theme: 'theme',
  /** Región y formatos de la compañía (JSON de kernel/format/settings.ts): en la base de la compañía, para trabajar sin señal. */
  tenantFormat: 'tenantFormat',
  /** Lote A4: última forma de contar elegida en Conteo ('BIN' por posición | 'PRODUCT' por producto); sin valor = por posición. */
  countEntryMode: 'countEntryMode',
  /** Lote A7: avisos de lotes de conteo parciales (líneas que el supervisor ya había corregido), JSON de features/count/countSkipped.ts. */
  countSkippedNotices: 'countSkippedNotices',
} as const
