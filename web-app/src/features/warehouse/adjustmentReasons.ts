// Lógica pura del selector de motivo del ajuste de inventario (InventoryAdjustModal).

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

/** Motivos que se ofrecen en pantalla: los del catálogo menos los reservados al sistema. */
export function selectableAdjustmentReasons<T extends { code: string }>(reasons: readonly T[]): T[] {
  return reasons.filter((r) => !SYSTEM_RESERVED_REASONS.has(r.code))
}
