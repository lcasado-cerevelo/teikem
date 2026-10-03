// Lote 1 (cambios de Almacén) — lógica pura de los filtros de Almacenes (lista y ficha), probada en warehouseFilters.test.ts.
// - Lista de almacenes: filtra EN EL CLIENTE (la lista es corta) por Código, Nombre, Tipo de zona (`zoneTypeCodes`),
//   Estatus (`statusCode`) y Dirección (texto libre sobre dirección, ciudad, estado y código postal).
// - Pestaña Posiciones: arma la consulta del API (`GET .../bins`) con los filtros de la pantalla.
// - Ciudad/ZIP del almacén: etiqueta de cada localidad del catálogo USPS y la ciudad que se guarda al elegirla.
import type { ComboOption } from '../../kernel/ui/comboMatch'
import { matchesQ, normalizeQ } from '../../kernel/ui/matchesQ'
import type { GetQuery, PostalLocalityDto, WarehouseDto, WarehouseZoneDto } from './api'

/** Valores distintos, sin vacíos, en orden natural (A-2 antes que A-10), como opciones `{ value, label }`. */
export function distinctOptions(values: readonly (string | null | undefined)[]): { value: string; label: string }[] {
  const set = [...new Set(values.filter((v): v is string => Boolean(v)))]
  set.sort((a, b) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' }))
  return set.map((v) => ({ value: v, label: v }))
}

/** "Dirección, Ciudad, Estado ZIP" de un almacén (lo que hay). */
export function warehouseAddress(w: Pick<WarehouseDto, 'line1' | 'city' | 'state' | 'postalCode'>): string {
  const region = [w.state, w.postalCode].filter(Boolean).join(' ')
  return [w.line1, w.city, region].filter(Boolean).join(', ')
}

export interface WarehouseListFilters {
  /** Códigos elegidos (vacío = todos). */
  codes: readonly string[]
  /** Nombres elegidos (vacío = todos). */
  names: readonly string[]
  /** Tipos de zona (InternalCode de ZoneType): el almacén pasa si tiene ALGUNA zona activa de esos tipos. */
  zoneTypes: readonly string[]
  /** Códigos de estatus (WarehouseStatus). */
  statuses: readonly string[]
  /** Texto libre: cada palabra en dirección, ciudad, estado o código postal (sin mayúsculas ni acentos). */
  address: string
}

export const EMPTY_WAREHOUSE_FILTERS: WarehouseListFilters = { codes: [], names: [], zoneTypes: [], statuses: [], address: '' }

/** Almacenes que cumplen todos los filtros. */
export function filterWarehouseRows<T extends WarehouseDto>(rows: readonly T[], f: WarehouseListFilters): T[] {
  return rows.filter(
    (w) =>
      (f.codes.length === 0 || f.codes.includes(w.code ?? '')) &&
      (f.names.length === 0 || f.names.includes(w.name ?? '')) &&
      (f.zoneTypes.length === 0 || (w.zoneTypeCodes ?? []).some((z) => f.zoneTypes.includes(z))) &&
      (f.statuses.length === 0 || f.statuses.includes(w.statusCode ?? '')) &&
      matchesQ(f.address, w.line1, w.city, w.state, w.postalCode),
  )
}

/** Filtros de texto de la pestaña Posiciones (Código = `search` del API; las partes de la ubicación, cada una la suya). */
export interface BinTextFilters {
  code: string
  aisle: string
  rack: string
  level: string
  position: string
}

export const EMPTY_BIN_TEXT: BinTextFilters = { code: '', aisle: '', rack: '', level: '', position: '' }

export type BinsQuery = GetQuery<'/api/v1/warehouses/{publicId}/bins'>

/** Consulta del API a partir de los filtros de la pestaña Posiciones (sin `skip`/`take`); vacíos = sin filtro. Lote F12:
 *  `onlyProvisional` = solo las posiciones pendientes de revisión (`isProvisional=true`; apagado no filtra). */
export function binsQuery(text: BinTextFilters, zoneIds: readonly string[], includeInactive: boolean, onlyWithStock: boolean, onlyProvisional = false): BinsQuery {
  const q: BinsQuery = { includeInactive, onlyWithStock }
  if (onlyProvisional) q.isProvisional = true
  const code = text.code.trim()
  const aisle = text.aisle.trim()
  const rack = text.rack.trim()
  const level = text.level.trim()
  const position = text.position.trim()
  if (code) q.search = code
  if (aisle) q.aisle = aisle
  if (rack) q.rack = rack
  if (level) q.level = level
  if (position) q.position = position
  if (zoneIds.length > 0) q.zoneIds = zoneIds.map(Number)
  return q
}

/** Opciones del combobox de zona de `BinModal`: solo zonas activas, "Código · Nombre" con el tipo como texto secundario. */
export function zoneOptions(zones: readonly WarehouseZoneDto[]): ComboOption[] {
  return zones
    .filter((z) => z.isActive !== false)
    .map((z) => ({
      value: String(z.id),
      label: [z.code, z.name].filter(Boolean).join(' · '),
      hint: z.zoneType ?? undefined,
    }))
}

/** País de casa: sus localidades no repiten el nombre del país en la opción. */
const HOME_COUNTRY = 'PR'

/**
 * Etiqueta de una localidad del catálogo USPS (`GET /postal-localities`): "ZIP · CIUDAD POSTAL (Municipio), Estado" y, fuera
 * de Puerto Rico, " · País". El municipio va entre paréntesis solo si existe y es distinto de la ciudad postal (sin
 * mayúsculas ni acentos): 00952 → "00952 · SABANA SECA (Toa Baja), PR"; 10001 → "10001 · NEW YORK, NY · Estados Unidos".
 */
export function localityOptionLabel(l: PostalLocalityDto): string {
  const city = l.city ?? ''
  const muni = l.municipality && normalizeQ(l.municipality) !== normalizeQ(city) ? ` (${l.municipality})` : ''
  const state = l.state ? `, ${l.state}` : ''
  const country = l.countryCode && l.countryCode !== HOME_COUNTRY && l.country ? ` · ${l.country}` : ''
  return `${[l.postalCode, city].filter(Boolean).join(' · ')}${muni}${state}${country}`
}

/** Ciudad que se guarda en el almacén al elegir una localidad: el municipio (PR, con acentos) o la ciudad postal USPS. */
export function localityCity(l: PostalLocalityDto): string {
  return l.municipality || l.city || ''
}
