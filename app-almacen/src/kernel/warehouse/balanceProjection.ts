// Señal débil (2026-10-10, decisión D2b: Transferir, Ajustar y Daño van a la cola de salida) — efecto de una operación PENDIENTE sobre los saldos
// locales, para que lo que se ve ya refleje lo que el operario acaba de hacer aunque todavía no haya llegado al servidor.
// Regla: saldo local = último saldo del servidor + efecto de las operaciones pendientes. Al encolar se aplica el efecto (+1); si el servidor la
// rechaza se deshace (−1); si la envía, la siguiente bajada trae el saldo verdadero; y cada bajada de saldos vuelve a sumar lo pendiente a las
// filas que reemplaza (`pendingDeltas`). Las filas nuevas (destino que no tenía ese producto) llevan un id negativo hasta que el servidor
// manda la verdadera. Daño no se proyecta (el servidor decide la posición de cuarentena y las reservas): se corrige en la siguiente bajada.
import { getDb, type SQLiteDatabase } from '../db/database'

export type ProjectedKind = 'transfer' | 'adjust'

/** Cuerpos tal como se guardan en la cola (features/transfer/transferLogic.ts y features/adjust/adjustLogic.ts). */
interface TransferBody { productPublicId: string; fromBinId: number; toBinId: number; quantity: number; fromWarehousePublicId: string; lotId?: number | null }
interface AdjustBody { productPublicId: string; warehousePublicId: string; binId: number; quantity: number; lotId?: number | null }

interface Delta { warehousePublicId: string; binId: number; productPublicId: string; lotId: number | null; delta: number }

export function deltasOf(kind: string, body: unknown): Delta[] {
  if (kind === 'transfer') {
    const b = body as TransferBody
    const lotId = b.lotId ?? null
    return [
      { warehousePublicId: b.fromWarehousePublicId, binId: b.fromBinId, productPublicId: b.productPublicId, lotId, delta: -b.quantity },
      { warehousePublicId: b.fromWarehousePublicId, binId: b.toBinId, productPublicId: b.productPublicId, lotId, delta: b.quantity },
    ]
  }
  if (kind === 'adjust') {
    const b = body as AdjustBody
    return [{ warehousePublicId: b.warehousePublicId, binId: b.binId, productPublicId: b.productPublicId, lotId: b.lotId ?? null, delta: b.quantity }]
  }
  return []
}

function findRow(db: SQLiteDatabase, d: Delta): { id: number; qty_on_hand: number } | null {
  return db.getFirstSync<{ id: number; qty_on_hand: number }>(
    `SELECT id, qty_on_hand FROM stock_balance
     WHERE warehouse_public_id = ? AND bin_id = ? AND product_public_id = ? AND ((lot_id IS NULL AND ? IS NULL) OR lot_id = ?)
     ORDER BY id DESC LIMIT 1`,
    [d.warehousePublicId, d.binId, d.productPublicId, d.lotId, d.lotId],
  )
}

function bump(db: SQLiteDatabase, d: Delta): void {
  const row = findRow(db, d)
  if (row) {
    db.runSync('UPDATE stock_balance SET qty_on_hand = ? WHERE id = ?', [Math.max(0, row.qty_on_hand + d.delta), row.id])
    return
  }
  if (d.delta <= 0) return // nada que restar de una fila que el aparato no tiene
  const product = db.getFirstSync<{ id: number }>('SELECT id FROM product WHERE public_id = ?', [d.productPublicId])
  const lot = d.lotId != null ? db.getFirstSync<{ lot_number: string | null; lot_expiry_date: string | null }>('SELECT lot_number, lot_expiry_date FROM stock_balance WHERE lot_id = ? LIMIT 1', [d.lotId]) : null
  const minId = db.getFirstSync<{ m: number | null }>('SELECT MIN(id) AS m FROM stock_balance')?.m ?? 0
  db.runSync(
    `INSERT INTO stock_balance (id, warehouse_public_id, bin_id, product_id, product_public_id, lot_id, lot_number, lot_expiry_date, qty_on_hand, qty_reserved, updated_at_utc)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?)`,
    [Math.min(-1, minId - 1), d.warehousePublicId, d.binId, product?.id ?? 0, d.productPublicId, d.lotId, lot?.lot_number ?? null, lot?.lot_expiry_date ?? null, d.delta, new Date().toISOString()],
  )
}

/** Aplica (+1) o deshace (−1) el efecto de una operación en los saldos locales. */
export function projectOperation(kind: string, body: unknown, sign: 1 | -1): void {
  const deltas = deltasOf(kind, body)
  if (deltas.length === 0) return
  const db = getDb()
  db.withTransactionSync(() => {
    for (const d of deltas) bump(db, { ...d, delta: d.delta * sign })
  })
}

/** Suma de los efectos de las operaciones pendientes por (almacén, posición, producto, lote): lo que hay que volver a sumar al saldo que baja del servidor. */
export function pendingDeltas(db: SQLiteDatabase): Map<string, number> {
  const out = new Map<string, number>()
  const rows = db.getAllSync<{ kind: string; body: string }>("SELECT kind, body FROM outbox WHERE status = 'pending' AND kind IN ('transfer', 'adjust')")
  for (const r of rows) {
    for (const d of deltasOf(r.kind, JSON.parse(r.body))) {
      const key = deltaKey(d.warehousePublicId, d.binId, d.productPublicId, d.lotId)
      out.set(key, (out.get(key) ?? 0) + d.delta)
    }
  }
  return out
}

export function deltaKey(warehousePublicId: string, binId: number | null, productPublicId: string, lotId: number | null): string {
  return `${warehousePublicId}|${binId ?? ''}|${productPublicId}|${lotId ?? ''}`
}
