// 2026-10-10 — Transferir en línea: mueve inventario en el servidor (POST /inventory/transfers/in-warehouse), sin cola: el operario necesita saber ya si
// se pudo (lo reservado no se mueve, la existencia pudo cambiar). Como Daño y Consultar, necesita señal.
import { api, unwrap } from '../../kernel/api/client'
import { buildTransferRequest, type TransferInput } from './transferLogic'

export async function transferInWarehouse(input: TransferInput): Promise<void> {
  await unwrap(api.POST('/api/v1/inventory/transfers/in-warehouse', { body: buildTransferRequest(input) }))
}
