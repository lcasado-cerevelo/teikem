// Pruebas de las reglas puras de la pestaña Remesas (Lote 87).
import { describe, expect, it } from 'vitest'
import { canApprove, canCancel, canExport, canRemit, canReview, commissionSourceKey, defaultPctError, defaultPeriod, newRemittanceError, parsePct } from './remittanceRules'

describe('acciones por estatus', () => {
  it('cuadrar solo en abierta, aprobar en cuadrada, remitir en aprobada', () => {
    expect([canReview('OPEN'), canReview('RECONCILED')]).toEqual([true, false])
    expect([canApprove('RECONCILED'), canApprove('OPEN')]).toEqual([true, false])
    expect([canRemit('APPROVED'), canRemit('REMITTED')]).toEqual([true, false])
  })
  it('cancelar solo abierta o cuadrada; exportar solo aprobada o remitida', () => {
    expect(['OPEN', 'RECONCILED', 'APPROVED', 'REMITTED'].map(canCancel)).toEqual([true, true, false, false])
    expect(['OPEN', 'RECONCILED', 'APPROVED', 'REMITTED'].map(canExport)).toEqual([false, false, true, true])
  })
})

describe('comisión', () => {
  it('vacío es «sin comisión propia»; acepta coma; rechaza fuera de 0–100 o con más de 2 decimales', () => {
    expect(parsePct('')).toEqual({ ok: true, value: null })
    expect(parsePct('8,5')).toEqual({ ok: true, value: 8.5 })
    expect(parsePct('0')).toEqual({ ok: true, value: 0 })
    expect(parsePct('100')).toEqual({ ok: true, value: 100 })
    expect(parsePct('100.5').ok).toBe(false)
    expect(parsePct('8.255').ok).toBe(false)
    expect(parsePct('abc').ok).toBe(false)
    expect(parsePct('-1').ok).toBe(false)
  })
  it('la comisión por omisión de la compañía es obligatoria', () => {
    expect(defaultPctError('')).toBe(true)
    expect(defaultPctError('8')).toBe(false)
    expect(defaultPctError('101')).toBe(true)
  })
  it('de dónde sale la que aplica el servidor', () => {
    expect(['REQUEST', 'CONTRACT', 'TENANT', null].map(commissionSourceKey)).toEqual(['request', 'contract', 'tenant', 'tenant'])
  })
})

describe('nueva remesa', () => {
  it('pide cliente, período en orden y una comisión válida', () => {
    expect(newRemittanceError(null, '2026-10-01', '2026-10-17', '')).toBe('client')
    expect(newRemittanceError('c', '', '2026-10-17', '')).toBe('period')
    expect(newRemittanceError('c', '2026-10-17', '2026-10-01', '')).toBe('order')
    expect(newRemittanceError('c', '2026-10-01', '2026-10-17', '150')).toBe('pct')
    expect(newRemittanceError('c', '2026-10-01', '2026-10-17', '8')).toBeNull()
  })
  it('el período por omisión son los últimos 30 días', () => {
    expect(defaultPeriod(new Date(2026, 9, 17))).toEqual({ from: '2026-09-17', to: '2026-10-17' })
  })
})
