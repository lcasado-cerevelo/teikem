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

/**
 * Señal débil (2026-10-11): la posición con su espacio libre, calculado EN EL APARATO: cupo de la posición (sincronizado, `bin.max_capacity_qty`) menos
 * lo que hay según los saldos locales (que ya incluyen lo pendiente de enviar). Sin cupo configurado, `freeQty` es null. Solo posiciones activas cuyo cupo ya
 * vino del servidor (`capacity_known`; una descargada antes de esta versión no lo sabe todavía: se pregunta al servidor) que el
 * aparato ya tiene; null si no la tiene (la creada después de la última sincronización: entonces se le pregunta al servidor). Es una ayuda (el servidor
 * valida y, para el cupo, solo avisa), así que un dato un poco atrasado no bloquea nada.
 */
export function findLocalBinWithFree(warehousePublicId: string, code: string): FoundBin | null {
  const wanted = code.trim()
  if (!wanted) return null
  const db = getDb()
  const row = db.getFirstSync<{ id: number; code: string; max_capacity_qty: number | null }>(
    'SELECT id, code, max_capacity_qty FROM bin WHERE warehouse_public_id = ? AND code = ? COLLATE NOCASE AND is_active = 1 AND capacity_known = 1 LIMIT 1',
    [warehousePublicId, wanted],
  )
  if (!row) return null
  const onHand =
    db.getFirstSync<{ q: number | null }>('SELECT SUM(qty_on_hand) AS q FROM stock_balance WHERE warehouse_public_id = ? AND bin_id = ?', [
      warehousePublicId,
      row.id,
    ])?.q ?? 0
  return { id: row.id, code: row.code, freeQty: freeQtyOf(row.max_capacity_qty, onHand) }
}

/**
 * Busca una posición del almacén por su código exacto (lo que se escaneó), con su espacio libre. Primero entre las que el aparato ya tiene (sin red,
 * ver findLocalBinWithFree); solo si no está ahí se le pregunta al servidor. null si no existe o no coincide.
 */
export async function findBinByCode(warehousePublicId: string, code: string): Promise<FoundBin | null> {
  const wanted = code.trim().toUpperCase()
  if (!wanted) return null
  const local = findLocalBinWithFree(warehousePublicId, code)
  if (local) return local
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
