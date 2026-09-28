// Lote 8A-app — Conteo empieza en línea: escanear la posición reclama el conteo en el servidor (POST /cycle-counts,
// docs/lote8A-app-decisiones.md, decisión heredada de que las posiciones son un recurso compartido); de ahí en
// adelante capturar lo encontrado y terminar van por la cola de salida (kernel/sync/outbox.ts), referenciando el id
// que ya se conoce, sin necesidad de reescribir nada.
import { api, ApiError, unwrap } from '../../kernel/api/client'
import { enqueue } from '../../kernel/sync/outbox'
import { buildBatchItems, type CapturedEntry, type ExpectedLine } from './countLogic'

export interface StartedCount {
  countId: number
  isBlind: boolean
  expectedLines: ExpectedLine[]
}

function mapExpectedLines(lines: { id?: number; productPublicId?: string; sku?: string | null; productName?: string | null; systemQty?: number | null }[] | null | undefined): ExpectedLine[] {
  return (lines ?? []).map((l) => ({
    lineId: l.id ?? 0,
    productPublicId: l.productPublicId ?? '',
    sku: l.sku ?? '',
    productName: l.productName ?? '',
    systemQty: l.systemQty ?? null,
  }))
}

/** Crea el conteo para esa posición (o falla si ya hay uno abierto ahí: el servidor lo rechaza). */
export async function startCountOnline(warehousePublicId: string, binId: number): Promise<StartedCount> {
  const detail = await unwrap(api.POST('/api/v1/cycle-counts', { body: { warehousePublicId, binIds: [binId] } }))
  return { countId: detail.count?.id ?? 0, isBlind: detail.isBlind ?? true, expectedLines: mapExpectedLines(detail.lines) }
}

/** Recupera las líneas esperadas de un conteo ya abierto (se cerró y reabrió la app: local_count guarda el id pero
 *  no la lista, así que se vuelve a pedir; de todas formas esta pantalla ya necesita señal). */
export async function fetchExpectedLines(countId: number): Promise<ExpectedLine[]> {
  const detail = await unwrap(api.GET('/api/v1/cycle-counts/{id}', { params: { path: { id: countId } } }))
  return mapExpectedLines(detail.lines)
}

/** Cancela el conteo en el servidor (libera la posición para otro). Necesita señal, igual que abrirlo. */
export async function cancelCountOnline(countId: number): Promise<void> {
  await unwrap(api.DELETE('/api/v1/cycle-counts/{id}', { params: { path: { id: countId } } }))
}

/** Encola el lote capturado y el cierre de esa posición (kernel/sync/outbox.ts): dos filas en orden, el lote primero. */
export function enqueueFinishCount(countId: number, binId: number, entries: CapturedEntry[]): void {
  enqueue({
    kind: 'countBatch',
    path: `/api/v1/cycle-counts/${countId}/lines/batch`,
    body: { lines: buildBatchItems(binId, entries) },
  })
  enqueue({ kind: 'countFinish', path: `/api/v1/cycle-counts/${countId}/finish`, body: {} })
}

export { ApiError }
