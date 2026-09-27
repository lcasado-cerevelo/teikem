import { describe, expect, it } from 'vitest'
import { dataTypeOf, fromFormValue, issueMessageKey, toFormValue, validateCustomField } from './values'

describe('campos personalizados: conversión', () => {
  it('toFormValue por tipo de dato', () => {
    expect(toFormValue('TEXT', 'CC-100')).toBe('CC-100')
    expect(toFormValue('TEXT', null)).toBe('')
    expect(toFormValue('NUMBER', 12.5)).toBe('12.5')
    expect(toFormValue('BOOL', true)).toBe(true)
    expect(toFormValue('BOOL', 'false')).toBe(false)
    expect(toFormValue('BOOL', null)).toBeNull()
    expect(toFormValue('DATE', '2026-07-20')).toBe('2026-07-20')
    expect(toFormValue('DATE', '2026-07-20T00:00:00')).toBe('2026-07-20')
    expect(toFormValue('MULTISELECT', ['A', 'B'])).toEqual(['A', 'B'])
    expect(toFormValue('MULTISELECT', 'A, B,')).toEqual(['A', 'B'])
    expect(toFormValue('SELECT', undefined)).toBe('')
  })

  it('DATETIME ida y vuelta conserva el instante (local en el control, UTC al enviar)', () => {
    const local = toFormValue('DATETIME', '2026-07-20T15:30:00Z')
    expect(typeof local).toBe('string')
    expect(fromFormValue('DATETIME', local)).toBe('2026-07-20T15:30:00.000Z')
  })

  it('fromFormValue: vacío = null, número como número, varias opciones como arreglo', () => {
    expect(fromFormValue('TEXT', '')).toBeNull()
    expect(fromFormValue('NUMBER', '1,250.5')).toBe(1250.5)
    expect(fromFormValue('NUMBER', 'abc')).toBe('abc')
    expect(fromFormValue('BOOL', false)).toBe(false)
    expect(fromFormValue('BOOL', null)).toBeNull()
    expect(fromFormValue('MULTISELECT', [])).toEqual([])
    expect(fromFormValue('SELECT', 'X')).toBe('X')
  })

  it('un tipo desconocido se trata como texto', () => {
    expect(dataTypeOf({ dataType: 'weird' })).toBe('TEXT')
    expect(dataTypeOf({ dataType: 'multiselect' })).toBe('MULTISELECT')
  })
})

describe('campos personalizados: validación en cliente', () => {
  it('obligatorio: vacío, null y lista vacía fallan; false es un valor', () => {
    const def = { dataType: 'TEXT', isRequired: true, validationJson: null }
    expect(validateCustomField(def, '')).toEqual([{ code: 'required' }])
    expect(validateCustomField({ ...def, dataType: 'MULTISELECT' }, [])).toEqual([{ code: 'required' }])
    expect(validateCustomField({ ...def, dataType: 'BOOL' }, null)).toEqual([{ code: 'required' }])
    expect(validateCustomField({ ...def, dataType: 'BOOL' }, false)).toEqual([])
    expect(validateCustomField({ ...def, isRequired: false }, '')).toEqual([])
  })

  it('tipo: número y fecha inválidos', () => {
    expect(validateCustomField({ dataType: 'NUMBER' }, 'x1')).toEqual([{ code: 'number' }])
    expect(validateCustomField({ dataType: 'DATE' }, '2026-02-31')).toEqual([{ code: 'date' }])
  })

  it('ValidationJson con el DSL (regex, longitudes, min/max)', () => {
    const def = { dataType: 'TEXT', validationJson: '{"regex":"^CC-\\\\d{3}$","minLength":6,"maxLength":6}' }
    expect(validateCustomField(def, 'CC-100')).toEqual([])
    expect(validateCustomField(def, 'CC-1').map((i) => i.code)).toEqual(['minLength', 'pattern'])
    expect(validateCustomField({ dataType: 'NUMBER', validationJson: '{"min":1,"max":10}' }, '11')).toEqual([{ code: 'max', limit: 10 }])
    // ValidationJson roto no bloquea la captura (el servidor lo rechaza al definir el campo)
    expect(validateCustomField({ dataType: 'TEXT', validationJson: '{roto' }, 'x')).toEqual([])
  })

  it('issueMessageKey arma la clave i18n y el parámetro', () => {
    expect(issueMessageKey({ code: 'max', limit: 10 })).toEqual({ key: 'customFields.errors.max', params: { limit: 10 } })
    expect(issueMessageKey({ code: 'required' })).toEqual({ key: 'customFields.errors.required' })
  })
})
