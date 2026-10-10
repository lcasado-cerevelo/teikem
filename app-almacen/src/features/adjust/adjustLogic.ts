// 2026-10-10 — Ajustar (desde Consultar): cambia SOLO la cantidad de una posición (sube o baja); mover de una posición a otra es una transferencia. Lógica pura.
import { parseQty } from '../count/countLogic'

export type AdjustDirection = 'up' | 'down'

export interface AdjustReason {
  code: string
  /** Clave i18n de la etiqueta. */
  key: string
}

/** Espejo de la web (adjustmentReasons.ts): Encontrado solo al subir; Daño, Pérdida y Vencido solo al bajar; Otro en las dos. Los motivos de sistema no se ofrecen. */
const ALL_REASONS: readonly (AdjustReason & { only?: AdjustDirection })[] = [
  { code: 'FOUND', key: 'adjust.reasonFound', only: 'up' },
  { code: 'DAMAGE', key: 'adjust.reasonDamage', only: 'down' },
  { code: 'LOSS', key: 'adjust.reasonLoss', only: 'down' },
  { code: 'EXPIRED', key: 'adjust.reasonExpired', only: 'down' },
  { code: 'OTHER', key: 'adjust.reasonOther' },
]

export function reasonsFor(direction: AdjustDirection | null): AdjustReason[] {
  return ALL_REASONS.filter((r) => direction == null || r.only == null || r.only === direction).map(({ code, key }) => ({ code, key }))
}

/** Cantidad con signo del API: positiva al subir, negativa al bajar. */
export function signedQuantity(direction: AdjustDirection, qty: number): number {
  return direction === 'down' ? -Math.abs(qty) : Math.abs(qty)
}

export const NOTE_MAX = 300

export type AdjustIssue = 'direction' | 'qty' | 'tooMany' | 'reason' | 'note' | 'noteTooLong'

/** Primer problema del formulario (en el orden en que se llena); null = se puede ajustar. `available` = en mano − reservado (límite al bajar). */
export function adjustIssue(i: { direction: AdjustDirection | null; qtyText: string; reason: string | null; note: string; available: number }): AdjustIssue | null {
  if (!i.direction) return 'direction'
  const qty = parseQty(i.qtyText)
  if (qty === null || !(qty > 0)) return 'qty'
  if (i.direction === 'down' && qty > i.available) return 'tooMany'
  if (!i.reason || !reasonsFor(i.direction).some((r) => r.code === i.reason)) return 'reason'
  const note = i.note.trim()
  if (note === '') return 'note'
  if (note.length > NOTE_MAX) return 'noteTooLong'
  return null
}

/** La existencia del sistema después del ajuste. */
export function afterQty(onHand: number, direction: AdjustDirection, qty: number): number {
  return direction === 'down' ? onHand - qty : onHand + qty
}

export interface AdjustInput {
  warehousePublicId: string
  productPublicId: string
  binId: number
  lotId: number | null
  direction: AdjustDirection
  quantity: number
  reason: string
  note: string
}

/** Cuerpo de POST /api/v1/inventory/adjustments/quantity. */
export function buildAdjustRequest(i: AdjustInput) {
  return {
    productPublicId: i.productPublicId,
    warehousePublicId: i.warehousePublicId,
    binId: i.binId,
    quantity: signedQuantity(i.direction, i.quantity),
    reason: i.reason,
    notes: i.note.trim(),
    lotId: i.lotId,
  }
}
