// Tabla estándar del kit sobre TanStack Table v9 (headless): orden por columna con flecha, paginación del servidor
// (o local si la lista llega completa), tarjetas bajo 720 px y acciones por fila con guardas de permiso/estatus.
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
import { useMemo, useState, type KeyboardEvent, type ReactNode } from 'react'
import { useAccess } from '../access/accessContext'
import { useT } from '../i18n/useT'
import { EmptyState } from './EmptyState'
import { Spinner } from './Spinner'
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
  /** Página actual (base 1). Con `onPage` la paginación es del servidor y `total` es obligatorio. */
  page?: number
  pageSize?: number
  /** Total de filas del servidor (paginación del servidor). */
  total?: number
  onPage?: (page: number) => void
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
}

/** A partir de cuántas columnas la tabla pasa sola a la variante compacta. */
export const DENSE_COLUMNS = 8

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
 *   page={page} pageSize={25} total={data.total} onPage={setPage} rowActions={actions} />`
 * Pasa `rows` memorizadas (useMemo o el `data` de la consulta): un arreglo nuevo en cada render reinicia la página local.
 */
export function DataTable<T extends RowData>(props: DataTableProps<T>) {
  const { columns, rows, rowKey, onSort, onPage, rowActions, onRowClick, rowClassName, loading, label } = props
  const dense = props.dense ?? columns.length + (rowActions?.length ? 1 : 0) >= DENSE_COLUMNS
  const t = useT()
  const cards = useMediaQuery(CARDS_QUERY)
  const allowed = useAllowed()

  // ----- orden: del servidor (controlado con onSort) o local -----
  const [localSort, setLocalSort] = useState<SortState | null>(props.defaultSort ?? null)
  const serverSort = onSort !== undefined
  const sort = serverSort ? (props.sort ?? null) : localSort
  const sorting = useMemo<SortingState>(() => (sort ? [{ id: sort.id, desc: sort.desc }] : []), [sort])

  // ----- paginación: del servidor (onPage + total) o local (pageSize) o ninguna -----
  const serverPaging = onPage !== undefined
  const pageSize = props.pageSize ?? (serverPaging ? 25 : Math.max(rows.length, 1))
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
          cell: (ctx) => col.cell(ctx.row.original),
          enableSorting: Boolean(col.sortValue) || Boolean(col.sortable),
          sortFn,
        }
      }),
    [columns],
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
  const from = total === 0 ? 0 : (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, total)

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
                {titleCol && <div className="ttl">{titleCol.cell(r)}</div>}
                <dl>
                  {columns
                    .filter((c) => c !== titleCol && c.card !== 'hidden')
                    .map((c) => (
                      <div key={c.id} style={{ display: 'contents' }}>
                        <dt>{c.header}</dt>
                        <dd>{c.cell(r)}</dd>
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
        <table className={dense ? 'lst densetbl' : 'lst'} aria-label={label} aria-busy={loading || undefined}>
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
      {rows.length > 0 && pageCount > 1 && (
        <nav className="dt-pager" aria-label={t('ui.table.pagination')}>
          <span>{t('ui.table.range', { from, to, total })}</span>
          <span className="pg">
            <button
              type="button"
              className="btn sm"
              disabled={!table.getCanPreviousPage()}
              onClick={() => table.previousPage()}
              aria-label={t('ui.table.prev')}
            >
              ‹
            </button>
            <span aria-current="page">{t('ui.table.pageOf', { page, pages: pageCount })}</span>
            <button
              type="button"
              className="btn sm"
              disabled={!table.getCanNextPage()}
              onClick={() => table.nextPage()}
              aria-label={t('ui.table.next')}
            >
              ›
            </button>
          </span>
        </nav>
      )}
    </div>
  )
}
