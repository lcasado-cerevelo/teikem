// Lógica pura de Pulso (formato de valores, rangos de fecha y elección del gráfico), separada para probarla sin montar Recharts.
import { parseApiDate } from '../../kernel/api/dates'

/**
 * Miles con coma, dos decimales si es dinero o si el valor no es entero, signo de dólar si es dinero.
 * Mismo criterio que la maqueta (Indicadores/Gráficos): números en formato "en-US" sin importar el idioma de la UI.
 */
export function formatValue(value: number | null | undefined, isMoney: boolean): string {
  if (value == null || Number.isNaN(value)) return '—'
  const withDecimals = isMoney || !Number.isInteger(value)
  const formatted = value.toLocaleString('en-US', withDecimals ? { minimumFractionDigits: 2, maximumFractionDigits: 2 } : { maximumFractionDigits: 0 })
  return isMoney ? `$${formatted}` : formatted
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
  const text = date.toLocaleDateString(lang, { weekday: 'long', day: 'numeric', month: 'long' })
  return text.charAt(0).toLocaleUpperCase(lang) + text.slice(1)
}

/** Fecha 'YYYY-MM-DD' en el idioma de la interfaz, sin corrimiento por la zona del navegador; '…' si no hay fecha. */
export function formatYmd(ymd: string, lang: string): string {
  if (!ymd) return '…'
  return new Date(`${ymd}T00:00:00Z`).toLocaleDateString(lang, { timeZone: 'UTC' })
}
