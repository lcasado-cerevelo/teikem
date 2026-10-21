// Lote 198 — empezar a despachar un documento sin dueño lo deja asignado a esta persona (la bitácora anota que fue desde Despacho en la app). Sin señal, o si otra persona ya lo tomó, no estorba:
// la asignación ordena el trabajo, no lo bloquea.
import { getDb } from '../../kernel/db/database'
import { takeExitNotice, takeSalesOrder } from '../myTasks/myTasksApi'
import type { LocalExitNotice } from '../exitDispatch/localExit'

/** Devuelve true si el documento quedó asignado a esta persona. */
export async function takeDispatchIfFree(doc: Pick<LocalExitNotice, 'id' | 'publicId' | 'kind' | 'assignedToUserId'>, userId: number | null): Promise<boolean> {
  if (userId == null || (doc.assignedToUserId ?? null) !== null) return false
  const order = doc.kind === 'SALES_ORDER'
  try {
    await (order ? takeSalesOrder(doc.publicId, 'MOBILE_DISPATCH') : takeExitNotice(doc.publicId, 'MOBILE_DISPATCH'))
    getDb().runSync(`UPDATE ${order ? 'sales_order' : 'exit_notice'} SET assigned_to_user_id = ? WHERE id = ?`, [userId, doc.id])
    return true
  } catch {
    return false
  }
}

/** Lo asignado a esta persona va primero (el orden anterior se conserva dentro de cada grupo). */
export function mineFirst<T extends { assignedToUserId?: number | null }>(list: readonly T[], userId: number | null): T[] {
  const rank = (d: T) => (userId != null && d.assignedToUserId === userId ? 0 : d.assignedToUserId == null ? 1 : 2)
  return list.map((d, i) => ({ d, i })).sort((a, b) => rank(a.d) - rank(b.d) || a.i - b.i).map((x) => x.d)
}
