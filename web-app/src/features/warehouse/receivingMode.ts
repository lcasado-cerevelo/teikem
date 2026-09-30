// Lote 16 — modo de recepción (lógica pura; espejo de `ReceivingModeRules.cs`). El almacén tiene su modo (el de los recibos
// nuevos) y cada recibo guarda la copia con que se abrió (D1, D2); sin valor = PUTAWAY ("Con acomodo"). En DIRECT
// ("Directo a posición") cada línea con mercancía lleva su posición destino (nunca de recepción ni de cruce de muelle, D5) y
// al confirmar entra ahí sin tareas; el cupo solo avisa (D4). Pruebas en receivingMode.test.ts.
import type { components } from '../../kernel/api/schema'

type Schemas = components['schemas']

/** Dominio del catálogo (LookupCode Entity = 'ReceivingMode'). */
export const RECEIVING_MODE_DOMAIN = 'ReceivingMode'
export const RECEIVING_MODES = { putaway: 'PUTAWAY', direct: 'DIRECT' } as const
export type ReceivingModeCode = (typeof RECEIVING_MODES)[keyof typeof RECEIVING_MODES]

/** Tipos de zona que NO pueden ser posición destino (D5): recepción y cruce de muelle. La cuarentena sí. */
export const TARGET_EXCLUDED_ZONE_TYPES = ['STAGING', 'CROSSDOCK'] as const
/** Tipos de zona de la posición de recepción (la del encabezado del recibo y la "por defecto" del almacén, D12). */
export const RECEIVING_ZONE_TYPES = ['STAGING', 'CROSSDOCK'] as const

/** Código normalizado: DIRECT (sin distinguir mayúsculas) o, cualquier otra cosa, PUTAWAY. */
export function normalizeReceivingMode(code: string | null | undefined): ReceivingModeCode {
  return (code ?? '').trim().toUpperCase() === RECEIVING_MODES.direct ? RECEIVING_MODES.direct : RECEIVING_MODES.putaway
}

export function isDirectMode(code: string | null | undefined): boolean {
  return normalizeReceivingMode(code) === RECEIVING_MODES.direct
}

/** Modo efectivo de un recibo: su copia; sin copia, el del almacén; sin ninguno, PUTAWAY. */
export function effectiveReceivingMode(receiptMode: string | null | undefined, warehouseMode: string | null | undefined): ReceivingModeCode {
  return normalizeReceivingMode(receiptMode && receiptMode.trim() ? receiptMode : warehouseMode)
}

/** Etiqueta del modo en el catálogo (`useLookups('ReceivingMode')`); sin catálogo, el código. */
export function receivingModeLabel(code: string | null | undefined, options: readonly { code: string; label: string }[]): string {
  const c = normalizeReceivingMode(code)
  return options.find((o) => o.code.toUpperCase() === c)?.label ?? c
}

/** La etiqueta dentro de una oración ("entrarán directo a posición"): primera letra en minúscula. */
export function inSentence(label: string): string {
  return label ? label.charAt(0).toLocaleLowerCase() + label.slice(1) : label
}

/** ¿Cambia el modo? (lo que decide si se pide confirmación al guardar la ficha del almacén). */
export function receivingModeChanged(current: string | null | undefined, next: string | null | undefined): boolean {
  return normalizeReceivingMode(current) !== normalizeReceivingMode(next)
}

/** Lo que se edita en la sección "Recepción" de la ficha del almacén. */
export interface WarehouseReceivingValues {
  receivingMode: string
  /** Id de la posición como texto ('' = ninguna: la primera STAGING del almacén). */
  defaultReceivingBinId: string
}

/**
 * Parte del PATCH del almacén con la sección "Recepción": solo lo que cambió. Modo → `receivingMode`; posición por defecto →
 * `defaultReceivingBinId` o, al vaciarla, `clearDefaultReceivingBin` (D12).
 */
export function warehouseReceivingPatch(
  w: Pick<Schemas['WarehouseDto'], 'receivingModeCode' | 'defaultReceivingBinId'>,
  v: WarehouseReceivingValues,
): Pick<Schemas['WarehousePatchRequest'], 'receivingMode' | 'defaultReceivingBinId' | 'clearDefaultReceivingBin'> {
  const out: Pick<Schemas['WarehousePatchRequest'], 'receivingMode' | 'defaultReceivingBinId' | 'clearDefaultReceivingBin'> = {}
  if (receivingModeChanged(w.receivingModeCode, v.receivingMode)) out.receivingMode = normalizeReceivingMode(v.receivingMode)
  const bin = v.defaultReceivingBinId ? Number(v.defaultReceivingBinId) : null
  if (bin !== (w.defaultReceivingBinId ?? null)) {
    if (bin === null) out.clearDefaultReceivingBin = true
    else out.defaultReceivingBinId = bin
  }
  return out
}

/** ¿La cantidad excede el espacio libre de la posición destino? Sin cupo (null) nunca excede (D4: solo avisa). */
export function exceedsCapacity(freeQty: number | null | undefined, qty: number | null | undefined): boolean {
  return freeQty != null && qty != null && Number.isFinite(qty) && qty > freeQty
}

/**
 * ¿La línea necesita posición destino para confirmar un recibo directo? (`ReceivingModeRules.NeedsTarget`): sí si recibió
 * algo, salvo un producto por lote sin lote; y no si tiene cruce de muelle asignado (entra a recepción, D11).
 */
export function needsTarget(line: {
  received: number | null | undefined
  trackingTypeCode?: string | null
  lotNumber?: string | null
  allocatedToCrossDock?: number | null
}): boolean {
  if (line.received == null || !Number.isFinite(line.received) || line.received <= 0) return false
  if ((line.allocatedToCrossDock ?? 0) > 0) return false
  if ((line.trackingTypeCode ?? '').toUpperCase() === 'LOT' && !line.lotNumber) return false
  return true
}

/** Primera sugerencia que cabe (o la primera, si ninguna cabe): la de la pista "Sugerida: …". */
export function topSuggestion<T extends { fits?: boolean }>(suggestions: readonly T[] | null | undefined): T | null {
  const list = suggestions ?? []
  return list.find((s) => s.fits !== false) ?? list[0] ?? null
}
