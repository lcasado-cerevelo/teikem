// Lote 8A-app — Acomodar necesita conexión (docs/lote8A-app-decisiones.md, segunda entrega): la lista de tareas y su
// asignación son un recurso compartido con la web y con otros aparatos, así que a diferencia de Recibir/Despacho/
// Conteo esta pantalla no encola nada en la cola de salida; cada acción es una llamada directa, y sin señal se avisa
// y se puede reintentar (docs/mobile/app-almacen-plan.md §2, pantalla 4).
import { api, unwrap } from '../../kernel/api/client'
import { findBinByCode } from '../../kernel/warehouse/binLookup'
import type { PutawayTask } from './putawayLogic'

export { findBinByCode }

export async function fetchOpenPutawayTasks(warehousePublicId: string): Promise<PutawayTask[]> {
  const page = await unwrap(
    api.GET('/api/v1/warehouse-tasks', {
      params: { query: { warehousePublicId, types: ['PUTAWAY'], includeClosed: false, take: 100 } },
    }),
  )
  return (page.items ?? []).map((t) => ({
    id: t.id ?? 0,
    sku: t.sku ?? '',
    productName: t.productName ?? '',
    quantity: t.quantity ?? null,
    toBinCode: t.toBinCode ?? null,
    assignedToUserId: t.assignedToUserId ?? null,
  }))
}

export interface PutawaySuggestion {
  binId: number
  binCode: string
  reason: string | null
}

export async function fetchPutawaySuggestions(taskId: number): Promise<PutawaySuggestion[]> {
  const rows = await unwrap(api.GET('/api/v1/warehouse-tasks/putaway-suggestions', { params: { query: { taskId, take: 3 } } }))
  return rows.map((r) => ({ binId: r.binId ?? 0, binCode: r.binCode ?? '', reason: r.reason ?? null }))
}

/** Marca la tarea "en curso" (la reclama este usuario). */
export async function startTask(taskId: number): Promise<void> {
  await unwrap(api.POST('/api/v1/warehouse-tasks/{id}/start', { params: { path: { id: taskId } } }))
}

export async function completeTask(taskId: number, toBinId: number, quantity: number | null): Promise<void> {
  await unwrap(
    api.POST('/api/v1/warehouse-tasks/{id}/complete', {
      params: { path: { id: taskId } },
      body: { toBinId, quantity: quantity ?? null },
    }),
  )
}
