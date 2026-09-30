// Lote 11 — cupo máximo en bloque (`POST /api/v1/warehouses/{publicId}/bins/capacity`, `warehouse.manage`): lógica pura
// del modal "Asignar cupo" (`BinCapacityModal`), probada en binCapacity.test.ts.
// - Alcance: Zona (ids), Pasillo, Rack, Nivel y Posición ("contiene", como el listado), "Solo posiciones sin cupo" y "Todo el
//   almacén" (`allBins`, solo cuando no hay ningún filtro: el API exige uno de los dos). Siempre posiciones activas
//   (`includeInactive: false`).
// - Acción: exactamente una de "Cupo máximo" (entero > 0 → `maxCapacityQty`) o "Quitar cupo" (`clear: true`).
// - Vista previa: `GET .../bins?take=1` con los MISMOS filtros → `total` (= `matched` del POST). El GET no tiene
//   `onlyWithoutCapacity`, así que con esa casilla el conteo sale de otra parte (ver `useBinCapacityPreview` en api.ts):
//   sin filtros de texto, Σ `binsWithoutCapacity` de las zonas; con texto, se cuentan las posiciones sin cupo recorriendo
//   el listado si son pocas (`BIN_CAPACITY_EXACT_LIMIT`) o se muestra el total como tope ("como máximo N").
import type { components } from '../../kernel/api/schema'
import type { GetQuery, WarehouseZoneDto } from './api'

export type BinCapacityRequest = components['schemas']['WarehouseBinCapacityRequest']
export type BinCapacityResult = components['schemas']['WarehouseBinCapacityResultDto']
type BinsQuery = GetQuery<'/api/v1/warehouses/{publicId}/bins'>

/** Alcance elegido en el modal (textos tal como se escriben; se recortan al armar la consulta). */
export interface BinCapacityScope {
  /** Ids de zona como texto (valores de `SearchSelect`); vacío = sin filtro de zona. */
  zoneIds: readonly string[]
  aisle: string
  rack: string
  level: string
  position: string
  /** Solo las posiciones que hoy no tienen cupo (no pisa los ya capturados). */
  onlyWithoutCapacity: boolean
  /** "Todo el almacén": solo cuenta cuando no hay ningún filtro de posiciones. */
  allBins: boolean
}

export const EMPTY_BIN_CAPACITY_SCOPE: BinCapacityScope = {
  zoneIds: [],
  aisle: '',
  rack: '',
  level: '',
  position: '',
  onlyWithoutCapacity: false,
  allBins: false,
}

/** Lo que el modal puede recibir ya elegido desde la pantalla que lo abre (zona de `?zone=`, filtros de la pestaña). */
export type BinCapacityInitial = Partial<Pick<BinCapacityScope, 'zoneIds' | 'aisle' | 'rack' | 'level' | 'position'>>

/** Acción: fijar un cupo (entero > 0) o quitarlo. */
export type BinCapacityAction = { kind: 'set'; qty: number } | { kind: 'clear' }

/** Más posiciones que esto (o "Todo el almacén") piden confirmación antes de aplicar. */
export const BIN_CAPACITY_CONFIRM_THRESHOLD = 100
/** Con "Solo posiciones sin cupo" y filtros de texto, hasta cuántas posiciones se recorren para contar exacto. */
export const BIN_CAPACITY_EXACT_LIMIT = 1000
/** Tope de `MaxCapacityQty` (INT del esquema). */
export const MAX_BIN_CAPACITY = 2_147_483_647

/** Filtros de texto no vacíos, recortados. */
function textFilters(scope: BinCapacityScope): Pick<BinsQuery, 'aisle' | 'rack' | 'level' | 'position'> {
  const out: Pick<BinsQuery, 'aisle' | 'rack' | 'level' | 'position'> = {}
  const aisle = scope.aisle.trim()
  const rack = scope.rack.trim()
  const level = scope.level.trim()
  const position = scope.position.trim()
  if (aisle) out.aisle = aisle
  if (rack) out.rack = rack
  if (level) out.level = level
  if (position) out.position = position
  return out
}

/** ¿Hay algún filtro de texto (pasillo, rack, nivel o posición)? */
export function hasTextFilter(scope: BinCapacityScope): boolean {
  return Object.keys(textFilters(scope)).length > 0
}

/** ¿Hay algún filtro de posiciones (zona o texto)? Es el `HasBinFilter` del API para lo que ofrece el modal. */
export function hasScopeFilter(scope: BinCapacityScope): boolean {
  return scope.zoneIds.length > 0 || hasTextFilter(scope)
}

/** El alcance es aplicable: algún filtro, o ninguno con "Todo el almacén" marcado. */
export function scopeReady(scope: BinCapacityScope): boolean {
  return hasScopeFilter(scope) || scope.allBins
}

/** ¿Se manda `allBins: true`? Solo sin filtros y con la casilla marcada (con filtros la casilla se ignora). */
export function sendsAllBins(scope: BinCapacityScope): boolean {
  return !hasScopeFilter(scope) && scope.allBins
}

/** Consulta de la vista previa (`GET .../bins`): mismos filtros que el POST, solo activas, `take=1` (basta `total`). */
export function capacityPreviewQuery(scope: BinCapacityScope): BinsQuery {
  const q: BinsQuery = { includeInactive: false, ...textFilters(scope), take: 1 }
  if (scope.zoneIds.length > 0) q.zoneIds = scope.zoneIds.map(Number)
  return q
}

/** Cuerpo del POST: los filtros, `allBins` solo sin filtros y exactamente uno de `maxCapacityQty` o `clear`. */
export function capacityRequestBody(scope: BinCapacityScope, action: BinCapacityAction): BinCapacityRequest {
  const body: BinCapacityRequest = {
    ...textFilters(scope),
    includeInactive: false,
    onlyWithoutCapacity: scope.onlyWithoutCapacity,
    allBins: sendsAllBins(scope),
  }
  if (scope.zoneIds.length > 0) body.zoneIds = scope.zoneIds.map(Number)
  if (action.kind === 'set') body.maxCapacityQty = action.qty
  else body.clear = true
  return body
}

/** Posiciones activas sin cupo de las zonas elegidas (vacío = todas), según `binsWithoutCapacity` del listado de zonas. */
export function zonesWithoutCapacity(zones: readonly WarehouseZoneDto[], zoneIds: readonly string[]): number {
  const chosen = zoneIds.length > 0 ? zones.filter((z) => zoneIds.includes(String(z.id))) : zones
  return chosen.reduce((sum, z) => sum + (z.binsWithoutCapacity ?? 0), 0)
}

/** Deja solo los ids de zona que existen en la lista (p. ej. `?zone=` de otro almacén o de una zona dada de baja). */
export function sanitizeZoneIds(zoneIds: readonly string[], zones: readonly WarehouseZoneDto[]): string[] {
  return zoneIds.filter((id) => zones.some((z) => String(z.id) === id))
}

export type CapacityQtyCheck = { ok: true; value: number } | { ok: false; code: 'required' | 'integer' | 'positive' | 'tooLarge' }

/** Valida el "Cupo máximo" escrito: obligatorio, entero, > 0 y dentro de INT. Los códigos se traducen con `…errors.<code>`. */
export function parseCapacityQty(text: string): CapacityQtyCheck {
  const s = text.trim()
  if (s === '') return { ok: false, code: 'required' }
  const n = Number(s)
  if (Number.isNaN(n) || !Number.isInteger(n)) return { ok: false, code: 'integer' }
  if (n <= 0) return { ok: false, code: 'positive' }
  if (n > MAX_BIN_CAPACITY) return { ok: false, code: 'tooLarge' }
  return { ok: true, value: n }
}

/** ¿Pedir confirmación? Más de `BIN_CAPACITY_CONFIRM_THRESHOLD` posiciones o "Todo el almacén". */
export function needsCapacityConfirmation(count: number, scope: BinCapacityScope): boolean {
  return count > BIN_CAPACITY_CONFIRM_THRESHOLD || sendsAllBins(scope)
}
