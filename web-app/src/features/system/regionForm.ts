// Formulario de Ajustes → Región y formatos: valores de texto del formulario ↔ ajustes de formato, opciones de cada lista
// (las permitidas que da `GET /tenant/format-options`, con las de la maqueta como respaldo) y el cuerpo del PUT. Lógica pura.
import { FORMAT_FIELDS, toFormatSettings, type FormatSettings, type TenantFormatOptionsDto } from '../../kernel/format'
import type { TenantSettingsUpdateRequest } from './tenantSettingsApi'

/** Valores del formulario (todo texto: los `<select>` trabajan con cadenas). */
export interface RegionFormValues {
  regionCode: string
  timeZoneId: string
  currencyCode: string
  currencySymbol: string
  currencySymbolPosition: string
  currencyDecimals: string
  dateOrder: string
  dateSeparator: string
  timeFormat: string
  weekStartDay: string
  thousandsSeparator: string
  decimalSeparator: string
  phoneCountryCode: string
  phoneMask: string
}

/** Zonas que ofrece la pantalla (las de la maqueta); una zona guardada que no esté se agrega tal cual. */
export const TIME_ZONES = [
  'America/Puerto_Rico',
  'America/New_York',
  'America/Chicago',
  'America/Denver',
  'America/Los_Angeles',
  'America/Anchorage',
  'Pacific/Honolulu',
] as const

/** Máscaras de teléfono que ofrece la pantalla (cada # es un dígito); una guardada distinta se agrega. */
export const PHONE_MASKS = ['(###) ###-####', '###-###-####', '###.###.####', '### ### ####'] as const

/** Monedas que ofrece la pantalla (la maqueta solo USD); una guardada distinta se agrega. */
export const CURRENCIES = ['USD'] as const

/** Respaldo de las listas permitidas (mismos valores que los CHECK de dbo.Tenant) si `format-options` no llegó. */
export const ALLOWED_FALLBACK = {
  currencySymbolPositions: ['B', 'A'],
  currencyDecimals: [0, 2, 3],
  dateOrders: ['MDY', 'DMY', 'YMD'],
  dateSeparators: ['/', '-', '.'],
  timeFormats: [12, 24],
  weekStartDays: [0, 1],
  thousandsSeparators: [',', '.', ' '],
  decimalSeparators: ['.', ','],
}

export type AllowedLists = typeof ALLOWED_FALLBACK

/** Listas permitidas: las del servidor si vienen, si no las de respaldo. */
export function allowedLists(options: TenantFormatOptionsDto | null | undefined): AllowedLists {
  const pick = <T>(v: T[] | null | undefined, def: T[]) => (v && v.length > 0 ? v : def)
  return {
    currencySymbolPositions: pick(options?.currencySymbolPositions, ALLOWED_FALLBACK.currencySymbolPositions),
    currencyDecimals: pick(options?.currencyDecimals, ALLOWED_FALLBACK.currencyDecimals),
    dateOrders: pick(options?.dateOrders, ALLOWED_FALLBACK.dateOrders),
    dateSeparators: pick(options?.dateSeparators, ALLOWED_FALLBACK.dateSeparators),
    timeFormats: pick(options?.timeFormats, ALLOWED_FALLBACK.timeFormats),
    weekStartDays: pick(options?.weekStartDays, ALLOWED_FALLBACK.weekStartDays),
    thousandsSeparators: pick(options?.thousandsSeparators, ALLOWED_FALLBACK.thousandsSeparators),
    decimalSeparators: pick(options?.decimalSeparators, ALLOWED_FALLBACK.decimalSeparators),
  }
}

/** Códigos de región que ofrece la pantalla (los de `format-options`; PR y US de respaldo). */
export function regionCodes(options: TenantFormatOptionsDto | null | undefined): string[] {
  const codes = (options?.regions ?? []).map((r) => (r.regionCode ?? '').toUpperCase()).filter(Boolean)
  return codes.length > 0 ? codes : ['PR', 'US']
}

export function toRegionForm(s: FormatSettings): RegionFormValues {
  return {
    regionCode: s.regionCode,
    timeZoneId: s.timeZoneId,
    currencyCode: s.currencyCode,
    currencySymbol: s.currencySymbol,
    currencySymbolPosition: s.currencySymbolPosition,
    currencyDecimals: String(s.currencyDecimals),
    dateOrder: s.dateOrder,
    dateSeparator: s.dateSeparator,
    timeFormat: String(s.timeFormat),
    weekStartDay: String(s.weekStartDay),
    thousandsSeparator: s.thousandsSeparator,
    decimalSeparator: s.decimalSeparator,
    phoneCountryCode: s.phoneCountryCode,
    phoneMask: s.phoneMask,
  }
}

const int = (v: string) => (/^\d+$/.test(v.trim()) ? Number(v) : null)

/** Ajustes que se ven en la VISTA PREVIA: los del formulario; un valor inválido toma el vigente (`fallback`). */
export function previewSettings(v: RegionFormValues, fallback: FormatSettings): FormatSettings {
  return toFormatSettings(
    {
      ...v,
      currencyDecimals: int(v.currencyDecimals),
      timeFormat: int(v.timeFormat),
      weekStartDay: int(v.weekStartDay),
      currencySymbol: v.currencySymbol.trim(),
      phoneCountryCode: v.phoneCountryCode.trim(),
    },
    fallback,
  )
}

/**
 * Cuerpo del PUT: la región y SIEMPRE el juego completo de los 13 campos (así el resultado es exactamente lo que se ve,
 * cambie o no la región: con región distinta el servidor parte de sus valores y los enviados mandan; con la misma, aplica
 * los enviados). Los textos van recortados; los números como números.
 */
export function regionRequestBody(v: RegionFormValues): TenantSettingsUpdateRequest {
  return {
    regionCode: v.regionCode,
    timeZoneId: v.timeZoneId.trim(),
    currencyCode: v.currencyCode.trim().toUpperCase(),
    currencySymbol: v.currencySymbol.trim(),
    currencySymbolPosition: v.currencySymbolPosition,
    currencyDecimals: int(v.currencyDecimals),
    dateOrder: v.dateOrder,
    dateSeparator: v.dateSeparator,
    timeFormat: int(v.timeFormat),
    weekStartDay: int(v.weekStartDay),
    thousandsSeparator: v.thousandsSeparator,
    decimalSeparator: v.decimalSeparator,
    phoneCountryCode: v.phoneCountryCode.trim(),
    phoneMask: v.phoneMask,
  }
}

/** ¿El formulario difiere de los valores de la región? ("Personalizada"). */
export function formIsCustom(v: RegionFormValues, region: FormatSettings | null): boolean {
  if (!region) return false
  const r = toRegionForm(region)
  return FORMAT_FIELDS.some((k) => String(v[k]).trim() !== String(r[k]))
}

/** Lista de opciones con el valor actual agregado si no está entre las ofrecidas. */
export function withCurrent<T extends string | number>(list: readonly T[], current: T | string): (T | string)[] {
  return list.some((x) => String(x) === String(current)) || String(current) === '' ? [...list] : [...list, current]
}
