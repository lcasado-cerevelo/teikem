// Lote 246 (Lote F) — reglas puras de «QuickBooks» en Órdenes de compra (sin React). Espejo de `QuickBooksSync` y `PurchaseOrderQuickBooksRules` (C#): el estado de una orden frente a
// QuickBooks lo calcula el servidor (`quickBooksState`); aquí solo se decide el color, qué acciones caben en cada estado y cómo se arman los envíos.
import type { components } from '../../kernel/api/schema'
import { matchesQ } from '../../kernel/ui/matchesQ'
import { markBatches } from '../sales/quickBooksRules'

export { base64ToBytes, downloadBase64File, markBatches, MARK_BATCH } from '../sales/quickBooksRules'

export type PurchaseQbOrder = components['schemas']['PurchaseQuickBooksOrderDto']
export type PurchaseQbPending = components['schemas']['PurchaseQuickBooksPendingDto']
export type PurchaseQbNewSupplier = components['schemas']['PurchaseQuickBooksNewSupplierDto']
export type PurchaseQbNewProduct = components['schemas']['QuickBooksNewProductDto']

/** Los estados posibles, en el orden del filtro. */
export const QB_STATES = ['PENDING', 'EXPORTED', 'IN_QUICKBOOKS', 'FROM_QUICKBOOKS', 'NOT_EXPORTABLE'] as const
export type QbState = (typeof QB_STATES)[number]
export const ORIGINS = ['TEIKEM', 'EXCEL', 'QUICKBOOKS'] as const
export type PoOrigin = (typeof ORIGINS)[number]

type Tone = 'neutral' | 'cap' | 'wh' | 'disp' | 'route' | 'deliv' | 'cod' | 'fail' | 'warn'

/** Color del chip de cada estado (paleta del tema): por pasar = aviso, exportada = por confirmar, en QuickBooks = verde, vino de QuickBooks = azul, sin pasar = neutro. */
export const QB_STATE_TONE: Record<QbState, Tone> = { PENDING: 'warn', EXPORTED: 'cap', IN_QUICKBOOKS: 'deliv', FROM_QUICKBOOKS: 'route', NOT_EXPORTABLE: 'neutral' }
export const ORIGIN_TONE: Record<PoOrigin, Tone> = { TEIKEM: 'neutral', EXCEL: 'wh', QUICKBOOKS: 'route' }

export function qbStateOf(code: string | null | undefined): QbState {
  return (QB_STATES as readonly string[]).includes(code ?? '') ? (code as QbState) : 'NOT_EXPORTABLE'
}
export function originOf(code: string | null | undefined): PoOrigin {
  return (ORIGINS as readonly string[]).includes(code ?? '') ? (code as PoOrigin) : 'TEIKEM'
}

/** ¿Se puede ofrecer para exportar o marcar? (por pasar o ya exportada sin confirmar). */
export const canPass = (state: QbState): boolean => state === 'PENDING' || state === 'EXPORTED'

/** Buscador libre de la lista de por pasar: número, proveedor (y su nombre en QuickBooks) y quién la exportó. */
export function filterPending(rows: readonly PurchaseQbOrder[], q: string): PurchaseQbOrder[] {
  if (!q.trim()) return [...rows]
  return rows.filter((r) => matchesQ(q, r.number, r.supplierName, r.quickBooksVendor, r.exportedByName))
}

/** Lo nuevo de una orden para «ver datos»: su proveedor (si es nuevo) y sus productos nuevos (por SKU). */
export function newDataOf(order: PurchaseQbOrder, pending: PurchaseQbPending | undefined): { supplier: PurchaseQbNewSupplier | null; products: PurchaseQbNewProduct[] } {
  const supplier = order.newSupplier ? (pending?.newSuppliers ?? []).find((s) => s.supplierId === order.supplierId) ?? null : null
  const skus = new Set((order.newProductSkus ?? []).map((s) => s.toUpperCase()))
  const products = (pending?.newProducts ?? []).filter((p) => skus.has((p.sku ?? '').toUpperCase()))
  return { supplier, products }
}

export const hasNewData = (o: PurchaseQbOrder): boolean => !!o.newSupplier || (o.newProductSkus ?? []).length > 0

/**
 * Cuerpo de un envío por selección: si se eligieron TODAS las por pasar se pide `all` (sin tope de 1000); si no, la lista de ids.
 * (Exportar nunca se parte: es un solo archivo; el servidor rechaza más de 1000 con «use todas».)
 */
export function selectionBody(ids: readonly string[], totalPending: number): { purchaseOrderIds?: string[]; all?: boolean } {
  const unique = [...new Set(ids.filter(Boolean))]
  return unique.length > 0 && unique.length === totalPending ? { all: true } : { purchaseOrderIds: unique }
}

/** Parte los ids en tandas de 1000 para marcar, confirmar o quitar (sin repetidas). */
export const batchesOf = (ids: readonly string[]): string[][] => markBatches(ids)

/** ¿La orden ya tiene una marca o un archivo? Sirve para avisar que editar o cancelar no pasa solo a QuickBooks. */
export const touchedQuickBooks = (state: QbState): boolean => state === 'EXPORTED' || state === 'IN_QUICKBOOKS'
