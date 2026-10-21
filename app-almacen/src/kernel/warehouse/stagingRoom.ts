// Lote 175 — espacio libre del staging (zona de recepción) del almacén, calculado EN EL APARATO: cupo de las posiciones de la zona STAGING que tienen cupo
// (sincronizado) menos lo que hay según los saldos locales. null = ninguna posición STAGING con cupo conocido (sin aviso). Es una ayuda: el servidor
// valida y para el cupo solo avisa.
import { getDb } from '../db/database'

export function localStagingFree(warehousePublicId: string): number | null {
  const db = getDb()
  const bins = db.getAllSync<{ id: number; max_capacity_qty: number }>(
    "SELECT id, max_capacity_qty FROM bin WHERE warehouse_public_id = ? AND zone_type_code = 'STAGING' AND is_active = 1 AND capacity_known = 1 AND max_capacity_qty IS NOT NULL",
    [warehousePublicId],
  )
  if (bins.length === 0) return null
  let free = 0
  for (const b of bins) {
    const onHand = db.getFirstSync<{ q: number | null }>('SELECT SUM(qty_on_hand) AS q FROM stock_balance WHERE warehouse_public_id = ? AND bin_id = ?', [warehousePublicId, b.id])?.q ?? 0
    free += Math.max(0, b.max_capacity_qty - onHand)
  }
  return free
}
