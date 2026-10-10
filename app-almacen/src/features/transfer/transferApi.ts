// 2026-10-10 (D2b) — Transferir va a la cola de salida: se guarda en el aparato, el saldo local refleja el movimiento de inmediato y se manda solo
// (con su Idempotency-Key) en cuanto hay señal. Si el servidor la rechaza (ya no hay disponible, zona no permitida…) queda «requiere revisión» en
// Sincronización y el efecto se deshace.
import { enqueue } from '../../kernel/sync/outbox'
import { projectOperation } from '../../kernel/warehouse/balanceProjection'
import { buildTransferRequest, type TransferInput } from './transferLogic'

/** Encola la transferencia y aplica su efecto a los saldos locales. Devuelve el id de la cola (para `flushNow`). */
export function queueTransfer(input: TransferInput): number {
  const body = buildTransferRequest(input)
  const id = enqueue({ kind: 'transfer', body })
  projectOperation('transfer', body, 1)
  return id
}
