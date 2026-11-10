import { describe, expect, it } from 'vitest'
import { componentNames, componentOptions, componentsText, exemptionError, nextDay, parsePercent, rateError, scopeBody, scopeOf, sumPercent, type ExemptionDraft, type RateDraft } from './taxRules'

const draft = (patch: Partial<RateDraft> = {}): RateDraft => ({
  code: 'IVU',
  name: 'IVU',
  from: '2026-01-01',
  to: '',
  components: [
    { key: 1, code: 'STATE', name: 'Estatal', percent: '10,5' },
    { key: 2, code: 'CITY', name: 'Municipal', percent: '1' },
  ],
  ...patch,
})

describe('taxRules', () => {
  it('lee porcentajes con coma o punto y rechaza lo que no es número', () => {
    expect(parsePercent('10,5')).toBe(10.5)
    expect(parsePercent(' 1 ')).toBe(1)
    expect(parsePercent('0.0001')).toBe(0.0001)
    expect(parsePercent('')).toBeNull()
    expect(parsePercent('abc')).toBeNull()
    expect(parsePercent('-1')).toBeNull()
  })

  it('suma los componentes sin errores de coma flotante', () => {
    expect(sumPercent(draft().components)).toBe(11.5)
    expect(sumPercent([{ percent: '0,1' }, { percent: '0,2' }])).toBe(0.3)
    expect(sumPercent([{ percent: 'x' }])).toBe(0)
  })

  it('valida la tasa en el mismo orden que el servidor', () => {
    expect(rateError(draft())).toBeNull()
    expect(rateError(draft({ code: ' ' }))).toBe('code')
    expect(rateError(draft({ name: '' }))).toBe('name')
    expect(rateError(draft({ from: '' }))).toBe('from')
    expect(rateError(draft({ to: '2025-12-31' }))).toBe('dates')
    expect(rateError(draft({ components: [] }))).toBe('components')
    expect(rateError(draft({ components: [{ key: 1, code: '', name: 'x', percent: '1' }] }))).toBe('componentFields')
    expect(rateError(draft({ components: [{ key: 1, code: 'A', name: 'x', percent: '101' }] }))).toBe('percent')
    expect(rateError(draft({ components: [{ key: 1, code: 'A', name: 'x', percent: 'abc' }] }))).toBe('percent')
    expect(rateError(draft({ components: [{ key: 1, code: 'A', name: 'x', percent: '60' }, { key: 2, code: 'B', name: 'y', percent: '50' }] }))).toBe('total')
  })

  it('valida la exención', () => {
    const ok: ExemptionDraft = { clientId: 1, type: 'Sin fines de lucro', certificate: 'C-1', from: '2026-01-01', to: '', notes: '', scope: 'ALL', components: [] }
    expect(exemptionError(ok)).toBeNull()
    expect(exemptionError({ ...ok, clientId: null })).toBe('client')
    expect(exemptionError({ ...ok, type: ' ' })).toBe('type')
    expect(exemptionError({ ...ok, certificate: '' })).toBe('certificate')
    expect(exemptionError({ ...ok, from: '' })).toBe('from')
    expect(exemptionError({ ...ok, to: '2025-01-01' })).toBe('dates')
    // Lote 237: «solo estos componentes» pide al menos uno; con «todo el impuesto» los componentes no cuentan
    expect(exemptionError({ ...ok, scope: 'COMPONENTS' })).toBe('components')
    expect(exemptionError({ ...ok, scope: 'COMPONENTS', components: ['CITY'] })).toBeNull()
    expect(exemptionError({ ...ok, scope: 'ALL', components: ['CITY'] })).toBeNull()
  })

  it('Lote 237: alcance de la exención — lo que se manda, las casillas y los nombres', () => {
    expect(scopeOf('COMPONENTS')).toBe('COMPONENTS')
    expect(scopeOf(undefined)).toBe('ALL')
    expect(scopeOf('OTRO')).toBe('ALL')
    expect(scopeBody({ scope: 'COMPONENTS', components: ['STATE', 'CITY'] })).toEqual({ scope: 'COMPONENTS', componentCodes: ['CITY', 'STATE'] })
    expect(scopeBody({ scope: 'ALL', components: ['CITY'] })).toEqual({ scope: 'ALL', componentCodes: [] })

    const rates = [
      { code: 'IVU', status: 'ACTIVE', components: [{ code: 'STATE', name: 'Estatal', percent: 10.5 }, { code: 'CITY', name: 'Municipal', percent: 1 }] },
      { code: 'IVU', status: 'CLOSED', components: [{ code: 'OLD', name: 'Viejo', percent: 11 }] },
      { code: 'OTRA', status: 'ACTIVE', components: [{ code: 'SPECIAL', name: 'Especial', percent: 2 }] },
    ]
    // los de la tasa por omisión vigente (ni la cerrada ni otra tasa)
    expect(componentOptions(rates, 'IVU')).toEqual([{ code: 'STATE', name: 'Estatal', percent: 10.5 }, { code: 'CITY', name: 'Municipal', percent: 1 }])
    // sin tasa por omisión: los de todas las vigentes
    expect(componentOptions(rates, null).map((o) => o.code)).toEqual(['STATE', 'CITY', 'SPECIAL'])
    // lo que la exención ya tenía y la tasa vigente no trae se conserva al final (sin porcentaje)
    expect(componentOptions(rates, 'IVU', ['CITY', 'COUNTY'])).toEqual([
      { code: 'STATE', name: 'Estatal', percent: 10.5 },
      { code: 'CITY', name: 'Municipal', percent: 1 },
      { code: 'COUNTY', name: 'COUNTY', percent: null },
    ])
    expect(componentOptions(undefined, 'IVU')).toEqual([])
    expect(componentNames(['CITY', 'COUNTY'], componentOptions(rates, 'IVU'))).toBe('Municipal, COUNTY')
    expect(componentNames(null, [])).toBe('')
  })

  it('texto de componentes y día siguiente (fin de mes y de año)', () => {
    expect(componentsText([{ name: 'Estatal', percent: 10.5 }, { name: 'Municipal', percent: 1 }])).toBe('Estatal 10.5 % + Municipal 1 %')
    expect(componentsText(null)).toBe('')
    expect(nextDay('2026-10-14')).toBe('2026-10-15')
    expect(nextDay('2026-10-31')).toBe('2026-11-01')
    expect(nextDay('2026-12-31')).toBe('2027-01-01')
  })
})
