// Pie de lista con el aspecto del pie de `DataTable` (rango y total, "Filas por página", Exportar y ‹ ›) para listas que
// no son `DataTable` (p. ej. una lista maestra `.unrow` paginada en el servidor). `DataTable` pinta su pie con este mismo
// componente, así que los dos se ven y se comportan igual.
import type { FetchAllResult } from '../api/fetchAllPages'
import { numberLocale } from '../i18n/numberFormat'
import { useLang, useT } from '../i18n/useT'
import type { DataColumn } from './DataTable'
import { ExportMenu } from './ExportMenu'
import type { ExportChildren } from './exportChildren'
import { exportTable, type ExportFormat } from './exportTable'
import { pageSizeOptions } from './pageSize'
import { useExportHeading } from './filterScopeContext'
import { usePanelTitle } from './panelContext'
import { toast } from './toast'
import './ui.css'

export interface ListPagerProps<T> {
  /** Página actual (base 1). */
  page: number
  pageSize: number
  /** Total de filas de la consulta. Con 0 el pie no se pinta. */
  total: number
  /** Cambio de página; sin él (o con una sola página) no hay ‹ ›. */
  onPage?: (page: number) => void
  /** "Filas por página": la pantalla guarda el tamaño y vuelve a la página 1. Sin él no hay selector. */
  onPageSize?: (size: number) => void
  /** Rango "1–25 de 552". Por defecto true. */
  showRange?: boolean
  /** Exportar con columnas: cómo se exporta cada campo (mismas reglas que `DataTable`: `exportValue`, texto de `cell`,
   *  `sortValue`; `exportable: false` = fuera). Junto con `exportRows` pinta el botón Exportar. */
  exportColumns?: readonly DataColumn<T>[]
  /** Filas a exportar (normalmente todo lo filtrado: `exportReceipts(query)` / `fetchAllPages`); si vienen truncadas, toast. */
  exportRows?: () => Promise<readonly T[] | FetchAllResult<T>>
  /** Base del nombre del archivo (y título de la hoja/PDF). Por defecto el título del `Panel`. */
  exportFileName?: string
  /** Exportación agrupada (opcional; solo con `exportColumns` + `exportRows`): filas hijas de cada fila, armadas con
   *  `exportChildren({ children, columns, emptyText, titleColumns })` de `exportGrouped.ts`. Excel/CSV = una fila por hija
   *  repitiendo la fila madre; PDF (carta horizontal) = un bloque por fila con su banda y la tablita de sus hijas. */
  exportChildren?: ExportChildren<T>
  /** Exportación propia (en vez de `exportColumns` + `exportRows`); la usa `DataTable`. */
  onExport?: (format: ExportFormat) => void | Promise<void>
  /** Filas que saldrán en el archivo (nota del menú). Por defecto `total`. */
  exportCount?: number
}

/**
 * `<ListPager page={page} pageSize={size} total={data?.total ?? 0} onPage={setPage}
 *   onPageSize={(n) => { setSize(n); setPage(1) }} exportColumns={cols} exportRows={() => exportReceipts(query)} />`
 */
export function ListPager<T>({
  page,
  pageSize,
  total,
  onPage,
  onPageSize,
  showRange = true,
  exportColumns,
  exportRows,
  exportFileName,
  exportChildren,
  onExport,
  exportCount,
}: ListPagerProps<T>) {
  const t = useT()
  const lang = useLang()
  const panelTitle = usePanelTitle()
  // compañía activa y oración de filtros del ámbito (FilterScope), leídas al exportar
  const exportHeading = useExportHeading()
  if (!(total > 0)) return null

  const size = Math.max(1, pageSize)
  const pageCount = Math.max(1, Math.ceil(total / size))
  const current = Math.min(Math.max(1, page), pageCount)
  const from = (current - 1) * size + 1
  const to = Math.min(current * size, total)

  const builtInExport =
    exportColumns && exportRows
      ? async (format: ExportFormat) => {
          const result = await exportRows()
          const items: readonly T[] = 'items' in result ? result.items : result
          if ('truncated' in result && result.truncated) {
            toast.info(t('ui.table.export.truncated', { count: items.length }))
          }
          await exportTable(format, exportColumns, items, {
            locale: numberLocale(lang),
            yes: t('ui.table.export.yes'),
            no: t('ui.table.export.no'),
            title: exportFileName ?? panelTitle,
            ...exportHeading(),
            children: exportChildren,
          })
        }
      : undefined
  const runExport = onExport ?? builtInExport

  return (
    <div className="dt-pager">
      {showRange && <span className="dt-range">{t('ui.table.range', { from, to, total })}</span>}
      {onPageSize && (
        <label className="dt-size">
          <span>{t('ui.table.pageSize')}</span>
          <select value={size} onChange={(e) => onPageSize(Number(e.target.value))}>
            {pageSizeOptions(size).map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </label>
      )}
      {runExport && <ExportMenu onExport={runExport} count={exportCount ?? total} />}
      {onPage && pageCount > 1 && (
        <nav className="pg" aria-label={t('ui.table.pagination')}>
          <button type="button" className="btn sm" disabled={current <= 1} onClick={() => onPage(current - 1)} aria-label={t('ui.table.prev')}>
            ‹
          </button>
          <span aria-current="page">{t('ui.table.pageOf', { page: current, pages: pageCount })}</span>
          <button
            type="button"
            className="btn sm"
            disabled={current >= pageCount}
            onClick={() => onPage(current + 1)}
            aria-label={t('ui.table.next')}
          >
            ›
          </button>
        </nav>
      )}
    </div>
  )
}
