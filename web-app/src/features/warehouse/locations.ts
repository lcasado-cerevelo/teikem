// Lógica pura de la pantalla Ubicaciones (`LocationsScreen`, maqueta `ubicaciones()`), Lote 1 (1.2 y 1.3).
// - Recuadros del río (uno por zona): ocupado vs. capacidad real de la zona, con los totales que ya calcula el API en
//   `WarehouseZoneDto`: capacidad = `capacityQty` (Σ cupo de sus posiciones activas con cupo), ocupado =
//   `qtyOnHandInCapacityBins` (existencia en esas mismas posiciones). Las posiciones sin cupo configurado no entran al
//   porcentaje (se avisan aparte con `binsWithoutCapacity`); si ninguna tiene cupo se muestra la existencia total sin
//   porcentaje. Nunca se inventa un porcentaje.
// - Estatus de una posición (`occupancy` del API): Vacía / Parcial / Llena / Sin cupo, con la misma regla que
//   `WarehouseRules.Occupancy` del servidor (réplica solo como respaldo si el DTO no la trae).
// - Filtros: todos van al servidor (`zoneIds`, `productPublicIds` y `occupancy`); el filtro Tipo (de zona) no existe en el
//   API y se traduce a los ids de las zonas de ese tipo, cruzados con el filtro Zona.
import type { GetQuery, WarehouseBinDto, WarehouseZoneDto } from './api'

export type BinOccupancy = 'EMPTY' | 'PARTIAL' | 'FULL' | 'NO_CAPACITY'
/** Estatus de posición, en el orden del filtro (códigos de `BinOccupancies` del dominio). */
export const BIN_OCCUPANCIES: readonly BinOccupancy[] = ['EMPTY', 'PARTIAL', 'FULL', 'NO_CAPACITY']

/** Tono del chip de cada estatus (vacía gris, parcial verde, llena roja como la maqueta, sin cupo en alerta). */
export const OCCUPANCY_TONE: Record<BinOccupancy, 'cap' | 'disp' | 'fail' | 'warn'> = {
  EMPTY: 'cap',
  PARTIAL: 'disp',
  FULL: 'fail',
  NO_CAPACITY: 'warn',
}

function isOccupancy(v: string | null | undefined): v is BinOccupancy {
  return (BIN_OCCUPANCIES as readonly string[]).includes(v ?? '')
}

/**
 * Estatus de una posición: el que manda el API (`occupancy`); si no llega, la misma regla del servidor: sin existencia =
 * EMPTY; con existencia y sin cupo = NO_CAPACITY; existencia ≥ cupo = FULL; si no, PARTIAL.
 */
export function binOccupancy(bin: Pick<WarehouseBinDto, 'occupancy' | 'qtyOnHand' | 'maxCapacityQty'>): BinOccupancy {
  if (isOccupancy(bin.occupancy)) return bin.occupancy
  const qty = bin.qtyOnHand ?? 0
  if (qty <= 0) return 'EMPTY'
  if (bin.maxCapacityQty == null) return 'NO_CAPACITY'
  return qty >= bin.maxCapacityQty ? 'FULL' : 'PARTIAL'
}

/** Porcentaje entero de ocupación de una posición respecto a su cupo; null sin cupo (no hay porcentaje que mostrar).
 *  Puede pasar de 100 si la existencia excede el cupo (la barra se recorta, el texto dice el valor real). */
export function binFillPct(qtyOnHand: number | null | undefined, maxCapacityQty: number | null | undefined): number | null {
  if (maxCapacityQty == null || maxCapacityQty <= 0) return null
  return Math.round((100 * Math.max(0, qtyOnHand ?? 0)) / maxCapacityQty)
}

/** Qué mostrar en la columna Producto: nada, el nombre (un solo producto) o "N productos". */
export type BinProductCell = { kind: 'none' } | { kind: 'one'; name: string; sku: string } | { kind: 'many'; count: number }

export function binProductCell(bin: Pick<WarehouseBinDto, 'productCount' | 'singleProductName' | 'singleProductSku'>): BinProductCell {
  const count = bin.productCount ?? 0
  if (count <= 0) return { kind: 'none' }
  if (count === 1) {
    const sku = bin.singleProductSku ?? ''
    return { kind: 'one', name: bin.singleProductName || sku, sku }
  }
  return { kind: 'many', count }
}

/**
 * Recuadro de zona del río. `mode`:
 * - `capacity`: al menos una posición con cupo → `occupied / capacity` y `pct` (las sin cupo se avisan con `binsWithoutCapacity`);
 * - `noCapacity`: tiene posiciones pero ninguna con cupo → solo la existencia total (`qtyOnHand`), `pct` null;
 * - `noBins`: la zona no tiene posiciones activas.
 */
export interface ZoneCapacity {
  zone: WarehouseZoneDto
  mode: 'capacity' | 'noCapacity' | 'noBins'
  capacity: number
  occupied: number
  /** Porcentaje entero ocupado/capacidad (puede pasar de 100); null sin capacidad. */
  pct: number | null
  binsWithoutCapacity: number
  qtyOnHand: number
  binCount: number
}

export function zoneCapacity(zone: WarehouseZoneDto): ZoneCapacity {
  const capacity = zone.capacityQty ?? 0
  const occupied = zone.qtyOnHandInCapacityBins ?? 0
  const binCount = zone.binCount ?? 0
  const mode: ZoneCapacity['mode'] = binCount <= 0 ? 'noBins' : capacity > 0 ? 'capacity' : 'noCapacity'
  return {
    zone,
    mode,
    capacity,
    occupied,
    pct: mode === 'capacity' ? Math.round((100 * occupied) / capacity) : null,
    binsWithoutCapacity: zone.binsWithoutCapacity ?? 0,
    qtyOnHand: zone.qtyOnHand ?? 0,
    binCount,
  }
}

/** Un recuadro por zona, en el orden recibido. */
export function zoneCapacities(zones: readonly WarehouseZoneDto[]): ZoneCapacity[] {
  return zones.map(zoneCapacity)
}

/** Clic en un recuadro: si esa zona ya es la única elegida, se quita el filtro; si no, queda solo esa zona. */
export function toggleZoneSelection(current: readonly string[], zoneId: string): string[] {
  return current.length === 1 && current[0] === zoneId ? [] : [zoneId]
}

/** Zonas de la URL (`?zone=3&zone=5` o `?zone=3,5`): solo ids enteros positivos, sin repetir. */
export function parseZoneParam(values: readonly string[]): string[] {
  const out: string[] = []
  for (const v of values.flatMap((x) => x.split(','))) {
    const s = v.trim()
    if (/^[1-9]\d*$/.test(s) && !out.includes(s)) out.push(s)
  }
  return out
}

export interface LocationFilters {
  /** Ids de zona como texto (vacío = todas); es lo que va en `?zone=`. */
  zoneIds: readonly string[]
  /** Códigos de tipo de zona (vacío = todos). */
  zoneTypes: readonly string[]
  /** publicIds de producto (vacío = todos): la posición debe tener existencia de al menos uno. */
  productPublicIds: readonly string[]
  /** Estatus (vacío = todos). */
  occupancy: readonly string[]
}

export const EMPTY_LOCATION_FILTERS: LocationFilters = { zoneIds: [], zoneTypes: [], productPublicIds: [], occupancy: [] }

export type BinListQuery = GetQuery<'/api/v1/warehouses/{publicId}/bins'>

/**
 * Consulta del listado (sin `skip`/`take`) a partir de los filtros. Tipo se traduce a las zonas de ese tipo y se cruza
 * con Zona. `impossible` = la combinación no deja ninguna zona (p. ej. Zona A + un tipo que A no tiene): la pantalla no
 * consulta y muestra la tabla vacía (el API leería `zoneIds` vacío como "todas").
 */
export function buildBinListQuery(f: LocationFilters, zones: readonly WarehouseZoneDto[]): { query: BinListQuery; impossible: boolean } {
  let zoneIds: number[] | undefined = f.zoneIds.length > 0 ? f.zoneIds.map(Number) : undefined
  if (f.zoneTypes.length > 0) {
    const typed = zones.filter((z) => z.id != null && f.zoneTypes.includes(z.zoneTypeCode ?? '')).map((z) => z.id as number)
    zoneIds = zoneIds ? zoneIds.filter((id) => typed.includes(id)) : typed
  }
  const query: BinListQuery = {
    includeInactive: false,
    zoneIds: zoneIds && zoneIds.length > 0 ? zoneIds : undefined,
    productPublicIds: f.productPublicIds.length > 0 ? [...f.productPublicIds] : undefined,
    occupancy: f.occupancy.length > 0 ? [...f.occupancy] : undefined,
  }
  return { query, impossible: zoneIds !== undefined && zoneIds.length === 0 }
}
