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

// ===== Lote 248: avisos de vencimiento de documentos (Ajustes → Operación) =====
export const DOC_EXPIRY_DEFAULT_THRESHOLDS = [30, 15, 5]
export const DOC_EXPIRY_MAX_THRESHOLDS = 10
export const DOC_EXPIRY_RANGE = 365

export type ThresholdErrorKey = 'format' | 'repeated' | 'tooMany'

/** Umbral escrito a mano: entero entre -365 y 365 (positivo = antes de vencer, 0 = el día, negativo = después). null si no se entiende. */
export function parseThreshold(text: string): number | null {
  const clean = text.trim()
  if (!/^-?\d+$/.test(clean)) return null
  const n = Number(clean)
  return Math.abs(n) <= DOC_EXPIRY_RANGE ? n : null
}

/** Problema de la lista de umbrales (el servidor repite la regla con sus mensajes). */
export function thresholdsError(list: readonly number[]): ThresholdErrorKey | null {
  if (list.some((n) => !Number.isInteger(n) || Math.abs(n) > DOC_EXPIRY_RANGE)) return 'format'
  if (new Set(list).size !== list.length) return 'repeated'
  if (list.length > DOC_EXPIRY_MAX_THRESHOLDS) return 'tooMany'
  return null
}

/** De mayor a menor, como los guarda el servidor. */
export function sortThresholds(list: readonly number[]): number[] {
  return [...list].sort((a, b) => b - a)
}

export function sameThresholds(a: readonly number[], b: readonly number[]): boolean {
  return a.length === b.length && sortThresholds(a).every((n, i) => n === sortThresholds(b)[i])
}
