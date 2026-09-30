// Lógica pura del selector de motivo del ajuste de inventario (InventoryAdjustModal y el bloque de ajuste de
// ProductEditorModal). Lote 14 (D11): el ajuste se captura como "Subir" o "Bajar" con una cantidad positiva y los motivos
// cambian según la dirección: Encontrado solo al subir; Daño, Pérdida y Vencido solo al bajar; los demás en las dos.

/**
 * Motivos reservados al sistema: los genera el propio proceso (recepción, conteo, reversa de recolección, saldo inicial de
 * la migración), nunca a mano. Espejo de `AdjustmentRules.SystemReasons` del API, que responde 400 si llegan en un ajuste manual.
 */
export const SYSTEM_RESERVED_REASONS: ReadonlySet<string> = new Set([
  'RECEIPT_VARIANCE',
  'COUNT_VARIANCE',
  'PICK_BATCH_REVERSAL',
  'OPENING_BALANCE',
])

/** Dirección de un ajuste manual: sube (entra) o baja (sale) el inventario de la posición. */
export type AdjustDirection = 'up' | 'down'

/** Motivos que solo tienen sentido al SUBIR (algo apareció). */
export const UP_ONLY_REASONS: ReadonlySet<string> = new Set(['FOUND'])

/** Motivos que solo tienen sentido al BAJAR (algo se perdió o se dañó). */
export const DOWN_ONLY_REASONS: ReadonlySet<string> = new Set(['DAMAGE', 'LOSS', 'EXPIRED'])

/** Motivos que se ofrecen en pantalla: los del catálogo menos los reservados al sistema. */
export function selectableAdjustmentReasons<T extends { code: string }>(reasons: readonly T[]): T[] {
  return reasons.filter((r) => !SYSTEM_RESERVED_REASONS.has(r.code))
}

/**
 * Motivos que se ofrecen para una dirección (D11): los seleccionables, sin los exclusivos de la otra dirección. Sin
 * dirección elegida se ofrecen todos los seleccionables (el formulario pide la dirección antes de guardar).
 */
export function reasonsForDirection<T extends { code: string }>(reasons: readonly T[], direction: AdjustDirection | '' | null | undefined): T[] {
  const selectable = selectableAdjustmentReasons(reasons)
  if (direction === 'up') return selectable.filter((r) => !DOWN_ONLY_REASONS.has(r.code))
  if (direction === 'down') return selectable.filter((r) => !UP_ONLY_REASONS.has(r.code))
  return selectable
}

/** true si el motivo vale para la dirección (al cambiar de dirección, un motivo que ya no vale se quita). */
export function reasonAllowed(code: string, direction: AdjustDirection | '' | null | undefined): boolean {
  if (!code) return true
  if (SYSTEM_RESERVED_REASONS.has(code)) return false
  if (direction === 'up') return !DOWN_ONLY_REASONS.has(code)
  if (direction === 'down') return !UP_ONLY_REASONS.has(code)
  return true
}

/** Cantidad con signo que recibe el API (`AdjustmentRequest.quantity`): positiva al subir, negativa al bajar. */
export function signedAdjustQuantity(direction: AdjustDirection, quantity: number): number {
  const magnitude = Math.abs(quantity)
  return direction === 'down' ? -magnitude : magnitude
}
