// Lote 177 — cuentas puras de la recolección maestra en pantalla (sin API). Pruebas en pickGroupView.test.ts.
import type { PickDocumentDto, PickGroupMovementDto } from './api'

/** Tipos de documento que se pueden recolectar juntos (el envío ya empacado no). */
export const GROUPABLE_KINDS = ['EXIT_NOTICE', 'SALES_ORDER'] as const

export const MIN_GROUP_DOCUMENTS = 2
export const MAX_GROUP_DOCUMENTS = 20

/** ¿Tiene algo reservado por sacar y es de un tipo que se puede juntar? */
export function isGroupable(d: Pick<PickDocumentDto, 'kind' | 'reserved'>): boolean {
  return (GROUPABLE_KINDS as readonly string[]).includes(d.kind ?? '') && (d.reserved ?? 0) > 0
}

export const docKey = (d: Pick<PickDocumentDto, 'kind' | 'publicId'>) => `${d.kind}:${d.publicId}`

/**
 * Por qué un documento no se puede elegir ahora (null = se puede): ya se llegó al máximo, o es de otro cliente que el de lo ya elegido
 * (una maestra es de un solo cliente y un solo destino). Se compara por nombre del cliente; el servidor valida por id.
 */
export function selectionBlock(d: Pick<PickDocumentDto, 'kind' | 'publicId' | 'clientName'>, chosen: readonly Pick<PickDocumentDto, 'kind' | 'publicId' | 'clientName'>[]): 'otherClient' | 'max' | null {
  if (chosen.some((c) => docKey(c) === docKey(d))) return null
  if (chosen.length >= MAX_GROUP_DOCUMENTS) return 'max'
  if (chosen.length > 0 && (chosen[0].clientName ?? '') !== (d.clientName ?? '')) return 'otherClient'
  return null
}

/** Cuerpo de POST /pick-batches/groups: los documentos elegidos, en el orden en que se eligieron (sin líneas = todo lo reservado). */
export function groupRequest(chosen: readonly Pick<PickDocumentDto, 'kind' | 'publicId'>[], note?: string) {
  return { documents: chosen.map((d) => ({ kind: d.kind ?? '', publicId: d.publicId })), note: note?.trim() || null }
}

/** Movimientos agrupados por documento (el orden en que salieron), para la ficha. */
export function movementsByDocument(moves: readonly PickGroupMovementDto[]): { document: string; kind: string; total: number; rows: PickGroupMovementDto[] }[] {
  const out: { document: string; kind: string; total: number; rows: PickGroupMovementDto[] }[] = []
  for (const m of moves) {
    let g = out.find((x) => x.document === (m.documentNumber ?? '') && x.kind === (m.documentKind ?? ''))
    if (!g) out.push((g = { document: m.documentNumber ?? '', kind: m.documentKind ?? '', total: 0, rows: [] }))
    g.rows.push(m)
    g.total += m.quantity ?? 0
  }
  return out
}
