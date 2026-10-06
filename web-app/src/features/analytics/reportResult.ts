// Lote F18 (Rentas F-R2) — lógica pura para pintar el resultado de una vista (reporte) del motor de Análisis
// (`POST /api/v1/analytics/reports/{id}/run` → `ReportRunResultDto { columns, rows, totals, total }`). El servidor ya filtró,
// agrupó, agregó y ordenó: aquí solo se convierte cada valor a texto según el tipo de su columna (Number, Date, Bool, Text) con
// los formatos de la compañía, sin volver a calcular nada (no hay un segundo motor en la web).
import type { components } from '../../kernel/api/schema'

export type ReportRunResult = components['schemas']['ReportRunResultDto']
export type ReportResultColumn = components['schemas']['ReportColumn']
export type ReportRow = Record<string, unknown>

/** Formateadores que se inyectan (los de `useFormat()`), para probar sin React. */
export interface ReportFormatters {
  number: (n: number) => string
  money: (n: number) => string
  dateTime: (v: string) => string
  yes: string
  no: string
}

/** Valor crudo de una fila para ordenar (números como números, fechas como texto ISO, booleanos 1/0; vacío = null). */
export function reportSortValue(row: ReportRow, column: ReportResultColumn): string | number | null {
  const v = row[column.key ?? '']
  if (v === null || v === undefined || v === '') return null
  if (typeof v === 'boolean') return v ? 1 : 0
  if (typeof v === 'number') return v
  if (column.type === 'Number') {
    const n = Number(v)
    return Number.isNaN(n) ? String(v) : n
  }
  return String(v)
}

/** Texto de una celda según el tipo de la columna; vacío = '—'. */
export function reportCellText(value: unknown, column: ReportResultColumn, fmt: ReportFormatters): string {
  if (value === null || value === undefined || value === '') return '—'
  switch (column.type) {
    case 'Number': {
      const n = typeof value === 'number' ? value : Number(value)
      if (Number.isNaN(n)) return String(value)
      return column.isMoney ? fmt.money(n) : fmt.number(n)
    }
    case 'Bool':
      return value === true || value === 'true' || value === 1 ? fmt.yes : fmt.no
    case 'Date':
      return typeof value === 'string' ? fmt.dateTime(value) : String(value)
    default:
      return typeof value === 'object' ? JSON.stringify(value) : String(value)
  }
}

/** Totales de una vista agrupada: una cifra por columna que trae valor en `totals` (en el orden de las columnas). */
export function reportTotalsItems(result: ReportRunResult | null | undefined, fmt: ReportFormatters): { key: string; label: string; value: string }[] {
  const totals = result?.totals
  if (!totals) return []
  return (result?.columns ?? [])
    .filter((c) => c.key && totals[c.key] !== null && totals[c.key] !== undefined && totals[c.key] !== '')
    .map((c) => ({ key: c.key ?? '', label: c.label ?? c.key ?? '', value: reportCellText(totals[c.key ?? ''], c, fmt) }))
}
