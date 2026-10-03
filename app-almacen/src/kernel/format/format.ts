// Formato de números, cantidades, dinero, fechas, horas y "hoy" con los ajustes de la compañía (lote "Región y formatos",
// parte app). Funciones PURAS: el último parámetro son los ajustes (por defecto, los vigentes de store.ts), así se prueban
// sin React ni base y las pantallas las usan a través de `useFormat()`. Mismo resultado que web-app/src/kernel/format/
// format.ts, pero sin depender de los datos de idioma de `Intl` (el motor de React Native no siempre los trae): los
// separadores, el orden de la fecha y el texto a. m./p. m. se arman aquí; `Intl` solo convierte de zona (zone.ts).
import type { FormatSettings } from './settings'
import { getFormatSettings } from './store'
import { zonedDay, zonedParts } from './zone'

export type Lang = 'es' | 'en'

// ------------------------------------------------------------------ números

export interface NumberOptions {
  /** Decimales mínimos (relleno con ceros). Por defecto 0. */
  minDecimals?: number
  /** Decimales máximos (redondeo). Por defecto 3. */
  maxDecimals?: number
}

/**
 * Número con los separadores de la compañía: miles SIEMPRE agrupados y hasta 3 decimales solo si los tiene (61023 →
 * "61,023"; 1.5 → "1.5"; con punto de miles y coma decimal: "61.023,125").
 */
export function formatNumber(n: number, opts: NumberOptions = {}, s: FormatSettings = getFormatSettings()): string {
  if (!Number.isFinite(n)) return String(n)
  const max = Math.max(0, opts.maxDecimals ?? 3)
  const min = Math.min(max, Math.max(0, opts.minDecimals ?? 0))
  const fixed = Math.abs(n).toFixed(max)
  let [intPart, decPart = ''] = fixed.split('.')
  // quita los ceros sobrantes de la derecha hasta el mínimo pedido
  while (decPart.length > min && decPart.endsWith('0')) decPart = decPart.slice(0, -1)
  const grouped = intPart.replace(/\B(?=(\d{3})+(?!\d))/g, s.thousandsSeparator)
  const zero = !/[1-9]/.test(intPart + decPart)
  const sign = n < 0 && !zero ? '-' : ''
  return `${sign}${grouped}${decPart ? `${s.decimalSeparator}${decPart}` : ''}`
}

/** Cantidad (unidades, saldos): hasta 3 decimales solo si los tiene. */
export function formatQuantity(n: number, s: FormatSettings = getFormatSettings()): string {
  return formatNumber(n, { maxDecimals: 3 }, s)
}

// ------------------------------------------------------------------ dinero

export interface MoneyOptions {
  /** Costo unitario fino: hasta 4 decimales. */
  unitPrice?: boolean
  /** +$5.00 / -$5.00 (el cero sin signo). */
  signed?: boolean
  /** Decimales fijos (por defecto los de la compañía). */
  decimals?: number
}

/** Dinero con el símbolo, la posición, los decimales y los separadores de la compañía: "$1,234.50", "-$12.00", "1.234,50 €". */
export function formatMoney(n: number, opts: MoneyOptions = {}, s: FormatSettings = getFormatSettings()): string {
  const decimals = opts.decimals ?? s.currencyDecimals
  const body = formatNumber(Math.abs(n), { minDecimals: decimals, maxDecimals: opts.unitPrice ? Math.max(4, decimals) : decimals }, s)
  const zero = !/[1-9]/.test(body)
  const sign = zero ? '' : n < 0 ? '-' : opts.signed && n > 0 ? '+' : ''
  // símbolo después con espacio duro (no se separa del monto al partir la línea)
  return s.currencySymbolPosition === 'A' ? `${sign}${body} ${s.currencySymbol}` : `${sign}${s.currencySymbol}${body}`
}

// ------------------------------------------------------------------ fechas y horas

/** Valor de fecha que aceptan los formateadores: 'YYYY-MM-DD' (día de calendario), instante del API (UTC) o `Date`. */
export type DateValue = string | Date | null | undefined

const YMD = /^(\d{4})-(\d{2})-(\d{2})$/
const pad2 = (n: number) => String(n).padStart(2, '0')

/** Instante de un valor (null si es un día de calendario sin hora, vacío o inválido). Un texto del API sin zona es UTC. */
function instantOf(v: DateValue): Date | null {
  if (v instanceof Date) return Number.isNaN(v.getTime()) ? null : v
  if (typeof v !== 'string') return null
  const t = v.trim()
  if (!t || YMD.test(t)) return null
  const hasZone = /(Z|[+-]\d{2}:?\d{2})$/i.test(t)
  const d = new Date(hasZone ? t : `${t}Z`)
  return Number.isNaN(d.getTime()) ? null : d
}

/** [año, mes, día] de un valor: un día de calendario tal cual (sin corrimiento); un instante, su día en la zona de la compañía. */
function dayParts(v: DateValue, s: FormatSettings): [string, string, string] | null {
  if (typeof v === 'string') {
    const m = YMD.exec(v.trim())
    if (m) return [m[1], m[2], m[3]]
  }
  const d = instantOf(v)
  if (!d) return null
  const [y, m, dd] = zonedDay(d, s.timeZoneId).split('-')
  return [y, m, dd]
}

/** Fecha corta en el orden y con el separador de la compañía: 10/02/2026 · 02/10/2026 · 2026-10-02 ('' sin fecha). */
export function formatDate(v: DateValue, s: FormatSettings = getFormatSettings()): string {
  const p = dayParts(v, s)
  if (!p) return ''
  const [y, m, d] = p
  const sp = s.dateSeparator
  if (s.dateOrder === 'DMY') return `${d}${sp}${m}${sp}${y}`
  if (s.dateOrder === 'YMD') return `${y}${sp}${m}${sp}${d}`
  return `${m}${sp}${d}${sp}${y}`
}

/** Día y mes sin año, en el mismo orden (MDY e YMD → mes/día; DMY → día/mes). */
export function formatDayMonth(v: DateValue, s: FormatSettings = getFormatSettings()): string {
  const p = dayParts(v, s)
  if (!p) return ''
  const [, m, d] = p
  return s.dateOrder === 'DMY' ? `${d}${s.dateSeparator}${m}` : `${m}${s.dateSeparator}${d}`
}

/** Texto de la hora (h:mm, con segundos si se piden) con el formato de la compañía; `lang` pone "a. m."/"AM". */
function clockText(hour: number, minute: number, second: number | null, lang: Lang, s: FormatSettings): string {
  const tail = `${pad2(minute)}${second === null ? '' : `:${pad2(second)}`}`
  if (s.timeFormat === 24) return `${pad2(hour)}:${tail}`
  const h12 = hour % 12 === 0 ? 12 : hour % 12
  const pm = hour >= 12
  const suffix = lang === 'en' ? (pm ? 'PM' : 'AM') : pm ? 'p. m.' : 'a. m.'
  return `${h12}:${tail} ${suffix}`
}

/** Hora de un instante en la zona de la compañía, con su formato de 12 o 24 horas ('' sin instante o con un día sin hora). */
export function formatTime(v: DateValue, lang: Lang, opts: { seconds?: boolean } = {}, s: FormatSettings = getFormatSettings()): string {
  const d = instantOf(v)
  if (!d) return ''
  const p = zonedParts(d, s.timeZoneId)
  return clockText(p.hour, p.minute, opts.seconds ? p.second : null, lang, s)
}

/** Fecha corta + hora de la compañía ("10/02/2026 9:30 p. m."); un día de calendario sin hora → solo la fecha. */
export function formatDateTime(v: DateValue, lang: Lang, s: FormatSettings = getFormatSettings()): string {
  const date = formatDate(v, s)
  if (!date) return ''
  const time = formatTime(v, lang, {}, s)
  return time ? `${date} ${time}` : date
}

/** "Hoy" 'YYYY-MM-DD' en la zona de la compañía. */
export function todayIso(now: Date = new Date(), s: FormatSettings = getFormatSettings()): string {
  return zonedDay(now, s.timeZoneId)
}

/** Fecha y hora "cortas" para una marca de tiempo: solo la hora si es de hoy (en la zona de la compañía); si no, fecha y hora. */
export function formatWhen(v: DateValue, lang: Lang, now: Date = new Date(), s: FormatSettings = getFormatSettings()): string {
  const d = instantOf(v)
  if (!d) return formatDate(v, s)
  return zonedDay(d, s.timeZoneId) === todayIso(now, s) ? formatTime(d, lang, {}, s) : formatDateTime(d, lang, s)
}

// ------------------------------------------------------------------ fechas escritas a mano (vencimiento de un lote)

/** Ejemplo del orden de la compañía para el campo de fecha: "MM/DD/AAAA" (es) · "DD.MM.YYYY" (en). */
export function datePlaceholder(lang: Lang, s: FormatSettings = getFormatSettings()): string {
  const [y, m, d] = lang === 'en' ? ['YYYY', 'MM', 'DD'] : ['AAAA', 'MM', 'DD']
  const sp = s.dateSeparator
  if (s.dateOrder === 'DMY') return `${d}${sp}${m}${sp}${y}`
  if (s.dateOrder === 'YMD') return `${y}${sp}${m}${sp}${d}`
  return `${m}${sp}${d}${sp}${y}`
}

/**
 * Fecha escrita a mano → 'YYYY-MM-DD' (lo que pide el API), o null si no es una fecha real. Acepta el orden de la compañía
 * con cualquier separador (/ - .) y, siempre, el formato ISO "2027-01-31" (el que se pedía antes de este lote).
 */
export function parseDateInput(text: string, s: FormatSettings = getFormatSettings()): string | null {
  const t = text.trim()
  if (!t) return null
  const parts = t.split(/[/.\-\s]+/).filter(Boolean)
  if (parts.length !== 3 || parts.some((p) => !/^\d+$/.test(p))) return null
  let y: string
  let m: string
  let d: string
  if (parts[0].length === 4) [y, m, d] = parts
  else if (s.dateOrder === 'DMY') [d, m, y] = parts
  else if (s.dateOrder === 'YMD') [y, m, d] = parts
  else [m, d, y] = parts
  if (y.length !== 4) return null
  const yi = Number(y)
  const mi = Number(m)
  const di = Number(d)
  const check = new Date(Date.UTC(yi, mi - 1, di))
  if (check.getUTCFullYear() !== yi || check.getUTCMonth() !== mi - 1 || check.getUTCDate() !== di) return null
  return `${y}-${pad2(mi)}-${pad2(di)}`
}
