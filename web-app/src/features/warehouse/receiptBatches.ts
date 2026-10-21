// Lote 174 (C2) — recibo con tandas: qué parte de lo recibido puede entrar a una tanda (mismas reglas que ReceiptBatchRules del servidor:
// sin serie, sin daño declarado y sin cruce de muelle) y cuántas unidades nuevas hay por confirmar. Pruebas en receiptBatches.test.ts.
import type { components } from '../../kernel/api/schema'

type LineDto = components['schemas']['ReceiptLineDto']

/** Unidades recibidas y aún no asentadas de una línea que puede entrar a una tanda (0 si no entra o no hay nada nuevo). */
export function batchableUnits(l: LineDto): number {
  if (l.trackingTypeCode === 'SERIAL' || (l.damagedQty ?? 0) > 0 || (l.allocatedToCrossDock ?? 0) > 0) return 0
  return Math.max((l.receivedQty ?? 0) - (l.postedQty ?? 0), 0)
}

/** Unidades nuevas por confirmar en una tanda. */
export function pendingBatchUnits(lines: readonly LineDto[] | null | undefined): number {
  return (lines ?? []).reduce((n, l) => n + batchableUnits(l), 0)
}

/** Unidades que se quedan para la confirmación final (series, con daño o con cruce de muelle). */
export function heldForFinalUnits(lines: readonly LineDto[] | null | undefined): number {
  return (lines ?? []).reduce((n, l) => {
    const rest = Math.max((l.receivedQty ?? 0) - (l.postedQty ?? 0), 0)
    return n + (batchableUnits(l) === 0 ? rest : 0)
  }, 0)
}
