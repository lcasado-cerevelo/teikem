// Reglas puras de la pantalla de Devoluciones de venta (Lote 82): qué admite cada estatus y la validación de lo que se pide y de lo que se recibe antes de enviar. El servidor repite estas reglas
// (SalesReturnRules); aquí solo lo que se puede decir antes de enviar.

export const RETURN_STATUSES = ['REQUESTED', 'AUTHORIZED', 'RECEIVED', 'CREDITED', 'REJECTED', 'CANCELLED'] as const
export const RETURN_REASONS = ['DAMAGED', 'WRONG_ITEM', 'NOT_NEEDED', 'EXPIRED', 'OTHER'] as const

/** «2,5» o «2.5» → 2.5; vacío o inválido → null. */
export function parseQty(text: string): number | null {
  const clean = text.trim().replace(',', '.')
  if (clean === '' || !/^\d+(\.\d+)?$/.test(clean)) return null
  return Number(clean)
}

function decimalsOf(n: number): number {
  const s = String(n)
  const i = s.indexOf('.')
  return i === -1 ? 0 : s.length - i - 1
}

export function returnCanAuthorize(status: string | null | undefined): boolean {
  return status === 'REQUESTED'
}

export function returnCanReject(status: string | null | undefined): boolean {
  return status === 'REQUESTED'
}

/** Se cancela una solicitada o autorizada que todavía no recibió nada. */
export function returnCanCancel(status: string | null | undefined, received: number | null | undefined): boolean {
  return (status === 'REQUESTED' || status === 'AUTHORIZED') && (received ?? 0) === 0
}

/** Se recibe una autorizada, o una recibida en parte que todavía tiene algo pendiente. */
export function returnCanReceive(status: string | null | undefined, pending: number | null | undefined): boolean {
  return status === 'AUTHORIZED' || (status === 'RECEIVED' && (pending ?? 0) > 0)
}

export function returnCanCredit(status: string | null | undefined): boolean {
  return status === 'RECEIVED'
}

// ---------------------------------------------------------------- pedir

export interface ReturnLineForm {
  salesOrderLineId: number
  quantity: string
  /** Lo que todavía se puede devolver de la línea (lo dice el servidor). */
  returnable: number
  blocked: boolean
}

export interface ReturnForm {
  reason: string
  note: string
  confirmOutsideWindow: boolean
  lines: ReturnLineForm[]
}

export type ReturnFormError = 'reason' | 'note' | 'lines' | 'quantity' | 'exceeds' | 'outside'

/** Lo que el servidor también valida antes de crear la devolución; solo cuentan las líneas con cantidad. */
export function returnFormError(f: ReturnForm, outsideWindow: boolean, canAuthorize: boolean): ReturnFormError | null {
  if (!f.reason) return 'reason'
  if (f.note.length > 500) return 'note'
  const chosen = f.lines.filter((l) => l.quantity.trim() !== '')
  if (chosen.length === 0) return 'lines'
  for (const l of chosen) {
    const q = parseQty(l.quantity)
    if (q === null || q <= 0 || decimalsOf(q) > 3) return 'quantity'
    if (q > l.returnable + 0.00001) return 'exceeds'
  }
  if (outsideWindow && !(canAuthorize && f.confirmOutsideWindow)) return 'outside'
  return null
}

export function toReturnRequest(orderPublicId: string, f: ReturnForm) {
  return {
    salesOrderPublicId: orderPublicId,
    reason: f.reason,
    note: f.note.trim() || undefined,
    confirmOutsideWindow: f.confirmOutsideWindow || undefined,
    lines: f.lines.filter((l) => l.quantity.trim() !== '').map((l) => ({ salesOrderLineId: l.salesOrderLineId, quantity: parseQty(l.quantity) ?? undefined })),
  }
}

// ---------------------------------------------------------------- recibir

export interface ReceiveLineForm {
  lineId: number
  pending: number
  good: string
  damaged: string
  damageNote: string
  discard: boolean
  lot: string
}

export interface ReceiveForm {
  lines: ReceiveLineForm[]
  closeRest: boolean
}

export type ReceiveFormError = 'nothing' | 'quantity' | 'exceeds' | 'damageNote'

function qtyOrZero(text: string): number | null {
  return text.trim() === '' ? 0 : parseQty(text)
}

/** Solo cuentan las líneas con algo bueno o dañado; cada cantidad hasta 3 decimales, sin pasar de lo pendiente y con descripción si hay dañado. */
export function receiveFormError(f: ReceiveForm): ReceiveFormError | null {
  let any = false
  for (const l of f.lines) {
    const g = qtyOrZero(l.good)
    const d = qtyOrZero(l.damaged)
    if (g === null || d === null || decimalsOf(g) > 3 || decimalsOf(d) > 3) return 'quantity'
    if (g + d === 0) continue
    any = true
    if (g + d > l.pending + 0.00001) return 'exceeds'
    if (d > 0 && l.damageNote.trim() === '') return 'damageNote'
  }
  return any ? null : 'nothing'
}

export function toReceiveRequest(f: ReceiveForm) {
  return {
    closeRest: f.closeRest || undefined,
    lines: f.lines
      .filter((l) => (qtyOrZero(l.good) ?? 0) + (qtyOrZero(l.damaged) ?? 0) > 0)
      .map((l) => ({
        lineId: l.lineId,
        goodQty: qtyOrZero(l.good) ?? 0,
        damagedQty: qtyOrZero(l.damaged) ?? 0,
        damageNote: (qtyOrZero(l.damaged) ?? 0) > 0 ? l.damageNote.trim() : undefined,
        damageDiscard: (qtyOrZero(l.damaged) ?? 0) > 0 && l.discard ? true : undefined,
        lot: l.lot.trim() ? { number: l.lot.trim() } : undefined,
      })),
  }
}

/** Los días de la ventana de devolución: un entero de 0 a 3650. */
export function windowDaysError(text: string): boolean {
  const n = Number(text)
  return text.trim() === '' || !Number.isInteger(n) || n < 0 || n > 3650
}
