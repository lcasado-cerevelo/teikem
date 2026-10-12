// Lote F-F2 — reglas puras de Vistas: armado de columns/groupJson/sortJson/filterJson y su lectura al editar, validaciones, galería y rotulado.
import { describe, expect, it } from 'vitest'
import type { DataSource } from './definitions'
import { parseFilterRows } from './definitions'
import { filterReportRows } from './reportResult'
import {
  aggregateKey,
  blankDraft,
  buildReportBody,
  canManageView,
  combinableRelations,
  countFilterConditions,
  decorateResult,
  draftFromReport,
  fieldGroups,
  fieldIndex,
  groupReportsBySource,
  hasIssues,
  nameTaken,
  parseGroupJson,
  parseSortJson,
  previewBody,
  runBody,
  setUsersCanEdit,
  sortChoices,
  usersCanEdit,
  validateDraft,
  viewSubtitle,
  withAggregateFn,
  withFilterRows,
  withGroupBy,
  withRelation,
  type ReportDto,
} from './viewRules'

const ORDERS: DataSource = {
  key: 'TRANSPORT_ORDER',
  label: 'Órdenes',
  dateField: 'CreatedAtUtc',
  fields: [
    { key: 'OrderNumber', label: 'Número de orden', type: 'Text' },
    { key: 'ClientName', label: 'Cliente', type: 'Text' },
    { key: 'CodAmount', label: 'Monto COD', type: 'Number', isMoney: true },
    { key: 'TotalPieces', label: 'Piezas', type: 'Number' },
  ],
  customFields: [{ key: 'cf.ref', label: 'Referencia', type: 'Text' }],
  relations: [
    { key: 'Client', targetSource: 'CLIENT', label: 'Cliente' },
    { key: 'Trip', targetSource: 'TRIP', label: 'Ruta' },
  ],
}
const CLIENT: DataSource = { key: 'CLIENT', label: 'Clientes', fields: [{ key: 'Name', label: 'Nombre', type: 'Text' }], customFields: [], relations: [] }
const SOURCES = [ORDERS, CLIENT]

function report(over: Partial<ReportDto> = {}): ReportDto {
  return { id: 1, baseEntityType: 'TRANSPORT_ORDER', name: 'V', isSystem: false, canEdit: true, isActive: true, columns: ['OrderNumber'], ...over }
}

describe('campos disponibles', () => {
  it('solo ofrece las relaciones cuyo destino el usuario puede leer', () => {
    expect(combinableRelations(ORDERS, SOURCES).map((r) => r.key)).toEqual(['Client'])
  })

  it('arma bloques: principal, personalizados y cada fuente combinada con claves REL.Campo', () => {
    const groups = fieldGroups(ORDERS, SOURCES, ['Client'])
    expect(groups.map((g) => g.id)).toEqual(['', 'cf', 'Client'])
    expect(groups[1].fields[0]).toMatchObject({ key: 'cf.ref', custom: true })
    expect(groups[2].fields[0]).toMatchObject({ key: 'Client.Name', label: 'Cliente · Nombre', relationKey: 'Client' })
    expect(fieldGroups(ORDERS, SOURCES, []).map((g) => g.id)).toEqual(['', 'cf'])
  })

  it('el índice conoce los campos de todas las relaciones (para rotular vistas guardadas)', () => {
    const idx = fieldIndex(ORDERS, SOURCES)
    expect(idx.get('Client.Name')?.label).toBe('Cliente · Nombre')
    expect(idx.get('CodAmount')?.isMoney).toBe(true)
    expect(idx.get('cf.ref')?.custom).toBe(true)
  })
})

describe('groupJson, sortJson y filtros guardados', () => {
  it('lee y escribe groupJson', () => {
    const g = parseGroupJson('{"by":["ClientName"],"aggregates":[{"fn":"COUNT"},{"fn":"SUM","field":"TotalPieces"}],"totals":true}')
    expect(g).toEqual({ by: ['ClientName'], aggregates: [{ fn: 'COUNT', field: '' }, { fn: 'SUM', field: 'TotalPieces' }], totals: true })
    expect(parseGroupJson(null)).toEqual({ by: [], aggregates: [], totals: false })
    expect(parseGroupJson('no es json').by).toEqual([])
  })

  it('lee sortJson con direcciones en cualquier mayúscula', () => {
    expect(parseSortJson('[{"field":"A","dir":"DESC"},{"field":"B"}]')).toEqual([{ field: 'A', dir: 'desc' }, { field: 'B', dir: 'asc' }])
    expect(parseSortJson('{"x":1}')).toEqual([])
  })

  it('el agregado se llama como lo devuelve el servidor', () => {
    expect(aggregateKey({ fn: 'COUNT', field: '' })).toBe('count_rows')
    expect(aggregateKey({ fn: 'SUM', field: 'TotalPieces' })).toBe('sum_TotalPieces')
  })

  it('cuenta condiciones de filtros and/or/not', () => {
    expect(countFilterConditions(null)).toBe(0)
    expect(countFilterConditions('{"and":[{"field":"A","op":"eq","value":1},{"field":"B","op":"isTrue"}]}')).toBe(2)
    expect(countFilterConditions('{"or":[{"field":"A"},{"not":{"field":"B"}}]}')).toBe(2)
    expect(countFilterConditions('basura')).toBe(0)
  })

  it('un operador en minúsculas del servidor (notnull) se lee con la forma del editor', () => {
    expect(parseFilterRows('{"and":[{"field":"ExpiryDate","op":"notnull"}]}')[0].op).toBe('notNull')
  })
})

describe('borrador ⇄ cuerpo del API', () => {
  const saved = report({
    name: 'Piezas',
    secondary: ['Client'],
    columns: [],
    filterJson: '{"and":[{"field":"ClientName","op":"contains","value":"FF2"}]}',
    groupJson: '{"by":["ClientName"],"aggregates":[{"fn":"COUNT"},{"fn":"SUM","field":"TotalPieces"}],"totals":true}',
    sortJson: '[{"field":"sum_TotalPieces","dir":"desc"}]',
    visibility: 'SHARED',
    shares: [{ userId: 7, roleId: null, canEdit: true }],
    scheduleCron: '0 7 * * *',
    deliveryEmails: 'a@b.c',
    chartType: 'BAR',
  })

  it('lo que se lee al editar vuelve igual al guardar', () => {
    const d = draftFromReport(saved)
    const body = buildReportBody(d, sortChoices(d, fieldIndex(ORDERS, SOURCES), () => ''))
    expect(body.groupJson).toBe(saved.groupJson)
    expect(body.sortJson).toBe(saved.sortJson)
    expect(JSON.parse(body.filterJson ?? '')).toEqual(JSON.parse(saved.filterJson ?? ''))
    expect(body).toMatchObject({ name: 'Piezas', secondary: ['Client'], columns: [], visibility: 'SHARED', shares: [{ userId: 7, roleId: null, canEdit: true }] })
  })

  it('reenvía lo que la pantalla no edita (programación, correos, tipo de gráfico) y no toca las descripciones', () => {
    const body = buildReportBody(draftFromReport(saved), [{ key: 'sum_TotalPieces' }])
    expect(body).toMatchObject({ scheduleCron: '0 7 * * *', deliveryEmails: 'a@b.c', chartType: 'BAR', descriptions: null })
  })

  it('una vista simple lleva sus columnas y sin groupJson; con agrupación las columnas salen vacías', () => {
    const d = { ...blankDraft('TRANSPORT_ORDER'), name: ' Mi vista ', columns: ['OrderNumber', 'cf.ref', 'Client.Name'], secondary: ['Client'] }
    const body = buildReportBody(d, [])
    expect(body).toMatchObject({ name: 'Mi vista', columns: ['OrderNumber', 'cf.ref', 'Client.Name'], groupJson: null, sortJson: null, filterJson: null, shares: [] })
    const g = buildReportBody({ ...d, groupBy: ['ClientName'], aggregates: [{ fn: 'COUNT', field: '' }] }, [])
    expect(g.columns).toEqual([])
    expect(g.groupJson).toBe('{"by":["ClientName"],"aggregates":[{"fn":"COUNT"}],"totals":false}')
  })

  it('el orden que ya no tiene de dónde salir se descarta; los compartidos se limpian si deja de ser SHARED', () => {
    const d = { ...blankDraft('X'), name: 'n', columns: ['A'], sort: [{ field: 'A', dir: 'asc' as const }, { field: 'Borrada', dir: 'desc' as const }], visibility: 'TENANT', shares: [{ userId: 1, roleId: null, canEdit: false }] }
    const body = buildReportBody(d, [{ key: 'A' }])
    expect(body.sortJson).toBe('[{"field":"A","dir":"asc"}]')
    expect(body.shares).toEqual([])
  })

  it('un filtro con or/not se conserva hasta que se agregue una fila', () => {
    const complex = '{"or":[{"field":"A","op":"eq","value":1},{"field":"B","op":"eq","value":2}]}'
    const d = draftFromReport(report({ filterJson: complex }))
    expect(d.keepFilter).toBe(complex)
    expect(buildReportBody(d, []).filterJson).toBe(complex)
    const edited = withFilterRows(d, [{ field: 'A', op: 'eq', value: '3', value2: '' }])
    expect(edited.keepFilter).toBeNull()
    expect(JSON.parse(buildReportBody(edited, []).filterJson ?? '')).toEqual({ and: [{ field: 'A', op: 'eq', value: 3 }] })
  })

  it('la vista previa espera columnas o agrupación y un campo en cada agregado distinto de Cantidad', () => {
    expect(previewBody(blankDraft('X'), [])).toBeNull()
    expect(previewBody({ ...blankDraft('X'), columns: ['A'] }, [])).not.toBeNull()
    expect(previewBody({ ...blankDraft('X'), groupBy: ['A'], aggregates: [{ fn: 'SUM', field: '' }] }, [])).toBeNull()
    expect(previewBody({ ...blankDraft('X'), groupBy: ['A'], aggregates: [{ fn: 'COUNT', field: '' }] }, [])?.name).toBe('_')
  })
})

describe('edición del borrador', () => {
  const d = {
    ...blankDraft('TRANSPORT_ORDER'),
    secondary: ['Client'],
    columns: ['OrderNumber', 'Client.Name'],
    filters: [{ field: 'Client.Name', op: 'eq' as const, value: 'x', value2: '' }],
    groupBy: ['Client.Name', 'ClientName'],
    aggregates: [{ fn: 'COUNT' as const, field: 'Client.Name' }],
    sort: [{ field: 'Client.Name', dir: 'asc' as const }],
  }

  it('quitar una fuente combinada quita todo lo que dependía de ella', () => {
    const next = withRelation(d, 'Client', false)
    expect(next).toMatchObject({ secondary: [], columns: ['OrderNumber'], filters: [], groupBy: ['ClientName'], aggregates: [], sort: [] })
  })

  it('quitar el último campo de agrupación apaga agregados, totales y el orden por agregado', () => {
    const grouped = { ...blankDraft('X'), groupBy: ['A'], aggregates: [{ fn: 'COUNT' as const, field: '' }], showTotals: true, sort: [{ field: 'count_rows', dir: 'asc' as const }, { field: 'A', dir: 'asc' as const }] }
    const next = withGroupBy(grouped, [])
    expect(next).toMatchObject({ groupBy: [], aggregates: [], showTotals: false, sort: [{ field: 'A', dir: 'asc' }] })
  })

  it('cambiar la función: COUNT conserva el campo; las demás piden un campo numérico', () => {
    expect(withAggregateFn({ fn: 'COUNT', field: '' }, 'SUM', ['TotalPieces', 'CodAmount'])).toEqual({ fn: 'SUM', field: 'TotalPieces' })
    expect(withAggregateFn({ fn: 'SUM', field: 'CodAmount' }, 'AVG', ['TotalPieces', 'CodAmount'])).toEqual({ fn: 'AVG', field: 'CodAmount' })
    expect(withAggregateFn({ fn: 'SUM', field: 'CodAmount' }, 'COUNT', [])).toEqual({ fn: 'COUNT', field: 'CodAmount' })
  })

  it('con agrupación se ordena por grupos y agregados; sin ella, por las columnas elegidas', () => {
    const idx = fieldIndex(ORDERS, SOURCES)
    expect(sortChoices({ ...d, groupBy: ['ClientName'], aggregates: [{ fn: 'SUM', field: 'TotalPieces' }] }, idx, () => 'Suma · Piezas')).toEqual([
      { key: 'ClientName', label: 'Cliente' },
      { key: 'sum_TotalPieces', label: 'Suma · Piezas' },
    ])
    expect(sortChoices({ ...blankDraft('X'), columns: ['OrderNumber'] }, idx, () => '')).toEqual([{ key: 'OrderNumber', label: 'Número de orden' }])
  })
})

describe('validaciones', () => {
  it('nombre y al menos una columna (no se exige con agrupación); agregado sin campo', () => {
    expect(validateDraft(blankDraft('X'))).toEqual({ name: 'nameRequired', columns: 'columnsRequired', aggregateFields: [] })
    expect(hasIssues(validateDraft({ ...blankDraft('X'), name: 'a', groupBy: ['A'] }))).toBe(false)
    expect(validateDraft({ ...blankDraft('X'), name: 'a', groupBy: ['A'], aggregates: [{ fn: 'SUM', field: '' }, { fn: 'COUNT', field: '' }] }).aggregateFields).toEqual([0])
    expect(validateDraft({ ...blankDraft('X'), name: '   ', columns: ['A'] }).name).toBe('nameRequired')
  })

  it('nombre repetido en la fuente al editar (sin distinguir mayúsculas, sin contar la propia ni las eliminadas)', () => {
    const list = [report({ id: 1, name: 'Ventas' }), report({ id: 2, name: 'Otra', isActive: false }), report({ id: 3, name: 'Ventas', baseEntityType: 'CLIENT' })]
    expect(nameTaken(' ventas ', 'TRANSPORT_ORDER', list, 9)).toBe(true)
    expect(nameTaken('Ventas', 'TRANSPORT_ORDER', list, 1)).toBe(false)
    expect(nameTaken('Otra', 'TRANSPORT_ORDER', list, 9)).toBe(false)
  })
})

describe('compartir', () => {
  it('«pueden editar» aplica a las personas, no a los roles', () => {
    const shares = [{ userId: 1, roleId: null, canEdit: false }, { userId: null, roleId: 4, canEdit: false }]
    expect(usersCanEdit(shares)).toBe(false)
    const on = setUsersCanEdit(shares, true)
    expect(on).toEqual([{ userId: 1, roleId: null, canEdit: true }, { userId: null, roleId: 4, canEdit: false }])
    expect(usersCanEdit(on)).toBe(true)
    expect(usersCanEdit([])).toBe(false)
  })
})

describe('galería', () => {
  const reports = [
    report({ id: 1, name: 'Zeta', isSystem: false }),
    report({ id: 2, name: 'Alfa', isSystem: true, canEdit: false }),
    report({ id: 3, name: 'Beta', baseEntityType: 'CLIENT', groupJson: '{"by":["Name"],"aggregates":[]}' }),
    report({ id: 4, name: 'Vieja', isActive: false }),
  ]

  it('un panel por fuente (por nombre), sistema primero y sin las eliminadas', () => {
    const groups = groupReportsBySource(reports, SOURCES)
    expect(groups.map((g) => g.source.key)).toEqual(['CLIENT', 'TRANSPORT_ORDER'])
    expect(groups[1].views.map((v) => v.name)).toEqual(['Alfa', 'Zeta'])
  })

  it('la búsqueda filtra por vista o por fuente y oculta lo que no coincide', () => {
    const only = (q: string) => (...texts: string[]) => texts.some((t) => t.toLowerCase().includes(q))
    expect(groupReportsBySource(reports, SOURCES, only('beta')).map((g) => g.source.key)).toEqual(['CLIENT'])
    expect(groupReportsBySource(reports, SOURCES, only('órdenes'))[0].views).toHaveLength(2)
  })

  it('las de sistema no se administran aunque canEdit venga en true', () => {
    expect(canManageView({ isSystem: true, canEdit: true })).toBe(false)
    expect(canManageView({ isSystem: false, canEdit: false })).toBe(false)
    expect(canManageView({ isSystem: false, canEdit: true })).toBe(true)
  })

  it('subtítulo: N columnas o Agrupado por X (con la etiqueta del campo)', () => {
    const idx = fieldIndex(CLIENT, SOURCES)
    expect(viewSubtitle(reports[2], idx)).toEqual({ kind: 'grouped', fields: 'Nombre', columns: 0 })
    expect(viewSubtitle(reports[0], idx)).toEqual({ kind: 'columns', fields: '', columns: 1 })
  })
})

describe('resultado', () => {
  const idx = fieldIndex(ORDERS, SOURCES)
  const run = {
    columns: [
      { key: 'ClientName', label: 'Cliente', type: 'Text' as const, isMoney: false },
      { key: 'count_rows', label: 'Cantidad', type: 'Number' as const, isMoney: false },
      { key: 'sum_CodAmount', label: 'Suma (CodAmount)', type: 'Number' as const, isMoney: false },
      { key: 'cf.ref', label: 'ref', type: 'Text' as const, isMoney: false },
    ],
    rows: [{ ClientName: 'Alfa', count_rows: 2 }, { ClientName: 'Bravo', count_rows: 1 }],
    total: 2,
  }

  it('rotula agregados («Suma · Monto COD», dinero solo si el campo lo es) y campos personalizados', () => {
    const out = decorateResult(run, { groupJson: '{"by":["ClientName"],"aggregates":[{"fn":"COUNT"},{"fn":"SUM","field":"CodAmount"}]}' }, idx, (fn) => ({ COUNT: 'Cantidad', SUM: 'Suma', AVG: '', MIN: '', MAX: '' })[fn])
    expect(out?.columns?.map((c) => c.label)).toEqual(['Cliente', 'Cantidad', 'Suma · Monto COD', 'Referencia'])
    expect(out?.columns?.[2]).toMatchObject({ isMoney: true, type: 'Number' })
    expect(out?.columns?.[3]).toMatchObject({ label: 'Referencia', type: 'Text' })
    expect(out?.rows).toBe(run.rows)
  })

  it('un conteo de un campo de dinero no es dinero', () => {
    const out = decorateResult({ columns: [{ key: 'count_CodAmount', label: 'x', type: 'Number', isMoney: true }], rows: [], total: 0 }, { groupJson: '{"by":["A"],"aggregates":[{"fn":"COUNT","field":"CodAmount"}]}' }, idx, () => 'Cantidad')
    expect(out?.columns?.[0]).toMatchObject({ label: 'Cantidad · Monto COD', isMoney: false })
  })

  it('el rango: Todo por omisión, sin fecha en la fuente siempre Todo, personalizado exige fechas en orden', () => {
    expect(runBody('', '', '', true)).toEqual({ dateRangeMode: 'ALL', take: 5000 })
    expect(runBody('LAST7', '', '', false)).toEqual({ dateRangeMode: 'ALL', take: 5000 })
    expect(runBody('LAST7', '', '', true)).toEqual({ dateRangeMode: 'LAST7', take: 5000 })
    expect(runBody('CUSTOM', '2026-10-01', '', true)).toBeNull()
    expect(runBody('CUSTOM', '2026-10-05', '2026-10-01', true)).toBeNull()
    expect(runBody('CUSTOM', '2026-10-01', '2026-10-05', true)).toEqual({ dateRangeMode: 'CUSTOM', dateFrom: '2026-10-01', dateTo: '2026-10-05', take: 5000 })
  })

  it('la búsqueda libre mira el texto que se ve (cada palabra en alguna columna)', () => {
    const fmt = { number: (n: number) => String(n), money: (n: number) => `$${n}`, dateTime: (v: string) => v, yes: 'Sí', no: 'No' }
    expect(filterReportRows(run, 'bravo', fmt)?.rows).toEqual([{ ClientName: 'Bravo', count_rows: 1 }])
    expect(filterReportRows(run, 'alfa 2', fmt)?.rows).toHaveLength(1)
    expect(filterReportRows(run, '  ', fmt)).toBe(run)
    expect(filterReportRows(run, 'zzz', fmt)?.rows).toEqual([])
  })
})
