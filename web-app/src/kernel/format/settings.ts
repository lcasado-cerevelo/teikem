// Región y formatos de la compañía (Lote 18 del backend, fase 3 web): el juego de valores con que se pinta TODA fecha, hora,
// número, dinero y teléfono de la interfaz. Sale de `GET /api/v1/tenant/settings` (columnas de dbo.Tenant) y, mientras carga
// o sin sesión, vale Puerto Rico (los mismos valores por defecto que `TenantFormatRules.Defaults("PR")` y que el SQL).
// El IDIOMA de la interfaz sigue siendo por usuario y solo decide los nombres de meses y días y el texto a. m./p. m.; la
// REGIÓN decide el orden de la fecha, los separadores, la hora de 12/24, la zona y la moneda. Lógica pura.
import type { components } from '../api/schema'

export type TenantFormatDto = components['schemas']['TenantFormatDto']
export type TenantFormatOptionsDto = components['schemas']['TenantFormatOptionsDto']

export type DateOrder = 'MDY' | 'DMY' | 'YMD'
export type SymbolPosition = 'B' | 'A'

/** Valores de formato de la compañía (mismos nombres que el DTO; ya normalizados y con su valor por defecto). */
export interface FormatSettings {
  regionCode: string
  /** Zona IANA de la compañía ("hoy", inicio y fin de cada día, hora que se ve). */
  timeZoneId: string
  currencyCode: string
  currencySymbol: string
  /** B = antes del monto ($1,234.50); A = después (1.234,50 $). */
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

/** Campos que trae una región (todo menos el código de la región): los que "Restaurar valores de la región" envía. */
export const FORMAT_FIELDS = [
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

export type FormatField = (typeof FORMAT_FIELDS)[number]

/** Puerto Rico: región por defecto de toda compañía (espejo de `TenantFormatRules` y de los DEFAULT de dbo.Tenant). */
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

/** Valores con que se pinta todo mientras no hay ajustes del servidor. */
export const DEFAULT_FORMAT: FormatSettings = PR_FORMAT

const DATE_ORDERS: readonly DateOrder[] = ['MDY', 'DMY', 'YMD']
const DATE_SEPARATORS = ['/', '-', '.']
const THOUSANDS_SEPARATORS = [',', '.', ' ']
const DECIMAL_SEPARATORS = ['.', ',']

/** true si el motor de fechas del navegador reconoce la zona (una zona desconocida haría fallar `Intl`). */
export function isKnownTimeZone(zone: string | null | undefined): boolean {
  if (!zone) return false
  try {
    new Intl.DateTimeFormat('en-US', { timeZone: zone })
    return true
  } catch {
    return false
  }
}

type FormatSource = Partial<Record<keyof FormatSettings, unknown>>

/**
 * Ajustes del servidor (TenantSettingsDto o TenantFormatDto) → `FormatSettings`, campo por campo: un valor ausente o fuera
 * de lo permitido toma el de `fallback` (Puerto Rico). Si los separadores chocan, se usan los del respaldo.
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

/** Mismos valores (campo por campo, incluida la región). */
export function sameFormat(a: FormatSettings, b: FormatSettings): boolean {
  return a.regionCode === b.regionCode && FORMAT_FIELDS.every((k) => a[k] === b[k])
}

/** Valores por defecto de una región según `GET /tenant/format-options` (si no viene, los de PR/US conocidos). */
export function regionDefaults(regionCode: string, options?: TenantFormatOptionsDto | null): FormatSettings | null {
  const code = regionCode.toUpperCase()
  const fromApi = options?.regions?.find((r) => (r.regionCode ?? '').toUpperCase() === code)
  if (fromApi) return toFormatSettings({ ...fromApi, regionCode: code }, code === 'US' ? US_FORMAT : PR_FORMAT)
  if (code === 'PR') return PR_FORMAT
  if (code === 'US') return US_FORMAT
  return null
}

/** true si algún valor difiere de los de su región ("Personalizada"); sin la región conocida, false. */
export function isRegionCustom(s: FormatSettings, region: FormatSettings | null): boolean {
  if (!region) return false
  return FORMAT_FIELDS.some((k) => String(s[k]) !== String(region[k]))
}

/**
 * Los separadores de miles y de decimales nunca pueden ser el mismo (el servidor lo rechaza). En pantalla no se pide un paso
 * intermedio: si el usuario escoge uno que choca con el otro, el otro se intercambia solo (como `setFmt` de la maqueta).
 */
export function withSeparator(s: FormatSettings, field: 'thousandsSeparator' | 'decimalSeparator', value: string): FormatSettings {
  const next = { ...s, [field]: value }
  if (next.thousandsSeparator === next.decimalSeparator) {
    const other = value === ',' ? '.' : ','
    if (field === 'thousandsSeparator') next.decimalSeparator = other
    else next.thousandsSeparator = other
  }
  return next
}
