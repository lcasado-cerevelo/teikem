// Lote 8A-app — consultas locales de Recibir: producto por código de barras o SKU, documento por número (orden) o
// referencia (aviso), y el recibo en curso (sobrevive a cerrar la app: local_receipt/local_receipt_line, schema.ts).
import { getDb } from '../../kernel/db/database'
import type { DraftLine } from './receiveLogic'

export type TrackingType = 'NONE' | 'LOT' | 'SERIAL'

export interface LocalProduct {
  publicId: string
  sku: string
  name: string
  trackingTypeCode: TrackingType
}

export function findProductByCode(code: string): LocalProduct | null {
  const row = getDb().getFirstSync<{ public_id: string; sku: string; name: string; tracking_type_code: string | null }>(
    'SELECT public_id, sku, name, tracking_type_code FROM product WHERE is_active = 1 AND (barcode = ? OR sku = ?) LIMIT 1',
    [code, code],
  )
  if (!row) return null
  const tracking = row.tracking_type_code === 'LOT' || row.tracking_type_code === 'SERIAL' ? row.tracking_type_code : 'NONE'
  return { publicId: row.public_id, sku: row.sku, name: row.name, trackingTypeCode: tracking }
}

export interface LocalDoc {
  kind: 'po' | 'asn'
  purchaseOrderPublicId: string | null
  asnId: number | null
  label: string
}

/** Un código escaneado puede ser el número de una orden de compra o la referencia de un aviso (docs/mobile/
 *  app-almacen-plan.md §2, pantalla 3). La orden de compra tiene prioridad si ambos coincidieran por error de captura. */
export function findDocByCode(warehousePublicId: string, code: string): LocalDoc | null {
  const db = getDb()
  const po = db.getFirstSync<{ public_id: string; number: string }>(
    'SELECT public_id, number FROM purchase_order WHERE is_active = 1 AND warehouse_public_id = ? AND number = ?',
    [warehousePublicId, code],
  )
  if (po) return { kind: 'po', purchaseOrderPublicId: po.public_id, asnId: null, label: po.number }
  const asn = db.getFirstSync<{ id: number; reference: string | null; purchase_order_number: string | null }>(
    'SELECT id, reference, purchase_order_number FROM asn WHERE is_active = 1 AND warehouse_public_id = ? AND reference = ?',
    [warehousePublicId, code],
  )
  if (asn) return { kind: 'asn', purchaseOrderPublicId: null, asnId: asn.id, label: asn.reference ?? asn.purchase_order_number ?? code }
  return null
}

export interface OpenReceipt {
  id: number
  warehousePublicId: string
  doc: LocalDoc | null
  lines: DraftLine[]
}

/** Crea el recibo local (blind si doc es null) y devuelve su id. Solo uno a la vez por aparato (pantalla 3 no permite
 *  abrir otro sin confirmar o cancelar el actual). */
export function startLocalReceipt(warehousePublicId: string, doc: LocalDoc | null): number {
  const db = getDb()
  db.runSync('DELETE FROM local_receipt_line WHERE receipt_id IN (SELECT id FROM local_receipt)')
  db.runSync('DELETE FROM local_receipt')
  const info = db.runSync(
    'INSERT INTO local_receipt (warehouse_public_id, purchase_order_public_id, asn_id, doc_label, created_at_utc) VALUES (?, ?, ?, ?, ?)',
    [warehousePublicId, doc?.purchaseOrderPublicId ?? null, doc?.asnId ?? null, doc?.label ?? null, new Date().toISOString()],
  )
  return info.lastInsertRowId
}

export function addLocalReceiptLine(receiptId: number, line: DraftLine): void {
  getDb().runSync(
    `INSERT INTO local_receipt_line (receipt_id, product_public_id, sku, product_name, tracking_type_code, received_qty, lot_number, expiry_date, serial_numbers)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
    [
      receiptId,
      line.productPublicId,
      line.sku,
      line.productName,
      line.trackingTypeCode,
      line.receivedQty,
      line.lotNumber,
      line.expiryDate,
      line.serialNumbers ? JSON.stringify(line.serialNumbers) : null,
    ],
  )
}

export function removeLocalReceiptLine(lineId: number): void {
  getDb().runSync('DELETE FROM local_receipt_line WHERE id = ?', [lineId])
}

export interface LocalReceiptLineRow extends DraftLine {
  id: number
}

/** El recibo abierto ahora mismo (si lo hay), con sus líneas: sobrevive a cerrar y volver a abrir la app. */
export function getOpenReceipt(): (OpenReceipt & { lineRows: LocalReceiptLineRow[] }) | null {
  const db = getDb()
  const header = db.getFirstSync<{
    id: number
    warehouse_public_id: string
    purchase_order_public_id: string | null
    asn_id: number | null
    doc_label: string | null
  }>('SELECT id, warehouse_public_id, purchase_order_public_id, asn_id, doc_label FROM local_receipt LIMIT 1')
  if (!header) return null
  const lineRows = db
    .getAllSync<{
      id: number
      product_public_id: string
      sku: string | null
      product_name: string | null
      tracking_type_code: string | null
      received_qty: number
      lot_number: string | null
      expiry_date: string | null
      serial_numbers: string | null
    }>('SELECT * FROM local_receipt_line WHERE receipt_id = ? ORDER BY id', [header.id])
    .map((r) => ({
      id: r.id,
      productPublicId: r.product_public_id,
      sku: r.sku ?? '',
      productName: r.product_name ?? '',
      trackingTypeCode: (r.tracking_type_code as TrackingType) ?? 'NONE',
      receivedQty: r.received_qty,
      lotNumber: r.lot_number,
      expiryDate: r.expiry_date,
      serialNumbers: r.serial_numbers ? (JSON.parse(r.serial_numbers) as string[]) : null,
    }))
  const doc =
    header.purchase_order_public_id || header.asn_id
      ? {
          kind: (header.asn_id ? 'asn' : 'po') as 'po' | 'asn',
          purchaseOrderPublicId: header.purchase_order_public_id,
          asnId: header.asn_id,
          label: header.doc_label ?? '',
        }
      : null
  return { id: header.id, warehousePublicId: header.warehouse_public_id, doc, lines: lineRows, lineRows }
}

/** El recibo confirmado se manda a la cola (kernel/sync/outbox.ts); ya no hace falta en la forma normalizada. */
export function discardLocalReceipt(): void {
  const db = getDb()
  db.runSync('DELETE FROM local_receipt_line WHERE receipt_id IN (SELECT id FROM local_receipt)')
  db.runSync('DELETE FROM local_receipt')
}
