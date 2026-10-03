// Ajustes → Operación: lógica pura de la matriz de acciones por estatus y del resumen de recepción por almacén.
import type { WarehouseDto } from '../../warehouse/api'
import { isDirectMode } from '../../warehouse/receivingMode'
import type { StatusCapabilityDto } from '../tenantSettingsApi'

/** Acciones de la matriz de órdenes (las de la maqueta; códigos de `Capabilities` del dominio). */
export const ORDER_CAPABILITIES = ['EDIT_CARGO', 'ASSIGN_TRIP', 'REPRICE', 'CANCEL'] as const

/** ¿El estatus permite la acción? Sin regla (ni de la compañía ni global) = permitido, como `StatusService.IsAllowedAsync`. */
export function capabilityAllowed(rules: readonly StatusCapabilityDto[] | undefined, status: string, cap: string): boolean {
  const rule = rules?.find((c) => (c.statusCode ?? '').toUpperCase() === status.toUpperCase() && (c.capability ?? '').toUpperCase() === cap.toUpperCase())
  return rule ? rule.isAllowed !== false : true
}

export interface RecvRow {
  w: WarehouseDto
  direct: boolean
  /** Con acomodo y sin posición de recepción por defecto: no podría recibir (aviso rojo). */
  noBin: boolean
  open: number | null
  pending: number | null
}

/** Filas del resumen de recepción: modo, falta de posición de recepción y conteos (null = no se pudo leer). */
export function recvSummaryRows(
  warehouses: readonly WarehouseDto[],
  counts: ReadonlyMap<string, { open: number | null; pending: number | null }>,
): RecvRow[] {
  return warehouses.map((w) => {
    const direct = isDirectMode(w.receivingModeCode)
    const c = counts.get(w.publicId ?? '')
    return { w, direct, noBin: !direct && !w.defaultReceivingBinId, open: c?.open ?? null, pending: c?.pending ?? null }
  })
}
