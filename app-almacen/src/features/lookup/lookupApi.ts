// Lote 8A-app — Consultar: saldos en línea (GET /inventory/balances, docs/lote8A-app-decisiones.md), con una copia en
// balance_cache (kernel/db/schema.ts v2) de la última respuesta por consulta para responder "de hace N min" sin
// señal. El código escaneado puede ser un producto (se filtra por productPublicId, ya sincronizado) o una posición
// (no está sincronizada: se manda tal cual como búsqueda libre, que el servidor ya resuelve por código de posición).
import { api, ApiError, unwrap } from '../../kernel/api/client'
import { getDb } from '../../kernel/db/database'
import { cacheKey, type BalanceRow } from './lookupLogic'

export interface LookupResult {
  rows: BalanceRow[]
  fromCache: boolean
  fetchedAtUtc: string
}

function mapRows(items: { id?: number; binCode?: string | null; productPublicId?: string; sku?: string | null; productName?: string | null; lotNumber?: string | null; qtyOnHand?: number; qtyAvailable?: number }[] | null | undefined): BalanceRow[] {
  return (items ?? []).map((r, i) => ({
    id: r.id ?? i,
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

export { ApiError }
