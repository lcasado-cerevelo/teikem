import { describe, expect, it } from 'vitest'
import { inDateRange } from './dateRange'
import { matchesQ } from './matchesQ'

describe('matchesQ', () => {
  it('q vacío o solo espacios coincide con todo', () => {
    expect(matchesQ('', 'ACME')).toBe(true)
    expect(matchesQ('   ', 'ACME')).toBe(true)
    expect(matchesQ(null, 'ACME')).toBe(true)
  })

  it('no distingue mayúsculas ni acentos', () => {
    expect(matchesQ('camion', 'Camión refrigerado')).toBe(true)
    expect(matchesQ('CAMIÓN', 'camion')).toBe(true)
  })

  it('cada palabra debe aparecer en alguno de los textos', () => {
    expect(matchesQ('acme norte', 'ACME', 'Zona Norte')).toBe(true)
    expect(matchesQ('acme sur', 'ACME', 'Zona Norte')).toBe(false)
  })

  it('ignora nulos y acepta números', () => {
    expect(matchesQ('42', null, undefined, 42)).toBe(true)
    expect(matchesQ('x', null, undefined)).toBe(false)
  })
})

describe('inDateRange', () => {
  it('extremos vacíos son abiertos y el rango es inclusivo', () => {
    expect(inDateRange('2026-09-01T10:00:00Z', { from: '', to: '' })).toBe(true)
    expect(inDateRange('2026-09-01T10:00:00Z', { from: '2026-09-01', to: '2026-09-01' })).toBe(true)
    expect(inDateRange('2026-09-02', { from: '', to: '2026-09-01' })).toBe(false)
    expect(inDateRange(null, { from: '2026-09-01', to: '' })).toBe(false)
  })
})
