// Lote 8A-app — consultas locales de Recibir: documento por número (orden) o referencia (aviso), y el recibo en curso
// (sobrevive a cerrar la app: local_receipt/local_receipt_line, schema.ts). El producto por código es compartido
// (Recibir, Despacho, Conteo): kernel/warehouse/productLookup.ts.
import { getDb } from '../../kernel/db/database'
import { findProductByCode, type LocalProduct, type TrackingType } from '../../kernel/warehouse/productLookup'
import { parseReceivingMode, type DocLine, type DraftLine, type LocalBin, type ReceivingMode } from './receiveLogic'

export { findProductByCode }
export type { LocalProduct, TrackingType }

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
  /** Lote 16: modo con que se abrió (copia del modo del almacén del aparato en ese momento); null = abierto con una app
   *  anterior al lote, se manda sin modo y entra "con acomodo" (D9-A). */
  receivingMode: ReceivingMode | null
}

/** Crea el recibo local (blind si doc es null) y devuelve su id. Solo uno a la vez por aparato (pantalla 3 no permite
 *  abrir otro sin confirmar o cancelar el actual). */
/** Un recibo a la vez por aparato (decisión de Luis, 2026-09-28): mientras uno esté abierto, ni la pantalla ni nada más
 *  puede empezar otro; solo se libera confirmándolo o cancelándolo (discardLocalReceipt). Cerrar y volver a abrir la
 *  app no lo pierde: sigue guardado y se retoma tal cual (getOpenReceipt), así que esta función nunca hace falta
 *  llamarla "para reemplazar" uno en curso — es un error del código que la llama, no un caso normal de uso. */
export function startLocalReceipt(warehousePublicId: string, doc: LocalDoc | null, receivingMode: ReceivingMode | null = null): number {
  const db = getDb()
  if (getOpenReceipt() !== null) {
    throw new Error('Ya hay un recibo en curso; hay que confirmarlo o cancelarlo antes de empezar otro.')
  }
  const info = db.runSync(
    `INSERT INTO local_receipt (warehouse_public_id, purchase_order_public_id, asn_id, doc_label, created_at_utc, receiving_mode)
     VALUES (?, ?, ?, ?, ?, ?)`,
    [warehousePublicId, doc?.purchaseOrderPublicId ?? null, doc?.asnId ?? null, doc?.label ?? null, new Date().toISOString(), receivingMode],
  )
  return info.lastInsertRowId
}

export function addLocalReceiptLine(receiptId: number, line: DraftLine): void {
  getDb().runSync(
    `INSERT INTO local_receipt_line (receipt_id, product_public_id, sku, product_name, tracking_type_code, received_qty, lot_number, expiry_date,
                                     serial_numbers, target_bin_code, damaged_qty, damage_cause, damage_note, damage_bin_code, damage_discard, damage_destination)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
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
      line.targetBinCode,
      line.damagedQty,
      line.damageCause,
      line.damageNote,
      line.damageBinCode,
      line.damageDiscard ? 1 : 0,
      line.damageDestination,
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
    receiving_mode: string | null
  }>('SELECT id, warehouse_public_id, purchase_order_public_id, asn_id, doc_label, receiving_mode FROM local_receipt LIMIT 1')
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
      target_bin_code: string | null
      damaged_qty: number
      damage_cause: string | null
      damage_note: string | null
      damage_bin_code: string | null
      damage_discard: number
      damage_destination: string | null
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
      targetBinCode: r.target_bin_code,
      damagedQty: r.damaged_qty ?? 0,
      damageCause: r.damage_cause,
      damageNote: r.damage_note,
      damageBinCode: r.damage_bin_code,
      damageDiscard: r.damage_discard === 1,
      damageDestination: r.damage_destination,
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
  return {
    id: header.id,
    warehousePublicId: header.warehouse_public_id,
    doc,
    lines: lineRows,
    lineRows,
    receivingMode: parseReceivingMode(header.receiving_mode),
  }
}

/** El recibo confirmado se manda a la cola (kernel/sync/outbox.ts); ya no hace falta en la forma normalizada. */
export function discardLocalReceipt(): void {
  const db = getDb()
  db.runSync('DELETE FROM local_receipt_line WHERE receipt_id IN (SELECT id FROM local_receipt)')
  db.runSync('DELETE FROM local_receipt')
}

// ------------------------------------------------------------------ Lote 16: posiciones y líneas del documento

/** Posiciones locales del almacén cuyo código coincide (sin distinguir mayúsculas) con lo escaneado, activas o no, con
 *  el tipo de su zona. La decisión (existe / activa / zona de guardado) la toma receiveLogic.validateTargetBin. */
export function findLocalBinsByCode(warehousePublicId: string, code: string): LocalBin[] {
  const wanted = code.trim()
  if (!wanted) return []
  return getDb()
    .getAllSync<{ code: string; zone_type_code: string | null; is_active: number }>(
      'SELECT code, zone_type_code, is_active FROM bin WHERE warehouse_public_id = ? AND code = ? COLLATE NOCASE',
      [warehousePublicId, wanted],
    )
    .map((b) => ({ code: b.code, zoneTypeCode: b.zone_type_code, isActive: b.is_active === 1 }))
}

/** Primera posición activa del almacén en una zona de ese tipo, por código (la misma que elige el servidor: la de cuarentena para lo dañado, la de recepción como segunda opción). */
export function findLocalBinCodeByZoneType(warehousePublicId: string, zoneTypeCode: string): string | null {
  return (
    getDb().getFirstSync<{ code: string }>(
      'SELECT code FROM bin WHERE warehouse_public_id = ? AND zone_type_code = ? AND is_active = 1 ORDER BY code LIMIT 1',
      [warehousePublicId, zoneTypeCode],
    )?.code ?? null
  )
}

/** Cuántas posiciones del almacén tiene el aparato (0 = todavía no se bajaron: hace falta sincronizar con señal). */
export function countLocalBins(warehousePublicId: string): number {
  return (
    getDb().getFirstSync<{ n: number }>('SELECT COUNT(*) AS n FROM bin WHERE warehouse_public_id = ?', [warehousePublicId])?.n ?? 0
  )
}

/** Líneas del documento del recibo tal como las tiene el aparato (para receiveLogic.findTargetConflict): las del aviso con
 *  su lote; las de la orden de compra con algo pendiente (el aviso del servidor nace de lo pendiente), sin lote. */
export function getDocLines(doc: LocalDoc | null): DocLine[] {
  if (!doc) return []
  const db = getDb()
  if (doc.asnId != null) {
    return db
      .getAllSync<{ product_public_id: string; lot_number: string | null }>(
        'SELECT product_public_id, lot_number FROM asn_line WHERE asn_id = ? ORDER BY id',
        [doc.asnId],
      )
      .map((l) => ({ productPublicId: l.product_public_id, lotNumber: l.lot_number }))
  }
  if (!doc.purchaseOrderPublicId) return []
  return db
    .getAllSync<{ product_public_id: string }>(
      `SELECT l.product_public_id FROM purchase_order_line l JOIN purchase_order po ON po.id = l.purchase_order_id
       WHERE po.public_id = ? AND l.qty_pending > 0 ORDER BY l.id`,
      [doc.purchaseOrderPublicId],
    )
    .map((l) => ({ productPublicId: l.product_public_id, lotNumber: null }))
}
