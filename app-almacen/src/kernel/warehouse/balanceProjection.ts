// Señal débil (2026-10-10, decisión D2b: Transferir, Ajustar y Daño van a la cola de salida) — efecto de una operación PENDIENTE sobre los saldos
// locales, para que lo que se ve ya refleje lo que el operario acaba de hacer aunque todavía no haya llegado al servidor.
// Regla: saldo local = último saldo del servidor + efecto de las operaciones pendientes. Al encolar se aplica el efecto (+1); si el servidor la
// rechaza se deshace (−1); si la envía, la siguiente bajada trae el saldo verdadero; y cada bajada de saldos vuelve a sumar lo pendiente a las
// filas que reemplaza (`pendingDeltas`). Las filas nuevas (destino que no tenía ese producto) llevan un id negativo hasta que el servidor
// manda la verdadera. Daño no se proyecta (el servidor decide la posición de cuarentena y las reservas): se corrige en la siguiente bajada.
// 2026-10-11 — Despacho manual (DMA, kind `manualIssue`): resta lo que sale de cada posición. Su cuerpo trae la posición pero NO el lote (el servidor
// saca por FEFO dentro de la posición), así que el lote se decide al encolar (`issueDeltas`, mismo orden: vence primero) y el efecto exacto se guarda
// con la fila de la cola (`outbox.projection_json`, schema v11): deshacer, reintentar y la bajada de saldos usan ese efecto guardado, no el cuerpo.
import { getDb, type SQLiteDatabase } from '../db/database'

export type ProjectedKind = 'transfer' | 'adjust' | 'manualIssue'

/** Cuerpos tal como se guardan en la cola (features/transfer/transferLogic.ts y features/adjust/adjustLogic.ts). */
interface TransferBody { productPublicId: string; fromBinId: number; toBinId: number; quantity: number; fromWarehousePublicId: string; lotId?: number | null }
interface AdjustBody { productPublicId: string; warehousePublicId: string; binId: number; quantity: number; lotId?: number | null }

/** Cuerpo del despacho manual (features/dispatch/manualIssueLogic.ts): líneas con posición y, si acaso, lote. */
interface IssueBody { warehousePublicId: string; lines: Array<{ productPublicId: string; quantity: number; binId: number; lotId?: number | null }> }

export interface Delta { warehousePublicId: string; binId: number; productPublicId: string; lotId: number | null; delta: number }

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
  applyDeltas(deltasOf(kind, body), sign)
}

/** Aplica (+1) o deshace (−1) un efecto ya calculado (el guardado con la fila de la cola). */
export function applyDeltas(deltas: readonly Delta[], sign: 1 | -1): void {
  if (deltas.length === 0) return
  const db = getDb()
  db.withTransactionSync(() => applyDeltasIn(db, deltas, sign))
}

/** Igual que `applyDeltas`, para usar DENTRO de una transacción que ya abrió quien llama (expo-sqlite no anida transacciones). */
export function applyDeltasIn(db: SQLiteDatabase, deltas: readonly Delta[], sign: 1 | -1): void {
  for (const d of deltas) bump(db, { ...d, delta: d.delta * sign })
}

/** El efecto de una fila de la cola: el guardado al encolar (`projection_json`) o, si no tiene, el que se deriva del cuerpo. */
export function deltasOfRow(row: { kind: string; body: string; projection_json?: string | null }): Delta[] {
  if (row.projection_json) {
    try {
      const parsed = JSON.parse(row.projection_json) as unknown
      return Array.isArray(parsed) ? (parsed as Delta[]) : []
    } catch {
      return []
    }
  }
  return deltasOf(row.kind, JSON.parse(row.body))
}

/** Aplica (+1) o deshace (−1) el efecto de una fila de la cola (rechazo del servidor, reintento). */
export function projectRow(row: { kind: string; body: string; projection_json?: string | null }, sign: 1 | -1): void {
  applyDeltas(deltasOfRow(row), sign)
}

/**
 * Despacho manual: qué se descuenta de cada saldo local. Por línea, en su posición, se reparte la cantidad entre los saldos del producto en el orden
 * de salida (sin lote primero, luego el que vence antes) contra lo DISPONIBLE (en mano − reservado), igual que el FEFO del servidor dentro de una
 * posición. Lo que el aparato no tiene (copia atrasada) no se resta: el servidor decide y la siguiente bajada trae lo verdadero.
 */
export function issueDeltas(body: unknown, db: SQLiteDatabase = getDb()): Delta[] {
  const b = body as IssueBody
  const out: Delta[] = []
  // lo ya repartido en este mismo despacho (dos líneas del mismo producto en la misma posición)
  const used = new Map<number, number>()
  for (const line of b.lines ?? []) {
    if (!line.binId || !(line.quantity > 0)) continue
    if (line.lotId != null) {
      out.push({ warehousePublicId: b.warehousePublicId, binId: line.binId, productPublicId: line.productPublicId, lotId: line.lotId, delta: -line.quantity })
      continue
    }
    const rows = db.getAllSync<{ id: number; lot_id: number | null; qty_on_hand: number; qty_reserved: number }>(
      `SELECT id, lot_id, qty_on_hand, qty_reserved FROM stock_balance
       WHERE warehouse_public_id = ? AND bin_id = ? AND product_public_id = ?
       ORDER BY (lot_id IS NOT NULL), (lot_expiry_date IS NULL), lot_expiry_date, lot_id, id`,
      [b.warehousePublicId, line.binId, line.productPublicId],
    )
    let left = line.quantity
    for (const r of rows) {
      if (left <= 0) break
      const free = Math.max(0, r.qty_on_hand - r.qty_reserved - (used.get(r.id) ?? 0))
      const take = Math.min(free, left)
      if (take <= 0) continue
      used.set(r.id, (used.get(r.id) ?? 0) + take)
      out.push({ warehousePublicId: b.warehousePublicId, binId: line.binId, productPublicId: line.productPublicId, lotId: r.lot_id, delta: -take })
      left = Math.round((left - take) * 1000) / 1000
    }
  }
  return out
}

/** Suma de los efectos de las operaciones pendientes por (almacén, posición, producto, lote): lo que hay que volver a sumar al saldo que baja del servidor. */
export function pendingDeltas(db: SQLiteDatabase): Map<string, number> {
  const out = new Map<string, number>()
  const rows = db.getAllSync<{ kind: string; body: string; projection_json: string | null }>(
    "SELECT kind, body, projection_json FROM outbox WHERE status = 'pending' AND kind IN ('transfer', 'adjust', 'manualIssue')",
  )
  for (const r of rows) {
    for (const d of deltasOfRow(r)) {
      const key = deltaKey(d.warehousePublicId, d.binId, d.productPublicId, d.lotId)
      out.set(key, (out.get(key) ?? 0) + d.delta)
    }
  }
  return out
}

export function deltaKey(warehousePublicId: string, binId: number | null, productPublicId: string, lotId: number | null): string {
  return `${warehousePublicId}|${binId ?? ''}|${productPublicId}|${lotId ?? ''}`
}
