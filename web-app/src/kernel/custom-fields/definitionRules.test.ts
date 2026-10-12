// Reglas puras de la administración de campos personalizados (lote F-F1).
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang, t } from '../i18n/i18n'
import {
  EMPTY_VALIDATION,
  FIELD_KEY_PATTERN,
  buildDefault,
  buildListRequest,
  buildUpsert,
  buildValidationJson,
  definitionSchema,
  definitionToValues,
  emptyDefinitionValues,
  hasLegacyOptions,
  isListType,
  isValidFieldKey,
  legacyOptions,
  listCodeOf,
  mapServerErrors,
  normalizeFieldKey,
  parseListOptions,
  readDefault,
  readValidation,
  validationFieldsFor,
} from './definitionRules'
import type { CustomFieldDefinitionDto } from './values'

beforeAll(() => setLang('es'))

describe('clave', () => {
  it('acepta lo que acepta el servidor y rechaza lo demás', () => {
    for (const ok of ['cost_center', 'ab', 'a1', 'zona_2', 'a'.repeat(60)]) expect(isValidFieldKey(ok)).toBe(true)
    for (const bad of ['a', '1abc', '_abc', 'cost-center', 'Cost Center', 'ñandú', '', 'a'.repeat(61)]) expect(isValidFieldKey(bad)).toBe(false)
    expect(FIELD_KEY_PATTERN.source).toBe('^[a-z][a-z0-9_]{1,59}$')
  })
  it('recorta y pasa a minúsculas como el servidor', () => {
    expect(normalizeFieldKey('  Cost_Center ')).toBe('cost_center')
    expect(isValidFieldKey('  Cost_Center ')).toBe(true)
  })
})

describe('validación amigable', () => {
  it('cada tipo ofrece solo los controles que el servidor evalúa', () => {
    expect(validationFieldsFor('TEXT')).toEqual(['regex', 'minLength', 'maxLength'])
    expect(validationFieldsFor('NUMBER')).toEqual(['min', 'max'])
    expect(validationFieldsFor('DATE')).toEqual(['min', 'max'])
    expect(validationFieldsFor('DATETIME')).toEqual(['min', 'max'])
    for (const type of ['BOOL', 'SELECT', 'MULTISELECT', 'LOOKUP_REF']) expect(validationFieldsFor(type)).toEqual([])
  })
  it('arma y lee validationJson por tipo', () => {
    const text = { ...EMPTY_VALIDATION, regex: '^CC-\\d{3}$', minLength: 6, maxLength: 6, min: 5 }
    expect(JSON.parse(buildValidationJson('TEXT', text) ?? '')).toEqual({ regex: '^CC-\\d{3}$', minLength: 6, maxLength: 6 })
    expect(JSON.parse(buildValidationJson('NUMBER', { ...EMPTY_VALIDATION, min: 0, max: 99.5, regex: 'x' }) ?? '')).toEqual({ min: 0, max: 99.5 })
    expect(buildValidationJson('BOOL', { ...EMPTY_VALIDATION, min: 1 })).toBeNull()
    expect(buildValidationJson('TEXT', EMPTY_VALIDATION)).toBeNull()
    expect(buildValidationJson('TEXT', { ...EMPTY_VALIDATION, regex: '   ' })).toBeNull()
    expect(readValidation('{"regex":"^A$","minLength":2,"maxLength":"5","min":-1,"max":10}').fields).toEqual({
      regex: '^A$',
      minLength: 2,
      maxLength: 5,
      min: -1,
      max: 10,
    })
  })
  it('lee "pattern" como regex, conserva claves desconocidas y tolera JSON roto', () => {
    const r = readValidation('{"pattern":"^X","note":"hola"}')
    expect(r.fields.regex).toBe('^X')
    expect(r.extra).toEqual({ note: 'hola' })
    expect(JSON.parse(buildValidationJson('TEXT', r.fields, r.extra) ?? '')).toEqual({ note: 'hola', regex: '^X' })
    expect(readValidation('{no es json').fields).toEqual(EMPTY_VALIDATION)
    expect(readValidation('[1,2]').fields).toEqual(EMPTY_VALIDATION)
    expect(readValidation(null).fields).toEqual(EMPTY_VALIDATION)
  })
})

describe('valor por defecto por tipo', () => {
  it('BOOL: casilla; sin marcar no hay valor por defecto', () => {
    expect(buildDefault('BOOL', { text: '', bool: true, multi: [] })).toBe('true')
    expect(buildDefault('BOOL', { text: '', bool: false, multi: [] })).toBeNull()
    expect(readDefault('BOOL', 'true').bool).toBe(true)
    expect(readDefault('BOOL', 'false').bool).toBe(false)
    expect(readDefault('BOOL', null).bool).toBe(false)
  })
  it('MULTISELECT: códigos separados por coma', () => {
    expect(buildDefault('MULTISELECT', { text: '', bool: false, multi: ['A', 'B'] })).toBe('A,B')
    expect(buildDefault('MULTISELECT', { text: '', bool: false, multi: [] })).toBeNull()
    expect(readDefault('MULTISELECT', 'A, B').multi).toEqual(['A', 'B'])
  })
  it('texto, número, fecha y selección van como texto; vacío = null', () => {
    for (const type of ['TEXT', 'NUMBER', 'DATE', 'SELECT', 'LOOKUP_REF']) {
      expect(buildDefault(type, { text: ' 12 ', bool: false, multi: [] })).toBe('12')
      expect(buildDefault(type, { text: '  ', bool: false, multi: [] })).toBeNull()
    }
    expect(readDefault('DATE', '2026-10-12T00:00:00').text).toBe('2026-10-12')
    expect(readDefault('TEXT', 'hola').text).toBe('hola')
  })
  it('DATETIME: del control (hora de la compañía) al instante UTC y de vuelta', () => {
    const iso = buildDefault('DATETIME', { text: '2026-10-12T09:30', bool: false, multi: [] })
    expect(iso).toBe('2026-10-12T13:30:00.000Z') // Puerto Rico = UTC-4
    expect(readDefault('DATETIME', iso).text).toBe('2026-10-12T09:30')
  })
})

describe('opciones y listas', () => {
  it('el código de un valor sale como el del servidor, sin acentos', () => {
    expect(listCodeOf('Alto')).toBe('ALTO')
    expect(listCodeOf('Más alto')).toBe('MAS_ALTO')
    expect(listCodeOf(' a-b/c ')).toBe('A_B_C')
    expect(listCodeOf('###')).toBe('')
  })
  it('«Alto, Medio, Bajo» → opciones únicas; lo repetido y lo vacío se ignoran; lo imposible se avisa', () => {
    expect(parseListOptions('Alto, Medio ,Bajo,, alto').options).toEqual([
      { code: 'ALTO', label: 'Alto' },
      { code: 'MEDIO', label: 'Medio' },
      { code: 'BAJO', label: 'Bajo' },
    ])
    const bad = parseListOptions(`Uno, ###, ${'x'.repeat(41)}`)
    expect(bad.options).toEqual([{ code: 'UNO', label: 'Uno' }])
    expect(bad.invalid).toHaveLength(2)
    expect(parseListOptions('').options).toEqual([])
  })
  it('arma el cuerpo de POST /catalogs/lists', () => {
    const req = buildListRequest(' Prioridad ', parseListOptions('Alto, Bajo').options)
    expect(req).toEqual({
      name: 'Prioridad',
      nameEn: 'Prioridad',
      description: null,
      values: [
        { code: 'ALTO', labels: { es: 'Alto', en: 'Alto' }, descriptions: null, extraJson: null, sortOrder: 10 },
        { code: 'BAJO', labels: { es: 'Bajo', en: 'Bajo' }, descriptions: null, extraJson: null, sortOrder: 20 },
      ],
    })
  })
  it('las opciones propias de una definición por API se leen y se conservan', () => {
    const def = {
      refEntity: null,
      options: [
        { id: 2, value: 'B', label: 'Be', labels: { es: 'Be' }, sortOrder: 20, isActive: true },
        { id: 1, value: 'A', label: 'A', labels: { es: 'A', en: 'A' }, sortOrder: 10, isActive: true },
        { id: 3, value: 'X', label: 'X', labels: {}, sortOrder: 30, isActive: false },
      ],
    }
    expect(legacyOptions(def).map((o) => o.value)).toEqual(['A', 'B'])
    expect(hasLegacyOptions(def)).toBe(true)
    expect(hasLegacyOptions({ ...def, refEntity: 'LISTA' })).toBe(false)
    expect(hasLegacyOptions(null)).toBe(false)
    expect(isListType('SELECT') && isListType('MULTISELECT') && isListType('LOOKUP_REF')).toBe(true)
    expect(isListType('TEXT')).toBe(false)
  })
})

const DEF: CustomFieldDefinitionDto = {
  id: 7,
  entityType: 'CLIENT',
  fieldKey: 'cost_center',
  label: 'Centro de costo',
  labels: { es: 'Centro de costo', en: 'Cost center' },
  description: 'Para facturar',
  dataType: 'TEXT',
  isRequired: true,
  isUnique: true,
  defaultValue: 'CC-000',
  validationJson: '{"regex":"^CC-\\\\d{3}$","note":1}',
  refEntity: null,
  showInList: true,
  sortOrder: 30,
  isActive: true,
  options: [],
}

describe('cuerpo del alta y la edición', () => {
  it('el alta copia la etiqueta española si falta la inglesa y limpia la clave', () => {
    const v = { ...emptyDefinitionValues(), fieldKey: ' Cost_Center ', labelEs: ' Centro ', regex: '^A$' }
    const body = buildUpsert(v)
    expect(body).toMatchObject({
      fieldKey: 'cost_center',
      labels: { es: 'Centro', en: 'Centro' },
      descriptions: {},
      dataType: 'TEXT',
      isRequired: false,
      isUnique: false,
      showInList: false,
      defaultValue: null,
      refEntity: null,
      sortOrder: 100,
      options: [],
    })
    expect(body.validationJson).toBe('{"regex":"^A$"}')
  })
  it('la edición conserva la clave, lee y vuelve a armar todo', () => {
    const v = definitionToValues(DEF)
    expect(v).toMatchObject({ fieldKey: 'cost_center', labelEs: 'Centro de costo', labelEn: 'Cost center', description: 'Para facturar', defaultText: 'CC-000', sortOrder: 30, isRequired: true })
    expect(v.regex).toBe('^CC-\\d{3}$')
    const body = buildUpsert({ ...v, fieldKey: 'otra' }, DEF)
    expect(body.fieldKey).toBe('cost_center')
    expect(body.descriptions).toEqual({ es: 'Para facturar', en: 'Para facturar' })
    expect(JSON.parse(body.validationJson ?? '')).toEqual({ note: 1, regex: '^CC-\\d{3}$' })
  })
  it('un tipo sin lista manda refEntity null y sin validación propia', () => {
    const v = { ...definitionToValues(DEF), dataType: 'BOOL', refEntity: 'LISTA', defaultBool: true }
    const body = buildUpsert(v, DEF)
    expect(body.refEntity).toBeNull()
    expect(body.defaultValue).toBe('true')
    expect(JSON.parse(body.validationJson ?? '')).toEqual({ note: 1 })
  })
  it('un tipo de lista manda la lista y vacía las opciones; sin lista conserva las propias', () => {
    const base = { ...definitionToValues(DEF), dataType: 'SELECT', refEntity: 'T1_PRIORIDAD', defaultText: 'ALTO' }
    const withList = buildUpsert(base, DEF)
    expect(withList.refEntity).toBe('T1_PRIORIDAD')
    expect(withList.options).toEqual([])
    expect(withList.defaultValue).toBe('ALTO')
    const own: CustomFieldDefinitionDto = { ...DEF, dataType: 'SELECT', options: [{ id: 1, value: 'A', label: 'A', labels: { es: 'A' }, sortOrder: 10, isActive: true }] }
    const kept = buildUpsert({ ...definitionToValues(own), refEntity: '' }, own)
    expect(kept.refEntity).toBeNull()
    expect(kept.options).toEqual([{ value: 'A', labels: { es: 'A' }, sortOrder: 10 }])
  })
})

describe('esquema del formulario', () => {
  const issues = (values: object, creating = true, ownOptions = false) => {
    const r = definitionSchema(t, { creating, ownOptions }).safeParse(values)
    return r.success ? {} : Object.fromEntries(r.error.issues.map((i) => [String(i.path[0]), i.message]))
  }
  const ok = { ...emptyDefinitionValues(), fieldKey: 'cost_center', labelEs: 'Centro' }

  it('un alta válida pasa', () => expect(issues(ok)).toEqual({}))
  it('clave con el mensaje exacto del servidor (solo en el alta)', () => {
    expect(issues({ ...ok, fieldKey: 'Mal-Nombre' }).fieldKey).toBe("Clave inválida: minúsculas, dígitos y '_' (2-60), empezando por letra.")
    expect(issues({ ...ok, fieldKey: 'x' }, false)).toEqual({})
  })
  it('etiqueta obligatoria con el mensaje exacto del servidor', () => {
    expect(issues({ ...ok, labelEs: '  ' }).labelEs).toBe('La etiqueta es obligatoria.')
  })
  it('un campo de lista exige lista; con opciones propias solo la selección simple/múltiple puede prescindir', () => {
    expect(issues({ ...ok, dataType: 'SELECT' }).refEntity).toBe('Escoge o crea una lista de valores')
    expect(issues({ ...ok, dataType: 'SELECT', refEntity: 'LISTA' })).toEqual({})
    expect(issues({ ...ok, dataType: 'MULTISELECT' }, true, true)).toEqual({})
    expect(issues({ ...ok, dataType: 'LOOKUP_REF' }, true, true).refEntity).toBeDefined()
    expect(issues({ ...ok, dataType: 'TEXT' })).toEqual({})
  })
  it('valida expresión, longitudes, rangos y el valor por defecto', () => {
    expect(issues({ ...ok, regex: '(' }).regex).toBeDefined()
    expect(issues({ ...ok, regex: '^A$' })).toEqual({})
    expect(issues({ ...ok, minLength: 5, maxLength: 2 }).maxLength).toBeDefined()
    expect(issues({ ...ok, minLength: -1 }).minLength).toBeDefined()
    expect(issues({ ...ok, dataType: 'NUMBER', min: 10, max: 1 }).max).toBeDefined()
    expect(issues({ ...ok, dataType: 'NUMBER', min: 1, max: 10, defaultText: '5' })).toEqual({})
    expect(issues({ ...ok, dataType: 'NUMBER', defaultText: 'abc' }).defaultText).toBeDefined()
    expect(issues({ ...ok, dataType: 'DATE', defaultText: '2026-13-45' }).defaultText).toBeDefined()
    expect(issues({ ...ok, dataType: 'DATE', defaultText: '2026-10-12' })).toEqual({})
    // los controles de otro tipo no estorban
    expect(issues({ ...ok, dataType: 'BOOL', regex: '(', min: 5, max: 1 })).toEqual({})
  })
  it('el orden es un entero no negativo (o vacío)', () => {
    expect(issues({ ...ok, sortOrder: 1.5 }).sortOrder).toBeDefined()
    expect(issues({ ...ok, sortOrder: -1 }).sortOrder).toBeDefined()
    expect(issues({ ...ok, sortOrder: null })).toEqual({})
  })
})

describe('errores del servidor → campos', () => {
  it('409 en el alta = clave repetida; en la edición = cambio de tipo', () => {
    expect(mapServerErrors(409, "Ya existe el campo 'a' en CLIENT.", {}, true, 'TEXT')).toEqual({ fieldKey: ["Ya existe el campo 'a' en CLIENT."] })
    expect(mapServerErrors(409, 'No se puede cambiar el tipo de un campo que ya tiene valores guardados.', {}, false, 'TEXT')).toEqual({
      dataType: ['No se puede cambiar el tipo de un campo que ya tiene valores guardados.'],
    })
  })
  it('400: etiquetas, opciones y validación se llevan al control que las causa', () => {
    expect(mapServerErrors(400, 'x', { labels: ['La etiqueta es obligatoria.'], refEntity: ["La lista 'X' no existe."] }, true, 'TEXT')).toEqual({
      labelEs: ['La etiqueta es obligatoria.'],
      refEntity: ["La lista 'X' no existe."],
    })
    expect(mapServerErrors(400, 'x', { options: ['Un campo de selección necesita opciones o una lista del catálogo (refEntity).'] }, true, 'SELECT').refEntity).toBeDefined()
    expect(mapServerErrors(400, 'x', { validationJson: ['ValidationJson no es JSON válido.'] }, true, 'NUMBER')).toEqual({ min: ['ValidationJson no es JSON válido.'] })
    expect(mapServerErrors(400, 'x', { validationJson: ['m'] }, true, 'BOOL')).toEqual({ dataType: ['m'] })
  })
})
