// Tabla estándar del kit sobre TanStack Table v9 (headless): orden por columna con flecha, paginación del servidor
// (o local por defecto si la lista llega completa), pie con rango/total, "Filas por página" y Exportar, tarjetas bajo
// 720 px, acciones por fila con guardas de permiso/estatus y exportación a Excel/CSV/PDF en el cliente (`exportTable.ts`):
// las filas cargadas o, con `exportRows`, todas las de la consulta del servidor.
import {
  createPaginatedRowModel,
  createSortedRowModel,
  functionalUpdate,
  rowPaginationFeature,
  rowSortingFeature,
  tableFeatures,
  useTable,
  type ColumnDef,
  type PaginationState,
  type RowData,
  type SortFn,
  type SortingState,
  type Updater,
} from '@tanstack/react-table'
import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import type { FetchAllResult } from '../api/fetchAllPages'
import { useAccess } from '../access/accessContext'
import { formatQuantity } from '../i18n/numberFormat'
import { useLang, useT } from '../i18n/useT'
import { EmptyState } from './EmptyState'
import type { ExportChildren } from './exportChildren'
import { ListPager } from './ListPager'
import { exportTable, type ExportFormat } from './exportTable'
import { useExportHeading } from './filterScopeContext'
import { usePanelTitle } from './panelContext'
import { Spinner } from './Spinner'
import { toast } from './toast'
import { CARDS_QUERY, useMediaQuery } from './useMediaQuery'
import './ui.css'

const features = tableFeatures({
  rowSortingFeature,
  sortedRowModel: createSortedRowModel(),
  rowPaginationFeature,
  paginatedRowModel: createPaginatedRowModel(),
})
type Features = typeof features

export type SortValue = string | number | boolean | Date | null | undefined

export interface DataColumn<T> {
  /** Identificador estable; en orden del servidor es el nombre que recibe `onSort`. */
  id: string
  /** Encabezado ya traducido. */
  header: string
  /** Contenido de la celda (y del renglón de la tarjeta en móvil). */
  cell: (row: T) => ReactNode
  /** Valor para ordenar en el cliente. Con `sortValue` la columna es ordenable. */
  sortValue?: (row: T) => SortValue
  /** Ordenable aunque no tenga `sortValue` (orden del servidor con `onSort`). */
  sortable?: boolean
  /** 'end' = columna numérica (alineada a la derecha, monoespaciada). */
  align?: 'start' | 'end'
  /** En tarjeta: 'title' la usa como título; 'hidden' no la muestra. Por defecto la primera columna es el título. */
  card?: 'title' | 'hidden'
  /** Valor exportado (Excel/CSV/PDF). Sin él se exporta el texto que muestra `cell`; si la celda no tiene texto
   *  (un componente sin children, p. ej. `StatusChip`), `sortValue`. */
  exportValue?: (row: T) => SortValue
  /** false = la columna no se exporta (casillas de selección, columnas solo visuales). */
  exportable?: boolean
  /** true = número con signo (+5 / -3) en el PDF agrupado (`exportChildren`). */
  signed?: boolean
}

export interface SortState {
  id: string
  desc: boolean
}

export interface RowAction<T> {
  key: string
  /** Texto de la acción (ya traducido): sin `icon`, es el texto del botón; con `icon`, su `aria-label`/`title`. */
  label: string
  onClick: (row: T) => void
  /** Guarda de permiso: sin él la acción no se pinta. Con arreglo exige todos. */
  perm?: string | readonly string[]
  /** Guarda por estatus/capabilities del DTO: false → no se pinta en esa fila. */
  visible?: (row: T) => boolean
  /** Se pinta deshabilitada (p. ej. mientras corre una mutación). */
  disabled?: (row: T) => boolean
  tone?: 'default' | 'flow' | 'danger'
  /** Ícono de `kernel/ui/actionIcons`: con él, el botón es solo ícono (28×28, `.rowbtn`), como en la maqueta. */
  icon?: ReactNode
}

export interface DataTableProps<T extends RowData> {
  columns: readonly DataColumn<T>[]
  rows: readonly T[]
  /** Clave estable de cada fila (publicId o id). */
  rowKey: (row: T) => string | number
  /** Orden actual. Con `onSort` el orden es del servidor (las filas ya llegan ordenadas); sin él, DataTable ordena. */
  sort?: SortState | null
  onSort?: (sort: SortState | null) => void
  /** Orden inicial cuando el orden es local. */
  defaultSort?: SortState | null
  /** Avisa el orden local cada vez que cambia (para que otra parte de la pantalla, p. ej. un reporte, use el mismo orden). */
  onSortChange?: (sort: SortState | null) => void
  /** Página actual (base 1). Con `onPage` la paginación es del servidor y `total` es obligatorio. */
  page?: number
  /** Filas por página. Servidor: el tamaño que pidió la pantalla. Local: el tamaño inicial (por defecto 25). */
  pageSize?: number
  /** Total de filas del servidor (paginación del servidor). */
  total?: number
  onPage?: (page: number) => void
  /** Paginación del servidor: el usuario cambió "Filas por página" (la pantalla guarda el tamaño y vuelve a la página 1).
   *  Sin ella, con `onPage`, el selector no se muestra. */
  onPageSize?: (size: number) => void
  /** false = sin paginación local: todas las filas, sin rango ni "Filas por página" (tablas de apoyo en modales, líneas
   *  con controles editables). Por defecto true. No afecta a la paginación del servidor (`onPage`). */
  pagination?: boolean
  /** Columnas distintas SOLO para exportar (p. ej. una columna extra de SKU que la tabla no muestra); sin ellas se exportan `columns`. */
  exportColumns?: readonly DataColumn<T>[]
  rowActions?: readonly RowAction<T>[]
  /** Clic en la fila (p. ej. abrir el detalle). También con Enter. */
  onRowClick?: (row: T) => void
  /** Clase extra de la fila (`<tr>` o tarjeta), p. ej. `'dim'` para atenuar un registro inactivo como la maqueta. */
  rowClassName?: (row: T) => string | undefined
  /** Qué pintar sin filas (por defecto EmptyState "Sin resultados"). */
  empty?: ReactNode
  loading?: boolean
  /** Texto accesible de la tabla. */
  label?: string
  /** Tabla compacta (`.densetbl`: menos padding y tipografía) para tablas con muchas columnas. Por defecto se activa sola
   *  desde `DENSE_COLUMNS` columnas (contando la de acciones). Nunca hay scrollbar horizontal propio: la tabla encoge. */
  dense?: boolean
  /** Botón "Exportar" (Excel/CSV/PDF) en el pie. Por defecto true; false en tablas de modales o de apoyo. Exporta las filas
   *  cargadas en el orden actual (todas en paginación local; con `onPage`, solo la página que llegó, salvo `exportRows`). */
  exportable?: boolean
  /** Filas a exportar en lugar de las cargadas: con paginación del servidor, todas las de la consulta actual (mismos
   *  filtros y orden). Acepta el resultado de `fetchAllPages` (si viene `truncated`, avisa con un toast). */
  exportRows?: () => Promise<readonly T[] | FetchAllResult<T>>
  /** Base del nombre del archivo exportado (y título de la hoja/PDF). Por defecto `label`, luego el título del `Panel`. */
  exportFileName?: string
  /** Exportación agrupada (opcional): filas hijas de cada fila (`exportChildren({ children, columns, emptyText })` de
   *  `exportGrouped.ts`). Excel/CSV = una fila por hija repitiendo la fila; PDF = un bloque por fila con sus hijas. */
  exportChildren?: ExportChildren<T>
  /** true = tarjetas aunque la ventana sea ancha (p. ej. la tabla vive en un panel angosto de `SplitPane`: decídelo con
   *  `useElementWidth`). Sin él (o false), tarjetas solo bajo 720 px de ventana. El pie no cambia. */
  forceCards?: boolean
}

/** A partir de cuántas columnas la tabla pasa sola a la variante compacta. */
export const DENSE_COLUMNS = 8

/** Tamaño de página por defecto (las opciones de "Filas por página" están en `ListPager`, que pinta el pie). */
export const DEFAULT_PAGE_SIZE = 25

/** Una celda que devuelve un número crudo se pinta con coma de miles ("1,250"); lo demás, tal cual. */
function displayCell(v: ReactNode, lang: string): ReactNode {
  return typeof v === 'number' && Number.isFinite(v) ? formatQuantity(v, lang) : v
}

const EMPTY_ROWS: never[] = []

function toComparable(v: SortValue): string | number | null {
  if (v === null || v === undefined || v === '') return null
  if (v instanceof Date) return v.getTime()
  if (typeof v === 'boolean') return v ? 1 : 0
  return v
}

const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' })

/** Une clases sin vacíos; sin ninguna, `undefined` (no pinta `class=""`). */
function joinClass(...parts: (string | undefined)[]): string | undefined {
  const cls = parts.filter(Boolean).join(' ')
  return cls || undefined
}

/** Comparación ascendente de dos valores ya normalizados (TanStack invierte el signo en descendente). */
function compareValues(x: string | number, y: string | number): number {
  if (typeof x === 'number' && typeof y === 'number') return x - y
  return collator.compare(String(x), String(y))
}

/** Orden local de filas fuera del modelo de la tabla (exportación con `exportRows`): mismo criterio, vacíos al final. */
export function sortRows<T>(rows: readonly T[], col: DataColumn<T> | undefined, desc: boolean): readonly T[] {
  const get = col?.sortValue
  if (!get) return rows
  return rows
    .map((row) => ({ row, v: toComparable(get(row)) }))
    .sort((a, b) => {
      if (a.v === null) return b.v === null ? 0 : 1
      if (b.v === null) return -1
      const c = compareValues(a.v, b.v)
      return desc ? -c : c
    })
    .map((x) => x.row)
}

function useAllowed(): (perm: RowAction<unknown>['perm']) => boolean {
  const { permissions } = useAccess()
  return (perm) => {
    if (!perm) return true
    const list = typeof perm === 'string' ? [perm] : perm
    return list.every((p) => permissions.has(p))
  }
}

/**
 * `<DataTable columns={cols} rows={items} rowKey={(r) => r.publicId!} sort={sort} onSort={setSort}
 *   page={page} pageSize={size} total={data.total} onPage={setPage} onPageSize={(n) => { setSize(n); setPage(1) }}
 *   exportRows={() => fetchAllPages((skip, take) => …)} rowActions={actions} />`
 * Pasa `rows` memorizadas (useMemo o el `data` de la consulta): un arreglo nuevo en cada render reinicia la página local.
 */
export function DataTable<T extends RowData>(props: DataTableProps<T>) {
  const { columns, rows: pageRows, rowKey, onSort, onPage, rowActions, onRowClick, rowClassName, loading, label } = props
  const dense = props.dense ?? columns.length + (rowActions?.length ? 1 : 0) >= DENSE_COLUMNS
  const t = useT()
  const lang = useLang()
  const cardsByViewport = useMediaQuery(CARDS_QUERY)
  const cards = props.forceCards === true || cardsByViewport
  const allowed = useAllowed()
  const panelTitle = usePanelTitle()
  // compañía activa y oración de filtros del ámbito (FilterScope), leídas al exportar
  const exportHeading = useExportHeading()
  const exportable = props.exportable ?? true

  // ----- orden: del servidor (controlado con onSort) o local -----
  const [localSort, setLocalSort] = useState<SortState | null>(props.defaultSort ?? null)
  const serverSort = onSort !== undefined
  const onSortChange = props.onSortChange
  useEffect(() => {
    onSortChange?.(localSort)
  }, [localSort, onSortChange])
  const sort = serverSort ? (props.sort ?? null) : localSort

  // ----- orden sobre TODO lo filtrado (2026-10-09, Luis): tabla paginada por el servidor, sin orden del servidor, con `exportRows` -----
  // Al ordenar se lee la consulta completa (la misma de Exportar, hasta el tope) y se ordena y pagina aquí: el orden vale para todas las
  // filas del filtro, no solo para la página en pantalla. Se vuelve a leer si cambia la página que manda la pantalla (filtros, recarga).
  const sortAll = onPage !== undefined && !serverSort && Boolean(props.exportRows) && localSort !== null
  const exportRef = useRef(props.exportRows)
  useEffect(() => {
    exportRef.current = props.exportRows
  })
  const [allRows, setAllRows] = useState<{ source: readonly T[]; items: readonly T[] } | null>(null)
  useEffect(() => {
    if (!sortAll) return
    let cancelled = false
    void (async () => {
      try {
        const result = await exportRef.current!()
        if (cancelled) return
        const items: readonly T[] = 'items' in result ? result.items : result
        if ('truncated' in result && result.truncated) toast.info(t('ui.table.export.truncated', { count: items.length }))
        setAllRows({ source: pageRows, items })
      } catch {
        if (!cancelled) toast.error(t('ui.table.sortAllFailed'))
      }
    })()
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- se relee cuando la pantalla manda otra página de filas (filtros, recarga) o se empieza a ordenar
  }, [sortAll, pageRows])
  const showingAll = sortAll && allRows !== null && allRows.source === pageRows
  const rows: readonly T[] = showingAll ? allRows.items : pageRows
  const sortingAll = sortAll && !showingAll
  const sorting = useMemo<SortingState>(() => (sort ? [{ id: sort.id, desc: sort.desc }] : []), [sort])

  // ----- paginación: del servidor (onPage + total), local (por defecto) o ninguna (pagination={false}) -----
  const serverPaging = onPage !== undefined && !showingAll
  const localPaging = !serverPaging && (props.pagination ?? true)
  const [localSize, setLocalSize] = useState(props.pageSize ?? DEFAULT_PAGE_SIZE)
  useEffect(() => {
    if (showingAll) setLocalSize(props.pageSize ?? DEFAULT_PAGE_SIZE)
  }, [showingAll, props.pageSize])
  const pageSize = serverPaging ? (props.pageSize ?? DEFAULT_PAGE_SIZE) : localPaging ? localSize : Math.max(rows.length, 1)
  const [localPage, setLocalPage] = useState(1)
  const [prevRows, setPrevRows] = useState(rows)
  if (!serverPaging && rows !== prevRows) {
    // otra lista (filtros, búsqueda): vuelve a la primera página
    setPrevRows(rows)
    setLocalPage(1)
  }
  const page = serverPaging ? Math.max(props.page ?? 1, 1) : localPage
  const pagination = useMemo<PaginationState>(() => ({ pageIndex: page - 1, pageSize }), [page, pageSize])

  const colDefs = useMemo<ColumnDef<Features, T, unknown>[]>(
    () =>
      columns.map((col) => {
        const sortFn: SortFn<Features, T> = (a, b, id) =>
          compareValues(a.getValue<string | number>(id), b.getValue<string | number>(id))
        return {
          id: col.id,
          header: col.header,
          // vacíos → undefined: sortUndefined 'last' los deja al final en ambos sentidos
          accessorFn: (row: T) => (col.sortValue ? (toComparable(col.sortValue(row)) ?? undefined) : undefined),
          sortUndefined: 'last' as const,
          cell: (ctx) => displayCell(col.cell(ctx.row.original), lang),
          enableSorting: Boolean(col.sortValue) || Boolean(col.sortable),
          sortFn,
        }
      }),
    [columns, lang],
  )

  const data = (rows.length ? rows : EMPTY_ROWS) as T[]

  const table = useTable<Features, T>({
    features,
    columns: colDefs,
    data,
    getRowId: (row) => String(rowKey(row)),
    state: { sorting, pagination },
    onSortingChange: (updater: Updater<SortingState>) => {
      const next = functionalUpdate(updater, sorting)[0]
      const value = next ? { id: next.id, desc: next.desc } : null
      if (serverSort) onSort(value)
      else setLocalSort(value)
    },
    onPaginationChange: (updater: Updater<PaginationState>) => {
      const next = functionalUpdate(updater, pagination).pageIndex + 1
      if (serverPaging) onPage(next)
      else setLocalPage(next)
    },
    manualSorting: serverSort,
    manualPagination: serverPaging,
    rowCount: serverPaging ? (props.total ?? rows.length) : undefined,
    enableSortingRemoval: false,
    sortDescFirst: false, // primer clic siempre ascendente (TanStack empieza en descendente en columnas numéricas)
    enableMultiSort: false,
    autoResetPageIndex: false,
  })

  const visibleRows = table.getRowModel().rows
  const total = serverPaging ? (props.total ?? rows.length) : rows.length
  const pageCount = Math.max(1, Math.ceil(total / pageSize))
  const paged = pageCount > 1
  const showRange = serverPaging || localPaging
  const sizeSelectable = serverPaging ? props.onPageSize !== undefined : localPaging
  const goToPage = (next: number) => table.setPageIndex(next - 1)
  const changePageSize = (size: number) => {
    if (serverPaging) {
      props.onPageSize?.(size)
    } else {
      setLocalSize(size)
      setLocalPage(1)
    }
  }
  // cuántas filas saldrán en el archivo (nota del menú Exportar)
  const exportCount = props.exportRows || !serverPaging ? total : rows.length

  const actionsFor = (row: T) =>
    (rowActions ?? []).filter((a) => allowed(a.perm) && (a.visible ? a.visible(row) : true))
  const hasActions = (rowActions ?? []).some((a) => allowed(a.perm))

  const renderActions = (row: T) => {
    const list = actionsFor(row)
    if (list.length === 0) return null
    return (
      <div className="rowacts">
        {list.map((a) =>
          a.icon ? (
            <button
              key={a.key}
              type="button"
              className={`rowbtn${a.tone === 'danger' ? ' danger' : ''}`}
              aria-label={a.label}
              title={a.label}
              disabled={a.disabled?.(row)}
              onClick={(e) => {
                e.stopPropagation()
                a.onClick(row)
              }}
            >
              {a.icon}
            </button>
          ) : (
            <button
              key={a.key}
              type="button"
              className={`btn sm${a.tone === 'flow' ? ' flow' : a.tone === 'danger' ? ' danger' : ''}`}
              disabled={a.disabled?.(row)}
              onClick={(e) => {
                e.stopPropagation()
                a.onClick(row)
              }}
            >
              {a.label}
            </button>
          ),
        )}
      </div>
    )
  }

  const rowKeyDown = (row: T) => (e: KeyboardEvent) => {
    if (onRowClick && e.key === 'Enter' && e.target === e.currentTarget) onRowClick(row)
  }

  // exporta todo lo cargado (no solo la página visible) en el orden actual; con `exportRows`, lo que devuelva (todas las
  // filas de la consulta del servidor), reordenado como la tabla si el orden es local
  const runExport = async (format: ExportFormat) => {
    let list: readonly T[]
    if (showingAll) {
      // ya se leyó toda la consulta para ordenar: es la misma lista (en el orden de la tabla)
      list = table.getPrePaginatedRowModel().rows.map((r) => r.original)
    } else if (props.exportRows) {
      const result = await props.exportRows()
      const items: readonly T[] = 'items' in result ? result.items : result
      if ('truncated' in result && result.truncated) {
        toast.info(t('ui.table.export.truncated', { count: items.length }))
      }
      list = !serverSort && sort ? sortRows(items, columns.find((c) => c.id === sort.id), sort.desc) : items
    } else {
      list = table.getPrePaginatedRowModel().rows.map((r) => r.original)
    }
    await exportTable(format, props.exportColumns ?? columns, list, {
      locale: lang,
      yes: t('ui.table.export.yes'),
      no: t('ui.table.export.no'),
      title: props.exportFileName ?? label ?? panelTitle,
      ...exportHeading(),
      children: props.exportChildren,
    })
  }

  const sortableCols = columns.filter((c) => c.sortValue || c.sortable)
  const titleCol = columns.find((c) => c.card === 'title') ?? columns.find((c) => c.card !== 'hidden')

  let body: ReactNode
  if (loading && rows.length === 0) {
    body = (
      <div className="dt-loading">
        <Spinner />
      </div>
    )
  } else if (rows.length === 0) {
    body = props.empty ?? <EmptyState title={t('ui.table.empty')} body={t('ui.table.emptyBody')} />
  } else if (cards) {
    body = (
      <>
        {sortableCols.length > 0 && (
          <div className="dt-sortbar">
            <select
              aria-label={t('ui.table.sortBy')}
              value={sort?.id ?? ''}
              onChange={(e) => {
                const id = e.target.value
                table.setSorting(id ? [{ id, desc: false }] : [])
              }}
            >
              <option value="">{t('ui.table.sortBy')}</option>
              {sortableCols.map((c) => (
                <option key={c.id} value={c.id}>
                  {c.header}
                </option>
              ))}
            </select>
            {sort && (
              <button
                type="button"
                className="btn sm"
                aria-label={sort.desc ? t('ui.table.sortDesc') : t('ui.table.sortAsc')}
                onClick={() => table.setSorting([{ id: sort.id, desc: !sort.desc }])}
              >
                {sort.desc ? '▼' : '▲'}
              </button>
            )}
          </div>
        )}
        <ul className="dt-cards" aria-label={label}>
          {visibleRows.map((row) => {
            const r = row.original
            return (
              <li
                key={row.id}
                className={joinClass('dt-card', onRowClick ? 'click' : undefined, rowClassName?.(r))}
                tabIndex={onRowClick ? 0 : undefined}
                onClick={onRowClick ? () => onRowClick(r) : undefined}
                onKeyDown={rowKeyDown(r)}
              >
                {titleCol && <div className="ttl">{displayCell(titleCol.cell(r), lang)}</div>}
                <dl>
                  {columns
                    .filter((c) => c !== titleCol && c.card !== 'hidden')
                    .map((c) => (
                      <div key={c.id} style={{ display: 'contents' }}>
                        <dt>{c.header}</dt>
                        <dd>{displayCell(c.cell(r), lang)}</dd>
                      </div>
                    ))}
                </dl>
                {renderActions(r)}
              </li>
            )
          })}
        </ul>
      </>
    )
  } else {
    body = (
      <div className="dt-scroll">
        <table className={dense ? 'lst densetbl' : 'lst'} aria-label={label} aria-busy={loading || sortingAll || undefined}>
          <thead>
            {table.getHeaderGroups().map((group) => (
              <tr key={group.id}>
                {group.headers.map((header) => {
                  const col = columns.find((c) => c.id === header.column.id)
                  const canSort = header.column.getCanSort()
                  const dir = header.column.getIsSorted()
                  const cls = [canSort ? 'sort' : '', col?.align === 'end' ? 'n' : ''].filter(Boolean).join(' ')
                  return (
                    <th
                      key={header.id}
                      className={cls || undefined}
                      scope="col"
                      aria-sort={dir === 'asc' ? 'ascending' : dir === 'desc' ? 'descending' : canSort ? 'none' : undefined}
                    >
                      {canSort ? (
                        <button type="button" className="sortbtn" onClick={header.column.getToggleSortingHandler()}>
                          <table.FlexRender header={header} />
                          {dir && (
                            <span className="arr" aria-hidden="true">
                              {dir === 'asc' ? '▲' : '▼'}
                            </span>
                          )}
                        </button>
                      ) : (
                        <table.FlexRender header={header} />
                      )}
                    </th>
                  )
                })}
                {hasActions && (
                  <th scope="col" className="n">
                    <span className="sr-only">{t('ui.table.actions')}</span>
                  </th>
                )}
              </tr>
            ))}
          </thead>
          <tbody>
            {visibleRows.map((row) => (
              <tr
                key={row.id}
                className={joinClass(onRowClick ? 'click' : undefined, rowClassName?.(row.original))}
                tabIndex={onRowClick ? 0 : undefined}
                onClick={onRowClick ? () => onRowClick(row.original) : undefined}
                onKeyDown={rowKeyDown(row.original)}
              >
                {row.getAllCells().map((cell) => {
                  const col = columns.find((c) => c.id === cell.column.id)
                  return (
                    <td key={cell.id} className={col?.align === 'end' ? 'n' : undefined}>
                      <table.FlexRender cell={cell} />
                    </td>
                  )
                })}
                {hasActions && <td className="acts">{renderActions(row.original)}</td>}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    )
  }

  return (
    <div className="dt">
      {body}
      {rows.length > 0 && (showRange || exportable) && (
        <ListPager<T>
          page={page}
          pageSize={pageSize}
          total={total}
          showRange={showRange}
          onPage={paged ? goToPage : undefined}
          onPageSize={sizeSelectable ? changePageSize : undefined}
          onExport={exportable ? runExport : undefined}
          exportCount={exportCount}
        />
      )}
    </div>
  )
}
