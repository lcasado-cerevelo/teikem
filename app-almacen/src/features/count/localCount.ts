// Lote 8A-app — el conteo local en curso (local_count/local_count_line, schema.ts): el conteo ya existe en el servidor
// desde que se abrió (countApi.startCountOnline / startProductCountOnline), aquí solo se guarda lo capturado hasta terminar.
// Un conteo a la vez por aparato (misma regla que Recibir y Despacho, docs/lote8A-app-decisiones.md).
// Lote A4 (schema v4): el conteo es por POSICIÓN (una posición; las líneas se capturan al escanear cada producto) o por
// PRODUCTO (todas las líneas se guardan al abrirlo, una por posición y lote, con la cantidad en blanco = NULL; así se puede
// capturar, terminar y retomar tras cerrar la app sin señal). Cada línea guarda su posición y su lote.
import { getDb } from '../../kernel/db/database'
import type { TrackingType } from '../../kernel/warehouse/productLookup'
import type { CapturedEntry, ExpectedLine, ProductCountLine } from './countLogic'
import { blankAsZero } from './countLogic'

/** BIN = una posición; PRODUCT = un producto en todas sus posiciones (conteos locales anteriores al Lote 24); OPEN = conteo abierto
 *  con varios productos, cada línea con su posición (Lote 24). */
export type CountMode = 'BIN' | 'PRODUCT' | 'OPEN'

export interface CountProduct {
  publicId: string
  sku: string
  name: string
  trackingTypeCode: TrackingType
}

export interface OpenCount {
  id: number
  countId: number
  warehousePublicId: string
  mode: CountMode
  /** Posición del conteo por posición; null en el conteo por producto (cada línea lleva la suya). */
  binId: number | null
  binCode: string | null
  /** Producto del conteo por producto; null en el conteo por posición. */
  product: CountProduct | null
  isBlind: boolean
}

const ALREADY_OPEN = 'Ya hay un conteo en curso; hay que terminarlo o cancelarlo antes de empezar otro.'

export function startLocalCount(
  warehousePublicId: string,
  bin: { id: number; code: string },
  started: { countId: number; isBlind: boolean },
): number {
  if (getOpenCount() !== null) throw new Error(ALREADY_OPEN)
  const info = getDb().runSync(
    "INSERT INTO local_count (count_id, warehouse_public_id, mode, bin_id, bin_code, is_blind, created_at_utc) VALUES (?, ?, 'BIN', ?, ?, ?, ?)",
    [started.countId, warehousePublicId, bin.id, bin.code, started.isBlind ? 1 : 0, new Date().toISOString()],
  )
  return info.lastInsertRowId
}

/** Lote 24 — abre localmente el conteo abierto (vacío) ya creado en el servidor; las líneas se agregan al escanear. */
export function startLocalOpenCount(warehousePublicId: string, started: { countId: number; isBlind: boolean }): number {
  if (getOpenCount() !== null) throw new Error(ALREADY_OPEN)
  const info = getDb().runSync(
    "INSERT INTO local_count (count_id, warehouse_public_id, mode, is_blind, created_at_utc) VALUES (?, ?, 'OPEN', ?, ?)",
    [started.countId, warehousePublicId, started.isBlind ? 1 : 0, new Date().toISOString()],
  )
  return info.lastInsertRowId
}

/** Lote 24 — una línea del conteo abierto: producto contado con su cantidad en la posición (y lote) elegidas. Va al servidor
 *  como línea nueva (posición + producto + lote por número) en el lote de captura al terminar. */
export function addOpenCountLine(
  localCountId: number,
  product: { publicId: string; sku: string; name: string },
  bin: { id: number; code: string; isProvisional: boolean },
  lot: { id: number | null; number: string; expiryDate: string | null } | null,
  countedQty: number,
): number {
  const info = getDb().runSync(
    `INSERT INTO local_count_line (local_count_id, line_id, product_public_id, sku, product_name, system_qty, counted_qty, is_extra,
                                   bin_id, bin_code, lot_id, lot_number, lot_expiry_date, is_provisional_bin)
     VALUES (?, NULL, ?, ?, ?, NULL, ?, 1, ?, ?, ?, ?, ?, ?)`,
    [localCountId, product.publicId, product.sku, product.name, countedQty, bin.id, bin.code, lot?.id ?? null, lot?.number ?? null, lot?.expiryDate ?? null, bin.isProvisional ? 1 : 0],
  )
  return info.lastInsertRowId
}

/** Abre localmente un conteo por producto ya creado en el servidor y guarda TODAS sus líneas en blanco (una transacción). */
export function startLocalProductCount(
  warehousePublicId: string,
  product: CountProduct,
  started: { countId: number; isBlind: boolean },
  lines: ProductCountLine[],
): number {
  if (getOpenCount() !== null) throw new Error(ALREADY_OPEN)
  const db = getDb()
  let id = 0
  db.withTransactionSync(() => {
    const info = db.runSync(
      `INSERT INTO local_count (count_id, warehouse_public_id, mode, product_public_id, sku, product_name, tracking_type_code, is_blind, created_at_utc)
       VALUES (?, ?, 'PRODUCT', ?, ?, ?, ?, ?, ?)`,
      [started.countId, warehousePublicId, product.publicId, product.sku, product.name, product.trackingTypeCode, started.isBlind ? 1 : 0, new Date().toISOString()],
    )
    id = info.lastInsertRowId
    for (const l of lines) {
      db.runSync(
        `INSERT INTO local_count_line (local_count_id, line_id, product_public_id, sku, product_name, system_qty, counted_qty, is_extra,
                                       bin_id, bin_code, lot_id, lot_number, is_provisional_bin)
         VALUES (?, ?, ?, ?, ?, ?, NULL, 0, ?, ?, ?, ?, ?)`,
        [id, l.lineId, l.productPublicId, l.sku, l.productName, l.systemQty, l.binId, l.binCode, l.lotId, l.lotNumber, l.binIsProvisional ? 1 : 0],
      )
    }
  })
  return id
}

type CountRow = {
  id: number
  count_id: number
  warehouse_public_id: string
  mode: string | null
  bin_id: number | null
  bin_code: string | null
  product_public_id: string | null
  sku: string | null
  product_name: string | null
  tracking_type_code: string | null
  is_blind: number
}

export function getOpenCount(): OpenCount | null {
  const row = getDb().getFirstSync<CountRow>('SELECT * FROM local_count LIMIT 1')
  if (!row) return null
  const mode: CountMode = row.mode === 'PRODUCT' ? 'PRODUCT' : row.mode === 'OPEN' ? 'OPEN' : 'BIN'
  const tracking: TrackingType = row.tracking_type_code === 'LOT' || row.tracking_type_code === 'SERIAL' ? row.tracking_type_code : 'NONE'
  return {
    id: row.id,
    countId: row.count_id,
    warehousePublicId: row.warehouse_public_id,
    mode,
    binId: row.bin_id,
    binCode: row.bin_code,
    product:
      mode === 'PRODUCT' && row.product_public_id
        ? { publicId: row.product_public_id, sku: row.sku ?? '', name: row.product_name ?? '', trackingTypeCode: tracking }
        : null,
    isBlind: row.is_blind === 1,
  }
}

/** Captura (o reemplaza) lo encontrado para una línea esperada (conteo por posición: la línea hereda la posición del conteo). */
export function captureExpectedLine(localCountId: number, line: ExpectedLine, countedQty: number): void {
  const db = getDb()
  const existing = db.getFirstSync<{ id: number }>('SELECT id FROM local_count_line WHERE local_count_id = ? AND line_id = ?', [localCountId, line.lineId])
  if (existing) {
    db.runSync('UPDATE local_count_line SET counted_qty = ? WHERE id = ?', [countedQty, existing.id])
    return
  }
  db.runSync(
    `INSERT INTO local_count_line (local_count_id, line_id, product_public_id, sku, product_name, system_qty, counted_qty, is_extra, bin_id, bin_code)
     SELECT ?, ?, ?, ?, ?, ?, ?, 0, c.bin_id, c.bin_code FROM local_count c WHERE c.id = ?`,
    [localCountId, line.lineId, line.productPublicId, line.sku, line.productName, line.systemQty, countedQty, localCountId],
  )
}

/** Agrega lo encontrado de un producto que no estaba en la lista esperada (conteo por posición: en la posición del conteo). */
export function addExtraLine(localCountId: number, product: { publicId: string; sku: string; name: string }, countedQty: number): void {
  getDb().runSync(
    `INSERT INTO local_count_line (local_count_id, line_id, product_public_id, sku, product_name, system_qty, counted_qty, is_extra, bin_id, bin_code)
     SELECT ?, NULL, ?, ?, ?, NULL, ?, 1, c.bin_id, c.bin_code FROM local_count c WHERE c.id = ?`,
    [localCountId, product.publicId, product.sku, product.name, countedQty, localCountId],
  )
}

/** "Otra posición" del conteo por producto: una fila nueva (en blanco) en una posición donde el sistema no tenía nada. */
export function addProductExtraRow(
  localCountId: number,
  product: { publicId: string; sku: string; name: string },
  bin: { id: number; code: string; isProvisional: boolean },
  lot: { number: string; expiryDate: string | null } | null,
): number {
  const info = getDb().runSync(
    `INSERT INTO local_count_line (local_count_id, line_id, product_public_id, sku, product_name, system_qty, counted_qty, is_extra,
                                   bin_id, bin_code, lot_id, lot_number, lot_expiry_date, is_provisional_bin)
     VALUES (?, NULL, ?, ?, ?, NULL, NULL, 1, ?, ?, NULL, ?, ?, ?)`,
    [localCountId, product.publicId, product.sku, product.name, bin.id, bin.code, lot?.number ?? null, lot?.expiryDate ?? null, bin.isProvisional ? 1 : 0],
  )
  return info.lastInsertRowId
}

/** Corrige la cantidad contada de una línea ya capturada (sin volver a escanear). */
export function updateLocalCountLineQty(id: number, countedQty: number): void {
  getDb().runSync('UPDATE local_count_line SET counted_qty = ? WHERE id = ?', [countedQty, id])
}

/** Conteo por producto: guarda lo escrito en un espacio (null = en blanco). Se guarda a cada cambio para poder retomarlo. */
export function setProductRowQty(id: number, countedQty: number | null): void {
  getDb().runSync('UPDATE local_count_line SET counted_qty = ? WHERE id = ?', [countedQty, id])
}

export function removeLocalCountLine(id: number): void {
  getDb().runSync('DELETE FROM local_count_line WHERE id = ?', [id])
}

export interface LocalCountLineRow {
  id: number
  lineId: number | null
  productPublicId: string
  sku: string
  productName: string
  systemQty: number | null
  countedQty: number
  isExtra: boolean
  binId: number | null
  binCode: string | null
}

type LineRow = {
  id: number
  line_id: number | null
  product_public_id: string
  sku: string | null
  product_name: string | null
  system_qty: number | null
  counted_qty: number | null
  is_extra: number
  bin_id: number | null
  bin_code: string | null
  lot_id: number | null
  lot_number: string | null
  lot_expiry_date: string | null
  is_provisional_bin: number
}

/** Líneas capturadas del conteo por posición (las que tienen cantidad). */
export function getCapturedLines(localCountId: number): LocalCountLineRow[] {
  return getDb()
    .getAllSync<LineRow>('SELECT * FROM local_count_line WHERE local_count_id = ? AND counted_qty IS NOT NULL ORDER BY id', [localCountId])
    .map((r) => ({
      id: r.id,
      lineId: r.line_id,
      productPublicId: r.product_public_id,
      sku: r.sku ?? '',
      productName: r.product_name ?? '',
      systemQty: r.system_qty,
      countedQty: r.counted_qty ?? 0,
      isExtra: r.is_extra === 1,
      binId: r.bin_id,
      binCode: r.bin_code,
    }))
}

export function toCapturedEntries(rows: LocalCountLineRow[]): CapturedEntry[] {
  return rows.map((r) => ({
    lineId: r.lineId,
    productPublicId: r.productPublicId,
    sku: r.sku,
    productName: r.productName,
    countedQty: r.countedQty,
    isExtra: r.isExtra,
    binId: r.binId,
  }))
}

/** Fila de la lista del conteo por producto: una por posición (y lote), con su espacio de cantidad. */
export interface ProductCountRow {
  id: number
  lineId: number | null
  productPublicId: string
  sku: string
  productName: string
  binId: number | null
  binCode: string
  lotId: number | null
  lotNumber: string | null
  lotExpiryDate: string | null
  /** Lo que el sistema espera ahí (null a ciegas o en una fila de "Otra posición"). */
  systemQty: number | null
  /** null = en blanco (se manda como 0). */
  countedQty: number | null
  isExtra: boolean
  isProvisionalBin: boolean
}

/** Filas del conteo por producto, en el orden en que llegaron (el del servidor: por posición) y las nuevas al final. */
export function getProductCountRows(localCountId: number): ProductCountRow[] {
  return getDb()
    .getAllSync<LineRow>('SELECT * FROM local_count_line WHERE local_count_id = ? ORDER BY is_extra, id', [localCountId])
    .map((r) => ({
      id: r.id,
      lineId: r.line_id,
      productPublicId: r.product_public_id,
      sku: r.sku ?? '',
      productName: r.product_name ?? '',
      binId: r.bin_id,
      binCode: r.bin_code ?? '',
      lotId: r.lot_id,
      lotNumber: r.lot_number,
      lotExpiryDate: r.lot_expiry_date,
      systemQty: r.system_qty,
      countedQty: r.counted_qty,
      isExtra: r.is_extra === 1,
      isProvisionalBin: r.is_provisional_bin === 1,
    }))
}

/** Lo que viaja al confirmar un conteo por producto: TODAS las filas, las en blanco como 0 (regla del dueño). */
export function toProductEntries(rows: ProductCountRow[]): CapturedEntry[] {
  return rows.map((r) => ({
    lineId: r.lineId,
    productPublicId: r.productPublicId,
    sku: r.sku,
    productName: r.productName,
    countedQty: blankAsZero(r.countedQty),
    isExtra: r.isExtra,
    binId: r.binId,
    lotNumber: r.lotNumber,
    lotExpiryDate: r.lotExpiryDate,
  }))
}

export function discardLocalCount(): void {
  const db = getDb()
  db.runSync('DELETE FROM local_count_line WHERE local_count_id IN (SELECT id FROM local_count)')
  db.runSync('DELETE FROM local_count')
}
