// Despacho manual (2026-10-11) — lógica pura de las pantallas «Despachos manuales»: cuerpo del POST, llave de idempotencia,
// etiqueta de estatus («Despachado»), filtro por motivo y enlace al Kárdex. Manual 06 §7b.
import type { components } from '../../kernel/api/schema'
import { buildCollectBody, type CollectLineLike } from './collectForm'
import type { PickBatchDto } from './api'

export type ManualIssueCreateRequest = components['schemas']['ManualIssueCreateRequest']

/** Cuerpo de `POST /api/v1/manual-issues`: las líneas con datos + motivo + nota (vacía = sin nota). */
export function buildManualIssueBody(values: {
  warehousePublicId?: string | null
  lines: readonly CollectLineLike[]
  reasonCode?: string
  note?: string
}): { body: ManualIssueCreateRequest; indexMap: number[] } {
  const { body, indexMap } = buildCollectBody(values)
  const note = (values.note ?? '').trim()
  return { body: { ...body, reasonCode: (values.reasonCode ?? '').trim() || null, note: note || null }, indexMap }
}

/**
 * Llave de idempotencia por intento: el mismo cuerpo (p. ej. reintento tras un corte de red) reusa la llave y el servidor
 * devuelve el mismo documento; si el usuario cambió algo, o ya se grabó, es una llave nueva.
 */
export function nextIdempotencyKey(
  prev: { key: string; bodyJson: string } | null,
  body: unknown,
  newId: () => string,
): { key: string; bodyJson: string } {
  const bodyJson = JSON.stringify(body)
  return prev && prev.bodyJson === bodyJson ? prev : { key: newId(), bodyJson }
}

/** Código del estatus que se pinta: un despacho manual en COLLECTED se muestra «Despachado»; CANCELLED sigue «Cancelado». */
export const DISPATCHED_CODE = 'DISPATCHED'

export function manualStatusLabel(b: Pick<PickBatchDto, 'statusCode' | 'status' | 'isActive'>, dispatched: string, cancelled: string): string {
  if (b.statusCode === 'CANCELLED' || b.isActive === false) return cancelled
  if (b.statusCode === 'COLLECTED') return dispatched
  return b.status ?? b.statusCode ?? ''
}

/** ¿Está cancelado (eliminado con reversa)? */
export const isManualCancelled = (b: Pick<PickBatchDto, 'statusCode' | 'isActive'>) => b.statusCode === 'CANCELLED' || b.isActive === false

/**
 * El servidor no filtra por motivo: se acota con la búsqueda libre (que también mira el motivo) y se descartan los que
 * coincidieron por otro lado (p. ej. la palabra en una nota). `search` manda sobre el motivo si el usuario escribió algo.
 */
export function reasonSearchTerm(q: string, reasonLabel: string | null): string | undefined {
  const text = q.trim()
  return text || reasonLabel || undefined
}

export function filterByReason<T extends Pick<PickBatchDto, 'reasonCode'>>(rows: readonly T[], reasonCode: string): T[] {
  return reasonCode ? rows.filter((r) => (r.reasonCode ?? '').toUpperCase() === reasonCode.toUpperCase()) : [...rows]
}

/** Movimientos del documento en el Kárdex (`?refEntity=&refId=`). */
export const manualIssueKardexLink = (b: Pick<PickBatchDto, 'id'>) => `/warehouse/kardex?refEntity=PICK_BATCH&refId=${b.id ?? ''}`

/** Texto de nota para tabla: una línea, recortada. */
export function shortNote(note: string | null | undefined, max = 60): string {
  const n = (note ?? '').replace(/\s+/g, ' ').trim()
  return n.length > max ? `${n.slice(0, max - 1)}…` : n
}
