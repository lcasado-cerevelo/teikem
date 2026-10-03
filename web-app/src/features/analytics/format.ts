// Lógica pura de Pulso (formato de valores, rangos de fecha y elección del gráfico), separada para probarla sin montar Recharts.
// Región y formatos (lote F9): los números y el dinero llevan los separadores, el símbolo y los decimales de la COMPAÑÍA, y
// las fechas su zona y su orden (`kernel/format`); el idioma solo pone los nombres de meses y días.
import { parseApiDate } from '../../kernel/api/dates'
import { formatDate, formatDateLong, formatMoney, formatNumber, getFormatSettings, withCurrencySymbol } from '../../kernel/format'

/**
 * Miles con el separador de la compañía; dinero con su símbolo y sus decimales; un no entero, dos decimales (mismo
 * criterio que la maqueta en Indicadores/Gráficos), sin importar el idioma de la interfaz.
 */
export function formatValue(value: number | null | undefined, isMoney: boolean): string {
  if (value == null || Number.isNaN(value)) return '—'
  if (isMoney) return formatMoney(value)
  return formatNumber(value, Number.isInteger(value) ? { maximumFractionDigits: 0 } : { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

export type ChartKind = 'line' | 'donut' | 'pie' | 'bar'

/** Tipo de gráfico a partir de `chartType` del DTO (código de ReportChartType): LINE, DONUT, PIE; el resto, barras. */
export function chartKind(chartType: string | null | undefined): ChartKind {
  switch ((chartType ?? '').toUpperCase()) {
    case 'LINE':
      return 'line'
    case 'DONUT':
      return 'donut'
    case 'PIE':
      return 'pie'
    default:
      return 'bar'
  }
}

/** Código del modo de rango personalizado (DateRangeModes.Custom del dominio). */
export const CUSTOM_RANGE = 'CUSTOM'

/** 'YYYY-MM-DD' (día UTC) de una marca de tiempo del API, restando `minusDays`; '' si no hay fecha o no es válida. */
export function apiDateToYmd(iso: string | null | undefined, minusDays = 0): string {
  if (!iso) return ''
  const date = parseApiDate(iso)
  if (Number.isNaN(date.getTime())) return ''
  date.setUTCDate(date.getUTCDate() - minusDays)
  return date.toISOString().slice(0, 10)
}

/**
 * Desde y Hasta ('YYYY-MM-DD') que eligió el usuario en un rango CUSTOM. El servidor (DateRangeResolver) devuelve
 * `toUtc` como límite EXCLUSIVO (el día siguiente a Hasta, 00:00 UTC): aquí se resta un día.
 */
export function customRangeDays(fromUtc: string | null | undefined, toUtc: string | null | undefined): { from: string; to: string } {
  return { from: apiDateToYmd(fromUtc), to: apiDateToYmd(toUtc, 1) }
}

/**
 * Título del Pulso (Lote F8a): la fecha del día en el idioma de la interfaz ("Lunes, 28 de septiembre" / "Monday,
 * September 28"), con solo la primera letra en mayúscula (el mes en español sigue en minúscula).
 */
export function pulseDateTitle(date: Date, lang: string): string {
  // el día de HOY en la zona de la compañía (no la del navegador)
  const text = formatDateLong(date, lang, { weekday: 'long', day: 'numeric', month: 'long' })
  return text.charAt(0).toLocaleUpperCase(lang) + text.slice(1)
}

/** Fecha 'YYYY-MM-DD' con el formato corto de la compañía, sin corrimiento de zona; '…' si no hay fecha. */
export function formatYmd(ymd: string, _lang: string): string {
  if (!ymd) return '…'
  return formatDate(ymd)
}

// Lote 15 — textos del dibujo de gráficos (ChartVisual): fechas del eje y del tooltip, y el total del centro de la dona.

const YMD = /^\d{4}-\d{2}-\d{2}$/

/** true si la etiqueta es un día 'YYYY-MM-DD' (agrupar por fecha: el motor ya lo da en el día local de la compañía). */
export function isDayLabel(label: string): boolean {
  return YMD.test(label)
}

/** Día 'YYYY-MM-DD' en corto para el eje ("30 sep" / "Sep 30"); sin corrimiento por la zona del navegador. */
export function shortDay(ymd: string, lang: string): string {
  return formatDateLong(ymd, lang, { day: 'numeric', month: 'short' })
}

/** Día 'YYYY-MM-DD' en largo para el tooltip ("miércoles, 30 de septiembre de 2026"). */
export function longDay(ymd: string, lang: string): string {
  return formatDateLong(ymd, lang, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })
}

/** Total del centro de la dona: completo si cabe; si es largo, compacto ("$1.2M"). */
export function donutCenter(total: number, isMoney: boolean): string {
  const full = formatValue(total, isMoney)
  if (full.length <= 10) return full
  const compact = formatNumber(Math.abs(total), { notation: 'compact', maximumFractionDigits: 1 })
  const sign = total < 0 ? '-' : ''
  return isMoney ? withCurrencySymbol(compact, sign, getFormatSettings().currencySymbol) : `${sign}${compact}`
}
