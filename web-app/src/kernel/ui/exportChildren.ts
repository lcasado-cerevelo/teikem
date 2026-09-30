// Exportación agrupada del kit (filas madre con hijas, p. ej. recibos con sus líneas): `exportChildren` prepara las hijas
// para `exportChildren` de `ListPager` / `DataTable` (o `exportTable`). Módulo liviano (sin jsPDF ni SheetJS): el armado y
// la descarga viven en exportGrouped.ts, que `exportTable` carga bajo demanda.
import { exportCellValue, type ExportableColumn, type ExportCell, type ExportOptions } from './exportTable'

/** Filas hijas ya preparadas para exportar (se arma con `exportChildren`; el tipo de la hija queda encapsulado). */
export interface ExportChildren<T> {
  headers: string[]
  /** true = columna numérica (a la derecha en el PDF). */
  numeric: boolean[]
  /** true = número con signo en el PDF (+5 / -3). */
  signed: boolean[]
  /** Celdas de las hijas de `row` ([] = sin hijas). */
  rows: (row: T, opts: ExportOptions) => ExportCell[][]
  /** Texto del bloque del PDF para una madre sin hijas ("Sin líneas"). */
  emptyText: string
  /** Cuántas de las primeras columnas de la madre forman el título de la banda del PDF (por defecto 1). */
  titleColumns: number
}

export interface ExportChildrenSpec<T, C> {
  /** Hijas de una fila (null/undefined = sin hijas). */
  children: (row: T) => readonly C[] | null | undefined
  /** Columnas de las hijas (mismas reglas que las de `DataTable`: `exportValue`, texto de `cell`, `sortValue`, `exportable`, `align`, `signed`). */
  columns: readonly ExportableColumn<C>[]
  emptyText: string
  titleColumns?: number
}

/** Prepara las hijas de una exportación agrupada: `exportChildren={exportChildren({ children: (r) => r.lines, columns, emptyText })}`. */
export function exportChildren<T, C>(spec: ExportChildrenSpec<T, C>): ExportChildren<T> {
  const cols = spec.columns.filter((c) => c.exportable !== false)
  return {
    headers: cols.map((c) => c.header),
    numeric: cols.map((c) => c.align === 'end'),
    signed: cols.map((c) => c.signed === true),
    rows: (row, opts) => (spec.children(row) ?? []).map((child) => cols.map((c) => exportCellValue(c, child, opts))),
    emptyText: spec.emptyText,
    titleColumns: Math.max(0, spec.titleColumns ?? 1),
  }
}
