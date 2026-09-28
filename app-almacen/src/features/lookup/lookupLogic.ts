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
