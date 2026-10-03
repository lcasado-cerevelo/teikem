// Estado de los formatos de la compañía, sin React (como el idioma en `i18n.ts`): las funciones puras de formato leen de aquí
// su valor por defecto y los componentes se suscriben con `useFormatSettings()`. Lo alimenta `FormatProvider` con
// `GET /api/v1/tenant/settings`; al guardar Ajustes (o invalidar esa consulta) cambia aquí y toda la app se vuelve a pintar
// sin recargar (`useT`/`useLang` también se suscriben).
import { DEFAULT_FORMAT, sameFormat, type FormatSettings } from './settings'

let current: FormatSettings = DEFAULT_FORMAT
const listeners = new Set<() => void>()

/** Formatos vigentes de la compañía activa (Puerto Rico mientras no hay ajustes). */
export function getFormatSettings(): FormatSettings {
  return current
}

/** Cambia los formatos vigentes; si no cambió ningún valor no avisa a nadie (misma referencia). */
export function setFormatSettings(next: FormatSettings): void {
  if (sameFormat(current, next)) return
  current = next
  listeners.forEach((l) => l())
}

/** Vuelve a Puerto Rico (sin sesión, cambio de compañía, pruebas). */
export function resetFormatSettings(): void {
  setFormatSettings(DEFAULT_FORMAT)
}

export function subscribeFormat(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

/** Zona horaria vigente de la compañía (IANA). */
export function tenantTimeZone(): string {
  return current.timeZoneId
}
