// Orden de salida en el aparato (2026-10-05): copia local de GET /inventory/exit-options del almacén por defecto (tabla `stock_exit`,
// schema v6). El ORDEN lo calcula el servidor (rank 1 = sale primero, la misma regla de la recolección): aquí no se ordena nada, solo se
// guarda y se lee por rank. Es una foto: se reemplaza completa (bajada de todo el almacén) o solo la de un producto (consulta en línea).
import { getDb } from '../../kernel/db/database'
import { getKv, KvKeys, setKv } from '../../kernel/db/kv'
import type { StockOption } from './dispatchLogic'

export interface StockExitRow extends StockOption {
  binId: number
  zoneCode: string | null
  lotId: number | null
}

function insert(warehousePublicId: string, productPublicId: string, rows: readonly StockExitRow[]): void {
  const db = getDb()
  for (const r of rows) {
    db.runSync(
      `INSERT OR REPLACE INTO stock_exit (warehouse_public_id, product_public_id, rank, bin_id, bin_code, zone_code, zone_type_code, lot_id, lot_number, expiry_date, available)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      [warehousePublicId, productPublicId, r.rank, r.binId, r.binCode, r.zoneCode, r.zoneTypeCode, r.lotId, r.lotNumber, r.expiryDate, r.available],
    )
  }
}

/** Reemplaza la foto completa del almacén (todas las páginas ya leídas) en una transacción. */
export function replaceStockExit(warehousePublicId: string, items: readonly (StockExitRow & { productPublicId: string })[]): void {
  const db = getDb()
  db.withTransactionSync(() => {
    db.runSync('DELETE FROM stock_exit WHERE warehouse_public_id = ?', [warehousePublicId])
    for (const r of items) insert(warehousePublicId, r.productPublicId, [r])
  })
}

/** Reemplaza solo la foto de un producto (lo que acaba de contestar el servidor en línea). */
export function replaceStockExitFor(warehousePublicId: string, productPublicId: string, rows: readonly StockExitRow[]): void {
  const db = getDb()
  db.withTransactionSync(() => {
    db.runSync('DELETE FROM stock_exit WHERE warehouse_public_id = ? AND product_public_id = ?', [warehousePublicId, productPublicId])
    insert(warehousePublicId, productPublicId, rows)
  })
}

type Row = {
  rank: number
  bin_id: number
  bin_code: string
  zone_code: string | null
  zone_type_code: string | null
  lot_id: number | null
  lot_number: string | null
  expiry_date: string | null
  available: number
}

/** Existencias disponibles del producto en el orden de salida del servidor. */
export function readStockExit(warehousePublicId: string, productPublicId: string): StockExitRow[] {
  return getDb()
    .getAllSync<Row>('SELECT * FROM stock_exit WHERE warehouse_public_id = ? AND product_public_id = ? ORDER BY rank', [warehousePublicId, productPublicId])
    .map((r) => ({
      rank: r.rank,
      binId: r.bin_id,
      binCode: r.bin_code,
      zoneCode: r.zone_code,
      zoneTypeCode: r.zone_type_code,
      lotId: r.lot_id,
      lotNumber: r.lot_number,
      expiryDate: r.expiry_date,
      available: r.available,
    }))
}

/** ¿Ya se bajó alguna vez el orden de salida de este almacén? (sin eso, "no hay existencia" no se puede afirmar sin señal). */
export function hasStockExitCopy(warehousePublicId: string): boolean {
  const row = getDb().getFirstSync<{ n: number }>('SELECT COUNT(*) AS n FROM sync_watermark WHERE resource = ?', [`stockExit:${warehousePublicId}`])
  return (row?.n ?? 0) > 0
}

/** Una fila de GET /inventory/exit-options → la que guarda y usa el aparato. */
export function mapExitRow(r: {
  productPublicId?: string
  binId?: number
  binCode?: string | null
  zoneCode?: string | null
  zoneTypeCode?: string | null
  lotId?: number | null
  lotNumber?: string | null
  expiryDate?: string | null
  available?: number
  rank?: number
}): StockExitRow & { productPublicId: string } {
  return {
    productPublicId: r.productPublicId ?? '',
    rank: r.rank ?? 0,
    binId: r.binId ?? 0,
    binCode: r.binCode ?? '',
    zoneCode: r.zoneCode ?? null,
    zoneTypeCode: r.zoneTypeCode ?? null,
    lotId: r.lotId ?? null,
    lotNumber: r.lotNumber ?? null,
    expiryDate: r.expiryDate ?? null,
    available: r.available ?? 0,
  }
}

/** 2026-10-20: ¿la compañía obliga la rotación en las salidas? Lo dice la última respuesta de GET /inventory/exit-options (`rotationEnforcement`); sin copia, no. */
export function readRotationEnforced(): boolean {
  try {
    return getKv(KvKeys.rotationEnforcement) === 'ENFORCE'
  } catch {
    return false
  }
}

/** Guarda lo que contestó el servidor ('ENFORCE' → obligar; cualquier otra cosa → sugerir). */
export function saveRotationEnforcement(value: string | null | undefined): void {
  try {
    setKv(KvKeys.rotationEnforcement, value === 'ENFORCE' ? 'ENFORCE' : 'SUGGEST')
  } catch {
    // es una comodidad sin señal: el servidor decide igual al recibir el despacho
  }
}

const REQUIREMENTS = ['FREE', 'ANY_DOCUMENT', 'EXIT_NOTICE', 'SALES_ORDER'] as const
export type DispatchRequirement = (typeof REQUIREMENTS)[number]

/** Lote 146: «el despacho exige documento» de la compañía, según la última respuesta de GET /inventory/exit-options; sin copia, libre (el servidor decide igual: 409). */
export function readDispatchRequirement(): DispatchRequirement {
  try {
    const v = getKv(KvKeys.dispatchRequirement)
    return (REQUIREMENTS as readonly string[]).includes(v ?? '') ? (v as DispatchRequirement) : 'FREE'
  } catch {
    return 'FREE'
  }
}

export function saveDispatchRequirement(value: string | null | undefined): void {
  try {
    setKv(KvKeys.dispatchRequirement, (REQUIREMENTS as readonly string[]).includes(value ?? '') ? value! : 'FREE')
  } catch {
    // comodidad sin señal
  }
}
