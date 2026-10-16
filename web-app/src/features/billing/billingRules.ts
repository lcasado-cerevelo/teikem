// Reglas puras de la pantalla de Facturación (Lote 73): validación del formulario de la corrida y del ajuste de una línea, y qué acciones admite cada estatus.
// El servidor repite estas reglas (BillingRules); aquí solo lo que se puede decir antes de enviar.

/** «8,5» o «8.5» → 8.5; vacío o inválido → null. */
export function parseAmount(text: string): number | null {
  const clean = text.trim().replace(',', '.')
  if (clean === '' || !/^\d+(\.\d+)?$/.test(clean)) return null
  return Number(clean)
}

export function decimalsOf(n: number): number {
  const s = String(n)
  const i = s.indexOf('.')
  return i === -1 ? 0 : s.length - i - 1
}

export interface RunForm {
  clientPublicId: string | null
  periodStart: string
  periodEnd: string
  onlyInPeriod: boolean
  /** Lote 74: qué se factura (la venta de producto, el flete o las dos cosas). */
  includeProducts: boolean
  includeFreight: boolean
  note: string
}

export type RunFormError = 'period' | 'order' | 'note' | 'include'

export function runFormError(f: RunForm): RunFormError | null {
  if (!f.periodStart || !f.periodEnd) return 'period'
  if (f.periodEnd < f.periodStart) return 'order'
  if (!f.includeProducts && !f.includeFreight) return 'include'
  if (f.note.length > 500) return 'note'
  return null
}

export function toRunRequest(f: RunForm) {
  return {
    clientPublicId: f.clientPublicId ?? undefined,
    periodStart: f.periodStart,
    periodEnd: f.periodEnd,
    onlyInPeriod: f.onlyInPeriod || undefined,
    includeProducts: f.includeProducts ? undefined : false,
    includeFreight: f.includeFreight ? undefined : false,
    note: f.note.trim() || undefined,
  }
}

export type AdjustError = 'amount' | 'decimals' | 'note' | 'noteLong'

export function adjustError(amount: string, note: string): AdjustError | null {
  const n = parseAmount(amount)
  if (n === null) return 'amount'
  if (decimalsOf(n) > 4) return 'decimals'
  if (note.trim() === '') return 'note'
  if (note.trim().length > 250) return 'noteLong'
  return null
}

/** Una corrida generada o revisada admite ajustar las líneas y cancelarla. */
export function runIsOpen(status: string | null | undefined): boolean {
  return status === 'GENERATED' || status === 'REVIEWED'
}

export const RUN_STATUSES = ['GENERATED', 'REVIEWED', 'APPROVED', 'EXPORTED', 'CANCELLED'] as const
export const INVOICE_STATUSES = ['DRAFT', 'ISSUED', 'PAID', 'OVERDUE', 'VOID'] as const

/** Una factura emitida o vencida se puede anular (si no tiene pagos: lo valida el servidor). */
export function invoiceCanVoid(status: string | null | undefined): boolean {
  return status === 'ISSUED' || status === 'OVERDUE'
}

export function todayIso(now = new Date()): string {
  const y = now.getFullYear()
  const m = String(now.getMonth() + 1).padStart(2, '0')
  const d = String(now.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

/** Primer día del mes de la fecha (para la fecha inicial por omisión de una corrida). */
export function monthStartIso(now = new Date()): string {
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-01`
}
