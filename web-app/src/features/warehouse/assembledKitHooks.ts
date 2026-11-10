// Lote 232 — ayudas del kit ARMADO para el editor de producto (sin componentes, para que AssembledKitEditor.tsx solo exporte componentes):
// fichas de las piezas escogidas, texto del primer problema de la composición, rastreo que resulta de las piezas y la composición en edición
// (piezas del servidor, si cambiaron y si el servidor la bloquea). Textos en `warehouse.products.assembled.*` y `warehouse.products.kit.err.*`.
import { useMemo, useState } from 'react'
import { useT } from '../../kernel/i18n'
import { useProductsByPublicId } from './api'
import { useAssembledKit } from './assembledKitApi'
import { decideTracking, piecesFromAssembled, piecesSignature, type AssembledPiecesProblem, type PieceInfo } from './assembledKitRules'
import { newKitPiece, type KitPiece } from './kitPieces'

const A = 'warehouse.products.assembled'

/** Texto del primer problema de las piezas (los básicos con los textos del editor de piezas; los del kit armado con los del servidor). */
export function usePiecesProblemText() {
  const t = useT()
  return (p: AssembledPiecesProblem | null): string | null => {
    if (!p) return null
    if (p.key === 'tooMany') return t(`${A}.err.tooMany`)
    if ('sku' in p) return t(`${A}.err.${p.key}`, { sku: p.sku })
    return t(`warehouse.products.kit.err.${p.key}`)
  }
}

/** Lo que se sabe de cada pieza escogida (su ficha, con la misma caché que el resto de la pantalla). */
export function usePieceInfos(pieces: readonly KitPiece[]): { infos: Map<string, PieceInfo>; loading: boolean } {
  const ids = useMemo(() => [...new Set(pieces.map((p) => p.productPublicId).filter((x): x is string => Boolean(x)))], [pieces])
  const results = useProductsByPublicId(ids, { handleAccessDenied: false })
  const infos = new Map<string, PieceInfo>()
  results.forEach((r, i) => {
    const p = r.data?.product
    if (p) infos.set(ids[i], { sku: p.sku ?? '', trackingTypeCode: p.trackingTypeCode, isKit: p.isKit, isAssembledKit: p.isAssembledKit, isActive: p.isActive, ownerClientPublicId: p.ownerClientPublicId ?? null })
  })
  return { infos, loading: results.some((r) => r.isLoading) }
}

/** Rastreo que resulta de las piezas (para el campo informativo); null si todavía no se sabe. */
export function trackingOfPieces(pieces: readonly KitPiece[], infos: ReadonlyMap<string, PieceInfo>): 'LOT' | 'NONE' | null {
  const chosen = pieces.filter((p) => p.productPublicId)
  if (chosen.length === 0 || chosen.some((p) => !infos.has(p.productPublicId!))) return null
  return decideTracking(chosen.map((p) => infos.get(p.productPublicId!)?.trackingTypeCode))
}

/** Composición de un kit armado en edición: piezas del servidor, si se pueden cambiar y si cambiaron (el editor hace el PUT al guardar). */
export function useEditKitComposition(publicId: string | null, isAssembledKit: boolean) {
  const q = useAssembledKit(isAssembledKit ? publicId : null, { handleAccessDenied: false })
  // null = sin tocar: se muestran las del servidor (llegan después de montar); lo que el usuario cambie no se pisa
  const [edited, setPieces] = useState<KitPiece[] | null>(null)
  const server = useMemo(() => {
    if (!q.data) return null
    const rows = piecesFromAssembled(q.data.pieces, (i) => `kp-srv-${i}`)
    return rows.length ? rows : [newKitPiece('kp-srv-0')]
  }, [q.data])
  const pieces = edited ?? server ?? []
  const changed = server !== null && edited !== null && piecesSignature(edited) !== piecesSignature(server)
  return { dto: q.data, loading: q.isLoading, pieces, setPieces, changed, locked: q.data?.compositionLocked === true }
}
