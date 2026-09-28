// Lote 8A-app — pares clave/valor de configuración del aparato (idioma, almacén elegido, URL del API, tema). No guarda
// secretos: el secreto del aparato y el PIN cifrado local viven en expo-secure-store (kernel/auth/secureSession.ts).
import { getDb } from './database'

export function getKv(key: string): string | null {
  const row = getDb().getFirstSync<{ value: string | null }>('SELECT value FROM kv WHERE key = ?', [key])
  return row?.value ?? null
}

export function setKv(key: string, value: string | null): void {
  if (value === null) {
    getDb().runSync('DELETE FROM kv WHERE key = ?', [key])
    return
  }
  getDb().runSync('INSERT INTO kv (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value', [
    key,
    value,
  ])
}

export const KvKeys = {
  lang: 'lang',
  apiBaseUrl: 'apiBaseUrl',
  selectedWarehousePublicId: 'selectedWarehousePublicId',
  theme: 'theme',
} as const
