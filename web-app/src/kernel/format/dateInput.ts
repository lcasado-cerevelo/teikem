// Captura de fechas y horas con el formato de la COMPAÑÍA (Región y formatos: orden MDY/DMY/YMD, separador, hora de 12/24; dictado de
// Luis, 2026-10-21): solo se teclean números y el campo los va acomodando mientras se escribe, igual que el teléfono. El valor que sale
// hacia las pantallas sigue siendo ISO («YYYY-MM-DD», y «YYYY-MM-DDTHH:mm» con hora). Lógica pura, sin React.
import type { DateOrder, FormatSettings } from './settings'
import { getFormatSettings } from './store'

/** Dígitos de una fecha completa (día, mes y año de 4 dígitos). */
export const DATE_DIGITS = 8
/** Dígitos de una hora completa (HH y mm). */
export const TIME_DIGITS = 4

/** Solo los dígitos de un texto. */
export function onlyDigits(v: string | null | undefined): string {
  return String(v ?? '').replace(/\D/g, '')
}

/** Largo de cada grupo (día, mes, año) en el orden de la compañía: MDY → [2,2,4]; YMD → [4,2,2]. */
function groups(order: DateOrder): number[] {
  return order === 'YMD' ? [4, 2, 2] : [2, 2, 4]
}

/** Qué es cada grupo, en el orden de la compañía. */
function parts(order: DateOrder): ('d' | 'm' | 'y')[] {
  return order === 'MDY' ? ['m', 'd', 'y'] : order === 'DMY' ? ['d', 'm', 'y'] : ['y', 'm', 'd']
}

const pad = (n: number, len = 2) => String(n).padStart(len, '0')

/** ¿Existe ese día en el calendario (con años de 4 dígitos)? */
export function isRealDate(y: number, m: number, d: number): boolean {
  if (!Number.isInteger(y) || !Number.isInteger(m) || !Number.isInteger(d) || y < 1000 || y > 9999 || m < 1 || m > 12 || d < 1) return false
  const dt = new Date(Date.UTC(y, m - 1, d))
  return dt.getUTCFullYear() === y && dt.getUTCMonth() === m - 1 && dt.getUTCDate() === d
}

/** «YYYY-MM-DD» de verdad (un día que existe). */
export function isIsoDate(v: string | null | undefined): boolean {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(v ?? ''))
  return !!m && isRealDate(Number(m[1]), Number(m[2]), Number(m[3]))
}

/** Pone los dígitos en la máscara de la compañía hasta donde alcancen: «10» → «10»; «102» → «10/2»; «10212026» → «10/21/2026». */
export function maskDate(v: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  const digits = onlyDigits(v).slice(0, DATE_DIGITS)
  const out: string[] = []
  let i = 0
  for (const len of groups(s.dateOrder)) {
    if (i >= digits.length) break
    out.push(digits.slice(i, i + len))
    i += len
  }
  return out.join(s.dateSeparator)
}

/** Ejemplo para el placeholder: «MM/DD/YYYY», «DD/MM/YYYY», «YYYY/MM/DD» (con el separador de la compañía). */
export function datePlaceholder(s: FormatSettings = getFormatSettings()): string {
  const name = { d: 'DD', m: 'MM', y: 'YYYY' } as const
  return parts(s.dateOrder).map((p) => name[p]).join(s.dateSeparator)
}

/** Dígitos completos de una fecha (8) en el orden de la compañía → ISO; null si falta algo o el día no existe. */
export function isoFromDigits(digits: string, s: FormatSettings = getFormatSettings()): string | null {
  if (digits.length !== DATE_DIGITS) return null
  let i = 0
  const got: Record<string, number> = {}
  const order = parts(s.dateOrder)
  groups(s.dateOrder).forEach((len, k) => {
    got[order[k]] = Number(digits.slice(i, i + len))
    i += len
  })
  return isRealDate(got.y, got.m, got.d) ? `${pad(got.y, 4)}-${pad(got.m)}-${pad(got.d)}` : null
}

/** ISO → el texto con la máscara de la compañía; '' si no es una fecha. */
export function dateToText(iso: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(String(iso ?? ''))
  if (!m) return ''
  const by = { y: m[1], m: m[2], d: m[3] }
  return parts(s.dateOrder).map((p) => by[p]).join(s.dateSeparator)
}

/**
 * Fecha PEGADA (o puesta por un autocompletado): se reconoce si es el orden de la compañía con cualquier separador («10/21/2026», «10-21-2026»,
 * «10.21.2026», «10212026», «1/5/2026») o si es ISO («2026-10-21», que no deja duda). Cualquier otra cosa (año de 2 dígitos, el orden
 * contrario cuando el día no cabe en el mes, letras…) se rechaza con null. Devuelve ISO.
 */
export function parsePastedDate(text: string | null | undefined, s: FormatSettings = getFormatSettings()): string | null {
  const t = String(text ?? '').trim()
  if (!t) return null
  if (/^\d{8}$/.test(t)) return isoFromDigits(t, s)
  if (/[^\d\s/.\-]/.test(t)) return null
  const tokens = t.split(/[^\d]+/).filter(Boolean)
  if (tokens.length !== 3) return null
  const named: Record<string, number> = {}
  if (tokens[0].length === 4) {
    // ISO (año primero): año-mes-día
    if (tokens[1].length > 2 || tokens[2].length > 2) return null
    named.y = Number(tokens[0]); named.m = Number(tokens[1]); named.d = Number(tokens[2])
  } else {
    const order = parts(s.dateOrder)
    const yi = order.indexOf('y')
    if (tokens[yi].length !== 4 || tokens.some((x, i) => i !== yi && x.length > 2)) return null
    order.forEach((p, i) => (named[p] = Number(tokens[i])))
  }
  return isRealDate(named.y, named.m, named.d) ? `${pad(named.y, 4)}-${pad(named.m)}-${pad(named.d)}` : null
}

// ---------------------------------------------------------------- hora

/**
 * Hora mientras se escribe: hasta 4 dígitos → «09», «09:3», «09:30». Un primer dígito que ya no puede ser el de las decenas de la hora (más de 2 con hora de 24,
 * más de 1 con hora de 12) se completa solo con el cero: «9» → «09».
 */
export function maskTime(v: string | null | undefined, format: 12 | 24 = 24): string {
  let d = onlyDigits(v).slice(0, TIME_DIGITS)
  if (d.length > 0 && Number(d[0]) > (format === 12 ? 1 : 2)) d = ('0' + d).slice(0, TIME_DIGITS)
  return d.length <= 2 ? d : `${d.slice(0, 2)}:${d.slice(2)}`
}

export type DayPeriod = 'AM' | 'PM'

/** «HH:mm» de 24 horas → el texto para el campo y su AM/PM (solo con hora de 12). */
export function timeToText(hhmm: string | null | undefined, format: 12 | 24): { text: string; period: DayPeriod | null } {
  const m = /^(\d{2}):(\d{2})/.exec(String(hhmm ?? ''))
  if (!m) return { text: '', period: null }
  const h = Number(m[1])
  if (format === 24) return { text: `${m[1]}:${m[2]}`, period: null }
  return { text: `${pad(h % 12 === 0 ? 12 : h % 12)}:${m[2]}`, period: h >= 12 ? 'PM' : 'AM' }
}

/** Texto de la hora (con AM/PM si es de 12) → «HH:mm» de 24 horas; null si está incompleta o fuera de rango. */
export function parseTimeText(text: string | null | undefined, period: DayPeriod | null, format: 12 | 24): string | null {
  const d = onlyDigits(text)
  if (d.length !== TIME_DIGITS) return null
  const h = Number(d.slice(0, 2))
  const min = Number(d.slice(2))
  if (min > 59) return null
  if (format === 24) return h <= 23 ? `${pad(h)}:${pad(min)}` : null
  if (h < 1 || h > 12) return null
  const p = period ?? 'AM'
  return `${pad(p === 'PM' ? (h % 12) + 12 : h % 12)}:${pad(min)}`
}

/** Hora pegada: «09:30», «9:30 pm», «0930», «21:05». null si no se entiende. */
export function parsePastedTime(text: string | null | undefined, format: 12 | 24): { hhmm: string; period: DayPeriod | null } | null {
  const t = String(text ?? '').trim()
  const m = /^(\d{1,2})[:.]?(\d{2})\s*([ap])?\.?\s*(m\.?)?$/i.exec(t)
  if (!m) return null
  let h = Number(m[1])
  const min = Number(m[2])
  if (min > 59) return null
  const mer = m[3]?.toUpperCase()
  if (mer) {
    if (h < 1 || h > 12) return null
    h = mer === 'P' ? (h % 12) + 12 : h % 12
  } else if (h > 23) return null
  const hhmm = `${pad(h)}:${pad(min)}`
  return { hhmm, period: format === 12 ? (h >= 12 ? 'PM' : 'AM') : null }
}

// ---------------------------------------------------------------- calendario

/** Una cuadrícula de 6 semanas (de 7 días) del mes: cada celda es un ISO, o null si es de otro mes. `weekStart` 0 = domingo, 1 = lunes. */
export function monthGrid(year: number, month: number, weekStart: 0 | 1): (string | null)[][] {
  const first = new Date(Date.UTC(year, month, 1)).getUTCDay()
  const lead = (first - weekStart + 7) % 7
  const days = new Date(Date.UTC(year, month + 1, 0)).getUTCDate()
  const cells: (string | null)[] = []
  for (let i = 0; i < lead; i++) cells.push(null)
  for (let d = 1; d <= days; d++) cells.push(`${pad(year, 4)}-${pad(month + 1)}-${pad(d)}`)
  while (cells.length < 42) cells.push(null)
  return Array.from({ length: 6 }, (_, r) => cells.slice(r * 7, r * 7 + 7))
}

/** El mes (0–11) y el año a que se llega moviendo `delta` meses. */
export function shiftMonth(year: number, month: number, delta: number): { year: number; month: number } {
  const t = year * 12 + month + delta
  return { year: Math.floor(t / 12), month: ((t % 12) + 12) % 12 }
}

/** ISO de un día movido `delta` días (para las flechas del calendario). */
export function shiftDay(iso: string, delta: number): string {
  const [y, m, d] = iso.split('-').map(Number)
  const dt = new Date(Date.UTC(y, m - 1, d + delta))
  return `${pad(dt.getUTCFullYear(), 4)}-${pad(dt.getUTCMonth() + 1)}-${pad(dt.getUTCDate())}`
}

/** ¿Cae el día dentro de min/max (ISO, ambos inclusivos y opcionales)? */
export function inRange(iso: string, min?: string | null, max?: string | null): boolean {
  return (!min || iso >= min) && (!max || iso <= max)
}
