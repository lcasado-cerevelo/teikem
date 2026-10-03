// Lote 8A-app — reglas puras de Conteo (docs/mobile/app-almacen-plan.md §2, pantalla 6). Sin API ni base.
// Lote A4 — "Contar por producto" (docs/conteo-por-producto-diseno.md, "App móvil"): una fila por posición (y lote) con un
// espacio para la cantidad; en blanco = 0; Confirmar cierra con una línea de resumen de los blancos; "Otra posición".
import type { LocalProduct } from '../../kernel/warehouse/productLookup'

export interface ExpectedLine {
  lineId: number
  productPublicId: string
  sku: string
  productName: string
  /** null si el usuario no tiene warehouse.count (conteo a ciegas): el servidor ya lo omite, no hace falta permiso aquí. */
  systemQty: number | null
}

/** Línea de un conteo por producto (POST /cycle-counts con productPublicIds): una por posición y lote con existencia. */
export interface ProductCountLine extends ExpectedLine {
  binId: number
  binCode: string
  lotId: number | null
  lotNumber: string | null
  binIsProvisional: boolean
}

export interface CapturedEntry {
  /** Línea esperada capturada (lineId) o un producto encontrado que no estaba en la lista (extra). */
  lineId: number | null
  productPublicId: string
  sku: string
  productName: string
  countedQty: number
  isExtra: boolean
  /** Posición de la línea (cada línea guarda la suya desde la v4 de la base local; en el conteo por posición es la del conteo). */
  binId: number | null
  /** Lote de una línea nueva ("Otra posición" de un producto con lote): por número; el servidor usa el existente o lo crea. */
  lotNumber?: string | null
  lotExpiryDate?: string | null
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
  lot?: { number: string; expiryDate?: string }
  countedQty?: number
}

/** Cuerpo de PUT /cycle-counts/{id}/lines/batch: las líneas esperadas van por lineId; lo encontrado que no estaba en
 *  la lista va por binId (el de CADA línea) + productPublicId (+ lote por número si lo lleva) (CountBatchItem,
 *  docs/lote8A-app-decisiones.md y docs/lote21-decisiones.md). */
export function buildBatchItems(entries: CapturedEntry[]): CountBatchItem[] {
  return entries.map((e) => {
    if (!e.isExtra) return { lineId: e.lineId ?? undefined, countedQty: e.countedQty }
    const item: CountBatchItem = { binId: e.binId ?? undefined, productPublicId: e.productPublicId, countedQty: e.countedQty }
    const lot = e.lotNumber?.trim()
    if (lot) item.lot = e.lotExpiryDate ? { number: lot, expiryDate: e.lotExpiryDate } : { number: lot }
    return item
  })
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

// ------------------------------------------------------------------ conteo por producto

/** El buscador de la lista del conteo por producto aparece solo con MÁS de estas posiciones (decisión del dueño: 6). */
export const PRODUCT_COUNT_SEARCH_THRESHOLD = 6

export function showProductSearch(rowCount: number, threshold: number = PRODUCT_COUNT_SEARCH_THRESHOLD): boolean {
  return rowCount > threshold
}

/** Guarda de entrada: un producto con serie no se cuenta por producto en la app (no hay captura de series): se avisa y no
 *  se crea nada (docs/conteo-por-producto-diseno.md §7). null = se puede contar. */
export function productCountBlocker(product: Pick<LocalProduct, 'trackingTypeCode'>): 'serial' | null {
  return product.trackingTypeCode === 'SERIAL' ? 'serial' : null
}

/** Un espacio en blanco es cero (regla del dueño). */
export function blankAsZero(qty: number | null): number {
  return qty ?? 0
}

export interface ProductCountSummary {
  /** Filas en blanco (se mandan como 0). */
  blanks: number
  /** Filas con algo escrito que no es una cantidad (no deja confirmar). */
  invalid: number
  total: number
}

/** Resumen de lo escrito en cada espacio: cuántos en blanco (se toman como 0) y cuántos no son una cantidad. */
export function summarizeProductCount(texts: readonly string[]): ProductCountSummary {
  let blanks = 0
  let invalid = 0
  for (const text of texts) {
    if (text.trim() === '') blanks += 1
    else if (parseQty(text) === null) invalid += 1
  }
  return { blanks, invalid, total: texts.length }
}

/** Confirmar cierra el conteo en un solo toque: basta con que no haya cantidades inválidas (los blancos son 0). */
export function canConfirmProductCount(summary: ProductCountSummary): boolean {
  return summary.total > 0 && summary.invalid === 0
}

export interface ProductRowLike {
  binCode: string
  lotNumber: string | null
}

/** Buscador de la lista: por código de posición o número de lote, sin distinguir mayúsculas. Vacío = todas. */
export function filterProductRows<T extends ProductRowLike>(rows: readonly T[], query: string): T[] {
  const q = query.trim().toUpperCase()
  if (!q) return [...rows]
  return rows.filter((r) => r.binCode.toUpperCase().includes(q) || (r.lotNumber ?? '').toUpperCase().includes(q))
}

export interface BinCodeInput {
  code: string
  aisle: string
  rack: string
  level: string
  position: string
}

/** Código con que quedará la posición nueva (misma regla que el servidor, WarehouseRules.ResolveBinCode): el código escrito
 *  (recortado, en mayúsculas) o, si no hay, las partes no vacías unidas con '-' (A01-R02-N3-P04). null = falta el dato.
 *  La validación de caracteres y largo la hace el servidor (sus mensajes se muestran tal cual). */
export function resolveBinCode(input: BinCodeInput): string | null {
  const code = input.code.trim().toUpperCase()
  if (code) return code
  const parts = [input.aisle, input.rack, input.level, input.position].map((p) => p.trim().toUpperCase()).filter(Boolean)
  return parts.length > 0 ? parts.join('-') : null
}

function sameLot(a: string | null | undefined, b: string | null | undefined): boolean {
  return (a ?? '').trim().toUpperCase() === (b ?? '').trim().toUpperCase()
}

/** ¿Ya hay en la lista una fila de esa posición (y ese lote)? Mandar dos renglones de la misma línea haría que el servidor
 *  rechazara el lote entero de captura ("La línea se repite en la solicitud."), que ya va en la cola sin poder corregirse. */
export function findListedRow<T extends ProductRowLike>(rows: readonly T[], binCode: string, lotNumber: string | null): T | null {
  const code = binCode.trim().toUpperCase()
  return rows.find((r) => r.binCode.trim().toUpperCase() === code && sameLot(r.lotNumber, lotNumber)) ?? null
}
