// Lee todas las páginas de una lista paginada por el servidor (`skip`/`take`) para exportarla completa con los filtros
// de la pantalla. Páginas de 200 (tope del API) hasta `EXPORT_MAX_ROWS` filas; `truncated` avisa si quedaron filas sin leer.
// Uso típico (pantalla con `useProducts(query)`):
//   exportRows={() => fetchAllPages((skip, take) => unwrap(api.GET('/api/v1/products', { params: { query: { ...query, skip, take } } })))}

/** Tamaño de página de las lecturas (tope de `take` en el API). */
export const EXPORT_PAGE_SIZE = 200
/** Máximo de filas que se leen para una exportación. */
export const EXPORT_MAX_ROWS = 10_000

/** Forma de una página del API: `items` y el `total` de la consulta (sin paginar). */
export interface PageLike<T> {
  items?: readonly T[] | null
  total?: number | null
}

export interface FetchAllResult<T> {
  items: T[]
  /** true = la consulta tiene más filas que las leídas (se cortó en `max`). */
  truncated: boolean
}

export interface FetchAllOptions {
  pageSize?: number
  max?: number
}

/**
 * Llama `fetchPage(skip, take)` de a `pageSize` hasta leer el `total`, recibir una página incompleta o llegar a `max`.
 * Si alguna página falla, el error se propaga (la exportación avisa con su toast de error).
 */
export async function fetchAllPages<T>(
  fetchPage: (skip: number, take: number) => Promise<PageLike<T>>,
  options: FetchAllOptions = {},
): Promise<FetchAllResult<T>> {
  const pageSize = Math.max(1, options.pageSize ?? EXPORT_PAGE_SIZE)
  const max = Math.max(1, options.max ?? EXPORT_MAX_ROWS)
  const items: T[] = []
  let total: number | null = null
  let lastFull = false
  while (items.length < max) {
    const take = Math.min(pageSize, max - items.length)
    const page = await fetchPage(items.length, take)
    const got = page.items ?? []
    items.push(...got)
    if (typeof page.total === 'number') total = page.total
    lastFull = got.length >= take
    if (got.length === 0) break
    // con total conocido se sigue aunque el API devuelva menos de lo pedido (su propio tope de `take`)
    if (total !== null ? items.length >= total : !lastFull) break
  }
  // con total conocido manda el total; sin él, se cortó si la última página llegó llena justo en el tope
  const truncated = total !== null ? total > items.length : lastFull && items.length >= max
  return { items, truncated }
}
