// Lote A6 — "Actualizar el conteo" tras el 409 de línea corregida (countRejection.ts): vuelve a pedir el conteo con
// GET /cycle-counts/{id} para que el operario vea cómo quedó. Solo cuando el operario toca el botón (nunca solo). No cambia quién
// ve las cantidades esperadas: de cada línea se toma el valor vigente (countedQty) y la marca de corrección, nunca systemQty.
import { api, ApiError, unwrap } from '../../kernel/api/client'
import { countRefreshState, type CountRefreshState, type RefreshedLine } from './countRejection'

export type CountRefreshResult =
  | { kind: 'ok'; state: Exclude<CountRefreshState, 'notFound'>; number: string; lines: RefreshedLine[] }
  | { kind: 'notFound' }
  /** Sin señal (o el servidor no respondió): no cambia nada, se puede volver a tocar. */
  | { kind: 'offline' }
  /** Otro error del servidor (403, 5xx…): su mensaje tal cual. */
  | { kind: 'error'; message: string }

export async function fetchCountForRejection(countId: number): Promise<CountRefreshResult> {
  try {
    const detail = await unwrap(api.GET('/api/v1/cycle-counts/{id}', { params: { path: { id: countId } } }))
    const state = countRefreshState(detail.count?.statusCode, detail.count?.isActive)
    if (state === 'notFound') return { kind: 'notFound' }
    const lines: RefreshedLine[] = (detail.lines ?? []).map((l) => ({
      lineId: l.id ?? 0,
      sku: l.sku ?? '',
      productName: l.productName ?? '',
      binCode: l.binCode ?? '',
      lotNumber: l.lotNumber ?? null,
      countedQty: l.countedQty ?? null,
      wasCorrected: l.wasCorrected ?? false,
      correctedByName: l.correctedByName ?? null,
    }))
    return { kind: 'ok', state, number: detail.count?.number ?? String(countId), lines }
  } catch (err) {
    if (!(err instanceof ApiError)) throw err
    if (err.code === 'network') return { kind: 'offline' }
    if (err.status === 404 || err.code === 'not_found') return { kind: 'notFound' }
    return { kind: 'error', message: err.title }
  }
}
