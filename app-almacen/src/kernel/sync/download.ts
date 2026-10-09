// Lote 8A-app — bajada por diferencia (docs/mobile/app-almacen-plan.md §1.1): un recurso a la vez, `since` = el
// `serverTimeUtc` de la PRIMERA página de la pasada anterior menos 5 minutos (margen de reloj entre aparato y
// servidor), se sigue `nextCursor` hasta que llega null. `take` fijo en 500 (el máximo que acepta el API).
// Solo los recursos que usa Recibir en esta entrega: productos, órdenes de compra y avisos (con sus líneas). Almacenes,
// tareas y categorías se agregan con Acomodar/Conteo (próxima entrega); las tablas locales ya existen (schema.ts).
// Lote 16: también las posiciones del almacén del aparato (GET /sync/bins, tabla `bin`), para el recibo directo a posición.
import { api, ApiError, unwrap } from '../api/client'
import { getActiveWarehousePublicId } from '../warehouse/activeWarehouse'
import { getDb, type SQLiteDatabase } from '../db/database'
import { mapExitRow, replaceStockExit, type StockExitRow } from '../../features/dispatch/stockExit'

const TAKE = 500

interface SyncPage<T> {
  items: T[]
  nextCursor: string | null
  serverTimeUtc: string
}

export interface DownloadResult {
  resource: string
  pages: number
  items: number
}

function subtractMinutes(isoUtc: string, minutes: number): string {
  const d = new Date(isoUtc)
  d.setUTCMinutes(d.getUTCMinutes() - minutes)
  return d.toISOString()
}

function watermarkOf(db: SQLiteDatabase, resource: string): string | undefined {
  const row = db.getFirstSync<{ since_utc: string | null }>('SELECT since_utc FROM sync_watermark WHERE resource = ?', [
    resource,
  ])
  return row?.since_utc ?? undefined
}

function saveWatermark(db: SQLiteDatabase, resource: string, firstServerTimeUtc: string): void {
  const since = subtractMinutes(firstServerTimeUtc, 5)
  db.runSync(
    `INSERT INTO sync_watermark (resource, since_utc, last_run_utc) VALUES (?, ?, ?)
     ON CONFLICT(resource) DO UPDATE SET since_utc = excluded.since_utc, last_run_utc = excluded.last_run_utc`,
    [resource, since, new Date().toISOString()],
  )
}

/** Baja todas las páginas de un recurso y aplica cada una (upsert/borrado) dentro de una transacción por página. */
async function runDiffDownload<T>(
  resource: string,
  fetchPage: (since: string | undefined, cursor: string | undefined) => Promise<SyncPage<T>>,
  apply: (db: SQLiteDatabase, items: T[]) => void,
): Promise<DownloadResult> {
  const db = getDb()
  const since = watermarkOf(db, resource)
  let cursor: string | undefined
  let firstServerTime: string | null = null
  let pages = 0
  let itemCount = 0
  for (;;) {
    const page = await fetchPage(since, cursor)
    firstServerTime ??= page.serverTimeUtc
    db.withTransactionSync(() => apply(db, page.items))
    itemCount += page.items.length
    pages += 1
    cursor = page.nextCursor ?? undefined
    if (!cursor) break
  }
  if (firstServerTime) saveWatermark(db, resource, firstServerTime)
  return { resource, pages, items: itemCount }
}

function applyProducts(db: SQLiteDatabase, items: Array<{
  id?: number
  publicId?: string
  sku?: string | null
  name?: string | null
  barcode?: string | null
  trackingTypeCode?: string | null
  baseUomCode?: string | null
  categoryId?: number | null
  ownerClientPublicId?: string | null
  ownerName?: string | null
  preferredBinId?: number | null
  isActive?: boolean
  packUomCode?: string | null
  packUomName?: string | null
  packQty?: number | null
}>): void {
  for (const p of items) {
    if (p.id == null) continue
    if (p.isActive === false) {
      db.runSync('DELETE FROM product WHERE id = ?', [p.id])
      continue
    }
    db.runSync(
      `INSERT INTO product (id, public_id, sku, name, barcode, tracking_type_code, base_uom_code, category_id,
                             owner_client_public_id, owner_name, preferred_bin_id, is_active, pack_uom_code, pack_uom_name, pack_qty)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?)
       ON CONFLICT(id) DO UPDATE SET public_id = excluded.public_id, sku = excluded.sku, name = excluded.name,
         barcode = excluded.barcode, tracking_type_code = excluded.tracking_type_code, base_uom_code = excluded.base_uom_code,
         category_id = excluded.category_id, owner_client_public_id = excluded.owner_client_public_id,
         owner_name = excluded.owner_name, preferred_bin_id = excluded.preferred_bin_id, is_active = 1,
         pack_uom_code = excluded.pack_uom_code, pack_uom_name = excluded.pack_uom_name, pack_qty = excluded.pack_qty`,
      [
        p.id,
        p.publicId ?? '',
        p.sku ?? null,
        p.name ?? '',
        p.barcode ?? null,
        p.trackingTypeCode ?? null,
        p.baseUomCode ?? null,
        p.categoryId ?? null,
        p.ownerClientPublicId ?? null,
        p.ownerName ?? null,
        p.preferredBinId ?? null,
        p.packUomCode ?? null,
        p.packUomName ?? null,
        p.packQty ?? null,
      ],
    )
  }
}

export async function downloadProducts(): Promise<DownloadResult> {
  return runDiffDownload(
    'products',
    async (since, cursor) => {
      const page = await unwrap(api.GET('/api/v1/sync/products', { params: { query: { since, cursor, take: TAKE } } }))
      return { items: page.items ?? [], nextCursor: page.nextCursor ?? null, serverTimeUtc: page.serverTimeUtc ?? new Date().toISOString() }
    },
    applyProducts,
  )
}

function applyPurchaseOrderLines(db: SQLiteDatabase, purchaseOrderId: number, lines: Array<{
  id?: number
  productPublicId?: string
  sku?: string | null
  productName?: string | null
  qtyOrdered?: number
  qtyReceived?: number
  qtyPending?: number
}> | null | undefined): void {
  db.runSync('DELETE FROM purchase_order_line WHERE purchase_order_id = ?', [purchaseOrderId])
  for (const l of lines ?? []) {
    db.runSync(
      `INSERT INTO purchase_order_line (id, purchase_order_id, product_public_id, sku, product_name, qty_ordered, qty_received, qty_pending)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
      [l.id ?? 0, purchaseOrderId, l.productPublicId ?? '', l.sku ?? null, l.productName ?? null, l.qtyOrdered ?? 0, l.qtyReceived ?? 0, l.qtyPending ?? 0],
    )
  }
}

function applyPurchaseOrders(db: SQLiteDatabase, items: Array<{
  id?: number
  publicId?: string
  number?: string | null
  warehousePublicId?: string
  supplierName?: string | null
  statusCode?: string | null
  expectedDate?: string | null
  isActive?: boolean
  lines?: Array<{ id?: number; productPublicId?: string; sku?: string | null; productName?: string | null; qtyOrdered?: number; qtyReceived?: number; qtyPending?: number }> | null
}>): void {
  for (const po of items) {
    if (po.id == null) continue
    if (po.isActive === false) {
      db.runSync('DELETE FROM purchase_order_line WHERE purchase_order_id = ?', [po.id])
      db.runSync('DELETE FROM purchase_order WHERE id = ?', [po.id])
      continue
    }
    db.runSync(
      `INSERT INTO purchase_order (id, public_id, number, warehouse_public_id, supplier_name, status_code, expected_date, is_active)
       VALUES (?, ?, ?, ?, ?, ?, ?, 1)
       ON CONFLICT(id) DO UPDATE SET public_id = excluded.public_id, number = excluded.number,
         warehouse_public_id = excluded.warehouse_public_id, supplier_name = excluded.supplier_name,
         status_code = excluded.status_code, expected_date = excluded.expected_date, is_active = 1`,
      [po.id, po.publicId ?? '', po.number ?? '', po.warehousePublicId ?? '', po.supplierName ?? null, po.statusCode ?? null, po.expectedDate ?? null],
    )
    applyPurchaseOrderLines(db, po.id, po.lines)
  }
}

export async function downloadPurchaseOrders(): Promise<DownloadResult> {
  return runDiffDownload(
    'purchaseOrders',
    async (since, cursor) => {
      const page = await unwrap(api.GET('/api/v1/sync/purchase-orders', { params: { query: { since, cursor, take: TAKE } } }))
      return { items: page.items ?? [], nextCursor: page.nextCursor ?? null, serverTimeUtc: page.serverTimeUtc ?? new Date().toISOString() }
    },
    applyPurchaseOrders,
  )
}

function applyAsnLines(db: SQLiteDatabase, asnId: number, lines: Array<{
  id?: number
  productPublicId?: string
  sku?: string | null
  productName?: string | null
  expectedQty?: number
  lotNumber?: string | null
  purchaseOrderLineId?: number | null
}> | null | undefined): void {
  db.runSync('DELETE FROM asn_line WHERE asn_id = ?', [asnId])
  for (const l of lines ?? []) {
    db.runSync(
      `INSERT INTO asn_line (id, asn_id, product_public_id, sku, product_name, expected_qty, lot_number, purchase_order_line_id)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
      [l.id ?? 0, asnId, l.productPublicId ?? '', l.sku ?? null, l.productName ?? null, l.expectedQty ?? 0, l.lotNumber ?? null, l.purchaseOrderLineId ?? null],
    )
  }
}

function applyAsns(db: SQLiteDatabase, items: Array<{
  id?: number
  warehousePublicId?: string
  clientPublicId?: string | null
  clientName?: string | null
  purchaseOrderPublicId?: string | null
  purchaseOrderNumber?: string | null
  reference?: string | null
  expectedDate?: string | null
  statusCode?: string | null
  isActive?: boolean
  lines?: Array<{ id?: number; productPublicId?: string; sku?: string | null; productName?: string | null; expectedQty?: number; lotNumber?: string | null; purchaseOrderLineId?: number | null }> | null
}>): void {
  for (const asn of items) {
    if (asn.id == null) continue
    if (asn.isActive === false) {
      db.runSync('DELETE FROM asn_line WHERE asn_id = ?', [asn.id])
      db.runSync('DELETE FROM asn WHERE id = ?', [asn.id])
      continue
    }
    db.runSync(
      `INSERT INTO asn (id, warehouse_public_id, client_public_id, client_name, purchase_order_public_id, purchase_order_number,
                         reference, expected_date, status_code, is_active)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 1)
       ON CONFLICT(id) DO UPDATE SET warehouse_public_id = excluded.warehouse_public_id, client_public_id = excluded.client_public_id,
         client_name = excluded.client_name, purchase_order_public_id = excluded.purchase_order_public_id,
         purchase_order_number = excluded.purchase_order_number, reference = excluded.reference,
         expected_date = excluded.expected_date, status_code = excluded.status_code, is_active = 1`,
      [
        asn.id,
        asn.warehousePublicId ?? '',
        asn.clientPublicId ?? null,
        asn.clientName ?? null,
        asn.purchaseOrderPublicId ?? null,
        asn.purchaseOrderNumber ?? null,
        asn.reference ?? null,
        asn.expectedDate ?? null,
        asn.statusCode ?? null,
      ],
    )
    applyAsnLines(db, asn.id, asn.lines)
  }
}

export async function downloadAsns(): Promise<DownloadResult> {
  return runDiffDownload(
    'asns',
    async (since, cursor) => {
      const page = await unwrap(api.GET('/api/v1/sync/asns', { params: { query: { since, cursor, take: TAKE } } }))
      return { items: page.items ?? [], nextCursor: page.nextCursor ?? null, serverTimeUtc: page.serverTimeUtc ?? new Date().toISOString() }
    },
    applyAsns,
  )
}

function applyBins(db: SQLiteDatabase, items: Array<{
  id?: number
  code?: string | null
  warehousePublicId?: string
  zoneId?: number
  zoneCode?: string | null
  zoneName?: string | null
  zoneTypeCode?: string | null
  aisle?: string | null
  rack?: string | null
  level?: string | null
  position?: string | null
  isActive?: boolean
  isProvisional?: boolean
}>): void {
  // A diferencia de productos, la posición inactiva se GUARDA con is_active = 0 (no se borra): así Recibir distingue
  // "no existe en este almacén" de "está desactivada" sin señal (receiveLogic.validateTargetBin).
  for (const b of items) {
    if (b.id == null) continue
    db.runSync(
      `INSERT INTO bin (id, code, warehouse_public_id, zone_id, zone_code, zone_name, zone_type_code, aisle, rack, level, position, is_active, is_provisional)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
       ON CONFLICT(id) DO UPDATE SET code = excluded.code, warehouse_public_id = excluded.warehouse_public_id,
         zone_id = excluded.zone_id, zone_code = excluded.zone_code, zone_name = excluded.zone_name,
         zone_type_code = excluded.zone_type_code, aisle = excluded.aisle, rack = excluded.rack, level = excluded.level,
         position = excluded.position, is_active = excluded.is_active, is_provisional = excluded.is_provisional`,
      [
        b.id,
        b.code ?? '',
        b.warehousePublicId ?? '',
        b.zoneId ?? null,
        b.zoneCode ?? null,
        b.zoneName ?? null,
        b.zoneTypeCode ?? null,
        b.aisle ?? null,
        b.rack ?? null,
        b.level ?? null,
        b.position ?? null,
        b.isActive === false ? 0 : 1,
        b.isProvisional === true ? 1 : 0,
      ],
    )
  }
}

/** Lote 16: posiciones (con el tipo de su zona) del almacén del aparato, para validar sin señal la posición destino del
 *  recibo directo. Marca de agua propia por almacén (`bins:{almacén}`): si cambia el almacén por defecto, el nuevo baja
 *  completo la primera vez. */
export async function downloadBins(warehousePublicId: string): Promise<DownloadResult> {
  const result = await runDiffDownload(
    `bins:${warehousePublicId}`,
    async (since, cursor) => {
      const page = await unwrap(
        api.GET('/api/v1/sync/bins', { params: { query: { warehousePublicId, since, cursor, take: TAKE } } }),
      )
      return { items: page.items ?? [], nextCursor: page.nextCursor ?? null, serverTimeUtc: page.serverTimeUtc ?? new Date().toISOString() }
    },
    applyBins,
  )
  return { ...result, resource: 'bins' }
}

/**
 * 2026-10-01: las órdenes de compra exigen `purchasing.view` y el módulo Compras. Un usuario sin ellos (p. ej. el Operador de
 * almacén desde que Luis le quitó los permisos de compras) recibe 403: eso NO es una falla de sincronización, solo no recibe
 * contra órdenes de compra. Antes el 403 abortaba toda la pasada ("No se pudo sincronizar") y los avisos y las posiciones, que
 * van después, nunca bajaban. Se salta el recurso y se borra lo que hubiera de antes (ya no tiene acceso).
 */
export async function downloadPurchaseOrdersIfAllowed(): Promise<DownloadResult> {
  try {
    return await downloadPurchaseOrders()
  } catch (err) {
    if (!(err instanceof ApiError) || err.status !== 403) throw err
    const db = getDb()
    db.withTransactionSync(() => {
      db.runSync('DELETE FROM purchase_order_line')
      db.runSync('DELETE FROM purchase_order')
      db.runSync('DELETE FROM sync_watermark WHERE resource = ?', ['purchaseOrders'])
    })
    return { resource: 'purchaseOrders', pages: 0, items: 0 }
  }
}

/** Cada cuánto se baja de nuevo todo el orden de salida (minutos) si no hubo movimientos propios que lo desactualicen: la foto es del almacén
 *  completo y la sincronización corre cada minuto. */
export const STOCK_EXIT_REFRESH_MINUTES = 5

function stockExitIsFresh(db: SQLiteDatabase, resource: string): boolean {
  const row = db.getFirstSync<{ last_run_utc: string | null }>('SELECT last_run_utc FROM sync_watermark WHERE resource = ?', [resource])
  if (!row?.last_run_utc) return false
  return Date.now() - new Date(row.last_run_utc).getTime() < STOCK_EXIT_REFRESH_MINUTES * 60_000
}

/**
 * Orden de salida del almacén del aparato (GET /inventory/exit-options, paginado de 500): foto completa que reemplaza la anterior, para
 * sugerir y exigir de dónde sale cada producto (FEFO) también sin señal. No se baja si la última pasada tiene menos de
 * STOCK_EXIT_REFRESH_MINUTES (salvo `force`: la última sincronización mandó movimientos del aparato). Sin permiso de inventario (403) se
 * salta (no es una falla de sincronización).
 */
export async function downloadStockExit(warehousePublicId: string, force = false): Promise<DownloadResult> {
  const db = getDb()
  const resource = `stockExit:${warehousePublicId}`
  if (!force && stockExitIsFresh(db, resource)) return { resource: 'stockExit', pages: 0, items: 0 }
  try {
    const items: Array<StockExitRow & { productPublicId: string }> = []
    let skip = 0
    let pages = 0
    let serverTime: string | null = null
    for (;;) {
      const page = await unwrap(api.GET('/api/v1/inventory/exit-options', { params: { query: { warehousePublicId, skip, take: TAKE } } }))
      serverTime ??= page.serverTimeUtc ?? new Date().toISOString()
      const got = page.items ?? []
      for (const r of got) items.push(mapExitRow(r))
      pages += 1
      skip += got.length
      if (got.length === 0 || skip >= (page.total ?? 0)) break
    }
    replaceStockExit(warehousePublicId, items)
    db.runSync(
      `INSERT INTO sync_watermark (resource, since_utc, last_run_utc) VALUES (?, ?, ?)
       ON CONFLICT(resource) DO UPDATE SET since_utc = excluded.since_utc, last_run_utc = excluded.last_run_utc`,
      [resource, serverTime, new Date().toISOString()],
    )
    return { resource: 'stockExit', pages, items: items.length }
  } catch (err) {
    if (!(err instanceof ApiError) || err.status !== 403) throw err
    return { resource: 'stockExit', pages: 0, items: 0 }
  }
}

/** Corre los recursos que usa Recibir, en orden (uno a la vez; no compite por la misma base local). Las posiciones solo
 *  si el aparato tiene almacén por defecto (Lote 16). */
export async function downloadForReceiving(options: { forceStockExit?: boolean } = {}): Promise<DownloadResult[]> {
  const results = [await downloadProducts(), await downloadPurchaseOrdersIfAllowed(), await downloadAsns()]
  const warehousePublicId = getActiveWarehousePublicId()
  if (warehousePublicId) {
    results.push(await downloadBins(warehousePublicId))
    results.push(await downloadStockExit(warehousePublicId, options.forceStockExit === true))
  }
  return results
}
