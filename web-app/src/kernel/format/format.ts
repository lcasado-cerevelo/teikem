// Formato de números, dinero, fechas y horas con los ajustes de la compañía (región y formatos). Funciones PURAS: el último
// parámetro son los ajustes (por defecto, los vigentes de `store.ts`), así se prueban sin React y los componentes las usan
// a través de `useFormat()`. Equivalen a money/fmtMoney/fmtDate/fmtDayMonth/fmtTime/fmtDateLong/todayISO de la maqueta.
import { parseApiDate } from '../api/dates'
import { tenantToday, zonedParts } from '../api/tenantZone'
import type { FormatSettings } from './settings'
import { getFormatSettings } from './store'

// ------------------------------------------------------------------ números

const numberFormats = new Map<string, Intl.NumberFormat>()

/** Formateador "en-US" (coma de miles, punto decimal) reutilizado por opciones. */
function enUs(opts: Intl.NumberFormatOptions): Intl.NumberFormat {
  const key = JSON.stringify(opts)
  let f = numberFormats.get(key)
  if (!f) {
    f = new Intl.NumberFormat('en-US', opts)
    numberFormats.set(key, f)
  }
  return f
}

/** Texto numérico de "en-US" (1,234.5) con los separadores de la compañía (1.234,5 · 1 234.5). */
export function applySeparators(text: string, s: FormatSettings = getFormatSettings()): string {
  if (s.thousandsSeparator === ',' && s.decimalSeparator === '.') return text
  return text.replace(/[,.]/g, (c) => (c === ',' ? s.thousandsSeparator : s.decimalSeparator))
}

/**
 * Número con los separadores de la compañía: miles SIEMPRE agrupados y hasta 3 decimales solo si los tiene (61023 →
 * "61,023"; 1.5 → "1.5"). `opts` son las de `Intl.NumberFormat` (decimales fijos, signo, compacto, porcentaje…).
 */
export function formatNumber(n: number, opts: Intl.NumberFormatOptions = {}, s: FormatSettings = getFormatSettings()): string {
  return applySeparators(enUs({ maximumFractionDigits: 3, useGrouping: 'always', ...opts }).format(n), s)
}

/** Lee un número escrito con los separadores de la compañía ("1,234.5", "-3", "+5"); null si no es un número. */
export function parseNumber(text: string, s: FormatSettings = getFormatSettings()): number | null {
  let v = text.replace(/[\s  ]/g, '')
  if (s.thousandsSeparator.trim()) v = v.split(s.thousandsSeparator).join('')
  if (s.decimalSeparator !== '.') v = v.split(s.decimalSeparator).join('.')
  v = v.replace(/^\+/, '').replace(/^−/, '-')
  if (!/^-?\d+(\.\d+)?$/.test(v)) return null
  return Number(v)
}

// ------------------------------------------------------------------ dinero

export interface MoneyOptions {
  /** Moneda del monto si no es la de la compañía (p. ej. la del contrato de un cliente). */
  currency?: string | null
  /** Costo unitario fino: hasta 4 decimales. */
  unitPrice?: boolean
  /** +$5.00 / -$5.00 (el cero sin signo). */
  signed?: boolean
  /** Decimales fijos (por defecto los de la compañía). */
  decimals?: number
}

/** Símbolo de una moneda: el de la compañía para su moneda (o sin moneda); otra, su símbolo corto ("€") o el código. */
export function currencySymbol(currency: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  const code = (currency ?? '').trim().toUpperCase()
  if (!code || code === s.currencyCode) return s.currencySymbol
  try {
    const part = new Intl.NumberFormat('en-US', { style: 'currency', currency: code, currencyDisplay: 'narrowSymbol' })
      .formatToParts(0)
      .find((p) => p.type === 'currency')
    return part?.value ?? code
  } catch {
    return code
  }
}

/** Monto ya formateado (sin signo) con el símbolo en su posición y el signo delante: "-$12.00" · "-12,00 €". */
export function withCurrencySymbol(body: string, sign: '' | '-' | '+', symbol: string, s: FormatSettings = getFormatSettings()): string {
  return s.currencySymbolPosition === 'A' ? `${sign}${body} ${symbol}` : `${sign}${symbol}${body}`
}

/**
 * Dinero con el símbolo, la posición, los decimales y los separadores de la compañía: "$1,234.50", "-$12.00", "1.234,50 $".
 * `lang` no cambia nada (el formato es de la compañía); se conserva por compatibilidad con las llamadas existentes.
 */
export function formatMoney(
  n: number,
  _lang?: string | null,
  opts: MoneyOptions = {},
  s: FormatSettings = getFormatSettings(),
): string {
  const decimals = opts.decimals ?? s.currencyDecimals
  const body = formatNumber(Math.abs(n), { minimumFractionDigits: decimals, maximumFractionDigits: opts.unitPrice ? Math.max(4, decimals) : decimals }, s)
  const zero = !/[1-9]/.test(body)
  const sign = zero ? '' : n < 0 ? '-' : opts.signed && n > 0 ? '+' : ''
  return withCurrencySymbol(body, sign, currencySymbol(opts.currency, s), s)
}

// ------------------------------------------------------------------ fechas y horas

/** Valor de fecha que aceptan los formateadores: 'YYYY-MM-DD' (día de calendario), instante del API (UTC) o `Date`. */
export type DateValue = string | Date | null | undefined

const YMD = /^(\d{4})-(\d{2})-(\d{2})$/

/** Instante de un valor (null si es un día de calendario sin hora, vacío o inválido). */
function instantOf(v: DateValue): Date | null {
  if (v instanceof Date) return Number.isNaN(v.getTime()) ? null : v
  if (typeof v !== 'string') return null
  const s = v.trim()
  if (!s || YMD.test(s)) return null
  const d = parseApiDate(s)
  return Number.isNaN(d.getTime()) ? null : d
}

/**
 * Año, mes y día ('YYYY', 'MM', 'DD') de un valor: un día de calendario tal cual (sin corrimiento de zona); un instante, su
 * día en la zona de la compañía. null si no es una fecha.
 */
export function dayParts(v: DateValue, s: FormatSettings = getFormatSettings()): [string, string, string] | null {
  if (typeof v === 'string') {
    const m = YMD.exec(v.trim())
    if (m) return [m[1], m[2], m[3]]
  }
  const d = instantOf(v)
  if (!d) return null
  const day = zonedParts(d.getTime(), s.timeZoneId).slice(0, 10)
  const [y, m, dd] = day.split('-')
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

const dateFormats = new Map<string, Intl.DateTimeFormat>()

function dtf(locale: string, opts: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const key = `${locale}|${JSON.stringify(opts)}`
  let f = dateFormats.get(key)
  if (!f) {
    f = new Intl.DateTimeFormat(locale, opts)
    dateFormats.set(key, f)
  }
  return f
}

/** Idioma de la interfaz para los nombres de meses y días y el texto a. m./p. m. ('es' | 'en'). */
export function uiLocale(lang: string | null | undefined): string {
  return (lang ?? '').toLowerCase().startsWith('en') ? 'en' : 'es'
}

/** Opciones de hora de la compañía: 12 h ("2:05 p. m.") o 24 h ("14:05"), en su zona. */
function timeOptions(s: FormatSettings, seconds: boolean): Intl.DateTimeFormatOptions {
  const h24 = s.timeFormat === 24
  return {
    timeZone: s.timeZoneId,
    hour: h24 ? '2-digit' : 'numeric',
    minute: '2-digit',
    ...(seconds ? { second: '2-digit' } : {}),
    hourCycle: h24 ? 'h23' : 'h12',
  }
}

/**
 * Hora de un instante en la zona de la compañía, con su formato de 12 o 24 horas; el idioma pone "a. m."/"AM". Sin instante
 * (o un día de calendario sin hora) → ''.
 */
export function formatTime(
  v: DateValue,
  lang: string | null | undefined,
  opts: { seconds?: boolean } = {},
  s: FormatSettings = getFormatSettings(),
): string {
  const d = instantOf(v)
  if (!d) return ''
  return dtf(uiLocale(lang), timeOptions(s, opts.seconds ?? false)).format(d)
}

/** Hora del día escrita como 'HH:mm' (sin fecha ni zona: una hora de reloj) con el formato de la compañía. */
export function formatTimeOfDay(hhmm: string | null | undefined, lang: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  const m = /^(\d{1,2}):(\d{2})/.exec((hhmm ?? '').trim())
  if (!m) return hhmm ?? ''
  // 2000-01-01 en UTC con la hora pedida, pintada en UTC: ninguna zona la corre
  const d = new Date(Date.UTC(2000, 0, 1, Number(m[1]), Number(m[2])))
  return dtf(uiLocale(lang), { ...timeOptions(s, false), timeZone: 'UTC' }).format(d)
}

/** Fecha corta + hora de la compañía ("10/02/2026 2:05 p. m."); un día de calendario sin hora → solo la fecha. */
export function formatDateTime(v: DateValue, lang: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  const date = formatDate(v, s)
  if (!date) return ''
  const time = formatTime(v, lang, {}, s)
  return time ? `${date} ${time}` : date
}

/**
 * Fecha con nombres de día y de mes en el idioma de la interfaz ("viernes, 2 de octubre de 2026"), en la zona de la
 * compañía. El orden de las partes lo pone el idioma (es una fecha escrita, no numérica). Un día de calendario no se corre.
 */
export function formatDateLong(
  v: DateValue,
  lang: string | null | undefined,
  opts: Intl.DateTimeFormatOptions = { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' },
  s: FormatSettings = getFormatSettings(),
): string {
  if (typeof v === 'string' && YMD.test(v.trim())) {
    return dtf(uiLocale(lang), { ...opts, timeZone: 'UTC' }).format(new Date(`${v.trim()}T12:00:00Z`))
  }
  const d = instantOf(v)
  if (!d) return ''
  return dtf(uiLocale(lang), { timeZone: s.timeZoneId, ...opts }).format(d)
}

/** "Hoy" 'YYYY-MM-DD' en la zona de la compañía (NO UTC: a las 8 p. m. en Puerto Rico `toISOString()` ya da mañana). */
export function todayIso(now: Date = new Date(), s: FormatSettings = getFormatSettings()): string {
  return tenantToday(now, s.timeZoneId)
}

/** Ahora en UTC, 'YYYY-MM-DD HH:mm UTC' (vista previa de Región y formatos). */
export function utcNowText(now: Date = new Date()): string {
  return `${now.toISOString().slice(0, 16).replace('T', ' ')} UTC`
}

/** Fecha escrita y hora de la compañía: "30 de septiembre de 2026, 10:15 a. m." (encabezados de reportes y exportaciones). */
export function formatDateLongTime(v: DateValue, lang: string | null | undefined, s: FormatSettings = getFormatSettings()): string {
  const date = formatDateLong(v, lang, { day: 'numeric', month: 'long', year: 'numeric' }, s)
  const time = formatTime(v, lang, {}, s)
  return date && time ? `${date}, ${time}` : date
}
