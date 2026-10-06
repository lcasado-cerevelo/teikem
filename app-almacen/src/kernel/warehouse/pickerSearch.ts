// Tarea 26 — búsqueda en la lista para los campos de producto y de posición (además de escanear): sobre la base local del aparato
// (productos y posiciones ya sincronizados), sin señal. Solo busca; elegir una fila entrega su código al campo, como si se hubiera escaneado.
import { getDb } from '../db/database'

export type PickKind = 'product' | 'bin' | 'any'

export interface PickItem {
  kind: 'product' | 'bin'
  /** Lo que recibe el campo al elegirla: el SKU del producto o el código de la posición. */
  code: string
  title: string
  subtitle: string | null
}

/** Tope de filas de la lista; más que esto se pide escribir para acotar. */
export const PICK_LIMIT = 50

/** Escapa % _ \ para usar el texto escrito como subcadena literal en LIKE ... ESCAPE '\'. */
export function likeTerm(text: string): string {
  return `%${text.trim().replace(/[\\%_]/g, (c) => `\\${c}`)}%`
}

export function searchProducts(query: string, limit = PICK_LIMIT): PickItem[] {
  const term = likeTerm(query)
  return getDb()
    .getAllSync<{ sku: string; name: string; barcode: string | null }>(
      `SELECT sku, name, barcode FROM product
       WHERE is_active = 1 AND (sku LIKE ? ESCAPE '\\' OR name LIKE ? ESCAPE '\\' OR barcode LIKE ? ESCAPE '\\')
       ORDER BY sku LIMIT ?`,
      [term, term, term, limit],
    )
    .map((p) => ({ kind: 'product' as const, code: p.sku, title: p.sku, subtitle: [p.name, p.barcode].filter(Boolean).join(' · ') || null }))
}

export function searchBins(warehousePublicId: string, query: string, limit = PICK_LIMIT): PickItem[] {
  const term = likeTerm(query)
  return getDb()
    .getAllSync<{ code: string; zone_code: string | null; zone_name: string | null }>(
      `SELECT code, zone_code, zone_name FROM bin
       WHERE warehouse_public_id = ? AND is_active = 1
         AND (code LIKE ? ESCAPE '\\' OR zone_code LIKE ? ESCAPE '\\' OR zone_name LIKE ? ESCAPE '\\'
              OR aisle LIKE ? ESCAPE '\\' OR rack LIKE ? ESCAPE '\\' OR level LIKE ? ESCAPE '\\' OR position LIKE ? ESCAPE '\\')
       ORDER BY code COLLATE NOCASE LIMIT ?`,
      [warehousePublicId, term, term, term, term, term, term, term, limit],
    )
    .map((b) => ({ kind: 'bin' as const, code: b.code, title: b.code, subtitle: [b.zone_code, b.zone_name].filter(Boolean).join(' · ') || null }))
}

/** Búsqueda del selector: producto, posición o las dos (productos primero). */
export function searchPicker(kind: PickKind, warehousePublicId: string | null, query: string): PickItem[] {
  const out: PickItem[] = []
  if (kind !== 'bin') out.push(...searchProducts(query))
  if (kind !== 'product' && warehousePublicId) out.push(...searchBins(warehousePublicId, query))
  return out
}
