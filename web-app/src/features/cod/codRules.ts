// Reglas puras de la pantalla COD (Lote 85): validar un cobro antes de enviarlo, juntar los códigos de los archivos y de la captura manual, y resumir lo que respondió el servidor. El servidor repite las
// validaciones; aquí solo lo que se puede decir antes de enviar.
import type { CodReconcileItemDto } from './codApi'
import type { ScannedCode } from './barcodeReader'

export const COD_STATUSES = ['PENDING', 'PARTIAL', 'COLLECTED', 'RECONCILED', 'REMITTED'] as const
export const COD_METHODS = ['CASH', 'CHECK', 'COMPANY_CHECK', 'ATH_MOVIL', 'CARD'] as const
export const RESULTS = ['RECONCILED', 'ALREADY_RECONCILED', 'NOT_FOUND', 'AMBIGUOUS', 'NO_COD', 'NOTHING_COLLECTED', 'DUPLICATE'] as const
export const MAX_CODES = 2000

export function needsCheckNumber(method: string): boolean {
  return method === 'CHECK' || method === 'COMPANY_CHECK'
}

/** «12,5» o «12.5» → 12.5; vacío, inválido o con más de 2 decimales → null. */
export function parseAmount(text: string): number | null {
  const clean = text.trim().replace(',', '.')
  if (!/^\d+(\.\d{1,2})?$/.test(clean)) return null
  const n = Number(clean)
  return n > 0 ? n : null
}

export interface CollectForm {
  method: string
  amount: string
  checkNumber: string
  reference: string
  notes: string
}

export type CollectError = 'method' | 'amount' | 'exceeds' | 'checkNumber' | 'reference' | 'notes'

export function collectFormError(f: CollectForm, pending: number): CollectError | null {
  if (!f.method) return 'method'
  const amount = parseAmount(f.amount)
  if (amount === null) return 'amount'
  if (amount > pending + 0.00001) return 'exceeds'
  if (needsCheckNumber(f.method) && f.checkNumber.trim() === '') return 'checkNumber'
  if (f.checkNumber.trim().length > 40) return 'checkNumber'
  if (f.reference.trim().length > 80) return 'reference'
  if (f.notes.trim().length > 300) return 'notes'
  return null
}

export function toCollectRequest(f: CollectForm) {
  return {
    method: f.method,
    amount: parseAmount(f.amount) ?? undefined,
    checkNumber: f.checkNumber.trim() || undefined,
    reference: f.reference.trim() || undefined,
    notes: f.notes.trim() || undefined,
  }
}

/** Una fila de la lista de códigos por procesar: de dónde salió (archivo y página, o «Manual»). */
export interface CodeRow {
  /** Clave estable de la fila (para quitarla). */
  key: number
  code: string
  source: string
}

/** Códigos escritos a mano: uno por línea, o separados por coma, punto y coma o espacios. Vacíos fuera. */
export function parseCodes(text: string): string[] {
  return text
    .split(/[\s,;]+/)
    .map((c) => c.trim())
    .filter((c) => c !== '')
}

export function rowsFromScanned(scanned: readonly ScannedCode[], firstKey: number): CodeRow[] {
  return scanned.map((s, i) => ({ key: firstKey + i, code: s.code, source: s.source }))
}

export function rowsFromManual(text: string, firstKey: number): CodeRow[] {
  return parseCodes(text).map((c, i) => ({ key: firstKey + i, code: c, source: MANUAL_SOURCE }))
}

export const MANUAL_SOURCE = 'Manual'

/** El cuerpo de POST /cod/reconcile. */
export function toReconcileRequest(rows: readonly CodeRow[], clientPublicId: string | null, source: string, dryRun: boolean) {
  return {
    codes: rows.map((r) => ({ code: r.code, source: r.source })),
    source: source || undefined,
    clientPublicId: clientPublicId ?? undefined,
    dryRun,
  }
}

/** ¿Se puede enviar la lista? (al menos uno y no más de MAX_CODES). */
export function codesError(rows: readonly CodeRow[]): 'none' | 'tooMany' | null {
  if (rows.length === 0) return 'none'
  return rows.length > MAX_CODES ? 'tooMany' : null
}

/** El origen de la corrida: los nombres de los archivos (sin repetir) y «Manual» si hubo captura a mano; recortado a lo que admite el servidor. */
export function runSource(rows: readonly CodeRow[]): string {
  const names = new Set<string>()
  for (const r of rows) names.add(r.source === MANUAL_SOURCE ? MANUAL_SOURCE : r.source.replace(/ p\d+$/, ''))
  const text = [...names].join(', ')
  return text.length > 300 ? text.slice(0, 297) + '…' : text
}

export type ResultTone = 'ok' | 'info' | 'warn' | 'bad'

export function resultTone(result: string | null | undefined): ResultTone {
  switch (result) {
    case 'RECONCILED':
      return 'ok'
    case 'ALREADY_RECONCILED':
    case 'DUPLICATE':
      return 'info'
    case 'NOT_FOUND':
      return 'bad'
    default:
      return 'warn'
  }
}

/** Cuántos códigos dieron cada resultado (para el resumen de la vista previa y de la corrida). */
export function countResults(items: readonly CodReconcileItemDto[]): Record<string, number> {
  const out: Record<string, number> = {}
  for (const i of items) out[i.result ?? ''] = (out[i.result ?? ''] ?? 0) + 1
  return out
}

/** Lo que se va a cuadrar en dinero en total (suma de las filas que se cuadrarían). */
export function reconcilableAmount(items: readonly CodReconcileItemDto[]): number {
  return items.filter((i) => i.result === 'RECONCILED').reduce((s, i) => s + (i.reconciled ?? 0), 0)
}
