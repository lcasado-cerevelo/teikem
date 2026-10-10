// Lote 8A-app — Consultar: saldos en línea (GET /inventory/balances, docs/lote8A-app-decisiones.md), con una copia en
// balance_cache (kernel/db/schema.ts v2) de la última respuesta por consulta para responder "de hace N min" sin
// señal. El código escaneado puede ser un producto (se filtra por productPublicId, ya sincronizado) o una posición
// (no está sincronizada: se manda tal cual como búsqueda libre, que el servidor ya resuelve por código de posición).
// Lote A8 — al escanear una POSICIÓN, Consultar muestra en vivo lo que el sistema dice que hay en ella: la posición se resuelve
// primero contra las sincronizadas (tabla `bin`, sin señal) y si no, contra el servidor (findBinByCode), y los saldos se piden
// por su id (`binIds`), no por texto libre (que también traería posiciones cuyo código solo CONTIENE lo escaneado).
import { api, ApiError, isNetworkError, unwrap } from '../../kernel/api/client'
import { getDb } from '../../kernel/db/database'
import { findBinByCode, type FoundBin } from '../../kernel/warehouse/binLookup'
import { binCacheKey, cacheKey, type BalanceRow } from './lookupLogic'

export interface LookupResult {
  rows: BalanceRow[]
  fromCache: boolean
  fetchedAtUtc: string
}

function mapRows(items: { id?: number; binId?: number | null; lotId?: number | null; zoneTypeCode?: string | null; binCode?: string | null; productPublicId?: string; sku?: string | null; productName?: string | null; lotNumber?: string | null; qtyOnHand?: number; qtyAvailable?: number }[] | null | undefined): BalanceRow[] {
  return (items ?? []).map((r, i) => ({
    id: r.id ?? i,
    binId: r.binId ?? null,
    lotId: r.lotId ?? null,
    zoneTypeCode: r.zoneTypeCode ?? null,
    binCode: r.binCode ?? null,
    productPublicId: r.productPublicId ?? '',
    sku: r.sku ?? '',
    productName: r.productName ?? '',
    lotNumber: r.lotNumber ?? null,
    qtyOnHand: r.qtyOnHand ?? 0,
    qtyAvailable: r.qtyAvailable ?? 0,
  }))
}

function writeCache(key: string, rows: BalanceRow[], fetchedAtUtc: string): void {
  const db = getDb()
  const existing = db.getFirstSync<{ cache_key: string }>('SELECT cache_key FROM balance_cache WHERE cache_key = ?', [key])
  if (existing) {
    db.runSync('UPDATE balance_cache SET payload_json = ?, fetched_at_utc = ? WHERE cache_key = ?', [JSON.stringify(rows), fetchedAtUtc, key])
  } else {
    db.runSync('INSERT INTO balance_cache (cache_key, payload_json, fetched_at_utc) VALUES (?, ?, ?)', [key, JSON.stringify(rows), fetchedAtUtc])
  }
}

function readCache(key: string): { rows: BalanceRow[]; fetchedAtUtc: string } | null {
  const row = getDb().getFirstSync<{ payload_json: string; fetched_at_utc: string }>('SELECT payload_json, fetched_at_utc FROM balance_cache WHERE cache_key = ?', [key])
  if (!row) return null
  return { rows: JSON.parse(row.payload_json) as BalanceRow[], fetchedAtUtc: row.fetched_at_utc }
}

/** Busca saldos por producto (ya conocido localmente) o, si no, por código libre (posición u otro): sin red, cae a
 *  la última respuesta guardada para ese mismo código; sin red y sin nada guardado, propaga el error. */
export async function searchBalances(warehousePublicId: string, code: string, productPublicId: string | null): Promise<LookupResult> {
  const key = cacheKey(warehousePublicId, code)
  try {
    const query = productPublicId
      ? { warehousePublicIds: [warehousePublicId], productPublicIds: [productPublicId], take: 50 }
      : { warehousePublicIds: [warehousePublicId], search: code, take: 50 }
    const page = await unwrap(api.GET('/api/v1/inventory/balances', { params: { query } }))
    const rows = mapRows(page.items)
    const fetchedAtUtc = new Date().toISOString()
    writeCache(key, rows, fetchedAtUtc)
    return { rows, fromCache: false, fetchedAtUtc }
  } catch (err) {
    if (err instanceof ApiError && err.code === 'network') {
      const cached = readCache(key)
      if (cached) return { rows: cached.rows, fromCache: true, fetchedAtUtc: cached.fetchedAtUtc }
    }
    throw err
  }
}

// ------------------------------------------------------------------ Lote A8: lo que hay en una posición

export type LookupBin = { kind: 'bin'; bin: FoundBin } | { kind: 'inactive'; code: string } | { kind: 'none' }

/** ¿Lo escaneado es una posición del almacén? Primero las posiciones sincronizadas (activas: no necesita señal); si no está,
 *  se pregunta al servidor (puede ser una posición creada después de la última sincronización). Si el servidor no responde
 *  (sin señal, error) se contesta `none` y la pantalla sigue con la búsqueda libre de siempre, que ya sabe avisar sin señal.
 *  `inactive`: solo existe dada de baja (en el aparato y el servidor no la encontró activa). */
export async function resolveLookupBin(warehousePublicId: string, code: string): Promise<LookupBin> {
  const wanted = code.trim()
  if (!wanted) return { kind: 'none' }
  const local = getDb().getAllSync<{ id: number; code: string; is_active: number }>(
    'SELECT id, code, is_active FROM bin WHERE warehouse_public_id = ? AND code = ? COLLATE NOCASE',
    [warehousePublicId, wanted],
  )
  const active = local.find((b) => b.is_active === 1)
  if (active) return { kind: 'bin', bin: { id: active.id, code: active.code } }
  const inactive = local.length > 0 ? { kind: 'inactive' as const, code: local[0].code } : null
  try {
    const remote = await findBinByCode(warehousePublicId, wanted)
    if (remote) return { kind: 'bin', bin: remote }
    return inactive ?? { kind: 'none' }
  } catch {
    return inactive ?? { kind: 'none' }
  }
}

export interface BinContentsResult extends LookupResult {
  /** true si la posición tiene más filas de saldo de las que se leyeron (tope BIN_CONTENT_MAX_PAGES páginas). */
  truncated: boolean
}

/** Filas por página (tope del API) y páginas como máximo: 1 000 filas de saldo en una sola posición ya es anómalo. */
export const BIN_CONTENT_PAGE = 200
export const BIN_CONTENT_MAX_PAGES = 5

/** Saldos de UNA posición (todas sus filas: producto × lote), en línea. Sin señal, la última respuesta guardada de esa misma
 *  posición; sin señal y sin nada guardado, propaga el error de red. */
export async function fetchBinContents(warehousePublicId: string, bin: FoundBin): Promise<BinContentsResult> {
  const key = binCacheKey(warehousePublicId, bin.code)
  try {
    const rows: BalanceRow[] = []
    let total = 0
    for (let page = 0; page < BIN_CONTENT_MAX_PAGES; page++) {
      const skip = page * BIN_CONTENT_PAGE
      const result = await unwrap(
        api.GET('/api/v1/inventory/balances', {
          params: { query: { warehousePublicIds: [warehousePublicId], binIds: [bin.id], skip, take: BIN_CONTENT_PAGE } },
        }),
      )
      const items = mapRows(result.items)
      rows.push(...items)
      total = result.total ?? rows.length
      if (items.length === 0 || rows.length >= total) break
    }
    const fetchedAtUtc = new Date().toISOString()
    writeCache(key, rows, fetchedAtUtc)
    return { rows, fromCache: false, fetchedAtUtc, truncated: rows.length < total }
  } catch (err) {
    if (isNetworkError(err)) {
      const cached = readCache(key)
      if (cached) return { rows: cached.rows, fromCache: true, fetchedAtUtc: cached.fetchedAtUtc, truncated: false }
    }
    throw err
  }
}

export { ApiError }
