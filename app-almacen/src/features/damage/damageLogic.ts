// 2026-10-08 — Reglas puras de «Daño» en la app (sin pantalla ni API): qué se captura, cuándo está listo y el cuerpo del POST /damage-reports.
// Un daño del ALMACÉN se reporta desde la posición donde está; uno que LLEGÓ DAÑADO se reporta con el número del recibo. Qué hacer con ello:
// mandarlo a cuarentena o desecharlo de una vez. La causa es solo informativa.
export type DamageOrigin = 'WAREHOUSE' | 'RECEIPT'
export type DamageDisposition = 'QUARANTINE' | 'DISCARD'

export const DAMAGE_CAUSES = [
  { code: 'ARRIVED_DAMAGED', key: 'damage.causeArrived' },
  { code: 'TRANSIT_ACCIDENT', key: 'damage.causeTransit' },
  { code: 'WAREHOUSE_ACCIDENT', key: 'damage.causeWarehouse' },
  { code: 'OTHER', key: 'damage.causeOther' },
] as const

/** Destino final al darle salida a lo dañado (catálogo DamageFinalDestination; el aparato usa los cuatro de fábrica). */
export const DAMAGE_DESTINATIONS = [
  { code: 'DISCARDED_WASTE', key: 'damage.destDiscarded' },
  { code: 'RETURNED_TO_SUPPLIER', key: 'damage.destReturned' },
  { code: 'DONATED', key: 'damage.destDonated' },
  { code: 'SOLD_AS_SALVAGE', key: 'damage.destSalvage' },
] as const

export interface DamageProduct {
  publicId: string
  sku: string
  name: string
  trackingTypeCode: string | null
}

export interface DamageDraft {
  origin: DamageOrigin | null
  /** Recibo del que llegó dañado (origen RECEIPT). */
  receipt: { publicId: string; number: string } | null
  /** Posición donde está lo dañado (origen WAREHOUSE). */
  bin: { id: number; code: string } | null
  product: DamageProduct | null
  qtyText: string
  lot: string
  cause: string
}

export const EMPTY_DAMAGE: DamageDraft = { origin: null, receipt: null, bin: null, product: null, qtyText: '', lot: '', cause: '' }

/** La causa que se propone según el origen: lo que llegó dañado «vino así»; lo del almacén, un accidente en el almacén. */
export function defaultCause(origin: DamageOrigin): string {
  return origin === 'RECEIPT' ? 'ARRIVED_DAMAGED' : 'WAREHOUSE_ACCIDENT'
}

/** Paso siguiente de la captura: qué se le pide a quien reporta. */
export type DamageStep = 'origin' | 'source' | 'product' | 'details'

export function damageStep(d: DamageDraft): DamageStep {
  if (!d.origin) return 'origin'
  if (d.origin === 'WAREHOUSE' ? !d.bin : !d.receipt) return 'source'
  if (!d.product) return 'product'
  return 'details'
}

/** Cantidad escrita (acepta coma decimal): null si no es un número mayor que 0. */
export function parseDamageQty(text: string): number | null {
  const n = Number(text.trim().replace(',', '.'))
  return Number.isFinite(n) && n > 0 ? n : null
}

export type DamageBlock = 'qty' | 'lot' | null

/** Qué falta para poder reportar (null = listo). Un producto por lote pide el lote. */
export function damageBlock(d: DamageDraft): DamageBlock {
  if (parseDamageQty(d.qtyText) === null) return 'qty'
  if (d.product?.trackingTypeCode === 'LOT' && !d.lot.trim()) return 'lot'
  return null
}

/** Cuerpo del POST /api/v1/damage-reports. */
export function buildDamageRequest(warehousePublicId: string, d: DamageDraft, disposition: DamageDisposition, finalDestination: string | null = null) {
  const warehouse = d.origin === 'WAREHOUSE'
  return {
    origin: d.origin,
    warehousePublicId,
    productPublicId: d.product?.publicId ?? null,
    receiptPublicId: warehouse ? null : (d.receipt?.publicId ?? null),
    fromBinId: warehouse ? (d.bin?.id ?? null) : null,
    quantity: parseDamageQty(d.qtyText),
    cause: d.cause || (d.origin ? defaultCause(d.origin) : null),
    disposition,
    finalDestination: disposition === 'DISCARD' ? finalDestination : null,
    lot: d.product?.trackingTypeCode === 'LOT' && d.lot.trim() ? { number: d.lot.trim() } : undefined,
  }
}
