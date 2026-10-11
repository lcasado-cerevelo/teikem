// Lote 13 — piezas compartidas (sin componentes) de las vistas de Recolección y empaque: dominio de estatus, la guarda de
// Eliminar, el texto "SKU ×cant" de la lista y las sugerencias del filtro "No. de orden".
import { useCallback } from 'react'
import { useCan } from '../../kernel/access'
import type { PickBatchDto } from './api'
import { formatNumber } from './lineRules'

export const PICK_BATCH_STATUS_DOMAIN = 'PickBatchStatus'

/**
 * ¿Se puede eliminar? `canDelete` del servidor y: recolección de empaque = warehouse.pick (y, si ya está empacada, además
 * orders.cancel: se borra su orden); despacho manual (DMA) = warehouse.issue.
 */
export function canDeletePickBatch(
  b: Pick<PickBatchDto, 'canDelete' | 'statusCode' | 'isManual'>,
  perms: { canCancelOrder: boolean; canPick: boolean; canIssue: boolean },
): boolean {
  if (b.canDelete !== true) return false
  if (b.isManual) return perms.canIssue
  return perms.canPick && (b.statusCode !== 'PACKED' || perms.canCancelOrder)
}

/** `canDeletePickBatch` con los permisos del usuario. */
export function usePickBatchCanDelete() {
  const canCancelOrder = useCan('orders.cancel')
  const canPick = useCan('warehouse.pick')
  const canIssue = useCan('warehouse.issue')
  return useCallback(
    (b: Pick<PickBatchDto, 'canDelete' | 'statusCode' | 'isManual'>) => canDeletePickBatch(b, { canCancelOrder, canPick, canIssue }),
    [canCancelOrder, canPick, canIssue],
  )
}

/** "SKU ×cant, SKU ×cant" de las líneas (como la maqueta): una línea por serie o por posición, sumadas por SKU en orden. */
export function batchProductsText(batch: Pick<PickBatchDto, 'lines'>, lang: string): string {
  const bySku = new Map<string, number>()
  for (const l of batch.lines ?? []) {
    const sku = l.sku ?? ''
    bySku.set(sku, (bySku.get(sku) ?? 0) + (l.quantity ?? 0))
  }
  return Array.from(bySku, ([sku, qty]) => `${sku} ×${formatNumber(qty, lang)}`).join(', ')
}

/** Números de orden distintos (sin distinguir mayúsculas) de las recolecciones, en el orden en que llegan. */
export function orderNumberSuggestions(batches: readonly Pick<PickBatchDto, 'orderNumber'>[]): string[] {
  const seen = new Set<string>()
  const out: string[] = []
  for (const b of batches) {
    const n = b.orderNumber?.trim()
    if (!n) continue
    const k = n.toLowerCase()
    if (seen.has(k)) continue
    seen.add(k)
    out.push(n)
  }
  return out
}
