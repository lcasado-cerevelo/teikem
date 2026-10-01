// Lote 8A-app — el conteo local en curso (local_count/local_count_line, schema.ts v2): el conteo ya existe en el
// servidor desde que se abrió (countApi.startCountOnline), aquí solo se guarda lo capturado hasta terminar. Un
// conteo a la vez por aparato (misma regla que Recibir y Despacho, docs/lote8A-app-decisiones.md).
import { getDb } from '../../kernel/db/database'
import type { CapturedEntry, ExpectedLine } from './countLogic'

export interface OpenCount {
  id: number
  countId: number
  warehousePublicId: string
  binId: number
  binCode: string
  isBlind: boolean
}

export function startLocalCount(
  warehousePublicId: string,
  bin: { id: number; code: string },
  started: { countId: number; isBlind: boolean },
): number {
  if (getOpenCount() !== null) {
    throw new Error('Ya hay un conteo en curso; hay que terminarlo o cancelarlo antes de empezar otro.')
  }
  const info = getDb().runSync(
    'INSERT INTO local_count (count_id, warehouse_public_id, bin_id, bin_code, is_blind, created_at_utc) VALUES (?, ?, ?, ?, ?, ?)',
    [started.countId, warehousePublicId, bin.id, bin.code, started.isBlind ? 1 : 0, new Date().toISOString()],
  )
  return info.lastInsertRowId
}

export function getOpenCount(): OpenCount | null {
  const row = getDb().getFirstSync<{ id: number; count_id: number; warehouse_public_id: string; bin_id: number; bin_code: string; is_blind: number }>(
    'SELECT * FROM local_count LIMIT 1',
  )
  if (!row) return null
  return { id: row.id, countId: row.count_id, warehousePublicId: row.warehouse_public_id, binId: row.bin_id, binCode: row.bin_code, isBlind: row.is_blind === 1 }
}

/** Captura (o reemplaza) lo encontrado para una línea esperada. */
export function captureExpectedLine(localCountId: number, line: ExpectedLine, countedQty: number): void {
  const db = getDb()
  const existing = db.getFirstSync<{ id: number }>('SELECT id FROM local_count_line WHERE local_count_id = ? AND line_id = ?', [localCountId, line.lineId])
  if (existing) {
    db.runSync('UPDATE local_count_line SET counted_qty = ? WHERE id = ?', [countedQty, existing.id])
    return
  }
  db.runSync(
    `INSERT INTO local_count_line (local_count_id, line_id, product_public_id, sku, product_name, system_qty, counted_qty, is_extra)
     VALUES (?, ?, ?, ?, ?, ?, ?, 0)`,
    [localCountId, line.lineId, line.productPublicId, line.sku, line.productName, line.systemQty, countedQty],
  )
}

/** Agrega lo encontrado de un producto que no estaba en la lista esperada. */
export function addExtraLine(localCountId: number, product: { publicId: string; sku: string; name: string }, countedQty: number): void {
  getDb().runSync(
    `INSERT INTO local_count_line (local_count_id, line_id, product_public_id, sku, product_name, system_qty, counted_qty, is_extra)
     VALUES (?, NULL, ?, ?, ?, NULL, ?, 1)`,
    [localCountId, product.publicId, product.sku, product.name, countedQty],
  )
}

/** Corrige la cantidad contada de una línea ya capturada (sin volver a escanear). */
export function updateLocalCountLineQty(id: number, countedQty: number): void {
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
}

export function getCapturedLines(localCountId: number): LocalCountLineRow[] {
  return getDb()
    .getAllSync<{
      id: number
      line_id: number | null
      product_public_id: string
      sku: string | null
      product_name: string | null
      system_qty: number | null
      counted_qty: number
      is_extra: number
    }>('SELECT * FROM local_count_line WHERE local_count_id = ? ORDER BY id', [localCountId])
    .map((r) => ({
      id: r.id,
      lineId: r.line_id,
      productPublicId: r.product_public_id,
      sku: r.sku ?? '',
      productName: r.product_name ?? '',
      systemQty: r.system_qty,
      countedQty: r.counted_qty,
      isExtra: r.is_extra === 1,
    }))
}

export function toCapturedEntries(rows: LocalCountLineRow[]): CapturedEntry[] {
  return rows.map((r) => ({ lineId: r.lineId, productPublicId: r.productPublicId, sku: r.sku, productName: r.productName, countedQty: r.countedQty, isExtra: r.isExtra }))
}

export function discardLocalCount(): void {
  const db = getDb()
  db.runSync('DELETE FROM local_count_line WHERE local_count_id IN (SELECT id FROM local_count)')
  db.runSync('DELETE FROM local_count')
}
