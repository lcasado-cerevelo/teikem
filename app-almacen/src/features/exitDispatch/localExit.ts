// Lote 146 — la copia local de los avisos de salida por despachar (schema v17: exit_notice, exit_notice_line, exit_notice_reservation) y el envío del despacho por la cola de salida.
// Igual que las órdenes de venta (features/salesDispatch/localSales.ts) pero sin kits, consignatario ni entrega. Lo que se despacha se refleja AL INSTANTE en la copia (reservado/despachado)
// y en los saldos; si el servidor lo rechaza, los saldos se deshacen solos (runOutbox) y el aviso se vuelve a bajar completo en la siguiente sincronización.
// Las tareas guiadas y el cuerpo de las líneas son los mismos que los de ventas (salesDispatchLogic): una línea de aviso es una línea normal (isKit = false).
import { getDb, type SQLiteDatabase } from '../../kernel/db/database'
import { enqueue } from '../../kernel/sync/outbox'
import { applyDeltasIn, issueDeltas } from '../../kernel/warehouse/balanceProjection'
import { buildDispatchLines, buildTasks, type LocalSalesLine, type SalesPick, type SalesTask } from '../salesDispatch/salesDispatchLogic'
import { applyDispatchLocally, getSalesOrderLines, kitConsumption, listSalesOrders } from '../salesDispatch/localSales'
import { buildGroupBody, docKey, realLineId, type GroupableNotice, type GroupKind } from './exitGroupLogic'

export interface LocalExitNotice {
  id: number
  publicId: string
  number: string
  warehousePublicId: string
  clientName: string
  reference: string | null
  destination: string | null
  statusCode: string | null
  requiredDate: string | null
  /** Lo reservado que se puede despachar ya. */
  reserved: number
  /** Lote 186: en «Recolectar juntos» también entran órdenes de venta (con la forma de un aviso); ausente = aviso de salida. */
  kind?: GroupKind
}

interface NoticeRow {
  id: number
  public_id: string
  number: string
  warehouse_public_id: string
  client_name: string | null
  reference: string | null
  destination: string | null
  status_code: string | null
  required_date: string | null
  reserved: number | null
}

function mapNotice(r: NoticeRow): LocalExitNotice {
  return {
    id: r.id,
    publicId: r.public_id,
    number: r.number,
    warehousePublicId: r.warehouse_public_id,
    clientName: r.client_name ?? '',
    reference: r.reference,
    destination: r.destination,
    statusCode: r.status_code,
    requiredDate: r.required_date,
    reserved: r.reserved ?? 0,
  }
}

const SELECT_NOTICE = `SELECT o.*, (SELECT SUM(l.qty_reserved) FROM exit_notice_line l WHERE l.exit_notice_id = o.id) AS reserved FROM exit_notice o`

/** Avisos del almacén con algo reservado que se pueda despachar, los más antiguos primero (la fecha requerida manda). */
export function listExitNotices(warehousePublicId: string): LocalExitNotice[] {
  return getDb()
    .getAllSync<NoticeRow>(`${SELECT_NOTICE} WHERE o.warehouse_public_id = ? ORDER BY (o.required_date IS NULL), o.required_date, o.id`, [warehousePublicId])
    .map(mapNotice)
    .filter((o) => o.reserved > 0)
}

/** Busca el aviso por su número (el escaneado o escrito, sin distinguir mayúsculas); también acepta solo los dígitos finales si es una sola coincidencia. */
export function findExitNoticeByNumber(warehousePublicId: string, code: string): LocalExitNotice | null {
  const wanted = code.trim()
  if (!wanted) return null
  const db = getDb()
  const exact = db.getFirstSync<NoticeRow>(`${SELECT_NOTICE} WHERE o.warehouse_public_id = ? AND o.number = ? COLLATE NOCASE LIMIT 1`, [warehousePublicId, wanted])
  if (exact) return mapNotice(exact)
  const tail = db.getAllSync<NoticeRow>(`${SELECT_NOTICE} WHERE o.warehouse_public_id = ? AND o.number LIKE ? ESCAPE '\\'`, [
    warehousePublicId,
    '%' + wanted.replace(/[\\%_]/g, (c) => '\\' + c),
  ])
  return tail.length === 1 ? mapNotice(tail[0]) : null
}

/** Las líneas del aviso con la forma de las de ventas (sin kit) para reutilizar las tareas guiadas. */
export function getExitNoticeLines(noticeId: number): LocalSalesLine[] {
  const db = getDb()
  const lines = db.getAllSync<{ id: number; product_public_id: string; sku: string | null; product_name: string | null; qty_ordered: number; qty_reserved: number; qty_shipped: number; qty_backordered: number }>(
    'SELECT * FROM exit_notice_line WHERE exit_notice_id = ? ORDER BY id',
    [noticeId],
  )
  return lines.map((l) => ({
    id: l.id,
    productPublicId: l.product_public_id,
    sku: l.sku ?? '',
    productName: l.product_name ?? '',
    qtyOrdered: l.qty_ordered,
    qtyReserved: l.qty_reserved,
    qtyShipped: l.qty_shipped,
    qtyBackordered: l.qty_backordered,
    isKit: false,
    components: [],
    reservations: db
      .getAllSync<{ bin_id: number; bin_code: string; lot_id: number | null; lot_number: string | null; quantity: number }>(
        'SELECT bin_id, bin_code, lot_id, lot_number, quantity FROM exit_notice_reservation WHERE exit_notice_line_id = ? ORDER BY id',
        [l.id],
      )
      .map((r) => ({ binId: r.bin_id, binCode: r.bin_code, lotId: r.lot_id, lotNumber: r.lot_number, quantity: r.quantity, productPublicId: null, sku: null, productName: null })),
  }))
}

export function deleteLocalExitNotice(db: SQLiteDatabase, id: number): void {
  db.runSync('DELETE FROM exit_notice_reservation WHERE exit_notice_line_id IN (SELECT id FROM exit_notice_line WHERE exit_notice_id = ?)', [id])
  db.runSync('DELETE FROM exit_notice_line WHERE exit_notice_id = ?', [id])
  db.runSync('DELETE FROM exit_notice WHERE id = ?', [id])
}

interface DispatchLine {
  lineId: number
  quantity: number
  binId?: number
  lotId?: number
}

/** Refleja un despacho en la copia local: baja lo reservado y sube lo despachado de la línea; quita la reserva usada (primero la de esa posición y lote). Sin nada reservado, borra el aviso. */
export function applyExitDispatchLocally(db: SQLiteDatabase, noticeId: number, lines: readonly DispatchLine[]): void {
  for (const dl of lines) {
    const line = db.getFirstSync<{ qty_reserved: number }>('SELECT qty_reserved FROM exit_notice_line WHERE id = ? AND exit_notice_id = ?', [dl.lineId, noticeId])
    if (!line) continue
    const take = Math.min(dl.quantity, line.qty_reserved)
    db.runSync('UPDATE exit_notice_line SET qty_reserved = qty_reserved - ?, qty_shipped = qty_shipped + ? WHERE id = ?', [take, take, dl.lineId])
    let left = take
    const rows = db.getAllSync<{ id: number; quantity: number }>(
      'SELECT id, quantity FROM exit_notice_reservation WHERE exit_notice_line_id = ? ORDER BY (bin_id = ? AND (lot_id IS ? OR ? IS NULL)) DESC, id',
      [dl.lineId, dl.binId ?? -1, dl.lotId ?? null, dl.lotId ?? null],
    )
    for (const r of rows) {
      if (left <= 0) break
      const used = Math.min(r.quantity, left)
      left = Math.round((left - used) * 1000) / 1000
      if (r.quantity - used <= 1e-9) db.runSync('DELETE FROM exit_notice_reservation WHERE id = ?', [r.id])
      else db.runSync('UPDATE exit_notice_reservation SET quantity = quantity - ? WHERE id = ?', [used, r.id])
    }
  }
  const open = db.getFirstSync<{ n: number | null }>('SELECT SUM(qty_reserved) AS n FROM exit_notice_line WHERE exit_notice_id = ?', [noticeId])
  if (!((open?.n ?? 0) > 0)) deleteLocalExitNotice(db, noticeId)
}

/** Después de bajar los avisos, vuelve a aplicar los despachos que siguen en la cola (el servidor todavía no los conoce y la copia que llegó los ignora). */
export function reapplyPendingExitDispatches(db: SQLiteDatabase): void {
  const rows = db.getAllSync<{ path: string; body: string }>("SELECT path, body FROM outbox WHERE kind = 'exitDispatch' AND status = 'pending' ORDER BY id")
  for (const row of rows) {
    const m = /\/exit-notices\/([^/]+)\/dispatch$/.exec(row.path)
    if (!m) continue
    const notice = db.getFirstSync<{ id: number }>('SELECT id FROM exit_notice WHERE public_id = ?', [m[1]])
    if (!notice) continue
    try {
      const body = JSON.parse(row.body) as { lines?: DispatchLine[] }
      applyExitDispatchLocally(db, notice.id, body.lines ?? [])
    } catch {
      // un cuerpo ilegible no detiene la bajada
    }
  }
  // Lote 178: lo mismo para las recolecciones maestras pendientes (cada documento lleva sus líneas)
  const groups = db.getAllSync<{ body: string }>("SELECT body FROM outbox WHERE kind = 'pickGroup' AND status = 'pending' ORDER BY id")
  for (const row of groups) {
    try {
      const body = JSON.parse(row.body) as { documents?: Array<{ kind?: string; publicId: string; lines?: DispatchLine[] }> }
      for (const d of body.documents ?? []) {
        if (d.kind === 'SALES_ORDER') continue   // Lote 186: las órdenes de la maestra se reaplican al bajar las órdenes (reapplyPendingSalesDispatches)
        const notice = db.getFirstSync<{ id: number }>('SELECT id FROM exit_notice WHERE public_id = ?', [d.publicId])
        if (notice) applyExitDispatchLocally(db, notice.id, d.lines ?? [])
      }
    } catch {
      // un cuerpo ilegible no detiene la bajada
    }
  }
}

/**
 * Encola el despacho del aviso y lo refleja de inmediato en el aviso y en los saldos locales, todo en una transacción.
 * Devuelve el id de la cola (para `flushNow`).
 */
export function queueExitDispatch(notice: LocalExitNotice, tasks: readonly SalesTask[], picks: readonly SalesPick[]): number {
  const lines = buildDispatchLines(tasks, picks)
  const db = getDb()
  let id = 0
  db.withTransactionSync(() => {
    const issue = lines.map((l) => {
      const task = tasks.find((t) => t.lineId === l.lineId)
      return { productPublicId: task?.productPublicId ?? '', quantity: l.quantity, binId: l.binId ?? 0, lotId: l.lotId ?? null }
    })
    const projection = issueDeltas({ warehousePublicId: notice.warehousePublicId, lines: issue }, db)
    id = enqueue({ kind: 'exitDispatch', path: `/api/v1/exit-notices/${notice.publicId}/dispatch`, body: { lines }, projection })
    applyDeltasIn(db, projection, 1)
    applyExitDispatchLocally(db, notice.id, lines)
  })
  return id
}

/** Lote 186: las órdenes de venta del almacén con algo reservado, con la forma de un aviso para elegirlas junto a los avisos en «Recolectar juntos». */
export function listGroupableOrders(warehousePublicId: string): LocalExitNotice[] {
  return listSalesOrders(warehousePublicId).map((o) => ({
    id: o.id,
    publicId: o.publicId,
    number: o.number,
    warehousePublicId: o.warehousePublicId,
    clientName: o.clientName,
    reference: null,
    destination: o.consigneeName,
    statusCode: o.statusCode,
    requiredDate: o.requiredDate,
    reserved: o.reserved,
    kind: 'SALES_ORDER' as const,
  }))
}

/**
 * Las tareas guiadas de varios documentos juntas (en el orden elegido) y a qué documento pertenece cada línea (por `docKey`). Las líneas y las tareas de una orden de venta van con su id de línea en
 * negativo para no chocar con las de un aviso (`realLineId` lo devuelve al del servidor).
 */
export function getGroupTasks(notices: readonly LocalExitNotice[]): { tasks: SalesTask[]; lineNotice: Map<number, number> } {
  const tasks: SalesTask[] = []
  const lineNotice = new Map<number, number>()
  for (const n of notices) {
    if (n.kind === 'SALES_ORDER') {
      const lines = getSalesOrderLines(n.id)
      for (const l of lines) lineNotice.set(-l.id, docKey(n))
      tasks.push(...buildTasks(lines).map((t) => ({ ...t, lineId: -t.lineId, key: `S${t.key}` })))
      continue
    }
    const lines = getExitNoticeLines(n.id)
    for (const l of lines) lineNotice.set(l.id, docKey(n))
    tasks.push(...buildTasks(lines))
  }
  return { tasks, lineNotice }
}

/**
 * Lote 178 — encola la recolección maestra de varios documentos (POST /pick-batches/groups) y la refleja de inmediato en cada uno y en los saldos locales, todo en una transacción. Devuelve el id
 * de la cola (para `flushNow`) o null si no alcanzan dos documentos con algo recogido. Si el servidor la rechaza, los saldos se deshacen solos (runOutbox).
 * Lote 186: los documentos pueden ser avisos de salida u órdenes de venta (una línea de kit sale de lo reservado de sus componentes).
 */
export function queueExitGroup(notices: readonly LocalExitNotice[], tasks: readonly SalesTask[], picks: readonly SalesPick[], lineNotice: ReadonlyMap<number, number>, note?: string | null): number | null {
  if (notices.length === 0) return null
  const warehousePublicId = notices[0].warehousePublicId
  const body = buildGroupBody(notices as readonly GroupableNotice[], tasks, picks, lineNotice, warehousePublicId)
  if (body.documents.length < 2) return null
  const db = getDb()
  let id = 0
  db.withTransactionSync(() => {
    const issue: Array<{ productPublicId: string; quantity: number; binId: number; lotId: number | null }> = []
    for (const l of buildDispatchLines(tasks, picks)) {
      if (l.binId === undefined) {
        // línea de kit: sale de lo reservado de sus componentes
        for (const c of kitConsumption(db, realLineId(l.lineId), l.quantity)) issue.push({ productPublicId: c.productPublicId, quantity: c.quantity, binId: c.binId, lotId: c.lotId })
        continue
      }
      issue.push({ productPublicId: tasks.find((t) => t.lineId === l.lineId)?.productPublicId ?? '', quantity: l.quantity, binId: l.binId, lotId: l.lotId ?? null })
    }
    const projection = issueDeltas({ warehousePublicId, lines: issue }, db)
    id = enqueue({ kind: 'pickGroup', body: { documents: body.documents, note: note?.trim() || null }, projection })
    applyDeltasIn(db, projection, 1)
    for (const d of body.documents) {
      const doc = notices.find((n) => n.publicId === d.publicId)
      if (!doc) continue
      if (d.kind === 'SALES_ORDER') applyDispatchLocally(db, doc.id, d.lines)
      else applyExitDispatchLocally(db, doc.id, d.lines)
    }
  })
  return id
}
