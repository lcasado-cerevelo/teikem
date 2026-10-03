// Formatos vigentes de la compañía activa, sin React (como el idioma en i18n.ts): las funciones puras de format.ts y
// phone.ts leen de aquí su valor por defecto y las pantallas se suscriben con `useFormat()`.
// - Se GUARDAN en la base local de la compañía (kv `tenantFormat`, la base de ese registro), así la app pinta con los
//   formatos de su compañía aunque arranque sin señal; los trae `refreshTenantFormat()` (tenantFormatApi.ts) al entrar con
//   el PIN y en cada sincronización, de modo que un cambio de región en la web llega en la siguiente pasada.
// - Mientras nunca han llegado (aparato recién registrado, sin señal) o sin compañía elegida: Puerto Rico.
// - Con varias compañías en el teléfono, cada una tiene los suyos: el valor en memoria se recuerda por base local y se
//   vuelve a leer al cambiar de registro.
import { getSessionState, subscribeSession } from '../auth/session'
import { getKv, KvKeys, setKv } from '../db/kv'
import { DEFAULT_FORMAT, pickFormat, sameFormat, toFormatSettings, type FormatSettings } from './settings'

let cache: { company: string; settings: FormatSettings } | null = null
const listeners = new Set<() => void>()

/** Registro activo (su base local); '' sin registro. */
function activeCompany(): string {
  const device = getSessionState().device
  return device ? (device.dbName ?? device.devicePublicId) : ''
}

function readStored(): FormatSettings {
  try {
    const raw = getKv(KvKeys.tenantFormat)
    return raw ? toFormatSettings(JSON.parse(raw) as Record<string, unknown>) : DEFAULT_FORMAT
  } catch {
    // base no disponible o JSON dañado: se pinta con Puerto Rico hasta la siguiente sincronización
    return DEFAULT_FORMAT
  }
}

/** Formatos vigentes de la compañía activa (Puerto Rico mientras no hay ajustes guardados). Misma referencia mientras no cambian. */
export function getFormatSettings(): FormatSettings {
  const company = activeCompany()
  if (cache && cache.company === company) return cache.settings
  const settings = company ? readStored() : DEFAULT_FORMAT
  cache = { company, settings }
  return settings
}

/** Guarda (en la base de la compañía activa) y aplica los formatos; si ningún valor cambió no avisa a nadie. */
export function setFormatSettings(next: FormatSettings): void {
  const company = activeCompany()
  if (!company) return
  const current = getFormatSettings()
  const value = pickFormat(next)
  if (sameFormat(current, value)) return
  setKv(KvKeys.tenantFormat, JSON.stringify(value))
  cache = { company, settings: value }
  listeners.forEach((l) => l())
}

/** Se suscribe a los cambios de formato y de compañía activa (cada compañía trae los suyos). */
export function subscribeFormat(listener: () => void): () => void {
  listeners.add(listener)
  const offSession = subscribeSession(listener)
  return () => {
    listeners.delete(listener)
    offSession()
  }
}

/** Solo para pruebas: olvida el valor en memoria (la base ya se limpia con __resetDbForTests). */
export function __resetFormatForTests(): void {
  cache = null
  listeners.clear()
}
