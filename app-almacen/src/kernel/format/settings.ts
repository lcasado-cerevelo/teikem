// Región y formatos de la compañía en la app de almacén (lote "Región y formatos", parte app). Mismo contrato que la web
// (web-app/src/kernel/format/settings.ts): el juego de valores con que se pinta toda fecha, hora, número, dinero y teléfono.
// Sale de `GET /api/v1/tenant/settings` (columnas de dbo.Tenant), se guarda en la base local de la compañía para trabajar
// sin señal (kernel/format/store.ts) y, mientras no ha llegado nunca, vale Puerto Rico (los mismos valores por defecto que
// `TenantFormatRules.Defaults("PR")` del backend y que el SQL). El IDIOMA sigue siendo del aparato y solo decide los
// textos (a. m./p. m., etiquetas); la REGIÓN decide el orden de la fecha, los separadores, 12/24 h, la zona y la moneda.
// Lógica pura, sin React ni base.
import type { components } from '../api/schema'

export type TenantSettingsDto = components['schemas']['TenantSettingsDto']

export type DateOrder = 'MDY' | 'DMY' | 'YMD'
export type SymbolPosition = 'B' | 'A'

/** Valores de formato de la compañía (mismos nombres que el DTO; ya normalizados y con su valor por defecto). */
export interface FormatSettings {
  regionCode: string
  /** Zona IANA de la compañía: decide "hoy" y la hora que se ve. */
  timeZoneId: string
  currencyCode: string
  currencySymbol: string
  /** B = antes del monto ($1,234.50); A = después (1.234,50 €). */
  currencySymbolPosition: SymbolPosition
  currencyDecimals: number
  dateOrder: DateOrder
  dateSeparator: string
  timeFormat: 12 | 24
  /** 0 = domingo, 1 = lunes. */
  weekStartDay: 0 | 1
  thousandsSeparator: string
  decimalSeparator: string
  phoneCountryCode: string
  /** Cada `#` es un dígito: "(###) ###-####". */
  phoneMask: string
}

/** Campos que se comparan y se guardan (todos). */
export const FORMAT_KEYS = [
  'regionCode',
  'timeZoneId',
  'currencyCode',
  'currencySymbol',
  'currencySymbolPosition',
  'currencyDecimals',
  'dateOrder',
  'dateSeparator',
  'timeFormat',
  'weekStartDay',
  'thousandsSeparator',
  'decimalSeparator',
  'phoneCountryCode',
  'phoneMask',
] as const satisfies readonly (keyof FormatSettings)[]

/** Puerto Rico: región por defecto de toda compañía y respaldo mientras no han llegado los ajustes del servidor. */
export const PR_FORMAT: FormatSettings = {
  regionCode: 'PR',
  timeZoneId: 'America/Puerto_Rico',
  currencyCode: 'USD',
  currencySymbol: '$',
  currencySymbolPosition: 'B',
  currencyDecimals: 2,
  dateOrder: 'MDY',
  dateSeparator: '/',
  timeFormat: 12,
  weekStartDay: 0,
  thousandsSeparator: ',',
  decimalSeparator: '.',
  phoneCountryCode: '+1',
  phoneMask: '(###) ###-####',
}

/** Estados Unidos (Este): solo cambia la zona respecto de Puerto Rico. */
export const US_FORMAT: FormatSettings = { ...PR_FORMAT, regionCode: 'US', timeZoneId: 'America/New_York' }

export const DEFAULT_FORMAT: FormatSettings = PR_FORMAT

const DATE_ORDERS: readonly DateOrder[] = ['MDY', 'DMY', 'YMD']
const DATE_SEPARATORS = ['/', '-', '.']
const THOUSANDS_SEPARATORS = [',', '.', ' ']
const DECIMAL_SEPARATORS = ['.', ',']

/** true si el motor de fechas reconoce la zona (una zona desconocida haría fallar `Intl`). Sin `Intl`, solo acepta la de
 *  Puerto Rico y la de Nueva York (las de las regiones conocidas), para no guardar una zona que luego no se pueda usar. */
export function isKnownTimeZone(zone: string | null | undefined): boolean {
  if (!zone) return false
  if (typeof Intl === 'undefined' || typeof Intl.DateTimeFormat !== 'function') return zone === PR_FORMAT.timeZoneId || zone === US_FORMAT.timeZoneId
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: zone })
    return true
  } catch {
    return false
  }
}

type FormatSource = Partial<Record<keyof FormatSettings, unknown>>

/**
 * Ajustes del servidor (TenantSettingsDto) o los guardados en la base → `FormatSettings`, campo por campo: un valor ausente
 * o fuera de lo permitido toma el de `fallback` (Puerto Rico). Si los separadores chocan, se usan los del respaldo.
 */
export function toFormatSettings(dto: FormatSource | null | undefined, fallback: FormatSettings = DEFAULT_FORMAT): FormatSettings {
  if (!dto) return fallback
  const str = (v: unknown, ok: (s: string) => boolean, def: string) => (typeof v === 'string' && ok(v) ? v : def)
  const num = <N extends number>(v: unknown, allowed: readonly N[], def: N): N =>
    typeof v === 'number' && (allowed as readonly number[]).includes(v) ? (v as N) : def
  const s: FormatSettings = {
    regionCode: str(dto.regionCode, (v) => v.trim().length > 0, fallback.regionCode).toUpperCase(),
    timeZoneId: str(dto.timeZoneId, isKnownTimeZone, fallback.timeZoneId),
    currencyCode: str(dto.currencyCode, (v) => /^[A-Z]{3}$/.test(v), fallback.currencyCode),
    currencySymbol: str(dto.currencySymbol, (v) => v.length >= 1 && v.length <= 3, fallback.currencySymbol),
    currencySymbolPosition: str(dto.currencySymbolPosition, (v) => v === 'B' || v === 'A', fallback.currencySymbolPosition) as SymbolPosition,
    currencyDecimals: num(dto.currencyDecimals, [0, 2, 3], fallback.currencyDecimals),
    dateOrder: str(dto.dateOrder, (v) => (DATE_ORDERS as readonly string[]).includes(v), fallback.dateOrder) as DateOrder,
    dateSeparator: str(dto.dateSeparator, (v) => DATE_SEPARATORS.includes(v), fallback.dateSeparator),
    timeFormat: num(dto.timeFormat, [12, 24] as const, fallback.timeFormat),
    weekStartDay: num(dto.weekStartDay, [0, 1] as const, fallback.weekStartDay),
    thousandsSeparator: str(dto.thousandsSeparator, (v) => THOUSANDS_SEPARATORS.includes(v), fallback.thousandsSeparator),
    decimalSeparator: str(dto.decimalSeparator, (v) => DECIMAL_SEPARATORS.includes(v), fallback.decimalSeparator),
    phoneCountryCode: str(dto.phoneCountryCode, (v) => /^\+\d{1,4}$/.test(v), fallback.phoneCountryCode),
    phoneMask: str(dto.phoneMask, (v) => v.includes('#') && v.length <= 30, fallback.phoneMask),
  }
  if (s.thousandsSeparator === s.decimalSeparator) {
    s.thousandsSeparator = fallback.thousandsSeparator
    s.decimalSeparator = fallback.decimalSeparator
  }
  return s
}

/** Mismos valores, campo por campo. */
export function sameFormat(a: FormatSettings, b: FormatSettings): boolean {
  return FORMAT_KEYS.every((k) => a[k] === b[k])
}

/** Solo los campos de formato (lo que se guarda en la base local; nada de nombre legal ni identificación fiscal). */
export function pickFormat(s: FormatSettings): FormatSettings {
  const out = {} as Record<string, unknown>
  for (const k of FORMAT_KEYS) out[k] = s[k]
  return out as unknown as FormatSettings
}
