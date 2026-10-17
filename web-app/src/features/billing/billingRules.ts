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

// ---------------------------------------------------------------- pagos y estado de cuenta (Lote 76)

export const PAYMENT_METHODS = ['CASH', 'CHECK', 'ACH', 'CARD'] as const

/** Solo una factura emitida o vencida recibe pagos. */
export function invoiceCanPay(status: string | null | undefined): boolean {
  return status === 'ISSUED' || status === 'OVERDUE'
}

/** Un pago se anula mientras la factura no esté pagada por completo (ese estatus es terminal). */
export function paymentCanVoid(invoiceStatus: string | null | undefined, voidedAtUtc: string | null | undefined): boolean {
  return invoiceCanPay(invoiceStatus) && !voidedAtUtc
}

export interface PaymentForm {
  amount: string
  method: string
  paidDate: string
  reference: string
}

export type PaymentFormError = 'amount' | 'decimals' | 'method' | 'exceeds' | 'future' | 'beforeIssue' | 'reference'

/** Lo que el servidor también valida (PaymentRules): monto con hasta 2 decimales sin pasar del saldo, método, fecha entre la emisión y hoy, referencia de hasta 80 caracteres. */
export function paymentFormError(f: PaymentForm, balance: number, today: string, issueDate: string | null | undefined): PaymentFormError | null {
  const n = parseAmount(f.amount)
  if (n === null || n <= 0) return 'amount'
  if (decimalsOf(n) > 2) return 'decimals'
  if (n > balance + 0.00001) return 'exceeds'
  if (!f.method) return 'method'
  if (f.paidDate > today) return 'future'
  if (issueDate && f.paidDate < issueDate) return 'beforeIssue'
  if (f.reference.trim().length > 80) return 'reference'
  return null
}

export function toPaymentRequest(f: PaymentForm, today: string) {
  return { amount: parseAmount(f.amount)!, method: f.method, paidDate: f.paidDate === today ? undefined : f.paidDate, reference: f.reference.trim() || undefined }
}

export type VoidPaymentError = 'required' | 'long'

export function voidPaymentError(reason: string): VoidPaymentError | null {
  const r = reason.trim()
  if (r === '') return 'required'
  if (r.length > 250) return 'long'
  return null
}

/** Fecha inicial por omisión del estado de cuenta: 90 días antes de la final (como el servidor). */
export function statementFrom(to: string): string {
  const d = new Date(`${to}T00:00:00`)
  d.setDate(d.getDate() - 90)
  return todayIso(d)
}

export const AGING_KEYS = ['current', 'days1To30', 'days31To60', 'days61To90', 'over90'] as const

// ---------------------------------------------------------------- plantillas de exportación (Lote 78)

export const EXPORT_DELIMITERS = [
  { value: ',', key: 'comma' },
  { value: ';', key: 'semicolon' },
  { value: '\t', key: 'tab' },
  { value: '|', key: 'pipe' },
] as const

export const EXPORT_DATE_FORMATS = ['YYYY-MM-DD', 'MM/DD/YYYY', 'DD/MM/YYYY'] as const
export const EXPORT_FORMATS = ['CSV', 'TXT'] as const

export interface TemplateColumn {
  field: string
  header: string
}

export interface TemplateForm {
  name: string
  format: string
  delimiter: string
  dateFormat: string
  includeHeader: boolean
  columns: TemplateColumn[]
}

export type TemplateFormError = 'name' | 'nameLong' | 'columns' | 'header'

/** Lo que el servidor también valida (ExportRules): nombre de hasta 80, al menos una columna y encabezados de hasta 60. */
export function templateFormError(f: TemplateForm): TemplateFormError | null {
  const name = f.name.trim()
  if (name === '') return 'name'
  if (name.length > 80) return 'nameLong'
  if (f.columns.length === 0) return 'columns'
  if (f.columns.some((c) => c.header.trim().length > 60)) return 'header'
  return null
}

export function toTemplateRequest(f: TemplateForm) {
  return {
    name: f.name.trim(),
    format: f.format,
    delimiter: f.delimiter,
    dateFormat: f.dateFormat,
    includeHeader: f.includeHeader,
    columns: f.columns.map((c) => ({ field: c.field, header: c.header.trim() || undefined })),
  }
}

/** Mueve una columna una posición (−1 arriba, +1 abajo); en los extremos no hace nada. */
export function moveColumn<T>(columns: readonly T[], index: number, dir: -1 | 1): T[] {
  const to = index + dir
  if (index < 0 || index >= columns.length || to < 0 || to >= columns.length) return [...columns]
  const next = [...columns]
  ;[next[index], next[to]] = [next[to], next[index]]
  return next
}

/** Los campos del catálogo que todavía no están en la plantilla, en el orden del catálogo. */
export function availableFields<T extends { code?: string }>(catalog: readonly T[], columns: readonly TemplateColumn[]): T[] {
  const used = new Set(columns.map((c) => c.field))
  return catalog.filter((f) => !used.has(f.code ?? ''))
}

/** Una plantilla nueva por omisión: CSV con coma, fecha ISO, encabezado y las columnas dadas. */
export function blankTemplate(columns: readonly TemplateColumn[] = []): TemplateForm {
  return { name: '', format: 'CSV', delimiter: ',', dateFormat: 'YYYY-MM-DD', includeHeader: true, columns: [...columns] }
}

/** Una corrida se exporta cuando está aprobada o ya exportada. */
export function runCanExport(status: string | null | undefined): boolean {
  return status === 'APPROVED' || status === 'EXPORTED'
}
