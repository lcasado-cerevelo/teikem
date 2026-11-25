// Lote 245 — reglas puras de las cadenas de clientes: armar las solicitudes de simulación y aplicación del alta masiva (con las exclusiones a mano), resumir la
// simulación, etiquetar cada acción (incluida EXCLUDED) y el texto «Sin cadena». Sin React ni API, para probarlas solas.
import { z } from 'zod'
import type { ChainBulkPreview, ChainBulkRequest, ChainBulkRow } from './chainsApi'

/** Acciones del alta masiva (las del servidor, `ClientChainCodes`) más EXCLUDED, que también pone la pantalla al desmarcar una fila. */
export type BulkAction = 'ASSIGN' | 'MOVE' | 'ALREADY' | 'SKIP_OTHER_CHAIN' | 'EXCLUDED'

export const BULK_ACTIONS: readonly BulkAction[] = ['ASSIGN', 'MOVE', 'ALREADY', 'SKIP_OTHER_CHAIN', 'EXCLUDED']

/** Mínimo de letras del prefijo (igual que el servidor). */
export const BULK_MIN_PREFIX = 3

/** Los ajustes de la simulación tal como están en pantalla. */
export interface BulkSettings {
  prefix: string
  wholeWord: boolean
  moveFromOtherChains: boolean
}

/** Texto del prefijo sin espacios sobrantes. */
export const cleanPrefix = (prefix: string): string => prefix.trim()

/** ¿Alcanza el prefijo para simular? */
export const prefixOk = (prefix: string): boolean => cleanPrefix(prefix).length >= BULK_MIN_PREFIX

/** Solicitud de SIMULACIÓN (sin exclusiones ni huella: la huella la calcula el servidor). */
export function previewRequest(s: BulkSettings): ChainBulkRequest {
  return { prefix: cleanPrefix(s.prefix), wholeWord: s.wholeWord, moveFromOtherChains: s.moveFromOtherChains }
}

/** Solicitud de APLICACIÓN: los mismos ajustes de la simulación, las exclusiones a mano y la huella que dio el servidor. */
export function applyRequest(preview: ChainBulkPreview, simulated: BulkSettings, excluded: ReadonlySet<string>): ChainBulkRequest {
  return {
    prefix: cleanPrefix(simulated.prefix),
    wholeWord: simulated.wholeWord,
    moveFromOtherChains: simulated.moveFromOtherChains,
    excludeClientPublicIds: [...excluded],
    fingerprint: preview.fingerprint,
  }
}

/** Una fila se puede excluir a mano solo si la simulación iba a escribirla (asignar o mover). */
export const canExclude = (row: ChainBulkRow): boolean => row.action === 'ASSIGN' || row.action === 'MOVE'

/** Acción que se ve en la fila: EXCLUDED si el usuario la desmarcó (sin volver a simular). */
export function effectiveAction(row: ChainBulkRow, excluded: ReadonlySet<string>): BulkAction {
  if (row.clientPublicId && canExclude(row) && excluded.has(row.clientPublicId)) return 'EXCLUDED'
  return (BULK_ACTIONS as readonly string[]).includes(row.action ?? '') ? (row.action as BulkAction) : 'ALREADY'
}

/** Descarta de las exclusiones lo que ya no es una fila escribible de la simulación nueva (al volver a simular se conservan las que siguen vigentes). */
export function keepExclusions(excluded: ReadonlySet<string>, preview: ChainBulkPreview | null): Set<string> {
  const writable = new Set((preview?.rows ?? []).filter(canExclude).map((r) => r.clientPublicId ?? ''))
  return new Set([...excluded].filter((id) => writable.has(id)))
}

/** Resumen de la simulación con las exclusiones a mano ya descontadas. */
export interface BulkSummary {
  matched: number
  toAssign: number
  toMove: number
  already: number
  skippedOtherChain: number
  excluded: number
  /** Clientes a los que se escribiría (asignar + mover). */
  applicable: number
}

export function summarize(preview: ChainBulkPreview, excluded: ReadonlySet<string>): BulkSummary {
  const rows = preview.rows ?? []
  let toAssign = 0
  let toMove = 0
  let already = 0
  let skipped = 0
  let ex = 0
  for (const r of rows) {
    const a = effectiveAction(r, excluded)
    if (a === 'ASSIGN') toAssign++
    else if (a === 'MOVE') toMove++
    else if (a === 'ALREADY') already++
    else if (a === 'SKIP_OTHER_CHAIN') skipped++
    else ex++
  }
  return { matched: preview.matched ?? rows.length, toAssign, toMove, already, skippedOtherChain: skipped, excluded: ex, applicable: toAssign + toMove }
}

/** ¿La simulación todavía vale para lo que hay en pantalla? (si cambió el prefijo o un interruptor, hay que volver a simular). */
export function previewIsCurrent(simulated: BulkSettings | null, s: BulkSettings): boolean {
  if (!simulated) return false
  return cleanPrefix(simulated.prefix) === cleanPrefix(s.prefix) && simulated.wholeWord === s.wholeWord && simulated.moveFromOtherChains === s.moveFromOtherChains
}

/** ¿Se puede aplicar? Hace falta una simulación vigente, que no sea «demasiadas coincidencias» y al menos un cliente a escribir. */
export function canApply(preview: ChainBulkPreview | null, simulated: BulkSettings | null, s: BulkSettings, excluded: ReadonlySet<string>): boolean {
  if (!preview || preview.tooMany || !previewIsCurrent(simulated, s)) return false
  return summarize(preview, excluded).applicable > 0
}

/** Clave i18n de la etiqueta de una acción. */
export const actionLabelKey = (a: BulkAction): string => `chains.bulk.action.${a}`

/** Clave i18n de «Sin cadena» (cliente sin cadena o cadena inactiva). */
export const NO_CHAIN_KEY = 'chains.noChain'

/** Nombre de la cadena de un cliente o «Sin cadena». */
export const chainOrNone = (name: string | null | undefined, t: (k: string) => string): string => (name && name.trim() ? name : t(NO_CHAIN_KEY))

/** Cuántos clientes ya están en otra cadena y se saltaron al asignar (para preguntar si moverlos). */
export const skippedCount = (r: { skippedOtherChain?: readonly unknown[] | null } | undefined): number => r?.skippedOtherChain?.length ?? 0

// ---------------------------------------------------------------- formulario de la cadena

export const CHAIN_NAME_MAX = 100
export const CHAIN_NOTES_MAX = 500

export interface ChainFormMessages {
  nameRequired: string
  nameMax: string
  notesMax: string
}

/** Esquema del formulario (nombre obligatorio hasta 100; notas opcionales hasta 500). */
export const chainSchema = (m: ChainFormMessages) =>
  z.object({
    name: z.string().trim().min(1, m.nameRequired).max(CHAIN_NAME_MAX, m.nameMax),
    notes: z.string().max(CHAIN_NOTES_MAX, m.notesMax),
  })

export interface ChainFormValues {
  name: string
  notes: string
}

/** Solicitud al crear. */
export const createChainRequest = (v: ChainFormValues): { name: string; notes?: string } => ({ name: v.name.trim(), notes: v.notes.trim() || undefined })

/** Solicitud al editar: las notas vacías las borran (`clearNotes`); lleva el rowVersion de la ficha que se está editando. */
export const updateChainRequest = (v: ChainFormValues, rowVersion: string | undefined): { name: string; notes?: string; clearNotes: boolean; rowVersion?: string } => {
  const notes = v.notes.trim()
  return { name: v.name.trim(), notes: notes || undefined, clearNotes: notes === '', rowVersion }
}
