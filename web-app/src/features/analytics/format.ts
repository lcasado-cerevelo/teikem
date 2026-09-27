// Lógica pura de Pulso (formato de valores y elección del gráfico), separada para probarla sin montar Recharts.

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
