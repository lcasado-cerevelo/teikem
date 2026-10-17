// Lote 96 — reglas puras de la liquidación a choferes.
import { describe, expect, it } from 'vitest'
import { adjustError, canAdjust, canApprove, canCancel, canExport, canReview, defaultPeriod, groupByDay, newAttemptError, newSettlementError, parseAmount } from './settlementRules'

describe('liquidación a choferes: reglas', () => {
  it('qué admite cada estatus', () => {
    expect([canReview('GENERATED'), canReview('REVIEWED')]).toEqual([true, false])
    expect([canApprove('REVIEWED'), canApprove('GENERATED'), canApprove('APPROVED')]).toEqual([true, false, false])
    expect(['GENERATED', 'REVIEWED', 'APPROVED', 'EXPORTED', 'CANCELLED'].map(canCancel)).toEqual([true, true, false, false, false])
    expect(['GENERATED', 'REVIEWED', 'APPROVED', 'EXPORTED', 'CANCELLED'].map(canAdjust)).toEqual([true, true, false, false, false])
    expect(['GENERATED', 'REVIEWED', 'APPROVED', 'EXPORTED', 'CANCELLED'].map(canExport)).toEqual([false, false, true, true, false])
  })

  it('valida el período y propone los últimos 7 días', () => {
    expect(newSettlementError('', '2026-10-17')).toBe('period')
    expect(newSettlementError('2026-10-17', '2026-10-10')).toBe('order')
    expect(newSettlementError('2026-10-10', '2026-10-17')).toBeNull()
    expect(defaultPeriod(new Date(2026, 9, 17))).toEqual({ from: '2026-10-10', to: '2026-10-17' })
  })

  it('lee montos con coma o punto y rechaza lo demás', () => {
    expect(parseAmount('12,5')).toEqual({ ok: true, value: 12.5 })
    expect(parseAmount(' 20.00 ')).toEqual({ ok: true, value: 20 })
    for (const bad of ['', 'abc', '-1', '1.234', '1000000.01']) expect(parseAmount(bad).ok).toBe(false)
  })

  it('el ajuste pide un monto válido y, si cambia, el motivo', () => {
    expect(adjustError('x', '', 10)).toBe('amount')
    expect(adjustError('12', '', 10)).toBe('note')
    expect(adjustError('12', 'Bono', 10)).toBeNull()
    expect(adjustError('10', '', 10)).toBeNull()   // igual al calculado: quita el ajuste, sin motivo
    expect(adjustError('12', 'x'.repeat(251), 10)).toBe('noteLong')
  })

  it('agrupa las líneas por día con su subtotal, en el orden en que vienen', () => {
    const groups = groupByDay([
      { serviceDate: '2026-10-16', amount: 10 },
      { serviceDate: '2026-10-16', amount: 5.5 },
      { serviceDate: '2026-10-17', amount: 3 },
    ])
    expect(groups.map((g) => [g.date, g.lines.length, g.subtotal])).toEqual([['2026-10-16', 2, 15.5], ['2026-10-17', 1, 3]])
    expect(groupByDay([])).toEqual([])
  })

  it('un intento a mano pide la orden y el chofer', () => {
    expect(newAttemptError(null, 'd')).toBe('order')
    expect(newAttemptError('o', '')).toBe('driver')
    expect(newAttemptError('o', 'd')).toBeNull()
  })
})
