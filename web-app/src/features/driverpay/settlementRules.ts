// Reglas puras de «Liquidación a choferes» (Lote 96): qué admite cada estatus, la validación de lo que se pide antes de enviar y el agrupado por día. El servidor repite las reglas; aquí solo lo que se puede decir antes de enviar.
export const SETTLEMENT_STATUSES = ['GENERATED', 'REVIEWED', 'APPROVED', 'EXPORTED', 'CANCELLED'] as const

export function canReview(status: string | null | undefined): boolean {
  return status === 'GENERATED'
}

export function canApprove(status: string | null | undefined): boolean {
  return status === 'REVIEWED'
}

/** Se cancela una generada o revisada (una aprobada o exportada ya no). */
export function canCancel(status: string | null | undefined): boolean {
  return status === 'GENERATED' || status === 'REVIEWED'
}

/** Solo se ajustan los montos mientras está generada o revisada. */
export function canAdjust(status: string | null | undefined): boolean {
  return status === 'GENERATED' || status === 'REVIEWED'
}

/** Se exporta una aprobada o exportada. */
export function canExport(status: string | null | undefined): boolean {
  return status === 'APPROVED' || status === 'EXPORTED'
}

export type NewSettlementError = 'period' | 'order'

/** Fechas «AAAA-MM-DD» (lo que dan los campos de fecha). */
export function newSettlementError(from: string, to: string): NewSettlementError | null {
  if (!from || !to) return 'period'
  if (to < from) return 'order'
  return null
}

/** El período por omisión: los últimos 7 días hasta hoy. */
export function defaultPeriod(today: Date): { from: string; to: string } {
  const iso = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
  const start = new Date(today)
  start.setDate(start.getDate() - 7)
  return { from: iso(start), to: iso(today) }
}

export interface AdjustValue {
  ok: boolean
  value: number | null
}

/** «12,5» o «12.50» → 12.5; vacío o con más de 2 decimales o fuera de 0 a 1,000,000 → no válido. */
export function parseAmount(text: string): AdjustValue {
  const clean = text.trim().replace(',', '.')
  if (!/^\d+(\.\d{1,2})?$/.test(clean)) return { ok: false, value: null }
  const n = Number(clean)
  return n >= 0 && n <= 1_000_000 ? { ok: true, value: n } : { ok: false, value: null }
}

export type AdjustError = 'amount' | 'note' | 'noteLong'

/** El ajuste de una línea: el monto válido y, si cambia el calculado, el motivo (hasta 250 caracteres). */
export function adjustError(amountText: string, note: string, calculated: number): AdjustError | null {
  const a = parseAmount(amountText)
  if (!a.ok || a.value === null) return 'amount'
  if (note.trim().length > 250) return 'noteLong'
  if (a.value !== calculated && note.trim() === '') return 'note'
  return null
}

export interface DayGroup<T> {
  date: string
  lines: T[]
  subtotal: number
}

/** Las líneas agrupadas por día del servicio (en el orden en que vienen), con el subtotal de cada día. */
export function groupByDay<T extends { serviceDate?: string | null; amount?: number | null }>(lines: readonly T[]): DayGroup<T>[] {
  const groups: DayGroup<T>[] = []
  for (const l of lines) {
    const date = l.serviceDate ?? ''
    let g = groups.find((x) => x.date === date)
    if (!g) groups.push((g = { date, lines: [], subtotal: 0 }))
    g.lines.push(l)
    g.subtotal += l.amount ?? 0
  }
  return groups
}

export type NewAttemptError = 'order' | 'driver'

export function newAttemptError(orderId: string | null, driverId: string): NewAttemptError | null {
  if (!orderId) return 'order'
  if (!driverId) return 'driver'
  return null
}
