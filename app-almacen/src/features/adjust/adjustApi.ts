// 2026-10-10 (D2b) — Ajustar cantidad (permiso warehouse.adjust) va a la cola de salida: se guarda en el aparato, el saldo local lo refleja al instante y
// se manda solo en cuanto hay señal. Un rechazo del servidor queda «requiere revisión» en Sincronización y deshace el efecto local.
import { enqueue } from '../../kernel/sync/outbox'
import { projectOperation } from '../../kernel/warehouse/balanceProjection'
import { buildAdjustRequest, type AdjustInput } from './adjustLogic'

/** Encola el ajuste y aplica su efecto a los saldos locales. Devuelve el id de la cola (para `flushNow`). */
export function queueAdjustment(input: AdjustInput): number {
  const body = buildAdjustRequest(input)
  const id = enqueue({ kind: 'adjust', body })
  projectOperation('adjust', body, 1)
  return id
}
