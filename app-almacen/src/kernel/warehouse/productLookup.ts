// Lote 8A-app — producto por código de barras o SKU, de la base local (kernel/sync/download.ts lo mantiene al día).
// Compartido por Recibir, Despacho y Conteo.
import { getDb } from '../db/database'

export type TrackingType = 'NONE' | 'LOT' | 'SERIAL'

export interface LocalProduct {
  publicId: string
  sku: string
  name: string
  trackingTypeCode: TrackingType
  /** Dueño 3PL del producto (null si es inventario propio del tenant), ya sincronizado con el producto. */
  ownerClientPublicId: string | null
  ownerName: string | null
}

export function findProductByCode(code: string): LocalProduct | null {
  const row = getDb().getFirstSync<{
    public_id: string
    sku: string
    name: string
    tracking_type_code: string | null
    owner_client_public_id: string | null
    owner_name: string | null
  }>(
    'SELECT public_id, sku, name, tracking_type_code, owner_client_public_id, owner_name FROM product WHERE is_active = 1 AND (barcode = ? OR sku = ?) LIMIT 1',
    [code, code],
  )
  if (!row) return null
  const tracking = row.tracking_type_code === 'LOT' || row.tracking_type_code === 'SERIAL' ? row.tracking_type_code : 'NONE'
  return {
    publicId: row.public_id,
    sku: row.sku,
    name: row.name,
    trackingTypeCode: tracking,
    ownerClientPublicId: row.owner_client_public_id,
    ownerName: row.owner_name,
  }
}

/** Empaque del producto (Caja de 12, Barril de 50…) de la base local, por SKU o por id público; null si no tiene o no está descargado. */
export function findProductPack(ref: { sku?: string | null; publicId?: string | null }): { name: string; qty: number } | null {
  try {
    const row = getDb().getFirstSync<{ pack_uom_name: string | null; pack_qty: number | null }>(
      'SELECT pack_uom_name, pack_qty FROM product WHERE (public_id = ? OR sku = ?) LIMIT 1',
      [ref.publicId ?? '', ref.sku ?? ''],
    )
    return row?.pack_uom_name && row.pack_qty && row.pack_qty > 0 ? { name: row.pack_uom_name, qty: row.pack_qty } : null
  } catch {
    return null
  }
}
