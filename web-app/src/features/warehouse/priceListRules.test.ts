import { describe, expect, it } from 'vitest'
import { EMPTY_LIST_DRAFT, listDraftError, parsePrice, publishBlocker, stateKey, stateTone, toListRequest } from './priceListRules'

describe('priceListRules', () => {
  it('estado: color y clave, desconocido = borrador', () => {
    expect(stateTone('ACTIVE')).toBe('deliv')
    expect(stateTone('SCHEDULED')).toBe('warn')
    expect(stateTone('EXPIRED')).toBe('fail')
    expect(stateTone('DRAFT')).toBe('cap')
    expect(stateKey('???')).toBe('DRAFT')
    expect(stateKey(null)).toBe('DRAFT')
    expect(stateKey('EXPIRED')).toBe('EXPIRED')
  })

  it('valida nombre, desde y rango', () => {
    const d = EMPTY_LIST_DRAFT('2026-11-07')
    expect(listDraftError(d)).toBe('nameRequired')
    expect(listDraftError({ ...d, name: ' Temporada ' })).toBeNull()
    expect(listDraftError({ ...d, name: 'x', validFrom: '' })).toBe('fromRequired')
    expect(listDraftError({ ...d, name: 'x', validTo: '2026-11-06' })).toBe('range')
    expect(listDraftError({ ...d, name: 'x', validTo: '2026-11-07' })).toBeNull()
  })

  it('el cuerpo recorta y, sin fecha final, manda clearValidTo', () => {
    const d = { ...EMPTY_LIST_DRAFT('2026-11-07'), name: '  Navidad ', notes: '  ' }
    expect(toListRequest(d)).toEqual({ name: 'Navidad', validFrom: '2026-11-07', validTo: null, clearValidTo: true, appliesToAll: true, notes: null })
    expect(toListRequest({ ...d, validTo: '2026-12-31', notes: 'n' })).toMatchObject({ validTo: '2026-12-31', clearValidTo: false, notes: 'n' })
  })

  it('precio: números válidos con punto o coma', () => {
    expect(parsePrice('12.5')).toBe(12.5)
    expect(parsePrice('12,5')).toBe(12.5)
    expect(parsePrice('0')).toBe(0)
    expect(parsePrice('')).toBeNull()
    expect(parsePrice('-1')).toBeNull()
    expect(parsePrice('1.23456')).toBeNull()
    expect(parsePrice('abc')).toBeNull()
  })

  it('publicar exige renglones y clientes (o todos)', () => {
    expect(publishBlocker(0, true, 0)).toBe('noItems')
    expect(publishBlocker(2, false, 0)).toBe('noClients')
    expect(publishBlocker(2, false, 1)).toBeNull()
    expect(publishBlocker(2, true, 0)).toBeNull()
  })
})
