// Reglas puras de la pestaña Remesas (Lote 87): qué admite cada estatus y la validación de lo que se pide antes de enviar. El servidor repite las reglas; aquí solo lo que se puede decir antes de enviar.
export const REMITTANCE_STATUSES = ['OPEN', 'RECONCILED', 'APPROVED', 'REMITTED'] as const

export function canReview(status: string | null | undefined): boolean {
  return status === 'OPEN'
}

export function canApprove(status: string | null | undefined): boolean {
  return status === 'RECONCILED'
}

export function canRemit(status: string | null | undefined): boolean {
  return status === 'APPROVED'
}

/** Se cancela una abierta o cuadrada (una aprobada o remitida ya no). */
export function canCancel(status: string | null | undefined): boolean {
  return status === 'OPEN' || status === 'RECONCILED'
}

/** Se exporta una aprobada o remitida. */
export function canExport(status: string | null | undefined): boolean {
  return status === 'APPROVED' || status === 'REMITTED'
}

export interface PctValue {
  ok: boolean
  /** null = vacío (sin comisión propia: manda la del contrato o la de la compañía). */
  value: number | null
}

/** «12,5» o «12.5» → 12.5; vacío → sin valor; fuera de 0–100 o con más de 2 decimales → no válido. */
export function parsePct(text: string): PctValue {
  const clean = text.trim().replace(',', '.')
  if (clean === '') return { ok: true, value: null }
  if (!/^\d+(\.\d{1,2})?$/.test(clean)) return { ok: false, value: null }
  const n = Number(clean)
  return n >= 0 && n <= 100 ? { ok: true, value: n } : { ok: false, value: null }
}

export type NewRemittanceError = 'client' | 'period' | 'order' | 'pct'

/** Fechas «AAAA-MM-DD» (lo que dan los campos de fecha). */
export function newRemittanceError(clientId: string | null, from: string, to: string, pctText: string): NewRemittanceError | null {
  if (!clientId) return 'client'
  if (!from || !to) return 'period'
  if (to < from) return 'order'
  if (!parsePct(pctText).ok) return 'pct'
  return null
}

/** El período por omisión: los últimos 30 días hasta hoy. */
export function defaultPeriod(today: Date): { from: string; to: string } {
  const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  const start = new Date(today)
  start.setDate(start.getDate() - 30)
  return { from: iso(start), to: iso(today) }
}

/** De dónde sale la comisión que aplica el servidor. */
export function commissionSourceKey(source: string | null | undefined): 'request' | 'contract' | 'tenant' {
  return source === 'REQUEST' ? 'request' : source === 'CONTRACT' ? 'contract' : 'tenant'
}

/** La comisión por omisión de la compañía: obligatoria, de 0 a 100, hasta 2 decimales. */
export function defaultPctError(text: string): boolean {
  const p = parsePct(text)
  return !p.ok || p.value === null
}
