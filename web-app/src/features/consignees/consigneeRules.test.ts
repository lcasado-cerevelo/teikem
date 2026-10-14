// Lote F-B — reglas puras de Consignatarios: esquema del formulario (mensajes del manual), requests de alta y PATCH, búsqueda y
// filtro de la lista, definición de plantillas de importación (espejo de ConsigneeImportMapping) y filas de la vista previa.
import { describe, expect, it } from 'vitest'
import enDict from '../../kernel/i18n/en.json'
import esDict from '../../kernel/i18n/es.json'
import {
  applyOwnerFilter,
  buildCreateRequest,
  buildPatchRequest,
  buildTemplateCreate,
  buildTemplatePatch,
  consigneeSchema,
  consigneeValuesOf,
  emptyConsigneeValues,
  FILTER_SHARED,
  matchesConsignee,
  pickPhone,
  buildConsigneeTemplateCreate,
  buildConsigneeTemplatePatch,
  conditionNeedsValue,
  previewFields,
  templateUsesField,
  variantIssues,
  variantPath,
  variantsOf,
  variantsWithIssues,
  withVariants,
  consigneeTemplateColumns,
  consigneeTemplateIssues,
  consigneeTemplateValuesOf,
  isExcelFile,
  joinPreview,
  moveJoinPart,
  syncJoins,
  templateFields,
  type ConsigneeTemplateValues,
  type ImportTemplate,
  type JoinDef,
  rowHasProblems,
  rowProblems,
  standardTemplateRequest,
  templateIssues,
  templateSummary,
  templateValuesOf,
  timeForApi,
  timeInput,
  type Consignee,
  type ImportField,
  type ImportRow,
  type TemplateValues,
} from './consigneeRules'

const M = {
  nameRequired: 'El nombre es obligatorio.',
  line1Required: 'La dirección (línea 1) es obligatoria.',
  cityRequired: 'La ciudad es obligatoria.',
  countryRequired: 'El país es obligatorio.',
  max: (n: number) => `Máximo ${n} caracteres.`,
  minutesInteger: 'Los minutos de servicio deben ser un número entero.',
  minutesNegative: 'Los minutos de servicio no pueden ser negativos.',
  windowPair: 'La ventana horaria requiere hora de inicio y hora de fin.',
  windowOrder: 'La hora de fin de la ventana debe ser posterior a la de inicio.',
  phoneInvalid: 'Teléfono inválido.',
}
const digits = (v: string) => v.replace(/\D/g, '').length === 10
const schema = consigneeSchema(M, digits)
const valid = { ...emptyConsigneeValues(), name: 'Farmacia', line1: 'Calle 1', city: 'Ponce' }
const messages = (v: unknown) => {
  const r = schema.safeParse(v)
  return r.success ? [] : r.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`)
}

const LOC: Consignee = {
  id: 1,
  publicId: 'l-1',
  clientPublicId: 'c-1',
  clientName: 'Acme',
  isShared: false,
  code: 'C-1',
  name: 'Farmacia',
  line1: 'Calle 1',
  line2: null,
  city: 'Ponce',
  state: 'PR',
  postalCode: '00716',
  country: 'PR',
  defaultServiceMinutes: 10,
  defaultWindowStart: '08:00:00',
  defaultWindowEnd: '12:00:00',
  accessNotes: null,
  deliveryNotes: 'Portón azul',
  allowDupInvoice: false,
  isActive: true,
  rowVersion: 'AAA=',
}

describe('esquema del formulario', () => {
  it('valores mínimos válidos: país PR por omisión y dueño compartido', () => {
    expect(valid.country).toBe('PR')
    expect(valid.clientPublicId).toBe('')
    expect(messages(valid)).toEqual([])
  })

  it('obligatorios con los mensajes exactos del manual', () => {
    expect(messages({ ...valid, name: ' ', line1: '', city: '' })).toEqual([
      `name: ${M.nameRequired}`,
      `line1: ${M.line1Required}`,
      `city: ${M.cityRequired}`,
    ])
    expect(messages({ ...valid, country: '' })).toEqual([`country: ${M.countryRequired}`])
  })

  it('topes de largo: nombre 200, código 40, línea 200, ciudad 100, estado 100, ZIP 20, notas 500', () => {
    expect(messages({ ...valid, name: 'x'.repeat(201) })).toEqual(['name: Máximo 200 caracteres.'])
    expect(messages({ ...valid, code: 'x'.repeat(41) })).toEqual(['code: Máximo 40 caracteres.'])
    expect(messages({ ...valid, city: 'x'.repeat(101) })).toEqual(['city: Máximo 100 caracteres.'])
    expect(messages({ ...valid, postalCode: 'x'.repeat(21) })).toEqual(['postalCode: Máximo 20 caracteres.'])
    expect(messages({ ...valid, accessNotes: 'x'.repeat(501) })).toEqual(['accessNotes: Máximo 500 caracteres.'])
    expect(messages({ ...valid, name: 'x'.repeat(200), accessNotes: 'x'.repeat(500) })).toEqual([])
  })

  it('minutos de servicio: entero y no negativo', () => {
    expect(messages({ ...valid, defaultServiceMinutes: -1 })).toEqual([`defaultServiceMinutes: ${M.minutesNegative}`])
    expect(messages({ ...valid, defaultServiceMinutes: 1.5 })).toEqual([`defaultServiceMinutes: ${M.minutesInteger}`])
    expect(messages({ ...valid, defaultServiceMinutes: null })).toEqual([])
  })

  it('ventana horaria: ambas o ninguna; el fin posterior al inicio', () => {
    expect(messages({ ...valid, defaultWindowStart: '08:00' })).toEqual([`defaultWindowEnd: ${M.windowPair}`])
    expect(messages({ ...valid, defaultWindowEnd: '12:00' })).toEqual([`defaultWindowStart: ${M.windowPair}`])
    expect(messages({ ...valid, defaultWindowStart: '12:00', defaultWindowEnd: '12:00' })).toEqual([`defaultWindowEnd: ${M.windowOrder}`])
    expect(messages({ ...valid, defaultWindowStart: '13:00', defaultWindowEnd: '12:00' })).toEqual([`defaultWindowEnd: ${M.windowOrder}`])
    expect(messages({ ...valid, defaultWindowStart: '08:00', defaultWindowEnd: '12:00' })).toEqual([])
  })

  it('teléfono: vacío o válido', () => {
    expect(messages({ ...valid, phone: '' })).toEqual([])
    expect(messages({ ...valid, phone: '(787) 555-0100' })).toEqual([])
    expect(messages({ ...valid, phone: '123' })).toEqual([`phone: ${M.phoneInvalid}`])
  })
})

describe('requests', () => {
  it('alta: tipo DELIVERY fijo, vacío → null, ventana HH:mm:00 solo con inicio y fin', () => {
    expect(buildCreateRequest({ ...valid, name: ' Farmacia ' })).toEqual({
      clientPublicId: null,
      code: null,
      name: 'Farmacia',
      locationType: 'DELIVERY',
      line1: 'Calle 1',
      line2: null,
      city: 'Ponce',
      state: null,
      postalCode: null,
      country: 'PR',
      defaultServiceMinutes: 0,
      defaultWindowStart: null,
      defaultWindowEnd: null,
      accessNotes: null,
      deliveryNotes: null,
      allowDupInvoice: false,
      contactName: null,
    })
    expect(buildCreateRequest({ ...valid, contactName: ' Ana Rivera ' }).contactName).toBe('Ana Rivera')
    const req = buildCreateRequest({ ...valid, clientPublicId: 'c-1', code: 'X1', defaultWindowStart: '08:00', defaultWindowEnd: '12:30', defaultServiceMinutes: 20, allowDupInvoice: true })
    expect(req).toMatchObject({ clientPublicId: 'c-1', code: 'X1', defaultWindowStart: '08:00:00', defaultWindowEnd: '12:30:00', defaultServiceMinutes: 20, allowDupInvoice: true })
  })

  it('horas: del API al campo y de vuelta', () => {
    expect(timeInput('08:00:00')).toBe('08:00')
    expect(timeInput(null)).toBe('')
    expect(timeForApi('08:00')).toBe('08:00:00')
    expect(timeForApi('08:00:30')).toBe('08:00:30')
  })

  it('PATCH sin cambios: nada que mandar; con cambios, solo lo cambiado y rowVersion', () => {
    const original = consigneeValuesOf(LOC, '')
    expect(buildPatchRequest(original, original, 'AAA=').changed).toBe(false)
    const { request, changed } = buildPatchRequest({ ...original, name: 'Farmacia Norte', deliveryNotes: '' }, original, 'AAA=')
    expect(changed).toBe(true)
    expect(request).toEqual({ rowVersion: 'AAA=', name: 'Farmacia Norte', deliveryNotes: '' })
  })

  it('PATCH de dueño: compartido → makeShared; otro cliente → clientPublicId', () => {
    const original = consigneeValuesOf(LOC, '')
    expect(buildPatchRequest({ ...original, clientPublicId: '' }, original, null).request).toMatchObject({ makeShared: true })
    expect(buildPatchRequest({ ...original, clientPublicId: 'c-2' }, original, null).request).toMatchObject({ clientPublicId: 'c-2' })
    const shared = consigneeValuesOf({ ...LOC, isShared: true, clientPublicId: null }, '')
    expect(shared.clientPublicId).toBe('')
  })

  it('PATCH de ventana: cambiar manda las dos horas; vaciar ambas manda clearWindow', () => {
    const original = consigneeValuesOf(LOC, '')
    expect(buildPatchRequest({ ...original, defaultWindowEnd: '13:00' }, original, null).request).toMatchObject({ defaultWindowStart: '08:00:00', defaultWindowEnd: '13:00:00' })
    const cleared = buildPatchRequest({ ...original, defaultWindowStart: '', defaultWindowEnd: '' }, original, null).request
    expect(cleared).toMatchObject({ clearWindow: true })
    expect(cleared).not.toHaveProperty('defaultWindowStart')
  })

  it('PATCH de minutos, país y «factura repetida»', () => {
    const original = consigneeValuesOf(LOC, '')
    expect(buildPatchRequest({ ...original, defaultServiceMinutes: 0, country: 'US', allowDupInvoice: true }, original, null).request).toEqual({
      rowVersion: null,
      defaultServiceMinutes: 0,
      country: 'US',
      allowDupInvoice: true,
    })
  })
})

describe('teléfono de la localización', () => {
  it('el principal activo que no sea correo; si no, el primero', () => {
    expect(pickPhone(undefined)).toBeUndefined()
    expect(pickPhone([{ id: 1, contactType: 'EMAIL', value: 'a@b.com', isPrimary: true, isActive: true }])).toBeUndefined()
    const a = { id: 1, contactType: 'PHONE', value: '7871', isPrimary: false, isActive: true }
    const b = { id: 2, contactType: 'MOBILE', value: '7872', isPrimary: true, isActive: true }
    const c = { id: 3, contactType: 'PHONE', value: '7873', isPrimary: true, isActive: false }
    expect(pickPhone([a, b])?.id).toBe(2)
    expect(pickPhone([a, c])?.id).toBe(1)
  })
})

describe('lista', () => {
  const labels = { shared: 'Compartido (todos los clientes)', active: 'Activo', inactive: 'Inactivo' }
  const shared: Consignee = { ...LOC, id: 2, publicId: 'l-2', isShared: true, clientPublicId: null, clientName: null, name: 'Colmado' }

  it('busca sin acentos por número, nombre, dirección, pueblo, dueño y estado', () => {
    expect(matchesConsignee(LOC, 'c-1', labels)).toBe(true)
    expect(matchesConsignee(LOC, 'FARMACIA ponce', labels)).toBe(true)
    expect(matchesConsignee(LOC, 'acme', labels)).toBe(true)
    expect(matchesConsignee(LOC, 'activo', labels)).toBe(true)
    expect(matchesConsignee({ ...LOC, isActive: false }, 'inactivo', labels)).toBe(true)
    expect(matchesConsignee(shared, 'compartido', labels)).toBe(true)
    expect(matchesConsignee(LOC, 'zzz', labels)).toBe(false)
  })

  it('«Compartidos» se resuelve por isShared; los demás filtros pasan tal cual', () => {
    expect(applyOwnerFilter([LOC, shared], FILTER_SHARED)).toEqual([shared])
    expect(applyOwnerFilter([LOC, shared], 'all')).toHaveLength(2)
    expect(applyOwnerFilter([LOC, shared], 'c-1')).toHaveLength(2)
  })
})

describe('plantillas de importación', () => {
  const FIELDS: ImportField[] = [
    { field: 'code', required: false, defaultAllowed: false },
    { field: 'name', required: true, defaultAllowed: false },
    { field: 'line1', required: true, defaultAllowed: false },
    { field: 'city', required: true, defaultAllowed: true },
    { field: 'clientCode', required: false, defaultAllowed: false },
    { field: 'clientName', required: false, defaultAllowed: false },
    { field: 'country', required: false, defaultAllowed: true },
  ]
  const TM = {
    columnsRequired: 'Indique al menos una columna.',
    nameLine1: 'La plantilla debe incluir las columnas name y line1.',
    city: 'La plantilla debe indicar la ciudad (city) o el código postal (postalCode), en una columna o en defaults.',
    clientBoth: 'Use clientCode o clientName, no ambos.',
    repeated: (f: string) => `El campo ${f} está repetido.`,
    noDefault: (f: string) => `El campo ${f} no admite valor por defecto.`,
    defaultValueRequired: (f: string) => `Escriba el valor por defecto de ${f}.`,
  }
  const cols = (...fields: string[]) => fields.map((field) => ({ field }))
  const issues = (v: Pick<TemplateValues, 'columns' | 'defaults'>) => templateIssues(v, FIELDS, TM).map((i) => `${i.path}: ${i.message}`)

  it('sin columnas; sin name y line1; sin ciudad; clientCode y clientName a la vez', () => {
    expect(issues({ columns: cols('', ''), defaults: [] })).toEqual([`columns: ${TM.columnsRequired}`])
    expect(issues({ columns: cols('name', 'city'), defaults: [] })).toEqual([`columns: ${TM.nameLine1}`])
    expect(issues({ columns: cols('name', 'line1'), defaults: [] })).toEqual([`columns: ${TM.city}`])
    expect(issues({ columns: cols('name', 'line1', 'city', 'clientCode', 'clientName'), defaults: [] })).toEqual([`columns: ${TM.clientBoth}`])
    expect(issues({ columns: cols('name', 'line1', 'city'), defaults: [] })).toEqual([])
  })

  it('la ciudad puede venir como default; un campo repetido se marca en la fila', () => {
    expect(issues({ columns: cols('name', 'line1'), defaults: [{ field: 'city', value: 'Ponce' }] })).toEqual([])
    expect(issues({ columns: cols('name', 'name', 'line1', 'city'), defaults: [] })).toEqual([`columns.1.field: ${TM.repeated('name')}`])
  })

  it('defaults: solo los campos que lo admiten y con valor', () => {
    expect(issues({ columns: cols('name', 'line1', 'city'), defaults: [{ field: 'name', value: 'x' }] })).toEqual([`defaults.0.field: ${TM.noDefault('name')}`])
    expect(issues({ columns: cols('name', 'line1', 'city'), defaults: [{ field: 'country', value: ' ' }] })).toEqual([`defaults.0.value: ${TM.defaultValueRequired('country')}`])
  })

  it('«Formato estándar»: name, code, line1, city, contactPhone', () => {
    expect(standardTemplateRequest('Formato estándar')).toEqual({
      name: 'Formato estándar',
      delimiter: ',',
      hasHeader: true,
      columns: [
        { position: 1, field: 'name' },
        { position: 2, field: 'code' },
        { position: 3, field: 'line1' },
        { position: 4, field: 'city' },
        { position: 5, field: 'contactPhone' },
      ],
    })
  })

  it('el editor numera por posición y omite las columnas ignoradas; el PATCH reemplaza todo', () => {
    const v: TemplateValues = { name: ' Mi formato ', delimiter: ';', hasHeader: false, columns: cols('name', '', 'line1', 'city'), defaults: [{ field: 'country', value: ' PR ' }, { field: '', value: 'x' }] }
    expect(buildTemplateCreate(v)).toEqual({
      name: 'Mi formato',
      delimiter: ';',
      hasHeader: false,
      columns: [
        { position: 1, field: 'name' },
        { position: 3, field: 'line1' },
        { position: 4, field: 'city' },
      ],
      defaults: { country: 'PR' },
    })
    expect(buildTemplatePatch({ ...v, defaults: [] })).toMatchObject({ defaults: null, clearDefaults: true })
    expect(buildTemplatePatch(v)).toMatchObject({ defaults: { country: 'PR' }, clearDefaults: null })
  })

  it('de la plantilla del API al editor: las posiciones sin campo quedan «no importar»', () => {
    const t = { name: 'T', delimiter: ';', hasHeader: false, columns: [{ position: 3, field: 'line1' }, { position: 1, field: 'name' }], defaults: { city: 'Ponce' } }
    expect(templateValuesOf(t)).toEqual({
      name: 'T',
      delimiter: ';',
      hasHeader: false,
      columns: [{ field: 'name' }, { field: '' }, { field: 'line1' }],
      defaults: [{ field: 'city', value: 'Ponce' }],
    })
    expect(templateValuesOf(null).columns).toHaveLength(5)
    expect(templateSummary(t, (f) => f.toUpperCase())).toBe('1. NAME · 3. LINE1')
  })
})

describe('vista previa', () => {
  const label = (f: string) => `<${f}>`
  it('errores por campo, error de la confirmación y avisos', () => {
    const row: ImportRow = { row: 2, line: 3, isValid: false, errors: { name: 'El nombre es obligatorio.' }, warnings: ['Aviso'], error: 'Falló' }
    expect(rowProblems(row, label)).toEqual({ errors: ['<name>: El nombre es obligatorio.', 'Falló'], warnings: ['Aviso'] })
    expect(rowHasProblems(row)).toBe(true)
    expect(rowHasProblems({ row: 1, isValid: true, warnings: [] })).toBe(false)
    expect(rowHasProblems({ row: 1, isValid: true, warnings: ['x'] })).toBe(true)
  })
})

describe('textos', () => {
  const keys = (o: unknown, prefix = ''): string[] =>
    Object.entries(o as Record<string, unknown>).flatMap(([k, v]) => (typeof v === 'object' && v !== null ? keys(v, `${prefix}${k}.`) : [`${prefix}${k}`]))

  it('es y en tienen las mismas claves en consignees.*', () => {
    expect(keys(enDict.consignees).sort()).toEqual(keys(esDict.consignees).sort())
  })

  it('hay etiqueta para cada campo de la lista de importación (18), para «(No usar)» y para el nav', () => {
    const fields = ['code', 'name', 'line1', 'line2', 'city', 'state', 'postalCode', 'country', 'clientCode', 'clientName', 'defaultServiceMinutes', 'defaultWindowStart', 'defaultWindowEnd', 'accessNotes', 'deliveryNotes', 'allowDupInvoice', 'contactPhone', 'contactName', 'ignore']
    for (const f of fields) {
      expect((esDict.consignees.import.fields as Record<string, string>)[f], f).toBeTruthy()
      expect((enDict.consignees.import.fields as Record<string, string>)[f], f).toBeTruthy()
    }
    expect(esDict.nav.consignees.title).toBe('Consignatarios')
  })
})

// ---------------------------------------------------------------- Lote F-B2: «(No usar)», columnas unidas, cliente por defecto, contacto

describe('mapeo de consignatarios (Lote F-B2)', () => {
  const MSG = {
    columnsRequired: 'Indique al menos una columna.',
    nameLine1: 'La plantilla debe incluir las columnas name y line1.',
    city: 'La plantilla debe indicar la ciudad (city) o el código postal (postalCode), en una columna o en defaults.',
    clientBoth: 'Use clientCode o clientName, no ambos.',
    repeated: (f: string) => `El campo ${f} está repetido.`,
    tooManyParts: (f: string) => `El campo ${f} admite a lo sumo 5 columnas unidas.`,
    noDefault: (f: string) => `El campo ${f} no admite valor por defecto.`,
    defaultValueRequired: (f: string) => `Escriba el valor por defecto de ${f}.`,
  }
  const FIELDS2: ImportField[] = ['code', 'name', 'line1', 'city', 'country', 'contactName'].map((field) => ({ field, required: false, defaultAllowed: !['code', 'name', 'line1'].includes(field) }))
  const cols = (...fields: string[][]) => fields.map((f) => ({ fields: f.map((field) => ({ field })) }))
  const base = (columns: ConsigneeTemplateValues['columns']): ConsigneeTemplateValues => ({ name: 'P', delimiter: ',', hasHeader: true, defaultClientPublicId: '', columns, defaults: [], variantName: '', conditions: [], moreVariants: [] })

  it('cada fila del editor viaja: sin campo = «ignore»; una columna a dos campos; un campo con varias columnas lleva su parte y separador', () => {
    const v = base(cols(['code', 'name'], ['name'], [''], ['line1'], ['line1'], ['city']))
    const joins = syncJoins(v.columns, [])
    expect(joins).toEqual([
      { field: 'name', positions: [1, 2], separators: [' '] },
      { field: 'line1', positions: [4, 5], separators: [' '] },
    ])
    const custom: JoinDef[] = [{ ...joins[0], separators: [' - '] }, moveJoinPart(joins[1], 0, 1)]
    expect(consigneeTemplateColumns(v, custom)).toEqual([
      { position: 1, field: 'code' },
      { position: 1, field: 'name', part: 1 },
      { position: 2, field: 'name', part: 2, separator: ' - ' },
      { position: 3, field: 'ignore' },
      { position: 4, field: 'line1', part: 2, separator: ' ' },
      { position: 5, field: 'line1', part: 1 },
      { position: 6, field: 'city' },
    ])
  })

  it('las uniones siguen a las columnas: conservan orden y separadores; la que queda con una columna desaparece', () => {
    const prev: JoinDef[] = [{ field: 'name', positions: [3, 1], separators: [' / '] }]
    expect(syncJoins(cols(['name'], [''], ['name'], ['name']), prev)).toEqual([{ field: 'name', positions: [3, 1, 4], separators: [' / ', ' '] }])
    expect(syncJoins(cols(['name'], ['line1']), prev)).toEqual([])
    expect(moveJoinPart({ field: 'name', positions: [1, 2], separators: ['-'] }, 0, -1).positions).toEqual([1, 2]) // ya es la primera
  })

  it('vista previa de la unión: mismo criterio que el servidor (una celda vacía no deja separador); sin archivo, «[Columna N]»', () => {
    const join: JoinDef = { field: 'name', positions: [1, 2, 3], separators: [' - ', ', '] }
    const row = ['C-001', 'Farmacia Uno', 'Ponce']
    expect(joinPreview(join, (p) => row[p - 1], (p) => `Columna ${p}`)).toBe('C-001 - Farmacia Uno, Ponce')
    expect(joinPreview(join, (p) => ['', 'Farmacia', 'Ponce'][p - 1], (p) => `Columna ${p}`)).toBe('Farmacia, Ponce')
    expect(joinPreview(join, () => undefined, (p) => `Columna ${p}`)).toBe('[Columna 1] - [Columna 2], [Columna 3]')
  })

  it('reglas de la definición con los mensajes del servidor', () => {
    expect(consigneeTemplateIssues(base(cols([''], [''])), FIELDS2, MSG)).toEqual([{ path: 'columns', message: MSG.columnsRequired }])
    expect(consigneeTemplateIssues(base(cols(['name'], ['line1'], ['city'], ['country'], ['country'])), FIELDS2, MSG)).toEqual([
      { path: 'columns.4.fields.0.field', message: 'El campo country está repetido.' },
    ])
    expect(consigneeTemplateIssues(base(cols(['name', 'name'], ['line1'], ['city'])), FIELDS2, MSG)[0]).toEqual({ path: 'columns.0.fields.1.field', message: 'El campo name está repetido.' })
    const six = consigneeTemplateIssues(base(cols(['name'], ['name'], ['name'], ['name'], ['name'], ['name'], ['line1'], ['city'])), FIELDS2, MSG)
    expect(six).toEqual([{ path: 'columns.5.fields.0.field', message: 'El campo name admite a lo sumo 5 columnas unidas.' }])
    expect(consigneeTemplateIssues(base(cols(['name'], ['name'], ['line1'], ['city'])), FIELDS2, MSG)).toEqual([]) // unir texto está permitido
  })

  it('de la plantilla del API al editor y de vuelta, con el cliente por defecto', () => {
    const tpl: ImportTemplate = {
      id: 1,
      publicId: 't',
      kind: 'CONSIGNEE',
      name: 'Excel',
      delimiter: ';',
      hasHeader: true,
      isActive: true,
      createdAtUtc: '2026-10-13T00:00:00',
      defaultClientPublicId: 'c-1',
      defaultClientName: 'Acme',
      columns: [
        { position: 1, field: 'code' },
        { position: 1, field: 'name', part: 1 },
        { position: 2, field: 'name', part: 2, separator: ' - ' },
        { position: 3, field: 'ignore' },
        { position: 4, field: 'line1' },
        { position: 5, field: 'city' },
      ],
    }
    const { values, joins } = consigneeTemplateValuesOf(tpl, 7)
    expect(values.columns).toEqual(cols(['code', 'name'], ['name'], [''], ['line1'], ['city'], [''], [''])) // el archivo trae 7 columnas
    expect(values.defaultClientPublicId).toBe('c-1')
    expect(joins).toEqual([{ field: 'name', positions: [1, 2], separators: [' - '] }])
    const create = buildConsigneeTemplateCreate(values, joins)
    expect(create.defaultClientPublicId).toBe('c-1')
    expect(create.columns).toHaveLength(8)
    expect(buildConsigneeTemplatePatch({ ...values, defaultClientPublicId: '' }, joins)).toMatchObject({ clearDefaultClient: true, clearDefaults: true })
    expect(buildConsigneeTemplateCreate({ ...values, defaultClientPublicId: '' }, joins)).not.toHaveProperty('defaultClientPublicId')
    expect(consigneeTemplateValuesOf(null, 9).values.columns).toHaveLength(9)
    expect(consigneeTemplateValuesOf(null).values.columns).toHaveLength(5)
  })

  it('resumen y columnas de la vista previa con «(No usar)» y columnas unidas', () => {
    const tpl = {
      columns: [
        { position: 2, field: 'name', part: 2, separator: ' - ' },
        { position: 1, field: 'name', part: 1 },
        { position: 1, field: 'code' },
        { position: 3, field: 'ignore' },
        { position: 4, field: 'line1' },
        { position: 5, field: 'ignore' },
      ],
    } as ImportTemplate
    const names: Record<string, string> = { code: 'Número', name: 'Nombre', line1: 'Dirección', ignore: '(No usar)' }
    const label = (f: string) => names[f] ?? f
    expect(templateSummary(tpl, label)).toBe('1. Número · 1+2. Nombre · 4. Dirección · 3, 5. (No usar)')
    expect(templateFields(tpl)).toEqual(['code', 'name', 'line1'])
  })

  it('reconoce un libro de Excel por la extensión o el tipo', () => {
    expect(isExcelFile({ name: 'Libro.XLSX', type: '' })).toBe(true)
    expect(isExcelFile({ name: 'x', type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })).toBe(true)
    expect(isExcelFile({ name: 'datos.csv', type: 'text/csv' })).toBe(false)
  })

  it('persona de contacto: tope de 150, PATCH solo si cambió y entra en la búsqueda', () => {
    const schema = consigneeSchema(M, () => true)
    expect(schema.safeParse({ ...valid, contactName: 'x'.repeat(151) }).error?.issues[0].message).toBe('Máximo 150 caracteres.')
    const original = consigneeValuesOf({ ...LOC, contactName: 'Ana' } as Consignee, '')
    expect(original.contactName).toBe('Ana')
    expect(buildPatchRequest({ ...original, contactName: 'Luis' }, original, null).request).toMatchObject({ contactName: 'Luis' })
    expect(buildPatchRequest(original, original, null).changed).toBe(false)
    expect(matchesConsignee({ ...LOC, contactName: 'Ana Rivera' } as Consignee, 'rivera', { shared: 'S', active: 'A', inactive: 'I' })).toBe(true)
  })
})

// Lote F-B3 — variantes de mapeo (espejo de ConsigneeImportVariants) y columnas que la vista previa agrega por el catálogo de ZIP.
describe('Plantillas con variantes y lista de códigos postales (Lote F-B3)', () => {
  const FIELDS3: ImportField[] = ['code', 'name', 'line1', 'line2', 'city', 'postalCode', 'contactName'].map((field) => ({ field, required: false, defaultAllowed: !['code', 'name', 'line1'].includes(field) }))
  const MSG3 = {
    columnsRequired: 'Indique al menos una columna.',
    nameLine1: 'La plantilla debe incluir las columnas name y line1.',
    city: 'La plantilla debe indicar la ciudad (city) o el código postal (postalCode), en una columna o en defaults.',
    clientBoth: 'Use clientCode o clientName, no ambos.',
    repeated: (f: string) => `El campo ${f} está repetido.`,
    tooManyParts: (f: string) => `El campo ${f} admite a lo sumo 5 columnas unidas.`,
    noDefault: (f: string) => `El campo ${f} no admite valor por defecto.`,
    defaultValueRequired: (f: string) => `Escriba el valor por defecto de ${f}.`,
    conditionColumn: 'Escoja la columna de la condición.',
    conditionValue: 'La condición «igual a» o «contiene» necesita un valor.',
    unreachable: (n: number) => `La variante ${n} no tiene condición y deja sin uso a las que le siguen; póngala al final o agréguele una condición.`,
  }
  const cols3 = (...fields: string[][]) => fields.map((f) => ({ fields: f.map((field) => ({ field })) }))
  const TPL = {
    publicId: 't',
    name: 'Mixta',
    delimiter: ',',
    hasHeader: true,
    defaults: {},
    columns: [{ position: 1, field: 'name' }],
    variants: [
      { name: 'Formato B', conditions: [{ position: 9, operator: 'EQUALS', value: 'PUERTO RICO' }], columns: [{ position: 2, field: 'name' }, { position: 6, field: 'line1' }, { position: 8, field: 'city' }] },
      {
        name: null,
        conditions: [],
        columns: [
          { position: 2, field: 'name' },
          { position: 3, field: 'line1', part: 1 },
          { position: 4, field: 'line1', part: 2, separator: ', ' },
          { position: 5, field: 'postalCode' },
        ],
      },
    ],
  } as unknown as ImportTemplate

  it('el ZIP basta en lugar del pueblo (en columna o como valor por defecto)', () => {
    const noDefaults = { defaults: [] as { field: string; value: string }[] }
    expect(consigneeTemplateIssues({ ...noDefaults, columns: cols3(['name'], ['line1'], ['postalCode']) }, FIELDS3, MSG3)).toEqual([])
    expect(consigneeTemplateIssues({ columns: cols3(['name'], ['line1']), defaults: [{ field: 'postalCode', value: '00901' }] }, FIELDS3, MSG3)).toEqual([])
    expect(consigneeTemplateIssues({ ...noDefaults, columns: cols3(['name'], ['line1']) }, FIELDS3, MSG3)).toEqual([{ path: 'columns', message: MSG3.city }])
  })

  it('la plantilla con variantes llega al editor: la 1 arriba y las demás en moreVariants, con sus uniones', () => {
    const { values, joins, moreJoins } = consigneeTemplateValuesOf(TPL, 9)
    expect(values.variantName).toBe('Formato B')
    expect(values.conditions).toEqual([{ position: '9', operator: 'EQUALS', value: 'PUERTO RICO' }])
    expect(values.columns).toHaveLength(9)
    expect(values.columns[7]).toEqual({ fields: [{ field: 'city' }] })
    expect(values.moreVariants).toHaveLength(1)
    expect(values.moreVariants[0]).toMatchObject({ name: '', conditions: [] })
    expect(values.moreVariants[0].columns[4]).toEqual({ fields: [{ field: 'postalCode' }] })
    expect(joins).toEqual([])
    expect(moreJoins).toEqual([[{ field: 'line1', positions: [3, 4], separators: [', '] }]])
    // sin variantes: lo de siempre
    expect(consigneeTemplateValuesOf({ ...TPL, variants: null } as ImportTemplate).values).toMatchObject({ moreVariants: [], conditions: [], variantName: '' })
    expect(variantsOf(values).map((v) => v.name)).toEqual(['Formato B', ''])
    expect(variantPath(0)).toBe('')
    expect(variantPath(2)).toBe('moreVariants.1.')
  })

  it('con dos o más variantes el request lleva variants (sin columns); al volver a una sola, el PATCH manda variants vacío', () => {
    const { values, joins, moreJoins } = consigneeTemplateValuesOf(TPL, 5)
    const create = buildConsigneeTemplateCreate(values, joins, moreJoins)
    expect(create).not.toHaveProperty('columns')
    expect(create.variants?.[0]).toEqual({
      name: 'Formato B',
      conditions: [{ position: 9, operator: 'EQUALS', value: 'PUERTO RICO' }],
      columns: [
        { position: 1, field: 'ignore' },
        { position: 2, field: 'name' },
        { position: 3, field: 'ignore' },
        { position: 4, field: 'ignore' },
        { position: 5, field: 'ignore' },
        { position: 6, field: 'line1' },
        { position: 7, field: 'ignore' },
        { position: 8, field: 'city' },
      ],
    })
    expect(create.variants?.[1]).toMatchObject({ name: null, conditions: [] })
    expect(create.variants?.[1].columns).toContainEqual({ position: 4, field: 'line1', part: 2, separator: ', ' })
    const single = withVariants(values, variantsOf(values).slice(0, 1))
    expect(buildConsigneeTemplatePatch(single, joins, [], true)).toMatchObject({ variants: [], columns: expect.any(Array) })
    expect(buildConsigneeTemplatePatch(single, joins, [], false)).not.toHaveProperty('variants')
    expect(buildConsigneeTemplatePatch(values, joins, moreJoins, true)).not.toHaveProperty('columns')
    // EMPTY / NOT_EMPTY no mandan valor
    const empty = withVariants(values, [{ ...variantsOf(values)[0], conditions: [{ position: '5', operator: 'NOT_EMPTY', value: 'x' }] }, variantsOf(values)[1]])
    expect(buildConsigneeTemplateCreate(empty, joins, moreJoins).variants?.[0].conditions).toEqual([{ position: 5, operator: 'NOT_EMPTY', value: null }])
    expect(conditionNeedsValue('CONTAINS')).toBe(true)
    expect(conditionNeedsValue('EMPTY')).toBe(false)
  })

  it('reglas de las variantes: columna y valor de la condición, sin condición solo al final y el mapeo de cada una', () => {
    const { values } = consigneeTemplateValuesOf(TPL, 9)
    expect(variantIssues(values, FIELDS3, MSG3)).toEqual([])
    const bad = withVariants(values, [
      { name: 'A', conditions: [], columns: cols3(['name'], ['line1'], ['city']) },
      { name: 'B', conditions: [{ position: '', operator: 'EQUALS', value: ' ' }], columns: cols3(['name'], ['city']) },
      { name: 'C', conditions: [], columns: cols3(['name'], ['line1']) },
    ])
    expect(variantIssues(bad, FIELDS3, MSG3)).toEqual([
      { path: 'conditions', message: MSG3.unreachable(1) },
      { path: 'moreVariants.0.conditions.0.position', message: MSG3.conditionColumn },
      { path: 'moreVariants.0.conditions.0.value', message: MSG3.conditionValue },
      { path: 'moreVariants.0.columns', message: MSG3.nameLine1 },
      { path: 'moreVariants.1.columns', message: MSG3.city },
    ])
    expect(variantsWithIssues(variantIssues(bad, FIELDS3, MSG3).map((i) => i.path), 3)).toEqual([0, 1, 2])
    // una sola variante: sin reglas de variantes
    expect(variantIssues(withVariants(values, variantsOf(values).slice(0, 1)), FIELDS3, MSG3)).toEqual([])
  })

  it('la vista previa muestra los campos de todas las variantes y los que dedujo la lista', () => {
    expect(templateFields(TPL)).toEqual(['name', 'line1', 'city', 'postalCode'])
    expect(previewFields(TPL, [{ inferred: ['state', 'city'] }, { inferred: null }])).toEqual(['name', 'line1', 'city', 'postalCode', 'state'])
    expect(previewFields({ columns: [{ position: 1, field: 'name' }] } as ImportTemplate, [{ inferred: ['postalCode'] }])).toEqual(['name', 'postalCode'])
    expect(templateUsesField(TPL, 'postalCode')).toBe(true)
    expect(templateUsesField(TPL, 'clientCode', 'clientName')).toBe(false)
  })

  it('los textos nuevos existen en los dos idiomas', () => {
    const keys = ['variantsTitle', 'addVariant', 'variantN', 'conditionsNone', 'zipAdjusted', 'inferredTag', 'adjustedTitle', 'rowCells', 'variantsMapping']
    for (const dict of [esDict, enDict]) {
      const imp = (dict as unknown as { consignees: { import: Record<string, unknown> } }).consignees.import
      for (const k of keys) expect(typeof imp[k]).toBe('string')
      expect(Object.keys(imp.operators as object)).toEqual(['EQUALS', 'CONTAINS', 'NOT_EMPTY', 'EMPTY'])
    }
  })
})
