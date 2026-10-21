// Lote 174 (C2) — recibo con tandas: qué parte de lo recibido puede entrar a una tanda (mismas reglas que ReceiptBatchRules del servidor:
// Lote 187: las series y el daño SÍ entran (una línea con serie, cuando tiene una serie por cada unidad); solo el cruce de muelle espera) y cuántas unidades nuevas hay por confirmar. Pruebas en receiptBatches.test.ts.
import type { components } from '../../kernel/api/schema'

type LineDto = components['schemas']['ReceiptLineDto']

/** Unidades recibidas y aún no asentadas de una línea que puede entrar a una tanda (0 si no entra o no hay nada nuevo). */
export function batchableUnits(l: LineDto): number {
  if ((l.allocatedToCrossDock ?? 0) > 0) return 0
  if (l.trackingTypeCode === 'SERIAL' && (l.serialNumbers?.length ?? 0) !== (l.receivedQty ?? 0)) return 0
  return Math.max((l.receivedQty ?? 0) - (l.postedQty ?? 0), 0)
}

/** Unidades nuevas por confirmar en una tanda. */
export function pendingBatchUnits(lines: readonly LineDto[] | null | undefined): number {
  return (lines ?? []).reduce((n, l) => n + batchableUnits(l), 0)
}

/** Unidades que se quedan para la confirmación final (series incompletas o con cruce de muelle). */
export function heldForFinalUnits(lines: readonly LineDto[] | null | undefined): number {
  return (lines ?? []).reduce((n, l) => {
    const rest = Math.max((l.receivedQty ?? 0) - (l.postedQty ?? 0), 0)
    return n + (batchableUnits(l) === 0 ? rest : 0)
  }, 0)
}
