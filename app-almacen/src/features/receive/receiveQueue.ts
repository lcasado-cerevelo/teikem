// Lote 198 — recibir VARIOS documentos seguidos: se marcan en la lista y se reciben uno tras otro. Si todos son del mismo dueño (mismo cliente, o todo lo propio) y del mismo almacén, se abre
// una carga automática (groupKey) y el servidor reúne los recibos en una recepción maestra; si no, quedan recibos separados. La cola sobrevive a cerrar la app (kv).
import { getDb } from '../../kernel/db/database'
import { getKv, setKv } from '../../kernel/db/kv'
import { takeAsn, takePurchaseOrder } from '../myTasks/myTasksApi'
import type { LocalDoc } from './localLookup'
import { ownerKeyOf } from './receiveTruck'

const KEY = 'receiveQueue'

/** ¿Los documentos marcados se reúnen en una recepción maestra? Dos o más y todos del mismo dueño. */
export function sharesOwner(docs: LocalDoc[]): boolean {
  if (docs.length < 2) return false
  const first = ownerKeyOf(docs[0])
  return docs.every((d) => ownerKeyOf(d) === first)
}

export function getQueue(): LocalDoc[] {
  try {
    const raw = getKv(KEY)
    return raw ? (JSON.parse(raw) as LocalDoc[]) : []
  } catch {
    return []
  }
}

export function setQueue(docs: LocalDoc[]): void {
  setKv(KEY, JSON.stringify(docs))
}

/** Saca de la cola el siguiente documento (null si no queda ninguno). */
export function popQueue(): LocalDoc | null {
  const q = getQueue()
  const next = q.shift() ?? null
  setQueue(q)
  return next
}

/** A quién está asignado el documento en la copia local (undefined si no se encuentra). */
export function docAssignee(doc: LocalDoc): number | null | undefined {
  const db = getDb()
  if (doc.kind === 'asn' && doc.asnId != null) return db.getFirstSync<{ a: number | null }>('SELECT assigned_to_user_id AS a FROM asn WHERE id = ?', [doc.asnId])?.a
  if (doc.kind === 'po' && doc.purchaseOrderPublicId) return db.getFirstSync<{ a: number | null }>('SELECT assigned_to_user_id AS a FROM purchase_order WHERE public_id = ?', [doc.purchaseOrderPublicId])?.a
  return undefined
}

/**
 * Lote 198: empezar a recibir un documento sin dueño lo deja asignado a esta persona (la bitácora anota que fue desde Recibir). Sin señal, o si otra persona ya lo tomó,
 * no estorba: la asignación ordena el trabajo, no lo bloquea. Devuelve true si quedó a nombre de esta persona.
 */
export async function takeDocIfFree(doc: LocalDoc, userId: number | null): Promise<boolean> {
  if (userId == null || docAssignee(doc) !== null) return false
  try {
    if (doc.kind === 'asn' && doc.asnId != null) {
      await takeAsn(doc.asnId, 'MOBILE_RECEIVE')
      getDb().runSync('UPDATE asn SET assigned_to_user_id = ? WHERE id = ?', [userId, doc.asnId])
    } else if (doc.kind === 'po' && doc.purchaseOrderPublicId) {
      await takePurchaseOrder(doc.purchaseOrderPublicId, 'MOBILE_RECEIVE')
      getDb().runSync('UPDATE purchase_order SET assigned_to_user_id = ? WHERE public_id = ?', [userId, doc.purchaseOrderPublicId])
    } else return false
    return true
  } catch {
    return false
  }
}
