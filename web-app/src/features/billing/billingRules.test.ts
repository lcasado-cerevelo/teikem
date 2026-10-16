// Lote 73 — reglas puras de la pantalla de Facturación.
import { describe, expect, it } from 'vitest'
import { adjustError, invoiceCanVoid, monthStartIso, parseAmount, runFormError, runIsOpen, todayIso, toRunRequest, type RunForm } from './billingRules'

const form = (patch: Partial<RunForm> = {}): RunForm => ({ clientPublicId: null, periodStart: '2026-10-01', periodEnd: '2026-10-15', onlyInPeriod: false, includeProducts: true, includeFreight: true, note: '', ...patch })

describe('formulario de la corrida', () => {
  it('pide las dos fechas, en orden, y una nota corta', () => {
    expect(runFormError(form())).toBeNull()
    expect(runFormError(form({ periodStart: '' }))).toBe('period')
    expect(runFormError(form({ periodEnd: '' }))).toBe('period')
    expect(runFormError(form({ periodStart: '2026-10-20' }))).toBe('order')
    expect(runFormError(form({ includeProducts: false, includeFreight: false }))).toBe('include')
    expect(runFormError(form({ includeProducts: false }))).toBeNull()
    expect(runFormError(form({ note: 'x'.repeat(501) }))).toBe('note')
  })

  it('el cuerpo omite lo vacío y solo manda onlyInPeriod si se marcó', () => {
    expect(toRunRequest(form())).toEqual({ clientPublicId: undefined, periodStart: '2026-10-01', periodEnd: '2026-10-15', onlyInPeriod: undefined, includeProducts: undefined, includeFreight: undefined, note: undefined })
    expect(toRunRequest(form({ includeFreight: false }))).toMatchObject({ includeProducts: undefined, includeFreight: false })
    expect(toRunRequest(form({ clientPublicId: 'c1', onlyInPeriod: true, note: '  Octubre  ' }))).toMatchObject({ clientPublicId: 'c1', onlyInPeriod: true, note: 'Octubre' })
  })

  it('fechas por omisión: el primer día del mes y hoy', () => {
    const d = new Date(2026, 9, 16)
    expect(todayIso(d)).toBe('2026-10-16')
    expect(monthStartIso(d)).toBe('2026-10-01')
  })
})

describe('ajuste de una línea', () => {
  it('monto numérico de hasta 4 decimales y motivo obligatorio', () => {
    expect(parseAmount('8,5')).toBe(8.5)
    expect(parseAmount('')).toBeNull()
    expect(parseAmount('-1')).toBeNull()
    expect(adjustError('8', 'Descuento')).toBeNull()
    expect(adjustError('x', 'Descuento')).toBe('amount')
    expect(adjustError('8.12345', 'Descuento')).toBe('decimals')
    expect(adjustError('8', '  ')).toBe('note')
    expect(adjustError('8', 'x'.repeat(251))).toBe('noteLong')
  })
})

describe('estatus', () => {
  it('solo una corrida generada o revisada está abierta; solo emitida o vencida se anula', () => {
    expect(['GENERATED', 'REVIEWED', 'APPROVED', 'EXPORTED', 'CANCELLED'].map(runIsOpen)).toEqual([true, true, false, false, false])
    expect(['DRAFT', 'ISSUED', 'PAID', 'OVERDUE', 'VOID'].map(invoiceCanVoid)).toEqual([false, true, false, true, false])
  })
})
