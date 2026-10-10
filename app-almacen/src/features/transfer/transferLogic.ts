// 2026-10-10 — Transferir: mover inventario de una posición a otra DEL MISMO almacén desde el aparato (permiso warehouse.transfer, el operario).
// Lógica pura (sin API ni base). Flujo: posición de origen → producto (y lote si hay varios) → cantidad → posición destino → confirmar.
import { parseQty } from '../count/countLogic'
import type { BalanceRow } from '../lookup/lookupLogic'

/** Posiciones que no se transfieren desde el aparato: cuarentena (Daño), en renta (Rentas) y cross-dock. El servidor también las rechaza. */
export const BLOCKED_ZONE_TYPES = ['QUARANTINE', 'RENTAL', 'CROSSDOCK'] as const

export function isBlockedZone(zoneTypeCode: string | null | undefined): boolean {
  return zoneTypeCode != null && (BLOCKED_ZONE_TYPES as readonly string[]).includes(zoneTypeCode.toUpperCase())
}

export interface TransferBin {
  id: number
  code: string
}

/** Un producto de la posición de origen: sus filas de saldo (una por lote) y lo que se puede mover (en mano − reservado). */
export interface TransferProduct {
  productPublicId: string
  sku: string
  productName: string
  rows: BalanceRow[]
  available: number
}

/** Agrupa las filas de saldo de la posición por producto; solo los que tienen algo movible (disponible > 0), por SKU. */
export function productsToMove(rows: readonly BalanceRow[]): TransferProduct[] {
  const byProduct = new Map<string, TransferProduct>()
  for (const r of rows) {
    if (!(r.qtyAvailable > 0)) continue
    const key = r.productPublicId || r.sku
    let p = byProduct.get(key)
    if (!p) {
      p = { productPublicId: r.productPublicId, sku: r.sku, productName: r.productName, rows: [], available: 0 }
      byProduct.set(key, p)
    }
    p.rows.push(r)
    p.available += r.qtyAvailable
  }
  return [...byProduct.values()].sort((a, b) => (a.sku.toUpperCase() < b.sku.toUpperCase() ? -1 : a.sku.toUpperCase() > b.sku.toUpperCase() ? 1 : 0))
}

/** Lo movible de la fila elegida (un lote) o del producto entero si no lleva lote. */
export function movableOf(product: TransferProduct, lotId: number | null): number {
  if (lotId == null) return product.rows.filter((r) => r.lotId == null).reduce((s, r) => s + r.qtyAvailable, 0)
  return product.rows.filter((r) => r.lotId === lotId).reduce((s, r) => s + r.qtyAvailable, 0)
}

/** ¿Hay que elegir lote? Sí si el producto tiene filas con lote en la posición (aunque sea una sola, se muestra cuál se mueve). */
export function lotChoices(product: TransferProduct): BalanceRow[] {
  return product.rows.filter((r) => r.lotId != null)
}

export type QtyIssue = 'invalid' | 'tooMany'

/** Valida la cantidad escrita contra lo movible. `null` = bien. */
export function qtyIssue(qtyText: string, movable: number): QtyIssue | null {
  const qty = parseQty(qtyText)
  if (qty === null || !(qty > 0)) return 'invalid'
  if (qty > movable) return 'tooMany'
  return null
}

export interface TransferInput {
  warehousePublicId: string
  productPublicId: string
  from: TransferBin
  to: TransferBin
  quantity: number
  lotId: number | null
}

/** Cuerpo de POST /api/v1/inventory/transfers/in-warehouse. */
export function buildTransferRequest(i: TransferInput) {
  return {
    productPublicId: i.productPublicId,
    fromBinId: i.from.id,
    toBinId: i.to.id,
    quantity: i.quantity,
    fromWarehousePublicId: i.warehousePublicId,
    lotId: i.lotId,
  }
}

/** ¿El destino es válido frente al origen? (la misma posición no). */
export function sameBin(a: TransferBin | null, b: TransferBin | null): boolean {
  return a != null && b != null && a.id === b.id
}
