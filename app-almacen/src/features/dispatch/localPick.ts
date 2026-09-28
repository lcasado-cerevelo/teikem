// Lote 8A-app — el despacho local en curso (local_pick/local_pick_line, schema.ts v2): sobrevive a cerrar la app,
// un despacho a la vez por aparato (misma regla que Recibir, docs/lote8A-app-decisiones.md).
import { getDb } from '../../kernel/db/database'
import type { PickLine } from './dispatchLogic'

export interface OpenPick {
  id: number
  warehousePublicId: string
  clientPublicId: string | null
  clientName: string | null
  lineRows: Array<PickLine & { id: number }>
}

export function startLocalPick(warehousePublicId: string, client: { publicId: string; name: string } | null): number {
  if (getOpenPick() !== null) {
    throw new Error('Ya hay un despacho en curso; hay que confirmarlo o cancelarlo antes de empezar otro.')
  }
  const info = getDb().runSync(
    'INSERT INTO local_pick (warehouse_public_id, client_public_id, client_name, created_at_utc) VALUES (?, ?, ?, ?)',
    [warehousePublicId, client?.publicId ?? null, client?.name ?? null, new Date().toISOString()],
  )
  return info.lastInsertRowId
}

export function addLocalPickLine(pickId: number, line: PickLine): void {
  getDb().runSync(
    `INSERT INTO local_pick_line (pick_id, product_public_id, sku, product_name, quantity, from_bin_code)
     VALUES (?, ?, ?, ?, ?, ?)`,
    [pickId, line.productPublicId, line.sku, line.productName, line.quantity, line.fromBinCode],
  )
}

export function removeLocalPickLine(lineId: number): void {
  getDb().runSync('DELETE FROM local_pick_line WHERE id = ?', [lineId])
}

export function getOpenPick(): OpenPick | null {
  const db = getDb()
  const header = db.getFirstSync<{ id: number; warehouse_public_id: string; client_public_id: string | null; client_name: string | null }>(
    'SELECT id, warehouse_public_id, client_public_id, client_name FROM local_pick LIMIT 1',
  )
  if (!header) return null
  const lineRows = db
    .getAllSync<{ id: number; product_public_id: string; sku: string | null; product_name: string | null; quantity: number; from_bin_code: string }>(
      'SELECT * FROM local_pick_line WHERE pick_id = ? ORDER BY id',
      [header.id],
    )
    .map((r) => ({
      id: r.id,
      productPublicId: r.product_public_id,
      sku: r.sku ?? '',
      productName: r.product_name ?? '',
      quantity: r.quantity,
      fromBinCode: r.from_bin_code,
    }))
  return { id: header.id, warehousePublicId: header.warehouse_public_id, clientPublicId: header.client_public_id, clientName: header.client_name, lineRows }
}

export function discardLocalPick(): void {
  const db = getDb()
  db.runSync('DELETE FROM local_pick_line WHERE pick_id IN (SELECT id FROM local_pick)')
  db.runSync('DELETE FROM local_pick')
}
