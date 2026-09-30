// Lote 15 (D5) — selección de almacén (y de categoría o producto) del Pulso, COMPARTIDA entre el panel "Almacén" y la franja
// "Almacén hoy": un solo valor por compañía y usuario en localStorage (la misma clave de F7A), leído con
// `useSyncExternalStore`, así que cambiarlo en un componente lo cambia en el otro sin recargar. Lectura tolerante: un valor
// corrupto, de otra forma o un localStorage inaccesible equivalen a "sin filtro"; si no se puede escribir (cuota, modo
// privado), la selección sigue valiendo en la sesión (memoria).
import { useCallback, useSyncExternalStore } from 'react'
import { isCategoryProductValue, type CategoryProductValue } from '../../kernel/ui/categoryTree'

/** Filtro del panel 'Almacén' (Lote F7A): almacén (null = todos) y categoría o producto (null = todos). */
export interface WarehousePulseFilter {
  warehousePublicId: string | null
  item: CategoryProductValue
}

export const NO_WAREHOUSE_FILTER: WarehousePulseFilter = { warehousePublicId: null, item: null }

/** Clave de localStorage de la selección; null si aún no se conoce la compañía o el usuario (no se guarda nada). */
export function warehouseFilterStorageKey(tenantId: number | null | undefined, userId: number | null | undefined): string | null {
  if (tenantId == null || userId == null) return null
  return `teikem.pulse.warehouseFilter.${tenantId}.${userId}`
}

/** Selección a partir del texto guardado (sin texto, corrupto o de otra forma = sin filtro). */
export function parseWarehouseFilter(raw: string | null): WarehousePulseFilter {
  if (!raw) return NO_WAREHOUSE_FILTER
  try {
    const parsed = JSON.parse(raw) as Record<string, unknown> | null
    if (!parsed || typeof parsed !== 'object') return NO_WAREHOUSE_FILTER
    const wh = typeof parsed.warehousePublicId === 'string' && parsed.warehousePublicId ? parsed.warehousePublicId : null
    const item = isCategoryProductValue(parsed.item ?? null) ? ((parsed.item ?? null) as CategoryProductValue) : null
    return { warehousePublicId: wh, item }
  } catch {
    return NO_WAREHOUSE_FILTER
  }
}

/** Texto guardado; undefined si localStorage no se puede leer. */
function readRaw(key: string): string | null | undefined {
  try {
    return window.localStorage.getItem(key)
  } catch {
    return undefined
  }
}

/** Selección guardada (o sin filtro si no hay, está corrupta o no se puede leer). */
export function readWarehouseFilter(key: string | null): WarehousePulseFilter {
  if (!key) return NO_WAREHOUSE_FILTER
  return parseWarehouseFilter(readRaw(key) ?? null)
}

/** Guarda la selección; sin filtro borra la entrada. Errores de almacenamiento (cuota, modo privado) se ignoran. */
export function writeWarehouseFilter(key: string | null, filter: WarehousePulseFilter): void {
  if (!key) return
  try {
    if (!filter.warehousePublicId && !filter.item) window.localStorage.removeItem(key)
    else window.localStorage.setItem(key, JSON.stringify(filter))
  } catch {
    // sin persistencia: el filtro sigue funcionando en la sesión (memoria del almacén)
  }
}

// ---------------------------------------------------------------------------------------------------------------------
// Almacén externo (useSyncExternalStore). La fuente de verdad es localStorage; `memory` guarda por clave el último texto
// visto y su objeto ya leído, para devolver SIEMPRE la misma referencia mientras el texto no cambie (requisito de
// useSyncExternalStore) y para recordar en la sesión una selección que no se pudo escribir.
// ---------------------------------------------------------------------------------------------------------------------

interface Entry {
  raw: string | null | undefined
  filter: WarehousePulseFilter
}

const memory = new Map<string, Entry>()
const listeners = new Set<() => void>()

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  // otra pestaña del mismo usuario también cambia la selección
  window.addEventListener('storage', listener)
  return () => {
    listeners.delete(listener)
    window.removeEventListener('storage', listener)
  }
}

/** Selección actual de esa clave (misma referencia mientras no cambie). */
export function getStoredWarehouseFilter(key: string | null): WarehousePulseFilter {
  if (!key) return NO_WAREHOUSE_FILTER
  const raw = readRaw(key)
  const entry = memory.get(key)
  // sin cambios en localStorage (o ilegible): lo último conocido, también si no se pudo escribir
  if (entry && (raw === undefined || entry.raw === raw)) return entry.filter
  const filter = raw === undefined ? NO_WAREHOUSE_FILTER : parseWarehouseFilter(raw)
  memory.set(key, { raw, filter })
  return filter
}

/** Cambia la selección de esa clave (guarda y avisa a todos los componentes que la leen). */
export function setStoredWarehouseFilter(key: string | null, filter: WarehousePulseFilter): void {
  if (!key) return
  writeWarehouseFilter(key, filter)
  memory.set(key, { raw: readRaw(key), filter })
  for (const listener of [...listeners]) listener()
}

/** `[selección, cambiar]` de la clave (compañía + usuario), sincronizada entre todos los componentes que la usan. */
export function useStoredWarehouseFilter(key: string | null): [WarehousePulseFilter, (filter: WarehousePulseFilter) => void] {
  const filter = useSyncExternalStore(
    subscribe,
    () => getStoredWarehouseFilter(key),
    () => NO_WAREHOUSE_FILTER,
  )
  const set = useCallback((next: WarehousePulseFilter) => setStoredWarehouseFilter(key, next), [key])
  return [filter, set]
}
