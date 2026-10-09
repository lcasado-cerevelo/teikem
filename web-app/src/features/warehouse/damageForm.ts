// Daños (2026-10-08): reglas del formulario de «Reportar daño», sin pantalla (para probarlas aparte).
import type { DamageReportRequest } from './api'

export type DamageOrigin = 'WAREHOUSE' | 'RECEIPT'
export type DamageDisposition = 'QUARANTINE' | 'DISCARD'

export const DAMAGE_CAUSES = ['ARRIVED_DAMAGED', 'TRANSIT_ACCIDENT', 'WAREHOUSE_ACCIDENT', 'OTHER'] as const

export interface DamageFormValues {
  origin: DamageOrigin
  warehousePublicId: string | null
  receiptPublicId: string
  productPublicId: string | null
  fromBinId: string
  lotId: string
  lotNumber: string
  quantity: number | null
  cause: string
  disposition: DamageDisposition | ''
  /** Posición donde se deja lo dañado (solo si se manda a cuarentena); vacía = la primera de cuarentena del almacén. */
  quarantineBinId: string
  /** Destino final al darle salida de una vez (opcional; vacío = tirado). */
  finalDestination: string
  notes: string
}

/** Causa que se propone según el origen: un daño de recibo suele «venir así»; uno del almacén, un accidente en el almacén. */
export function defaultCause(origin: DamageOrigin): string {
  return origin === 'RECEIPT' ? 'ARRIVED_DAMAGED' : 'WAREHOUSE_ACCIDENT'
}

/** Cuerpo del POST /damage-reports a partir del formulario (la posición de origen solo cuenta en un daño del almacén; el recibo, en uno de recibo). */
export function damageBody(v: DamageFormValues, tracking: string): DamageReportRequest {
  const warehouse = v.origin === 'WAREHOUSE'
  return {
    origin: v.origin,
    warehousePublicId: v.warehousePublicId,
    productPublicId: v.productPublicId,
    receiptPublicId: warehouse ? null : v.receiptPublicId || null,
    fromBinId: warehouse && v.fromBinId ? Number(v.fromBinId) : null,
    quantity: v.quantity,
    cause: v.cause,
    disposition: v.disposition || null,
    quarantineBinId: v.disposition === 'QUARANTINE' && v.quarantineBinId ? Number(v.quarantineBinId) : null,
    lotId: tracking === 'LOT' && v.lotId ? Number(v.lotId) : null,
    // un lote que todavía no existe solo se puede dar de alta al recibir lo dañado en cuarentena
    lot: tracking === 'LOT' && !v.lotId && !warehouse && v.lotNumber.trim() ? { number: v.lotNumber.trim() } : null,
    finalDestination: v.disposition === 'DISCARD' && v.finalDestination ? v.finalDestination : null,
    notes: v.notes.trim() || null,
  }
}
