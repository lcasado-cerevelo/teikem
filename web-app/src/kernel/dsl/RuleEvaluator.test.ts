// Casos de tests/Teikem.Tests/RuleEvaluatorTests.cs portados 1:1 (primer bloque) + operadores restantes del DSL.
import { describe, expect, it } from 'vitest'
import { compileFilter, matches, referencedFields, validateValue } from './RuleEvaluator'

const utc = (y: number, m: number, d: number) => new Date(Date.UTC(y, m - 1, d))

describe('RuleEvaluator (casos compartidos con el servidor)', () => {
  it('And_simplified_array_form_matches_all_conditions', () => {
    const f = compileFilter('[{"field":"Status","op":"eq","value":"DELIVERED"},{"field":"Pieces","op":"gt","value":2}]')
    expect(f({ Status: 'delivered', Pieces: 3 })).toBe(true)
    expect(f({ Status: 'delivered', Pieces: 2 })).toBe(false)
  })

  it('Or_and_not_nesting_works', () => {
    const f = compileFilter('{"or":[{"field":"A","op":"eq","value":1},{"not":{"field":"B","op":"isNull"}}]}')
    expect(f({ A: 0, B: 'x' })).toBe(true)
    expect(f({ A: 1, B: null })).toBe(true)
    expect(f({ A: 0, B: null })).toBe(false)
  })

  it('Dates_compare_as_dates_not_as_parsed_floats', () => {
    const f = compileFilter('{"field":"Date","op":"gte","value":"2026-07-20"}')
    expect(f({ Date: utc(2026, 7, 25) })).toBe(true)
    expect(f({ Date: utc(2026, 1, 25) })).toBe(false)
    expect(f({ Date: '2026-07-25T10:00:00Z' })).toBe(true)
  })

  it('In_between_contains_operators', () => {
    expect(matches({ S: 'B' }, '{"field":"S","op":"in","value":["A","B"]}')).toBe(true)
    expect(matches({ N: 5 }, '{"field":"N","op":"between","value":[1,10]}')).toBe(true)
    expect(matches({ N: 11 }, '{"field":"N","op":"between","value":[1,10]}')).toBe(false)
    expect(matches({ T: 'Farmacia Las Marías' }, '{"field":"T","op":"contains","value":"marías"}')).toBe(true)
  })

  it('Unknown_operator_throws', () => {
    expect(() => matches({ A: 1 }, '{"field":"A","op":"xyz","value":1}')).toThrow()
  })

  it('Validation_spec_regex_min_max_length', () => {
    const spec = '{"regex":"^CC-\\\\d{3}$","minLength":6,"maxLength":6}'
    expect(validateValue('CC-100', spec)).toHaveLength(0)
    expect(validateValue('CC-1', spec).length).toBeGreaterThan(0)
    expect(validateValue(5, '{"min":1,"max":10}')).toHaveLength(0)
    expect(validateValue(11, '{"min":1,"max":10}').length).toBeGreaterThan(0)
  })

  it('Referenced_fields_are_collected', () => {
    const fields = referencedFields('{"and":[{"field":"A","op":"eq","value":1},{"or":[{"field":"B","op":"isNull"}]}]}')
    expect(fields).toContain('A')
    expect(fields).toContain('B')
  })
})

describe('RuleEvaluator (resto de operadores y bordes)', () => {
  it('ne, lt, lte, gte con números y texto numérico', () => {
    expect(matches({ N: 3 }, { field: 'N', op: 'ne', value: 4 })).toBe(true)
    expect(matches({ N: '3' }, { field: 'N', op: 'eq', value: 3 })).toBe(true)
    expect(matches({ N: 3 }, { field: 'N', op: 'lt', value: 10 })).toBe(true)
    expect(matches({ N: 10 }, { field: 'N', op: 'lte', value: 10 })).toBe(true)
    expect(matches({ N: 11 }, { field: 'N', op: 'lte', value: 10 })).toBe(false)
    // "9" < "10" como número, no como texto
    expect(matches({ N: '9' }, { field: 'N', op: 'lt', value: '10' })).toBe(true)
  })

  it('comparar con un campo nulo es falso (salvo eq null del lado del campo)', () => {
    expect(matches({ N: null }, { field: 'N', op: 'gt', value: 1 })).toBe(false)
    expect(matches({ N: null }, { field: 'N', op: 'lt', value: 1 })).toBe(false)
    expect(matches({}, { field: 'N', op: 'eq', value: 1 })).toBe(false)
    // Como el servidor: "value": null del JSON no es igual a un campo nulo
    expect(matches({ N: null }, '{"field":"N","op":"eq","value":null}')).toBe(false)
    expect(matches({ N: '' }, '{"field":"N","op":"eq","value":null}')).toBe(true)
  })

  it('startsWith, endsWith, notIn sin distinguir mayúsculas', () => {
    expect(matches({ T: 'Almacén Norte' }, { field: 'T', op: 'startsWith', value: 'almacén' })).toBe(true)
    expect(matches({ T: 'Almacén Norte' }, { field: 'T', op: 'endsWith', value: 'NORTE' })).toBe(true)
    expect(matches({ T: null }, { field: 'T', op: 'contains', value: '' })).toBe(false)
    expect(matches({ S: 'c' }, { field: 'S', op: 'notIn', value: ['A', 'B'] })).toBe(true)
    expect(matches({ S: 'a' }, { field: 'S', op: 'notIn', value: ['A', 'B'] })).toBe(false)
    expect(matches({ S: 'A' }, { field: 'S', op: 'in', value: 'a' })).toBe(true)
    expect(matches({ S: 'A' }, { field: 'S', op: 'in' })).toBe(false)
  })

  it('isNull/notNull tratan el texto vacío como vacío', () => {
    expect(matches({ A: '' }, { field: 'A', op: 'isNull' })).toBe(true)
    expect(matches({}, { field: 'A', op: 'isNull' })).toBe(true)
    expect(matches({ A: 0 }, { field: 'A', op: 'notNull' })).toBe(true)
    expect(matches({ A: '' }, { field: 'A', op: 'notNull' })).toBe(false)
  })

  it('isTrue/isFalse aceptan booleanos, "true"/"false" y "1"/"0"', () => {
    expect(matches({ F: true }, { field: 'F', op: 'isTrue' })).toBe(true)
    expect(matches({ F: 'TRUE' }, { field: 'F', op: 'isTrue' })).toBe(true)
    expect(matches({ F: '0' }, { field: 'F', op: 'isFalse' })).toBe(true)
    expect(matches({ F: null }, { field: 'F', op: 'isFalse' })).toBe(false)
    expect(matches({ F: 'x' }, { field: 'F', op: 'isTrue' })).toBe(false)
  })

  it('between con fechas y con rango mal formado', () => {
    const range = { field: 'D', op: 'between', value: ['2026-07-01', '2026-07-31'] }
    expect(matches({ D: '2026-07-15' }, range)).toBe(true)
    expect(matches({ D: '2026-08-01' }, range)).toBe(false)
    expect(matches({ D: 5 }, { field: 'D', op: 'between', value: [1] })).toBe(false)
  })

  it('fechas con desfase se llevan a UTC y sin zona se asumen UTC', () => {
    expect(matches({ D: '2026-07-20T02:00:00-04:00' }, { field: 'D', op: 'eq', value: '2026-07-20T06:00:00Z' })).toBe(true)
    expect(matches({ D: '2026-07-20T06:00:00' }, { field: 'D', op: 'eq', value: '2026-07-20T06:00:00Z' })).toBe(true)
  })

  it('op por defecto es eq; campo sin distinguir mayúsculas; filtro vacío deja pasar todo', () => {
    expect(matches({ status: 'OK' }, { field: 'Status', value: 'ok' })).toBe(true)
    expect(matches({ A: 1 }, '')).toBe(true)
    expect(matches({ A: 1 }, null)).toBe(true)
    expect(matches({ A: 1 }, { and: [] })).toBe(true)
    expect(matches({ A: 1 }, { or: [] })).toBe(false)
  })

  it('or evalúa en corto: un operador desconocido en una rama no visitada no lanza', () => {
    const f = compileFilter({ or: [{ field: 'A', op: 'eq', value: 1 }, { field: 'A', op: 'xyz' }] })
    expect(f({ A: 1 })).toBe(true)
    expect(() => f({ A: 2 })).toThrow()
  })

  it('validación: pattern como alias, regex inválida, texto numérico con min/max, nulo válido', () => {
    expect(validateValue('abc', '{"pattern":"^\\\\d+$"}')).toEqual([{ code: 'pattern' }])
    expect(validateValue('abc', '{"regex":"("}')).toEqual([{ code: 'patternInvalid' }])
    expect(validateValue('15', '{"max":10}')).toEqual([{ code: 'max', limit: 10 }])
    expect(validateValue(null, '{"min":1}')).toEqual([])
    expect(validateValue('ab', '{"minLength":"3"}')).toEqual([{ code: 'minLength', limit: 3 }])
    expect(validateValue('x', null)).toEqual([])
  })

  it('validación de fechas: min/max en días relativos a hoy', () => {
    const now = new Date()
    const today = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate()))
    const yesterday = new Date(today.getTime() - 86_400_000)
    expect(validateValue(yesterday, '{"min":0}')).toEqual([{ code: 'minDate', limit: 0 }])
    expect(validateValue(today, '{"min":0,"max":30}')).toEqual([])
    expect(validateValue(new Date(today.getTime() + 40 * 86_400_000), '{"max":30}')).toEqual([{ code: 'maxDate', limit: 30 }])
  })
})
