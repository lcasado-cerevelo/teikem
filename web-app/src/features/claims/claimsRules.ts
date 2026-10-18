// Reglas puras de «Reclamaciones» (Lote 111): validación del formulario de reclamación y de resolución (el API manda al final con los mismos mensajes).
export const CLAIM_NOTES_MAX = 1000
export const CLAIM_RESOLUTIONS = ['PAID', 'REJECTED', 'CLOSED'] as const
export type ClaimResolution = (typeof CLAIM_RESOLUTIONS)[number]

/** Número con coma o punto → número; vacío → null; texto inválido → NaN. */
export function parseAmount(raw: string): number | null {
  const s = raw.trim().replace(',', '.')
  if (s === '') return null
  return /^-?\d+(\.\d{1,2})?$/.test(s) ? Number(s) : Number.NaN
}

export interface CreateForm {
  declaredValue: string
  notes: string
}
export function createFormError(f: CreateForm): 'badValue' | 'negativeValue' | 'notesTooLong' | null {
  const v = parseAmount(f.declaredValue)
  if (v !== null && Number.isNaN(v)) return 'badValue'
  if (v !== null && v < 0) return 'negativeValue'
  if (f.notes.trim().length > CLAIM_NOTES_MAX) return 'notesTooLong'
  return null
}

export interface ResolveForm {
  resolution: ClaimResolution | ''
  amountPaid: string
  notes: string
}
export function resolveFormError(f: ResolveForm): 'resolution' | 'amountRequired' | 'badValue' | 'notesTooLong' | null {
  if (f.resolution === '') return 'resolution'
  if (f.resolution === 'PAID') {
    const v = parseAmount(f.amountPaid)
    if (v === null) return 'amountRequired'
    if (Number.isNaN(v) || v < 0) return 'badValue'
  }
  if (f.notes.trim().length > CLAIM_NOTES_MAX) return 'notesTooLong'
  return null
}

export function toResolveRequest(f: ResolveForm, rowVersion?: string | null) {
  return {
    resolution: f.resolution,
    amountPaid: f.resolution === 'PAID' ? parseAmount(f.amountPaid) : null,
    notes: f.notes.trim() || null,
    rowVersion: rowVersion ?? null,
  }
}
