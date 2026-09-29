// Formato relativo del "Último latido" de un aparato ("hace 3 min"). Lógica pura (probada aparte de la pantalla).
import { parseApiDate } from '../../kernel/api/dates'

// Minuto y hora abreviados ("hace 3 min", "hace 2 h", como el mock); día en adelante con la palabra completa
// ("hace 6 días": Intl en 'short' los deja en "6 d", muy poco claro para una fecha).
const SHORT_UNITS: readonly [Intl.RelativeTimeFormatUnit, number][] = [
  ['hour', 60 * 60],
  ['minute', 60],
  ['second', 1],
]
const LONG_UNITS: readonly [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 60 * 60 * 24 * 365],
  ['month', 60 * 60 * 24 * 30],
  ['week', 60 * 60 * 24 * 7],
  ['day', 60 * 60 * 24],
]

/** "hace 3 min" / "hace 6 días" en el idioma dado; '' si no hay fecha o no es válida. `now` para pruebas. */
export function formatRelative(iso: string | null | undefined, lang: string, now: Date = new Date()): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  const seconds = Math.round((date.getTime() - now.getTime()) / 1000)
  for (const [unit, secondsInUnit] of LONG_UNITS) {
    if (Math.abs(seconds) >= secondsInUnit) {
      return new Intl.RelativeTimeFormat(lang, { numeric: 'auto', style: 'long' }).format(Math.round(seconds / secondsInUnit), unit)
    }
  }
  for (const [unit, secondsInUnit] of SHORT_UNITS) {
    if (Math.abs(seconds) >= secondsInUnit || unit === 'second') {
      return new Intl.RelativeTimeFormat(lang, { numeric: 'auto', style: 'short' }).format(Math.round(seconds / secondsInUnit), unit)
    }
  }
  return ''
}
