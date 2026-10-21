// Lote 172 (B1) — llamadas en línea de «Mis tareas»: tomar un documento sin asignar y traer los acomodos asignados a este usuario (con copia para verlos sin señal).
import { api, unwrap } from '../../kernel/api/client'
import { getKv, setKv } from '../../kernel/db/kv'

export async function takeAsn(id: number): Promise<void> {
  await unwrap(api.POST('/api/v1/asns/{id}/take', { params: { path: { id } } }))
}

export async function takeExitNotice(publicId: string): Promise<void> {
  await unwrap(api.POST('/api/v1/exit-notices/{publicId}/take', { params: { path: { publicId } } }))
}

export interface MyPutawayTask {
  id: number
  sku: string
  productName: string
  quantity: number | null
  toBinCode: string | null
}

const cacheKey = (warehousePublicId: string, userId: number) => `myPutaway:${warehousePublicId}:${userId}`

/** La última lista de acomodos asignados que se bajó (para mostrarla sin señal). */
export function getCachedMyPutaway(warehousePublicId: string, userId: number): MyPutawayTask[] {
  try {
    const raw = getKv(cacheKey(warehousePublicId, userId))
    return raw ? (JSON.parse(raw) as MyPutawayTask[]) : []
  } catch {
    return []
  }
}

/** Acomodos abiertos asignados a este usuario (`assignedToMe`); si se logra bajar, quedan guardados para la próxima vez sin señal. */
export async function fetchMyPutaway(warehousePublicId: string, userId: number): Promise<MyPutawayTask[]> {
  const page = await unwrap(
    api.GET('/api/v1/warehouse-tasks', { params: { query: { warehousePublicId, types: ['PUTAWAY'], assignedToMe: true, includeClosed: false, take: 100 } } }),
  )
  const list = (page.items ?? []).map((t) => ({
    id: t.id ?? 0,
    sku: t.sku ?? '',
    productName: t.productName ?? '',
    quantity: t.quantity ?? null,
    toBinCode: t.toBinCode ?? null,
  }))
  setKv(cacheKey(warehousePublicId, userId), JSON.stringify(list))
  return list
}
