// Búsqueda global de «Buscar o ejecutar» (2026-10-21; docs/busqueda-global-dictado-2026-10-20.md). Lógica pura: a dónde lleva cada tipo de resultado, el
// título del grupo y cuándo se abre solo. El servidor (`GET /api/v1/search`) solo dice QUÉ encontró; aquí se decide la pantalla.
import type { components } from '../api/schema'

export type SearchHit = components['schemas']['SearchHitDto']

/** Letras mínimas para buscar (igual que el servidor). */
export const SEARCH_MIN_LENGTH = 2
/** Letras mínimas para que un resultado único y exacto se abra solo (con menos, escribir «OR» abriría el primer envío llamado «OR»). */
export const AUTO_OPEN_MIN_LENGTH = 4
/** Espera entre teclas antes de preguntar al servidor (ms). */
export const SEARCH_DEBOUNCE_MS = 250

/** Tipos que el servidor puede devolver, en el orden en que se muestran sus grupos. */
export const SEARCH_TYPES = [
  'ORDER', 'TRIP', 'CLAIM', 'CLIENT', 'CONSIGNEE', 'WAREHOUSE', 'ZONE', 'BIN', 'PRODUCT', 'SUPPLIER', 'RECEIPT', 'PICK_BATCH', 'EXIT_NOTICE', 'ARRIVAL_NOTICE',
  'PURCHASE_ORDER', 'SALES_ORDER', 'INVOICE', 'RENTAL',
] as const
export type SearchType = (typeof SEARCH_TYPES)[number]

const enc = encodeURIComponent

/**
 * Ruta de la pantalla que abre un resultado. Los que tienen ficha directa llevan a su ficha; los que no (reclamaciones, proveedores, facturas, consignatarios)
 * abren su lista ya filtrada con `?q=` (decisión de Luis: «que se abra la pantalla de búsqueda filtrada por ese»).
 */
export function hitHref(hit: Pick<SearchHit, 'type' | 'id' | 'title' | 'keys'>): string {
  const id = hit.id ?? ''
  const title = hit.title ?? ''
  const k = hit.keys ?? {}
  switch (hit.type) {
    case 'ORDER': return `/orders/${enc(id)}`
    case 'TRIP': return `/ops/monitor?trip=${enc(id)}`
    case 'CLAIM': return `/ops/claims?q=${enc(title)}`
    case 'CLIENT': return `/catalog/clients?client=${enc(id)}`
    case 'CONSIGNEE': return `/catalog/consignees?q=${enc(title)}`
    case 'WAREHOUSE': return `/warehouse/warehouses?warehouse=${enc(id)}`
    case 'ZONE':
    case 'BIN': return `/warehouse/locations?warehouse=${enc(k.warehouse ?? '')}&zone=${enc(k.zone ?? '')}`
    case 'PRODUCT': return `/warehouse/products/${enc(id)}`
    case 'SUPPLIER': return `/warehouse/suppliers?q=${enc(title)}`
    case 'RECEIPT': return `/warehouse/receipts?receipt=${enc(id)}`
    case 'PICK_BATCH': return `/warehouse/pick-batches/${enc(id)}`
    case 'ARRIVAL_NOTICE': return `/warehouse/notices?q=${enc(title)}`
    case 'EXIT_NOTICE': return `/warehouse/notices?notice=${enc(id)}`
    case 'PURCHASE_ORDER': return `/warehouse/purchase-orders/${enc(id)}`
    case 'SALES_ORDER': return `/sales-orders?order=${enc(id)}`
    case 'INVOICE': return `/money/billing?q=${enc(title)}`
    case 'RENTAL': return `/warehouse/rentals?rental=${enc(id)}`
    default: return '/'
  }
}

/**
 * ¿Se abre solo? Solo si la búsqueda ya está completa (no hay otra espera en curso), el texto tiene al menos {@link AUTO_OPEN_MIN_LENGTH} letras y el servidor
 * devolvió UN solo resultado y es EXACTO. Con uno exacto y otros parecidos, o varios, o uno solo parecido: se muestran para escoger.
 */
export function autoOpenHit(hits: readonly SearchHit[] | undefined, query: string, settled: boolean): SearchHit | null {
  if (!settled || query.trim().length < AUTO_OPEN_MIN_LENGTH) return null
  if (!hits || hits.length !== 1) return null
  return hits[0].exact ? hits[0] : null
}
