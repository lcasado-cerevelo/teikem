// Lote 198 — recibir VARIOS documentos seguidos: se marcan en la lista y se reciben uno tras otro. Si todos son del mismo dueño (mismo cliente, o todo lo propio) y del mismo almacén, se abre
// una carga automática (groupKey) y el servidor reúne los recibos en una recepción maestra; si no, quedan recibos separados. La cola sobrevive a cerrar la app (kv).
import { getDb } from '../../kernel/db/database'
import { getKv, setKv } from '../../kernel/db/kv'
import { takeAsn, takePurchaseOrder } from '../myTasks/myTasksApi'
import type { DraftLine } from './receiveLogic'
import type { ExpectedLine, LocalDoc } from './localLookup'
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

/** Los documentos de esta tanda: el del recibo abierto y los que siguen en la cola (en el orden en que se marcaron). */
export function batchDocs(open: LocalDoc | null): LocalDoc[] {
  return open ? [open, ...getQueue()] : []
}

/** Lo esperado de varios documentos junto (por producto): suma lo esperado y lo ya recibido por partes. */
export function mergeExpected(lists: readonly (readonly ExpectedLine[])[]): ExpectedLine[] {
  const by = new Map<string, ExpectedLine>()
  for (const list of lists)
    for (const l of list) {
      const cur = by.get(l.productPublicId)
      if (cur) by.set(l.productPublicId, { ...cur, expectedQty: cur.expectedQty + l.expectedQty, postedQty: cur.postedQty + l.postedQty })
      else by.set(l.productPublicId, { ...l })
    }
  return [...by.values()]
}

export interface DocSlice {
  doc: LocalDoc
  lines: DraftLine[]
}

/**
 * Lote 199 — recibir varios documentos juntos: lo capturado (un solo conteo por producto) se reparte entre los documentos EN EL ORDEN en que se marcaron, cada uno hasta lo que le falta
 * (esperado − ya recibido); lo que sobre de todos va al último documento que trae el producto. Las series se reparten en orden y el daño se asigna a las primeras piezas.
 * Devuelve solo los documentos que reciben algo. Lógica pura.
 */
export function allocateLines(lines: readonly DraftLine[], docs: readonly LocalDoc[], expected: readonly (readonly ExpectedLine[])[]): DocSlice[] {
  const remaining = expected.map((list) => {
    const m = new Map<string, number>()
    for (const l of list) m.set(l.productPublicId, (m.get(l.productPublicId) ?? 0) + Math.max(0, l.expectedQty - l.postedQty))
    return m
  })
  const slices: DocSlice[] = docs.map((doc) => ({ doc, lines: [] }))
  for (const line of lines) {
    let qty = line.receivedQty
    let serialsFrom = 0
    let damagedLeft = line.damagedQty
    const piece = (take: number): DraftLine => {
      const dmg = Math.min(damagedLeft, take)
      damagedLeft -= dmg
      const serials = line.serialNumbers ? line.serialNumbers.slice(serialsFrom, serialsFrom + take) : null
      serialsFrom += take
      return dmg > 0 ? { ...line, receivedQty: take, serialNumbers: serials, damagedQty: dmg } : { ...line, receivedQty: take, serialNumbers: serials, damagedQty: 0, damageCause: null, damageNote: null, damageBinCode: null, damageDiscard: false, damageDestination: null }
    }
    for (let i = 0; i < docs.length && qty > 1e-9; i++) {
      const room = remaining[i].get(line.productPublicId) ?? 0
      if (room <= 1e-9) continue
      const take = Math.min(qty, room)
      slices[i].lines.push(piece(take))
      remaining[i].set(line.productPublicId, room - take)
      qty -= take
    }
    if (qty > 1e-9) {
      let target = -1
      expected.forEach((list, i) => {
        if (list.some((l) => l.productPublicId === line.productPublicId)) target = i
      })
      const extra = piece(qty)
      const lines = slices[target >= 0 ? target : 0].lines
      const last = lines[lines.length - 1]
      // el sobrante se suma a la línea de ese producto que ya lleva el documento (una sola línea por producto)
      if (last && last.productPublicId === extra.productPublicId && last.lotNumber === extra.lotNumber && last.targetBinCode === extra.targetBinCode && !extra.damagedQty) {
        lines[lines.length - 1] = { ...last, receivedQty: last.receivedQty + extra.receivedQty, serialNumbers: last.serialNumbers && extra.serialNumbers ? [...last.serialNumbers, ...extra.serialNumbers] : last.serialNumbers }
      } else lines.push(extra)
    }
  }
  return slices.filter((x) => x.lines.length > 0)
}
