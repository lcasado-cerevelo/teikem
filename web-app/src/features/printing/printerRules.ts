// Lote F-G1 — reglas puras de «Impresoras y labels»: el esquema del modal de impresora (mismos mensajes y límites que el servidor,
// PrintingRules), el request de alta y edición (solo lo cambiado + rowVersion; el uso nunca se manda en la edición) y la plantilla
// elegida al abrir el constructor.
import { z } from 'zod'
import type { LabelTemplateDto, PrinterDto, PrinterRequest } from './printingApi'

/** Valor del selector del constructor para el label integrado (sin plantilla). */
export const BUILT_IN_ID = '__builtin__'

/** El valor elegido al abrir: la predeterminada de la compañía, si no la primera activa, si no el integrado. */
export function initialTemplateId(list: readonly LabelTemplateDto[]): string {
  return list.find((x) => x.isDefault && x.isActive)?.publicId ?? list.find((x) => x.isActive)?.publicId ?? BUILT_IN_ID
}

export const LABEL_MEDIA_OPTIONS = ['4X6', '4X4', '4X2'] as const

type Translate = (key: string, params?: Record<string, string | number>) => string

/** Esquema del modal (mismos mensajes y límites que el servidor, PrintingRules). */
export function printerSchema(t: Translate) {
  return z.object({
    name: z.string().trim().min(1, t('printing.printer.errors.nameRequired')).max(80, t('printing.printer.errors.nameMax')),
    purpose: z.enum(['LABEL', 'DOCUMENT'], { error: t('printing.printer.errors.purposeRequired') }),
    connectionType: z.enum(['BROWSER', 'QZ_TRAY']),
    systemName: z.string().trim().max(200, t('printing.printer.errors.systemNameMax')),
    model: z.string().trim().max(80, t('printing.printer.errors.modelMax')),
    mediaSize: z.string(),
    rotateMedia: z.boolean(),
    notes: z.string().trim().max(300, t('printing.printer.errors.notesMax')),
  })
}
export type PrinterValues = z.infer<ReturnType<typeof printerSchema>>

export function printerValuesOf(p: PrinterDto | null): PrinterValues {
  return {
    name: p?.name ?? '',
    purpose: (p?.purpose as 'LABEL' | 'DOCUMENT' | undefined) ?? 'LABEL',
    connectionType: p?.connectionType === 'QZ_TRAY' ? 'QZ_TRAY' : 'BROWSER',
    systemName: p?.systemName ?? '',
    model: p?.model ?? '',
    mediaSize: p?.mediaSize ?? '4X6',
    rotateMedia: p?.rotateMedia ?? false,
    notes: p?.notes ?? '',
  }
}

/** Alta: todo; edición: lo cambiado (el uso nunca) + rowVersion. Texto vacío = quitarlo. */
export function buildPrinterRequest(v: PrinterValues, original: PrinterDto | null): PrinterRequest {
  const media = v.purpose === 'DOCUMENT' ? 'LETTER' : (LABEL_MEDIA_OPTIONS as readonly string[]).includes(v.mediaSize) ? v.mediaSize : '4X6'
  if (!original)
    return { name: v.name.trim(), purpose: v.purpose, connectionType: v.connectionType, systemName: v.systemName.trim() || null, model: v.model.trim() || null, mediaSize: media, rotateMedia: v.rotateMedia, notes: v.notes.trim() || null }
  const o = printerValuesOf(original)
  const req: PrinterRequest = { rowVersion: original.rowVersion }
  if (v.name.trim() !== o.name) req.name = v.name.trim()
  if (v.connectionType !== o.connectionType) req.connectionType = v.connectionType
  if (v.systemName.trim() !== o.systemName) req.systemName = v.systemName.trim()
  if (v.model.trim() !== o.model) req.model = v.model.trim()
  if (media !== o.mediaSize) req.mediaSize = media
  if (v.rotateMedia !== o.rotateMedia) req.rotateMedia = v.rotateMedia
  if (v.notes.trim() !== o.notes) req.notes = v.notes.trim()
  return req
}
