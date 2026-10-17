// Lote 83 (P6) — la copia local de las devoluciones de venta por recibir (schema v16: sales_return, sales_return_line) y el envío de la recepción por la cola de salida. Lo que se recibe se refleja AL
// INSTANTE en la copia (baja lo pendiente; sin nada pendiente la devolución sale de la lista); si el servidor lo rechaza, la devolución se vuelve a bajar completa en la siguiente sincronización.
// A diferencia del despacho, los saldos de posiciones NO se proyectan: el servidor decide dónde entra lo recibido (zona de recepción con tareas de acomodo, o directo) y la existencia llega con la
// siguiente bajada de saldos.
import { getDb, type SQLiteDatabase } from '../../kernel/db/database'
import { enqueue } from '../../kernel/sync/outbox'
import { buildReceiveBody, type LineEntry, type ReturnLine } from './returnReceiveLogic'

export interface LocalReturn {
  id: number
  publicId: string
  number: string
  warehousePublicId: string
  clientName: string
  orderNumber: string
  statusCode: string | null
  note: string | null
  /** Lo que falta por recibir en total. */
  pending: number
}

interface ReturnRow {
  id: number
  public_id: string
  number: string
  warehouse_public_id: string
  client_name: string | null
  order_number: string | null
  status_code: string | null
  note: string | null
  pending: number | null
}

function mapReturn(r: ReturnRow): LocalReturn {
  return {
    id: r.id,
    publicId: r.public_id,
    number: r.number,
    warehousePublicId: r.warehouse_public_id,
    clientName: r.client_name ?? '',
    orderNumber: r.order_number ?? '',
    statusCode: r.status_code,
    note: r.note,
    pending: r.pending ?? 0,
  }
}

const SELECT_RETURN = `SELECT r.*, (SELECT SUM(MAX(l.qty_requested - l.qty_received, 0)) FROM sales_return_line l WHERE l.sales_return_id = r.id) AS pending FROM sales_return r`

/** Devoluciones del almacén con algo por recibir, las más antiguas primero. */
export function listSalesReturns(warehousePublicId: string): LocalReturn[] {
  return getDb()
    .getAllSync<ReturnRow>(`${SELECT_RETURN} WHERE r.warehouse_public_id = ? ORDER BY r.requested_at_utc, r.id`, [warehousePublicId])
    .map(mapReturn)
    .filter((r) => r.pending > 0)
}

/** Busca la devolución por su número (el escaneado o escrito, sin distinguir mayúsculas); también acepta solo los dígitos finales si es una sola coincidencia. */
export function findSalesReturnByNumber(warehousePublicId: string, code: string): LocalReturn | null {
  const wanted = code.trim()
  if (!wanted) return null
  const db = getDb()
  const exact = db.getFirstSync<ReturnRow>(`${SELECT_RETURN} WHERE r.warehouse_public_id = ? AND r.number = ? COLLATE NOCASE LIMIT 1`, [warehousePublicId, wanted])
  if (exact) return mapReturn(exact)
  const tail = db.getAllSync<ReturnRow>(`${SELECT_RETURN} WHERE r.warehouse_public_id = ? AND r.number LIKE ? ESCAPE '\\'`, [
    warehousePublicId,
    '%' + wanted.replace(/[\\%_]/g, (c) => '\\' + c),
  ])
  return tail.length === 1 ? mapReturn(tail[0]) : null
}

/** Las líneas de la devolución que todavía tienen algo pendiente. */
export function getSalesReturnLines(returnId: number): ReturnLine[] {
  return getDb()
    .getAllSync<{ id: number; product_public_id: string; sku: string | null; product_name: string | null; tracking_type_code: string | null; qty_requested: number; qty_received: number; qty_damaged: number }>(
      'SELECT * FROM sales_return_line WHERE sales_return_id = ? ORDER BY id',
      [returnId],
    )
    .map((l) => ({
      id: l.id,
      productPublicId: l.product_public_id,
      sku: l.sku ?? '',
      productName: l.product_name ?? '',
      trackingTypeCode: l.tracking_type_code ?? 'NONE',
      qtyRequested: l.qty_requested,
      qtyReceived: l.qty_received,
      qtyDamaged: l.qty_damaged,
      pending: Math.max(l.qty_requested - l.qty_received, 0),
    }))
    .filter((l) => l.pending > 0)
}

export function deleteLocalSalesReturn(db: SQLiteDatabase, id: number): void {
  db.runSync('DELETE FROM sales_return_line WHERE sales_return_id = ?', [id])
  db.runSync('DELETE FROM sales_return WHERE id = ?', [id])
}

interface ReceivedLine {
  lineId: number
  goodQty?: number
  damagedQty?: number
}

/** Refleja una recepción en la copia local: sube lo recibido y lo dañado de cada línea; con `closeRest` lo que no llegó deja de estar pendiente; sin nada pendiente la devolución se borra. */
export function applyReceiveLocally(db: SQLiteDatabase, returnId: number, lines: readonly ReceivedLine[], closeRest: boolean): void {
  for (const rl of lines) {
    const good = rl.goodQty ?? 0
    const damaged = rl.damagedQty ?? 0
    db.runSync('UPDATE sales_return_line SET qty_received = qty_received + ?, qty_damaged = qty_damaged + ? WHERE id = ? AND sales_return_id = ?', [good + damaged, damaged, rl.lineId, returnId])
  }
  if (closeRest) db.runSync('UPDATE sales_return_line SET qty_requested = qty_received WHERE sales_return_id = ?', [returnId])
  const open = db.getFirstSync<{ n: number | null }>('SELECT SUM(MAX(qty_requested - qty_received, 0)) AS n FROM sales_return_line WHERE sales_return_id = ?', [returnId])
  if (!((open?.n ?? 0) > 1e-9)) deleteLocalSalesReturn(db, returnId)
}

/** Después de bajar las devoluciones, vuelve a aplicar las recepciones que siguen en la cola (el servidor todavía no las conoce y la copia que llegó las ignora). */
export function reapplyPendingReturnReceipts(db: SQLiteDatabase): void {
  const rows = db.getAllSync<{ path: string; body: string }>("SELECT path, body FROM outbox WHERE kind = 'salesReturnReceive' AND status = 'pending' ORDER BY id")
  for (const row of rows) {
    const m = /\/sales-returns\/([^/]+)\/receive$/.exec(row.path)
    if (!m) continue
    const ret = db.getFirstSync<{ id: number }>('SELECT id FROM sales_return WHERE public_id = ?', [m[1]])
    if (!ret) continue
    try {
      const body = JSON.parse(row.body) as { lines?: ReceivedLine[]; closeRest?: boolean }
      applyReceiveLocally(db, ret.id, body.lines ?? [], body.closeRest === true)
    } catch {
      // un cuerpo ilegible no detiene la bajada
    }
  }
}

/** Encola la recepción de la devolución y la refleja de inmediato en la copia local, todo en una transacción. Devuelve el id de la cola (para `flushNow`). */
export function queueReturnReceive(ret: LocalReturn, lines: readonly ReturnLine[], entries: Readonly<Record<number, LineEntry>>, closeRest: boolean): number {
  const body = buildReceiveBody(lines, entries, closeRest)
  const db = getDb()
  let id = 0
  db.withTransactionSync(() => {
    id = enqueue({ kind: 'salesReturnReceive', path: `/api/v1/sales-returns/${ret.publicId}/receive`, body })
    applyReceiveLocally(db, ret.id, body.lines, closeRest)
  })
  return id
}
