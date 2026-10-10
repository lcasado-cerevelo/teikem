// Lote 8A-app — resolver una posición escaneada a su id real. Necesita señal: las posiciones no se sincronizan en
// este lote (solo productos, órdenes de compra y avisos, docs/lote8A-app-decisiones.md), así que Acomodar, Despacho
// (al empacar) y Conteo hacen esta misma llamada cuando ya de todas formas necesitan conexión.
// Lote 1: el listado de posiciones llega paginado (`{ total, skip, take, items }`, máx. 200 por página) y `search` compara
// por subcadena (código, zona, pasillo, rack, nivel y posición), así que se busca el código escaneado en el servidor y se
// elige la coincidencia EXACTA de código (sin distinguir mayúsculas) dentro de `items`; si no está en la primera página
// (muchas posiciones contienen ese texto) se siguen leyendo páginas hasta encontrarla o agotar el total (tope MAX_PAGES).
import { api, unwrap } from '../api/client'
import { getDb } from '../db/database'

export interface FoundBin {
  id: number
  code: string
  /** Espacio libre según el cupo de la posición (cupo − existencia, nunca negativo); null = la posición no tiene cupo configurado. */
  freeQty?: number | null
}

/** Espacio libre de una posición del listado: null sin cupo configurado. */
export function freeQtyOf(maxCapacityQty: number | null | undefined, qtyOnHand: number | null | undefined): number | null {
  return maxCapacityQty == null ? null : Math.max(0, maxCapacityQty - (qtyOnHand ?? 0))
}

/** Filas por página (tope del API). */
export const BIN_LOOKUP_PAGE = 200
/** Páginas como máximo: 1 000 posiciones que contienen el código escaneado ya es un caso anómalo. */
const MAX_PAGES = 5

/** Busca una posición del almacén por su código exacto (lo que se escaneó). null si no existe o no coincide. */
export async function findBinByCode(warehousePublicId: string, code: string): Promise<FoundBin | null> {
  const wanted = code.trim().toUpperCase()
  if (!wanted) return null
  for (let page = 0; page < MAX_PAGES; page++) {
    const skip = page * BIN_LOOKUP_PAGE
    const result = await unwrap(
      api.GET('/api/v1/warehouses/{publicId}/bins', {
        params: { path: { publicId: warehousePublicId }, query: { search: code.trim(), skip, take: BIN_LOOKUP_PAGE } },
      }),
    )
    const items = result.items ?? []
    const match = items.find((b) => (b.code ?? '').toUpperCase() === wanted)
    if (match?.id != null) return { id: match.id, code: match.code ?? code, freeQty: freeQtyOf(match.maxCapacityQty, match.qtyOnHand) }
    if (items.length === 0 || skip + items.length >= (result.total ?? 0)) return null
  }
  return null
}

/**
 * Señal débil (2026-10-10): la posición escaneada se busca PRIMERO entre las que el aparato ya tiene (sincronizadas, activas) y no espera a la red;
 * solo si no está ahí (p. ej. creada después de la última sincronización) se le pregunta al servidor. No trae el espacio libre (`freeQty`):
 * quien lo necesita sigue usando `findBinByCode`.
 */
export async function resolveBinLocalFirst(warehousePublicId: string, code: string): Promise<FoundBin | null> {
  const wanted = code.trim()
  if (!wanted) return null
  const row = getDb().getFirstSync<{ id: number; code: string }>(
    'SELECT id, code FROM bin WHERE warehouse_public_id = ? AND code = ? COLLATE NOCASE AND is_active = 1 LIMIT 1',
    [warehousePublicId, wanted],
  )
  if (row) return { id: row.id, code: row.code }
  return findBinByCode(warehousePublicId, code)
}
