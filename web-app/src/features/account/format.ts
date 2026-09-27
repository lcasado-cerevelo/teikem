// Utilidades puras de "Mi cuenta": fechas y descripción corta del dispositivo de una sesión.
import { parseApiDate } from '../../kernel/api/dates'

/** Fecha y hora local en el idioma de la interfaz (el API manda UTC, a veces sin 'Z'); '' si no hay fecha o no es válida. */
export function formatDateTime(iso: string | null | undefined, lang: string): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  return new Intl.DateTimeFormat(lang, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

const BROWSERS: readonly [RegExp, string][] = [
  [/Edg(e|A|iOS)?\//, 'Edge'],
  [/OPR\/|Opera/, 'Opera'],
  [/SamsungBrowser\//, 'Samsung Internet'],
  [/Firefox\/|FxiOS\//, 'Firefox'],
  [/Chrome\/|CriOS\//, 'Chrome'],
  [/Safari\//, 'Safari'],
]

const SYSTEMS: readonly [RegExp, string][] = [
  [/Windows/, 'Windows'],
  [/Android/, 'Android'],
  [/iPhone|iPad|iPod/, 'iOS'],
  [/Mac OS X|Macintosh/, 'macOS'],
  [/CrOS/, 'ChromeOS'],
  [/Linux/, 'Linux'],
]

/**
 * "Navegador · Sistema" a partir del User-Agent guardado en la sesión (`deviceInfo`). Si no se reconoce nada,
 * devuelve el texto tal cual (recortado); null si no hay dato.
 */
export function describeDevice(deviceInfo: string | null | undefined): string | null {
  const ua = deviceInfo?.trim()
  if (!ua) return null
  const browser = BROWSERS.find(([re]) => re.test(ua))?.[1]
  const system = SYSTEMS.find(([re]) => re.test(ua))?.[1]
  if (browser || system) return [browser, system].filter(Boolean).join(' · ')
  return ua.length > 60 ? `${ua.slice(0, 57)}…` : ua
}
