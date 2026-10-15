// Lote 65 (P4, S2) — la copia local de las órdenes de venta por despachar (schema v14: sales_order, sales_order_line, sales_order_reservation) y el envío del despacho
// por la cola de salida. Lo que se despacha se refleja AL INSTANTE en la copia (reservado/despachado de la orden) y en los saldos (existencia de la posición); si el
// servidor lo rechaza, los saldos se deshacen solos (runOutbox) y la orden se vuelve a bajar completa en la siguiente sincronización.
import { getDb, type SQLiteDatabase } from '../../kernel/db/database'
import { enqueue } from '../../kernel/sync/outbox'
import { applyDeltasIn, issueDeltas } from '../../kernel/warehouse/balanceProjection'
import { buildDispatchBody, buildDispatchLines, type LocalSalesLine, type SalesPick, type SalesTask } from './salesDispatchLogic'

export interface LocalSalesOrder {
  id: number
  publicId: string
  number: string
  warehousePublicId: string
  clientName: string
  consigneeName: string | null
  statusCode: string | null
  requiredDate: string | null
  /** Lo reservado que se puede despachar ya. */
  reserved: number
}

interface OrderRow {
  id: number
  public_id: string
  number: string
  warehouse_public_id: string
  client_name: string | null
  consignee_name: string | null
  status_code: string | null
  required_date: string | null
  reserved: number | null
}

function mapOrder(r: OrderRow): LocalSalesOrder {
  return {
    id: r.id,
    publicId: r.public_id,
    number: r.number,
    warehousePublicId: r.warehouse_public_id,
    clientName: r.client_name ?? '',
    consigneeName: r.consignee_name,
    statusCode: r.status_code,
    requiredDate: r.required_date,
    reserved: r.reserved ?? 0,
  }
}

const SELECT_ORDER = `SELECT o.*, (SELECT SUM(l.qty_reserved) FROM sales_order_line l WHERE l.sales_order_id = o.id) AS reserved FROM sales_order o`

/** Órdenes del almacén con algo reservado que se pueda despachar, las más antiguas primero (la fecha requerida manda). */
export function listSalesOrders(warehousePublicId: string): LocalSalesOrder[] {
  return getDb()
    .getAllSync<OrderRow>(`${SELECT_ORDER} WHERE o.warehouse_public_id = ? ORDER BY (o.required_date IS NULL), o.required_date, o.id`, [warehousePublicId])
    .map(mapOrder)
    .filter((o) => o.reserved > 0)
}

/** Busca la orden por su número (el escaneado o escrito, sin distinguir mayúsculas); también acepta solo los dígitos finales si es una sola coincidencia. */
export function findSalesOrderByNumber(warehousePublicId: string, code: string): LocalSalesOrder | null {
  const wanted = code.trim()
  if (!wanted) return null
  const db = getDb()
  const exact = db.getFirstSync<OrderRow>(`${SELECT_ORDER} WHERE o.warehouse_public_id = ? AND o.number = ? COLLATE NOCASE LIMIT 1`, [warehousePublicId, wanted])
  if (exact) return mapOrder(exact)
  const tail = db.getAllSync<OrderRow>(`${SELECT_ORDER} WHERE o.warehouse_public_id = ? AND o.number LIKE ? ESCAPE '\\'`, [
    warehousePublicId,
    '%' + wanted.replace(/[\\%_]/g, (c) => '\\' + c),
  ])
  return tail.length === 1 ? mapOrder(tail[0]) : null
}

export function getSalesOrderLines(orderId: number): LocalSalesLine[] {
  const db = getDb()
  const lines = db.getAllSync<{ id: number; product_public_id: string; sku: string | null; product_name: string | null; qty_ordered: number; qty_reserved: number; qty_shipped: number; qty_backordered: number }>(
    'SELECT * FROM sales_order_line WHERE sales_order_id = ? ORDER BY id',
    [orderId],
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
    reservations: db
      .getAllSync<{ bin_id: number; bin_code: string; lot_id: number | null; lot_number: string | null; quantity: number }>(
        'SELECT bin_id, bin_code, lot_id, lot_number, quantity FROM sales_order_reservation WHERE sales_order_line_id = ? ORDER BY id',
        [l.id],
      )
      .map((r) => ({ binId: r.bin_id, binCode: r.bin_code, lotId: r.lot_id, lotNumber: r.lot_number, quantity: r.quantity })),
  }))
}

export function deleteLocalSalesOrder(db: SQLiteDatabase, id: number): void {
  db.runSync('DELETE FROM sales_order_reservation WHERE sales_order_line_id IN (SELECT id FROM sales_order_line WHERE sales_order_id = ?)', [id])
  db.runSync('DELETE FROM sales_order_line WHERE sales_order_id = ?', [id])
  db.runSync('DELETE FROM sales_order WHERE id = ?', [id])
}

interface DispatchLine {
  lineId: number
  quantity: number
  binId: number
  lotId?: number
}

/** Refleja un despacho en la copia local: baja lo reservado y sube lo despachado de la línea; quita la reserva usada (primero la de esa posición y lote). */
export function applyDispatchLocally(db: SQLiteDatabase, orderId: number, lines: readonly DispatchLine[]): void {
  for (const dl of lines) {
    const line = db.getFirstSync<{ qty_reserved: number }>('SELECT qty_reserved FROM sales_order_line WHERE id = ? AND sales_order_id = ?', [dl.lineId, orderId])
    if (!line) continue
    const take = Math.min(dl.quantity, line.qty_reserved)
    db.runSync('UPDATE sales_order_line SET qty_reserved = qty_reserved - ?, qty_shipped = qty_shipped + ? WHERE id = ?', [take, take, dl.lineId])
    let left = take
    const rows = db.getAllSync<{ id: number; bin_id: number; lot_id: number | null; quantity: number }>(
      'SELECT id, bin_id, lot_id, quantity FROM sales_order_reservation WHERE sales_order_line_id = ? ORDER BY (bin_id = ? AND (lot_id IS ? OR ? IS NULL)) DESC, id',
      [dl.lineId, dl.binId, dl.lotId ?? null, dl.lotId ?? null],
    )
    for (const r of rows) {
      if (left <= 0) break
      const used = Math.min(r.quantity, left)
      left = Math.round((left - used) * 1000) / 1000
      if (r.quantity - used <= 1e-9) db.runSync('DELETE FROM sales_order_reservation WHERE id = ?', [r.id])
      else db.runSync('UPDATE sales_order_reservation SET quantity = quantity - ? WHERE id = ?', [used, r.id])
    }
  }
  const open = db.getFirstSync<{ n: number | null }>('SELECT SUM(qty_reserved) AS n FROM sales_order_line WHERE sales_order_id = ?', [orderId])
  if (!((open?.n ?? 0) > 0)) deleteLocalSalesOrder(db, orderId)
}

/** Después de bajar las órdenes, vuelve a aplicar los despachos que siguen en la cola (el servidor todavía no los conoce y la copia que llegó los ignora). */
export function reapplyPendingSalesDispatches(db: SQLiteDatabase): void {
  const rows = db.getAllSync<{ path: string; body: string }>("SELECT path, body FROM outbox WHERE kind = 'salesDispatch' AND status = 'pending' ORDER BY id")
  for (const row of rows) {
    const m = /\/sales-orders\/([^/]+)\/dispatch$/.exec(row.path)
    if (!m) continue
    const order = db.getFirstSync<{ id: number }>('SELECT id FROM sales_order WHERE public_id = ?', [m[1]])
    if (!order) continue
    try {
      const body = JSON.parse(row.body) as { lines?: DispatchLine[] }
      applyDispatchLocally(db, order.id, body.lines ?? [])
    } catch {
      // un cuerpo ilegible no detiene la bajada
    }
  }
}

/**
 * Encola el despacho de la orden (con la entrega si se pidió) y lo refleja de inmediato en la orden y en los saldos locales, todo en una transacción.
 * Devuelve el id de la cola (para `flushNow`).
 */
export function queueSalesDispatch(order: LocalSalesOrder, tasks: readonly SalesTask[], picks: readonly SalesPick[], delivery: { packages: number } | null): number {
  const body = buildDispatchBody(tasks, picks, delivery)
  const db = getDb()
  let id = 0
  db.withTransactionSync(() => {
    const lines = buildDispatchLines(tasks, picks)
    const projection = issueDeltas(
      {
        warehousePublicId: order.warehousePublicId,
        lines: lines.map((l) => {
          const task = tasks.find((t) => t.lineId === l.lineId)
          return { productPublicId: task?.productPublicId ?? '', quantity: l.quantity, binId: l.binId, lotId: l.lotId ?? null }
        }),
      },
      db,
    )
    id = enqueue({ kind: 'salesDispatch', path: `/api/v1/sales-orders/${order.publicId}/dispatch`, body, projection })
    applyDeltasIn(db, projection, 1)
    applyDispatchLocally(db, order.id, lines)
  })
  return id
}
