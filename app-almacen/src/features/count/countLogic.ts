// Lote 8A-app — reglas puras de Conteo (docs/mobile/app-almacen-plan.md §2, pantalla 6). Sin API ni base.
export interface ExpectedLine {
  lineId: number
  productPublicId: string
  sku: string
  productName: string
  /** null si el usuario no tiene warehouse.count (conteo a ciegas): el servidor ya lo omite, no hace falta permiso aquí. */
  systemQty: number | null
}

export interface CapturedEntry {
  /** Línea esperada capturada (lineId) o un producto encontrado que no estaba en la lista (extra). */
  lineId: number | null
  productPublicId: string
  sku: string
  productName: string
  countedQty: number
  isExtra: boolean
}

export function parseQty(text: string): number | null {
  if (text.trim() === '') return null
  const n = Number(text.trim().replace(',', '.'))
  return Number.isFinite(n) && n >= 0 ? n : null
}

export interface CountBatchItem {
  lineId?: number
  binId?: number
  productPublicId?: string
  countedQty?: number
}

/** Cuerpo de PUT /cycle-counts/{id}/lines/batch: las líneas esperadas van por lineId; lo encontrado que no estaba en
 *  la lista va por binId + productPublicId (CountBatchItem, docs/lote8A-app-decisiones.md). */
export function buildBatchItems(binId: number, entries: CapturedEntry[]): CountBatchItem[] {
  return entries.map((e) =>
    e.isExtra
      ? { binId, productPublicId: e.productPublicId, countedQty: e.countedQty }
      : { lineId: e.lineId ?? undefined, countedQty: e.countedQty },
  )
}

/** Líneas esperadas que todavía no se han capturado (para mostrar "lo que falta"). */
export function remainingExpectedLines(expected: ExpectedLine[], capturedLineIds: Set<number>): ExpectedLine[] {
  return expected.filter((l) => !capturedLineIds.has(l.lineId))
}

/** Producto escaneado → línea esperada que le corresponde (prefiere una sin capturar; si todas ya se capturaron, la
 *  primera, para permitir corregir la cantidad). null si el producto no estaba en la lista (se captura como extra). */
export function matchExpectedLine(expected: ExpectedLine[], capturedLineIds: Set<number>, productPublicId: string): ExpectedLine | null {
  const uncaptured = expected.find((l) => l.productPublicId === productPublicId && !capturedLineIds.has(l.lineId))
  if (uncaptured) return uncaptured
  return expected.find((l) => l.productPublicId === productPublicId) ?? null
}
