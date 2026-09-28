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
