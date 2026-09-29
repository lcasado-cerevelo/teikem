// Lógica pura de la pantalla Ubicaciones (`LocationsScreen`, maqueta `ubicaciones()`): ocupación por zona, productos por
// posición (desde los saldos del almacén), estado de la posición y filtros Zona / Tipo / Producto / Estado.
// La posición no tiene capacidad registrada en el esquema (`WarehouseBin` solo tiene `MaxWeightKg`), así que el estado es
// 'Vacía' u 'Ocupada' (la maqueta distingue 'Parcial' y 'Llena' con la capacidad) y la barra de ocupación de la fila es
// relativa a la posición con más unidades del almacén.
import type { BalanceDto, WarehouseBinDto, WarehouseZoneDto } from './api'
import { productLabel } from './api'

export type BinState = 'EMPTY' | 'OCCUPIED'
export const BIN_STATES: readonly BinState[] = ['EMPTY', 'OCCUPIED']

/** Producto guardado en una posición (sin repetir aunque tenga varios lotes). */
export interface BinProduct {
  publicId: string
  sku: string
  name: string
}

/** Fila de la tabla de Ubicaciones. */
export interface LocationRow {
  bin: WarehouseBinDto
  /** Nombre de la zona (o su código si no se encontró). */
  zoneName: string
  zoneTypeCode: string
  products: BinProduct[]
  state: BinState
  /** 0..100: unidades de la posición respecto a la posición con más unidades del almacén. */
  fill: number
}

/** Ocupación de una zona para el río: posiciones con existencias / posiciones activas. */
export interface ZoneOccupancy {
  zone: WarehouseZoneDto
  used: number
  total: number
  /** Porcentaje entero (0 si la zona no tiene posiciones). */
  pct: number
}

export interface LocationFilters {
  /** Ids de zona como texto (vacío = todas). */
  zoneIds: readonly string[]
  /** Códigos de tipo de zona (vacío = todos). */
  zoneTypes: readonly string[]
  /** publicIds de producto (vacío = todos): la posición debe tener al menos uno. */
  products: readonly string[]
  /** Estados (vacío = todos). */
  states: readonly string[]
}

export const EMPTY_LOCATION_FILTERS: LocationFilters = { zoneIds: [], zoneTypes: [], products: [], states: [] }

/** Estado de una posición según sus unidades en mano. */
export function binState(qtyOnHand: number | null | undefined): BinState {
  return (qtyOnHand ?? 0) > 0 ? 'OCCUPIED' : 'EMPTY'
}

/** Productos por posición a partir de las líneas de saldo (solo con unidades en mano), ordenados por SKU. */
export function productsByBin(lines: readonly BalanceDto[]): Map<number, BinProduct[]> {
  const map = new Map<number, BinProduct[]>()
  for (const l of lines) {
    if (l.binId == null || (l.qtyOnHand ?? 0) <= 0 || !l.productPublicId) continue
    const list = map.get(l.binId) ?? []
    if (!list.some((p) => p.publicId === l.productPublicId)) {
      list.push({ publicId: l.productPublicId, sku: l.sku ?? '', name: l.productName ?? l.sku ?? '' })
      map.set(l.binId, list)
    }
  }
  for (const list of map.values()) list.sort((a, b) => a.sku.localeCompare(b.sku, undefined, { numeric: true }))
  return map
}

/** Opciones del filtro Producto: los productos que hay en el almacén ("SKU · Nombre"), ordenados por SKU. */
export function productOptions(byBin: ReadonlyMap<number, readonly BinProduct[]>): { value: string; label: string }[] {
  const seen = new Map<string, BinProduct>()
  for (const list of byBin.values()) for (const p of list) if (!seen.has(p.publicId)) seen.set(p.publicId, p)
  return [...seen.values()]
    .sort((a, b) => a.sku.localeCompare(b.sku, undefined, { numeric: true }))
    .map((p) => ({ value: p.publicId, label: productLabel(p) }))
}

/** Un nodo por zona (en el orden recibido) con sus posiciones activas ocupadas y totales. */
export function zoneOccupancy(zones: readonly WarehouseZoneDto[], bins: readonly WarehouseBinDto[]): ZoneOccupancy[] {
  return zones.map((zone) => {
    const inZone = bins.filter((b) => b.zoneId === zone.id && b.isActive !== false)
    const used = inZone.filter((b) => binState(b.qtyOnHand) === 'OCCUPIED').length
    const total = inZone.length
    return { zone, used, total, pct: total > 0 ? Math.round((100 * used) / total) : 0 }
  })
}

/** Filas de la tabla: cada posición con su zona, productos, estado y barra relativa. */
export function buildLocationRows(
  bins: readonly WarehouseBinDto[],
  zones: readonly WarehouseZoneDto[],
  byBin: ReadonlyMap<number, readonly BinProduct[]>,
): LocationRow[] {
  const zoneById = new Map(zones.map((z) => [z.id, z]))
  const maxQty = bins.reduce((m, b) => Math.max(m, b.qtyOnHand ?? 0), 0)
  return bins.map((bin) => {
    const zone = zoneById.get(bin.zoneId)
    const qty = bin.qtyOnHand ?? 0
    return {
      bin,
      zoneName: zone?.name || bin.zoneCode || '',
      zoneTypeCode: bin.zoneTypeCode ?? zone?.zoneTypeCode ?? '',
      products: [...(bin.id != null ? (byBin.get(bin.id) ?? []) : [])],
      state: binState(qty),
      fill: qty > 0 && maxQty > 0 ? Math.max(1, Math.min(100, Math.round((100 * qty) / maxQty))) : 0,
    }
  })
}

/** Aplica los filtros Zona, Tipo, Producto y Estado (cada uno vacío = sin filtro). */
export function filterLocationRows(rows: readonly LocationRow[], f: LocationFilters): LocationRow[] {
  return rows.filter(
    (r) =>
      (f.zoneIds.length === 0 || f.zoneIds.includes(String(r.bin.zoneId))) &&
      (f.zoneTypes.length === 0 || f.zoneTypes.includes(r.zoneTypeCode)) &&
      (f.products.length === 0 || r.products.some((p) => f.products.includes(p.publicId))) &&
      (f.states.length === 0 || f.states.includes(r.state)),
  )
}
