// Lote 8A-app — reglas puras de Consultar (docs/mobile/app-almacen-plan.md §2, pantalla 7). Sin API ni base.
export interface BalanceRow {
  id: number
  binCode: string | null
  productPublicId: string
  sku: string
  productName: string
  lotNumber: string | null
  qtyOnHand: number
  qtyAvailable: number
}

/** Clave de caché: por almacén y código exacto escaneado (mayúsculas, sin espacios extra). */
export function cacheKey(warehousePublicId: string, code: string): string {
  return `${warehousePublicId}:${code.trim().toUpperCase()}`
}

/** Minutos transcurridos desde que se guardó la respuesta, para el aviso "datos de hace N min". */
export function minutesAgo(fetchedAtUtc: string, now: Date): number {
  const ms = now.getTime() - new Date(fetchedAtUtc).getTime()
  return Math.max(0, Math.round(ms / 60000))
}

// ------------------------------------------------------------------ Lote A8: lo que hay en una posición

/** Un producto de la posición escaneada: las filas de saldo del mismo producto (una por lote) juntas en una sola. */
export interface BinContentItem {
  productPublicId: string
  sku: string
  productName: string
  /** Lotes distintos con existencia en la posición, en el orden en que llegaron (solo se muestran). */
  lots: string[]
  qtyOnHand: number
  qtyAvailable: number
}

/** Junta las filas de saldo de una posición por producto (varios lotes = una fila con la suma y la lista de lotes) y las
 *  ordena por SKU, para que la lista se lea igual que la hoja impresa de la posición. */
export function aggregateBinContents(rows: readonly BalanceRow[]): BinContentItem[] {
  const byProduct = new Map<string, BinContentItem>()
  for (const r of rows) {
    const key = r.productPublicId || r.sku
    let item = byProduct.get(key)
    if (!item) {
      item = { productPublicId: r.productPublicId, sku: r.sku, productName: r.productName, lots: [], qtyOnHand: 0, qtyAvailable: 0 }
      byProduct.set(key, item)
    }
    const lot = r.lotNumber?.trim()
    if (lot && !item.lots.includes(lot)) item.lots.push(lot)
    item.qtyOnHand += r.qtyOnHand
    item.qtyAvailable += r.qtyAvailable
  }
  // orden simple por SKU en mayúsculas (sin Intl: el motor del aparato no siempre trae los datos de idioma)
  return [...byProduct.values()].sort((a, b) => {
    const x = a.sku.toUpperCase()
    const y = b.sku.toUpperCase()
    return x < y ? -1 : x > y ? 1 : 0
  })
}

/** Lotes que se nombran en la fila; con más, se dice "y N más". */
export const BIN_CONTENT_MAX_LOTS_SHOWN = 3

/** Parte los lotes de una fila en los que se nombran y cuántos quedan sin nombrar. */
export function lotsToShow(lots: readonly string[], max: number = BIN_CONTENT_MAX_LOTS_SHOWN): { shown: string[]; more: number } {
  return { shown: lots.slice(0, max), more: Math.max(0, lots.length - max) }
}

/** Filtro del buscador de la lista (aparece con más de 6 productos, misma regla que la lista del conteo por producto): por
 *  SKU, nombre o lote, sin distinguir mayúsculas. */
export function filterBinContents(items: readonly BinContentItem[], query: string): BinContentItem[] {
  const q = query.trim().toUpperCase()
  if (!q) return [...items]
  return items.filter(
    (i) => i.sku.toUpperCase().includes(q) || i.productName.toUpperCase().includes(q) || i.lots.some((l) => l.toUpperCase().includes(q)),
  )
}

/** Clave de caché de lo que hay en una posición: distinta de la búsqueda libre del mismo texto (otra consulta, otro resultado). */
export function binCacheKey(warehousePublicId: string, binCode: string): string {
  return cacheKey(warehousePublicId, `BIN:${binCode}`)
}
