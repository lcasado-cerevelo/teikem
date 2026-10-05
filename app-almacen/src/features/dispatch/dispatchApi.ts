// Lote 8A-app — Despacho: recolectar (producto, cantidad, posición) funciona sin señal con datos locales; elegir a
// quién se despacha (consignatario) y resolver las posiciones escaneadas a su id real necesitan señal un momento
// porque ni las ubicaciones ni las posiciones se sincronizan en este lote (docs/lote8A-app-decisiones.md, decisión 3
// heredada del backend: solo clientes 3PL, cuyo dueño ya viene con el producto sincronizado). Una vez resuelto todo,
// mandar el despacho es una sola llamada (kernel/sync/outbox.ts, kind 'pack'): se intenta en el momento y, si no hay
// señal, se encola igual que Recibir.
import { api, ApiError, isNetworkError, unwrap } from '../../kernel/api/client'
import { enqueue } from '../../kernel/sync/outbox'
import { findBinByCode } from '../../kernel/warehouse/binLookup'
import { buildCollectAndPackBody, type ClientChoice, type ConsigneeChoice, type PickLine, type ResolvedPickLine, uniqueBinCodes } from './dispatchLogic'

/** Clientes activos a quienes se puede despachar inventario PROPIO (con inventario de un cliente 3PL no hace falta: es ese). */
export async function fetchClientsForOwnDispatch(): Promise<ClientChoice[]> {
  const rows = await unwrap(api.GET('/api/v1/clients', { params: { query: { includeInactive: false } } }))
  return rows.map((r) => ({ publicId: r.publicId ?? '', label: [r.name, r.code].filter(Boolean).join(' · ') }))
}

export async function fetchConsigneesForClient(clientPublicId: string): Promise<ConsigneeChoice[]> {
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
    const bin = await findBinByCode(warehousePublicId, code)
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
