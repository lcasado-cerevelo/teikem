import { describe, expect, it } from 'vitest'
import { ACCOUNTING_KINDS, canAccept, canCancel, canExport, defaultPeriod, newRunError } from './accountingRules'

describe('reglas de la contabilización (Lote 181)', () => {
  it('solo una generada se acepta; se cancela una generada o aceptada; se exporta una aceptada o exportada', () => {
    expect([canAccept('GENERATED'), canAccept('ACCEPTED'), canAccept('EXPORTED'), canAccept('CANCELLED'), canAccept(undefined)]).toEqual([true, false, false, false, false])
    expect([canCancel('GENERATED'), canCancel('ACCEPTED'), canCancel('EXPORTED'), canCancel('CANCELLED')]).toEqual([true, true, false, false])
    expect([canExport('GENERATED'), canExport('ACCEPTED'), canExport('EXPORTED'), canExport('CANCELLED')]).toEqual([false, true, true, false])
  })

  it('el período pide las dos fechas y la final no puede ser anterior', () => {
    expect(newRunError('', '2026-10-21')).toBe('period')
    expect(newRunError('2026-10-01', '')).toBe('period')
    expect(newRunError('2026-10-21', '2026-10-01')).toBe('order')
    expect(newRunError('2026-10-01', '2026-10-01')).toBeNull()
  })

  it('el período por omisión son los últimos 30 días hasta hoy', () => {
    expect(defaultPeriod(new Date(2026, 9, 21))).toEqual({ from: '2026-09-21', to: '2026-10-21' })
  })

  it('cada tipo usa los permisos y el alcance de plantillas de su módulo', () => {
    expect(ACCOUNTING_KINDS.PURCHASES).toMatchObject({ view: 'purchasing.view', manage: 'purchasing.manage', export: 'purchasing.manage', scope: 'PURCHASE_ACCOUNTING' })
    expect(ACCOUNTING_KINDS.DISPATCHES).toMatchObject({ view: 'billing.view', manage: 'billing.generate', export: 'billing.export', scope: 'DISPATCH_ACCOUNTING' })
  })
})
