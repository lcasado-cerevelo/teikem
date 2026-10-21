// Lote 199 — llamadas en línea de reasignar desde el aparato y la lista de personas (guardada para mostrar «Asignado a: nombre» sin señal).
import { api, unwrap } from '../../kernel/api/client'
import { getDb } from '../../kernel/db/database'
import { getKv, setKv } from '../../kernel/db/kv'

export interface Assignee {
  id: number
  name: string
}

const KEY = 'assignees'

export function getCachedAssignees(): Assignee[] {
  try {
    const raw = getKv(KEY)
    return raw ? (JSON.parse(raw) as Assignee[]) : []
  } catch {
    return []
  }
}

/** Baja las personas de la compañía (con ver todo o reasignar) y las guarda; sin señal o sin permiso devuelve lo guardado. */
export async function fetchAssignees(): Promise<Assignee[]> {
  try {
    const rows = await unwrap(api.GET('/api/v1/warehouse-tasks/assignees'))
    const list = rows.filter((r) => r.id != null).map((r) => ({ id: r.id as number, name: r.name ?? '' }))
    setKv(KEY, JSON.stringify(list))
    return list
  } catch {
    return getCachedAssignees()
  }
}

export function assigneeName(id: number | null | undefined, list: readonly Assignee[] = getCachedAssignees()): string {
  if (id == null) return ''
  return list.find((a) => a.id === id)?.name || `#${id}`
}

export type ReassignKind = 'asn' | 'po' | 'exit' | 'sales' | 'task'

const LOCAL: Record<Exclude<ReassignKind, 'task'>, { table: string; by: string }> = {
  asn: { table: 'asn', by: 'id' },
  po: { table: 'purchase_order', by: 'public_id' },
  exit: { table: 'exit_notice', by: 'public_id' },
  sales: { table: 'sales_order', by: 'public_id' },
}

/** Reasigna (userId null = sin asignar). `key` es el id del aviso de llegada y de la tarea, o el publicId del resto. Con éxito, la copia local queda al día. */
export async function reassign(kind: ReassignKind, key: number | string, userId: number | null): Promise<void> {
  const body = { userId }
  if (kind === 'asn') await unwrap(api.POST('/api/v1/asns/{id}/assign', { params: { path: { id: key as number } }, body }))
  else if (kind === 'task') await unwrap(api.POST('/api/v1/warehouse-tasks/{id}/assign', { params: { path: { id: key as number } }, body }))
  else if (kind === 'po') await unwrap(api.POST('/api/v1/purchase-orders/{publicId}/assign', { params: { path: { publicId: key as string } }, body }))
  else if (kind === 'exit') await unwrap(api.POST('/api/v1/exit-notices/{publicId}/assign', { params: { path: { publicId: key as string } }, body }))
  else await unwrap(api.POST('/api/v1/sales-orders/{publicId}/assign', { params: { path: { publicId: key as string } }, body }))
  if (kind !== 'task') {
    const l = LOCAL[kind]
    getDb().runSync(`UPDATE ${l.table} SET assigned_to_user_id = ? WHERE ${l.by} = ?`, [userId, key])
  }
}
