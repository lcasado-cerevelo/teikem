// Lote 8A-app — Despacho: recolectar (producto, cantidad, posición) funciona sin señal con datos locales; elegir a
// quién se despacha (consignatario) y resolver las posiciones escaneadas a su id real necesitan señal un momento
// porque ni las ubicaciones ni las posiciones se sincronizan en este lote (docs/lote8A-app-decisiones.md, decisión 3
// heredada del backend: solo clientes 3PL, cuyo dueño ya viene con el producto sincronizado). Una vez resuelto todo,
// mandar el despacho es una sola llamada (kernel/sync/outbox.ts, kind 'pack'): se intenta en el momento y, si no hay
// señal, se encola igual que Recibir.
// 2026-10-11: «Completar despacho» ya no recolecta sin empacar (POST /pick-batches, kind 'collect'): es el despacho manual (queueManualIssue).
import { api, ApiError, isNetworkError, unwrap } from '../../kernel/api/client'
import { getDb } from '../../kernel/db/database'
import { enqueue } from '../../kernel/sync/outbox'
import { applyDeltasIn, issueDeltas } from '../../kernel/warehouse/balanceProjection'
import { buildManualIssueBody } from './manualIssueLogic'
import { discardLocalPick } from './localPick'
import { findBinByCode } from '../../kernel/warehouse/binLookup'
import { findLocalBin } from '../count/countApi'
import { hasStockExitCopy, mapExitRow, readStockExit, replaceStockExitFor } from './stockExit'
import { buildCollectAndPackBody, type StockOption, type ClientChoice, type ConsigneeChoice, type PickLine, type ResolvedPickLine, uniqueBinCodes } from './dispatchLogic'

/** De dónde puede salir el producto y de qué fuente: `server` = recién preguntado (orden de salida del servidor, GET /inventory/exit-options; se
 *  guarda también en el aparato); `device` = sin señal, la copia bajada al aparato; `none` = sin señal y nunca se bajó (no se sabe). */
export interface StockOptionsResult {
  options: StockOption[]
  source: 'server' | 'device' | 'none'
}

/** Existencias DISPONIBLES del producto en el almacén, en el orden de salida del servidor (rank). En línea pide la foto actual; sin señal usa la
 *  copia del aparato, que baja la sincronización (kernel/sync/download.ts: downloadStockExit). La regla de orden vive solo en el servidor. */
export async function fetchStockOptions(warehousePublicId: string, productPublicId: string): Promise<StockOptionsResult> {
  try {
    const page = await unwrap(
      api.GET('/api/v1/inventory/exit-options', { params: { query: { warehousePublicId, productPublicIds: [productPublicId], take: 500 } } }),
    )
    const rows = (page.items ?? []).map(mapExitRow)
    replaceStockExitFor(warehousePublicId, productPublicId, rows)
    return { options: rows, source: 'server' }
  } catch {
    // sin señal (o sin permiso para consultar): lo que bajó el aparato
    return hasStockExitCopy(warehousePublicId) ? { options: readStockExit(warehousePublicId, productPublicId), source: 'device' } : { options: [], source: 'none' }
  }
}

/** ¿El aparato ya bajó este recurso al menos una vez? (marca de agua guardada; sin ella la tabla local podría estar vacía por no haberse sincronizado, no porque no haya nada). */
function hasSynced(resource: string): boolean {
  return getDb().getFirstSync<{ since_utc: string | null }>('SELECT since_utc FROM sync_watermark WHERE resource = ?', [resource]) != null
}

/**
 * Clientes activos a quienes se puede despachar inventario PROPIO (con inventario de un cliente 3PL no hace falta: es ese). Señal débil (2026-10-11): si el aparato ya bajó los
 * clientes (GET /sync/clients) se leen DEL APARATO, sin red; si no (primer uso, o el usuario no tiene `warehouse.pick` y baja 403), se piden al servidor como antes (clients.read).
 */
export async function fetchClientsForOwnDispatch(): Promise<ClientChoice[]> {
  if (hasSynced('clients')) {
    return getDb()
      .getAllSync<{ public_id: string; name: string; code: string | null }>('SELECT public_id, name, code FROM client ORDER BY name COLLATE NOCASE, code')
      .map((r) => ({ publicId: r.public_id, label: [r.name, r.code].filter(Boolean).join(' · ') }))
  }
  const rows = await unwrap(api.GET('/api/v1/clients', { params: { query: { includeInactive: false } } }))
  return rows.map((r) => ({ publicId: r.publicId ?? '', label: [r.name, r.code].filter(Boolean).join(' · ') }))
}

/**
 * Consignatarios que acepta la orden de ese cliente: los suyos y los compartidos del operador. Del aparato si ya bajó las localizaciones (GET /sync/consignee-locations);
 * si no, del servidor como antes (locations.read).
 */
export async function fetchConsigneesForClient(clientPublicId: string): Promise<ConsigneeChoice[]> {
  if (hasSynced('consignee_locations')) {
    return getDb()
      .getAllSync<{ public_id: string; name: string; code: string | null; city: string | null }>(
        'SELECT public_id, name, code, city FROM consignee_location WHERE client_public_id = ? OR client_public_id IS NULL ORDER BY name COLLATE NOCASE, code',
        [clientPublicId],
      )
      .map((r) => ({ publicId: r.public_id, label: [r.name, r.city].filter(Boolean).join(' · ') || (r.code ?? '') }))
  }
  const rows = await unwrap(api.GET('/api/v1/locations', { params: { query: { clientId: clientPublicId } } }))
  return rows.map((r) => ({ publicId: r.publicId ?? '', label: [r.name, r.city].filter(Boolean).join(' · ') || (r.code ?? '') }))
}

export interface ResolveBinsResult {
  lines: ResolvedPickLine[]
  notFound: string[]
}

/** Resuelve cada código de posición distinto a su id real (una llamada por código). Los que no existan quedan en
 *  `notFound`; con al menos uno sin resolver no se debe seguir a empacar. */
export async function resolveBinCodes(warehousePublicId: string, lines: PickLine[]): Promise<ResolveBinsResult> {
  const codes = uniqueBinCodes(lines)
  const resolved = new Map<string, number>()
  const notFound: string[] = []
  for (const code of codes) {
    // primero las posiciones del aparato (sin señal); si no está, el servidor
    const local = findLocalBin(warehousePublicId, code)
    const bin = local ?? (await findBinByCode(warehousePublicId, code))
    if (bin) resolved.set(code, bin.id)
    else notFound.push(code)
  }
  if (notFound.length > 0) return { lines: [], notFound }
  return { lines: lines.map((l) => ({ ...l, fromBinId: resolved.get(l.fromBinCode) ?? 0 })), notFound: [] }
}

/** Intenta mandar el despacho ya mismo; sin red, lo encola (kernel/sync/outbox.ts) para cuando la haya. */
export async function submitCollectAndPack(
  warehousePublicId: string,
  clientPublicId: string,
  consigneeLocationPublicId: string,
  pieces: number,
  lines: ResolvedPickLine[],
): Promise<{ queued: boolean }> {
  const body = buildCollectAndPackBody(warehousePublicId, clientPublicId, consigneeLocationPublicId, pieces, lines)
  try {
    await unwrap(api.POST('/api/v1/pick-batches/collect-and-pack', { body: body as never }))
    return { queued: false }
  } catch (err) {
    if (err instanceof ApiError && isNetworkError(err)) {
      enqueue({ kind: 'pack', body })
      return { queued: true }
    }
    throw err
  }
}

/**
 * «Completar despacho» = despacho manual (decisión del dueño 2026-10-11, DMA-#####): salida sin entrega con motivo y nota. Va a la cola de salida
 * como Transferir/Ajustar (kind 'manualIssue', POST /api/v1/manual-issues con su Idempotency-Key, en orden FIFO): en UNA transacción se encola, se
 * resta del saldo local lo que sale (el efecto exacto queda guardado con la fila para deshacerlo si el servidor la rechaza) y se cierra el despacho
 * local, así nunca quedan a la vez el despacho abierto y su envío pendiente (si la app se cierra a la mitad, no se puede mandar dos veces).
 * Devuelve el id de la cola (para `flushNow`).
 */
export function queueManualIssue(warehousePublicId: string, reasonCode: string, note: string, lines: ResolvedPickLine[]): number {
  const body = buildManualIssueBody(warehousePublicId, reasonCode, note, lines)
  const db = getDb()
  let id = 0
  db.withTransactionSync(() => {
    const projection = issueDeltas(body, db)
    id = enqueue({ kind: 'manualIssue', body, projection })
    applyDeltasIn(db, projection, 1)
    discardLocalPick()
  })
  return id
}
