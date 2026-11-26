// Lote 248 — reglas puras de los documentos del cliente con vigencia: marcas del tipo (ExtraJson: «lleva vigencia» y «es de impuestos»), estado y color del chip,
// y validación del formulario. El servidor repite todas las reglas; aquí solo se evita el viaje de ida y vuelta.
import type { ChipTone } from '../../kernel/ui'

/** Marcas de un tipo de documento (espejo de ClientDocumentTypeMarks): sin marcas = ni lleva vigencia ni es de impuestos. */
export interface DocumentTypeMarks {
  requiresValidTo: boolean
  isTax: boolean
}

export const CLIENT_DOCUMENT_TYPE_ENTITY = 'ClientDocumentType'
export const MAX_DOCUMENT_NUMBER = 60

export function parseDocumentTypeMarks(json: string | null | undefined): DocumentTypeMarks {
  const marks: DocumentTypeMarks = { requiresValidTo: false, isTax: false }
  if (!json || !json.trim()) return marks
  try {
    const o = JSON.parse(json) as Record<string, unknown>
    if (o && typeof o === 'object' && !Array.isArray(o)) {
      for (const [k, v] of Object.entries(o)) {
        if (typeof v !== 'boolean') continue
        if (k.toLowerCase() === 'requiresvalidto') marks.requiresValidTo = v
        else if (k.toLowerCase() === 'istax') marks.isTax = v
      }
    }
  } catch {
    // un JSON roto se ignora, como en el servidor
  }
  return marks
}

export function documentTypeMarksJson(marks: DocumentTypeMarks): string {
  return JSON.stringify({ requiresValidTo: marks.requiresValidTo, isTax: marks.isTax })
}

/** Color del chip de estado de vigencia (los estados los calcula el servidor con la fecha «hasta»). */
export const EXPIRY_TONE: Record<string, ChipTone> = {
  VALID: 'deliv',
  EXPIRING: 'warn',
  EXPIRED: 'fail',
  RENEWED: 'neutral',
  MISSING_VALID_TO: 'warn',
  NO_EXPIRY: 'neutral',
}

export function expiryTone(state: string | null | undefined): ChipTone {
  return EXPIRY_TONE[state ?? ''] ?? 'neutral'
}

/** Clave del texto del estado (`clients.documents.states.*`); un estado desconocido se muestra como «sin vigencia». */
export function expiryStateKey(state: string | null | undefined): string {
  return `clients.documents.states.${state && state in EXPIRY_TONE ? state : 'NO_EXPIRY'}`
}

export interface DocumentDraft {
  type: string
  number: string
  from: string
  to: string
}

export type DocumentErrorKey = 'type' | 'file' | 'number' | 'toRequired' | 'dates'

/** Primer problema del formulario de subida (null = bien). `requiresValidTo` viene de las marcas del tipo escogido. */
export function documentError(d: DocumentDraft, hasFile: boolean, marks: DocumentTypeMarks): DocumentErrorKey | null {
  if (!d.type) return 'type'
  if (!hasFile) return 'file'
  return documentFieldsError(d, marks)
}

/** Lo mismo sin tipo ni archivo (editar un documento ya subido). */
export function documentFieldsError(d: Pick<DocumentDraft, 'number' | 'from' | 'to'>, marks: DocumentTypeMarks): DocumentErrorKey | null {
  if (d.number.trim().length > MAX_DOCUMENT_NUMBER) return 'number'
  if (marks.requiresValidTo && !d.to) return 'toRequired'
  if (d.from && d.to && d.to < d.from) return 'dates'
  return null
}

/** Texto corto de las fechas de vigencia para una tabla: «desde → hasta», con lo que falte como «—». */
export function validityRange(from: string | null | undefined, to: string | null | undefined, fmt: (v: string) => string): string {
  if (!from && !to) return '—'
  return `${from ? fmt(from) : '—'} → ${to ? fmt(to) : '—'}`
}
