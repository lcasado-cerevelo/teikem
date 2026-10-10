// Señal débil (2026-10-10) — saldos por posición del almacén leídos de la base local (tabla `stock_balance`, que mantiene
// kernel/sync/download.ts desde GET /sync/balances). Las pantallas muestran esto AL INSTANTE y la sincronización en segundo plano lo
// pone al día. El nombre del producto, el código de la posición y el tipo de zona se unen con `product` y `bin` (también locales).
import { getDb } from '../db/database'
import type { BalanceRow } from '../../features/lookup/lookupLogic'

interface Row {
  id: number
  bin_id: number | null
  lot_id: number | null
  zone_type_code: string | null
  bin_code: string | null
  product_public_id: string
  sku: string | null
  product_name: string | null
  lot_number: string | null
  qty_on_hand: number
  qty_reserved: number
}

const SELECT = `
  SELECT b.id, b.bin_id, b.lot_id, bn.zone_type_code, bn.code AS bin_code, b.product_public_id, p.sku, p.name AS product_name,
         b.lot_number, b.qty_on_hand, b.qty_reserved
  FROM stock_balance b
  LEFT JOIN product p ON p.public_id = b.product_public_id
  LEFT JOIN bin bn ON bn.id = b.bin_id`

function toRow(r: Row): BalanceRow {
  return {
    id: r.id,
    binId: r.bin_id,
    lotId: r.lot_id,
    zoneTypeCode: r.zone_type_code,
    binCode: r.bin_code,
    productPublicId: r.product_public_id,
    sku: r.sku ?? '',
    productName: r.product_name ?? '',
    lotNumber: r.lot_number,
    qtyOnHand: r.qty_on_hand,
    qtyAvailable: Math.max(0, r.qty_on_hand - r.qty_reserved),
  }
}

/** Saldos de una posición (todas sus filas: producto × lote), por código de posición y luego SKU. */
export function localBalancesForBin(warehousePublicId: string, binId: number): BalanceRow[] {
  return getDb()
    .getAllSync<Row>(`${SELECT} WHERE b.warehouse_public_id = ? AND b.bin_id = ? ORDER BY p.sku, b.lot_number`, [warehousePublicId, binId])
    .map(toRow)
}

/** Saldos de un producto en el almacén (una fila por posición y lote), por código de posición. */
export function localBalancesForProduct(warehousePublicId: string, productPublicId: string): BalanceRow[] {
  return getDb()
    .getAllSync<Row>(`${SELECT} WHERE b.warehouse_public_id = ? AND b.product_public_id = ? ORDER BY bn.code, b.lot_number`, [
      warehousePublicId,
      productPublicId,
    ])
    .map(toRow)
}

/** Búsqueda libre sobre lo local: SKU, nombre, código de posición o lote que CONTIENEN el texto (sin distinguir mayúsculas). */
export function localBalancesSearch(warehousePublicId: string, text: string, limit = 50): BalanceRow[] {
  const like = `%${text.trim().replace(/[\\%_]/g, (c) => `\\${c}`)}%`
  return getDb()
    .getAllSync<Row>(
      `${SELECT} WHERE b.warehouse_public_id = ? AND (p.sku LIKE ? ESCAPE '\\' OR p.name LIKE ? ESCAPE '\\' OR bn.code LIKE ? ESCAPE '\\' OR b.lot_number LIKE ? ESCAPE '\\')
       ORDER BY bn.code, p.sku LIMIT ?`,
      [warehousePublicId, like, like, like, like, limit],
    )
    .map(toRow)
}

/** ¿El aparato ya bajó saldos de este almacén alguna vez? (si no, «no hay nada» no significa «no hay existencia»). */
export function balancesSyncedAtUtc(warehousePublicId: string): string | null {
  const row = getDb().getFirstSync<{ last_run_utc: string | null }>('SELECT last_run_utc FROM sync_watermark WHERE resource = ?', [
    `balances:${warehousePublicId}`,
  ])
  return row?.last_run_utc ?? null
}
