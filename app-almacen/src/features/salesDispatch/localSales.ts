// Lote 65 (P4, S2) — la copia local de las órdenes de venta por despachar (schema v14/v15: sales_order, sales_order_line, sales_order_reservation, sales_order_component) y el envío del despacho
// por la cola de salida. Lo que se despacha se refleja AL INSTANTE en la copia (reservado/despachado de la orden) y en los saldos (existencia de la posición); si el servidor lo rechaza, los saldos se
// deshacen solos (runOutbox) y la orden se vuelve a bajar completa en la siguiente sincronización.
// Lote 71 (kits): una línea de kit se despacha en kits (sin posición ni lote); lo que sale de cada posición lo deciden las reservas de sus componentes, igual que en el servidor.
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
  const lines = db.getAllSync<{ id: number; product_public_id: string; sku: string | null; product_name: string | null; qty_ordered: number; qty_reserved: number; qty_shipped: number; qty_backordered: number; is_kit: number }>(
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
    isKit: l.is_kit === 1,
    components: db
      .getAllSync<{ product_public_id: string; sku: string | null; product_name: string | null; qty_per_kit: number }>(
        'SELECT product_public_id, sku, product_name, qty_per_kit FROM sales_order_component WHERE sales_order_line_id = ? ORDER BY id',
        [l.id],
      )
      .map((c) => ({ productPublicId: c.product_public_id, sku: c.sku ?? '', name: c.product_name ?? '', qtyPerKit: c.qty_per_kit })),
    reservations: db
      .getAllSync<{ bin_id: number; bin_code: string; lot_id: number | null; lot_number: string | null; quantity: number; product_public_id: string | null; sku: string | null; product_name: string | null }>(
        'SELECT bin_id, bin_code, lot_id, lot_number, quantity, product_public_id, sku, product_name FROM sales_order_reservation WHERE sales_order_line_id = ? ORDER BY id',
        [l.id],
      )
      .map((r) => ({ binId: r.bin_id, binCode: r.bin_code, lotId: r.lot_id, lotNumber: r.lot_number, quantity: r.quantity, productPublicId: r.product_public_id, sku: r.sku, productName: r.product_name })),
  }))
}

export function deleteLocalSalesOrder(db: SQLiteDatabase, id: number): void {
  db.runSync('DELETE FROM sales_order_reservation WHERE sales_order_line_id IN (SELECT id FROM sales_order_line WHERE sales_order_id = ?)', [id])
  db.runSync('DELETE FROM sales_order_component WHERE sales_order_line_id IN (SELECT id FROM sales_order_line WHERE sales_order_id = ?)', [id])
  db.runSync('DELETE FROM sales_order_line WHERE sales_order_id = ?', [id])
  db.runSync('DELETE FROM sales_order WHERE id = ?', [id])
}

interface DispatchLine {
  lineId: number
  quantity: number
  /** Ausentes en una línea de kit (se despacha de lo reservado de sus componentes). */
  binId?: number
  lotId?: number
}

interface Consumption {
  productPublicId: string
  binId: number
  lotId: number | null
  quantity: number
}

/**
 * Lo que sale de las reservas de una línea de KIT al despachar `kits`: de cada componente (cantidad por kit × kits), las filas de reserva en el orden en que las dejó el servidor
 * (el mismo orden con el que el servidor consume). No modifica nada.
 */
export function kitConsumption(db: SQLiteDatabase, lineId: number, kits: number): Consumption[] {
  const out: Consumption[] = []
  const comps = db.getAllSync<{ product_public_id: string; qty_per_kit: number }>('SELECT product_public_id, qty_per_kit FROM sales_order_component WHERE sales_order_line_id = ? ORDER BY id', [lineId])
  for (const c of comps) {
    let left = kits * c.qty_per_kit
    const rows = db.getAllSync<{ bin_id: number; lot_id: number | null; quantity: number }>(
      'SELECT bin_id, lot_id, quantity FROM sales_order_reservation WHERE sales_order_line_id = ? AND product_public_id = ? ORDER BY id',
      [lineId, c.product_public_id],
    )
    for (const r of rows) {
      if (left <= 1e-9) break
      const take = Math.min(r.quantity, left)
      out.push({ productPublicId: c.product_public_id, binId: r.bin_id, lotId: r.lot_id, quantity: take })
      left = Math.round((left - take) * 1000) / 1000
    }
  }
  return out
}

function isKitLine(db: SQLiteDatabase, lineId: number): boolean {
  return db.getFirstSync<{ is_kit: number }>('SELECT is_kit FROM sales_order_line WHERE id = ?', [lineId])?.is_kit === 1
}

/** Refleja un despacho en la copia local: baja lo reservado y sube lo despachado de la línea; quita la reserva usada (primero la de esa posición y lote; en un kit, la de sus componentes). */
export function applyDispatchLocally(db: SQLiteDatabase, orderId: number, lines: readonly DispatchLine[]): void {
  for (const dl of lines) {
    const line = db.getFirstSync<{ qty_reserved: number }>('SELECT qty_reserved FROM sales_order_line WHERE id = ? AND sales_order_id = ?', [dl.lineId, orderId])
    if (!line) continue
    const take = Math.min(dl.quantity, line.qty_reserved)
    if (isKitLine(db, dl.lineId)) {
      // primero lo que sale de los componentes (con las reservas todavía intactas), luego se descuentan
      for (const c of kitConsumption(db, dl.lineId, take)) {
        const rows = db.getAllSync<{ id: number; quantity: number }>(
          'SELECT id, quantity FROM sales_order_reservation WHERE sales_order_line_id = ? AND product_public_id = ? AND bin_id = ? AND (lot_id IS ? OR (lot_id IS NULL AND ? IS NULL)) ORDER BY id',
          [dl.lineId, c.productPublicId, c.binId, c.lotId, c.lotId],
        )
        let left = c.quantity
        for (const r of rows) {
          if (left <= 1e-9) break
          const used = Math.min(r.quantity, left)
          left = Math.round((left - used) * 1000) / 1000
          if (r.quantity - used <= 1e-9) db.runSync('DELETE FROM sales_order_reservation WHERE id = ?', [r.id])
          else db.runSync('UPDATE sales_order_reservation SET quantity = quantity - ? WHERE id = ?', [used, r.id])
        }
      }
      db.runSync('UPDATE sales_order_line SET qty_reserved = qty_reserved - ?, qty_shipped = qty_shipped + ? WHERE id = ?', [take, take, dl.lineId])
      continue
    }
    db.runSync('UPDATE sales_order_line SET qty_reserved = qty_reserved - ?, qty_shipped = qty_shipped + ? WHERE id = ?', [take, take, dl.lineId])
    let left = take
    const rows = db.getAllSync<{ id: number; bin_id: number; lot_id: number | null; quantity: number }>(
      'SELECT id, bin_id, lot_id, quantity FROM sales_order_reservation WHERE sales_order_line_id = ? ORDER BY (bin_id = ? AND (lot_id IS ? OR ? IS NULL)) DESC, id',
      [dl.lineId, dl.binId ?? -1, dl.lotId ?? null, dl.lotId ?? null],
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
  // Lote 186: lo mismo para las órdenes que van dentro de una recolección maestra pendiente
  const groups = db.getAllSync<{ body: string }>("SELECT body FROM outbox WHERE kind = 'pickGroup' AND status = 'pending' ORDER BY id")
  for (const row of groups) {
    try {
      const body = JSON.parse(row.body) as { documents?: Array<{ kind?: string; publicId: string; lines?: DispatchLine[] }> }
      for (const d of body.documents ?? []) {
        if (d.kind !== 'SALES_ORDER') continue
        const order = db.getFirstSync<{ id: number }>('SELECT id FROM sales_order WHERE public_id = ?', [d.publicId])
        if (order) applyDispatchLocally(db, order.id, d.lines ?? [])
      }
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
    // lo que sale de cada posición: producto normal = la posición leída; kit = lo reservado de sus componentes
    const issue: Array<{ productPublicId: string; quantity: number; binId: number; lotId: number | null }> = []
    for (const l of lines) {
      if (l.binId === undefined) {
        for (const c of kitConsumption(db, l.lineId, l.quantity)) issue.push({ productPublicId: c.productPublicId, quantity: c.quantity, binId: c.binId, lotId: c.lotId })
        continue
      }
      const task = tasks.find((t) => t.lineId === l.lineId)
      issue.push({ productPublicId: task?.productPublicId ?? '', quantity: l.quantity, binId: l.binId, lotId: l.lotId ?? null })
    }
    const projection = issueDeltas({ warehousePublicId: order.warehousePublicId, lines: issue }, db)
    id = enqueue({ kind: 'salesDispatch', path: `/api/v1/sales-orders/${order.publicId}/dispatch`, body, projection })
    applyDeltasIn(db, projection, 1)
    applyDispatchLocally(db, order.id, lines)
  })
  return id
}
