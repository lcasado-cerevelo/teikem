// Lote 137 — reglas puras del formato de archivo: rutas, validación en cliente (mismos mensajes que el servidor), valores del formulario y cuerpo del API.
import { describe, expect, it } from 'vitest'
import { buildFormatConfig, emptyFormat, formatIsPlain, formatIssues, formatValuesOf, isStructured, isValidPath, type FormatMessages, type FormatValues } from './formatRules'

const M: FormatMessages = {
  sheet: 'hoja',
  recordsRequired: 'órdenes',
  recordsRoot: 'raíz órdenes',
  linesRoot: 'raíz líneas',
  columnsRequired: 'columnas',
  columnsMax: 'máx columnas',
  headerRequired: 'nombre',
  headerMax: 'nombre largo',
  headerRepeated: (h) => `repetida ${h}`,
  pathInvalid: (p) => `ruta ${p}`,
  orderRoot: (p) => `orden ${p}`,
}

const json = (over: Partial<FormatValues> = {}): FormatValues => ({
  kind: 'JSON',
  sheet: '',
  recordsPath: '$.orders[*]',
  linesPath: '@.items[*]',
  columns: [{ header: 'Orden', path: '^.ref' }, { header: 'Tipo', path: '@.type' }],
  ...over,
})
const issues = (v: FormatValues) => formatIssues(v, M).map((i) => `${i.path}:${i.message}`)

describe('isValidPath', () => {
  it('acepta $ @ ^ con .nombre, [n] y [*], y atributos y #text de XML', () => {
    for (const p of ['$', '$.a', '$.a.b[0].c', '$.orders[*]', '@.lines[*]', '^.ref', '$.Pedidos.Orden[*]', '^.@id', '@.#text', '$.a:b']) expect(isValidPath(p), p).toBe(true)
  })

  it('rechaza lo que no empieza con $ @ ^ o tiene pasos mal formados', () => {
    for (const p of ['', 'a.b', '$a', '$.', '$..a', '$.a[', '$.a[x]', '$.a[1234567]', '$.a b', `$.${'x'.repeat(200)}`]) expect(isValidPath(p), p).toBe(false)
  })
})

describe('formatIssues', () => {
  it('CSV, Excel y automático sin mapeo no piden nada (salvo el largo de la hoja)', () => {
    expect(issues(emptyFormat())).toEqual([])
    expect(issues({ ...emptyFormat(), kind: 'CSV' })).toEqual([])
    expect(issues({ ...emptyFormat(), kind: 'EXCEL', sheet: 'Pedidos' })).toEqual([])
    expect(issues({ ...emptyFormat(), kind: 'EXCEL', sheet: 'h'.repeat(32) })).toEqual(['sheet:hoja'])
  })

  it('JSON y XML piden la ruta de las órdenes, que empiece con $ y esté bien formada', () => {
    expect(issues(json())).toEqual([])
    expect(issues(json({ recordsPath: '' }))).toEqual(['recordsPath:órdenes'])
    expect(issues(json({ recordsPath: 'orders' }))).toEqual(['recordsPath:ruta orders'])
    expect(issues(json({ recordsPath: '@.orders[*]' }))).toEqual(['recordsPath:raíz órdenes'])
    expect(issues({ ...json(), kind: 'XML' })).toEqual([])
  })

  it('la ruta de líneas es relativa a la orden (@) y las columnas con ^ exigen líneas', () => {
    expect(issues(json({ linesPath: '$.items[*]' }))).toEqual(['linesPath:raíz líneas'])
    expect(issues(json({ linesPath: 'items' }))).toEqual(['linesPath:ruta items'])
    expect(issues(json({ linesPath: '' }))).toEqual(['columns.0.path:orden ^.ref'])
    expect(issues(json({ linesPath: '', columns: [{ header: 'A', path: '@.a' }] }))).toEqual([])
  })

  it('las columnas: al menos una, con nombre único y ruta válida, hasta 60', () => {
    expect(issues(json({ columns: [] }))).toEqual(['columns:columnas'])
    expect(issues(json({ columns: [{ header: ' ', path: '@.a' }] }))).toEqual(['columns.0.header:nombre'])
    expect(issues(json({ columns: [{ header: 'x'.repeat(81), path: '@.a' }] }))).toEqual(['columns.0.header:nombre largo'])
    expect(issues(json({ columns: [{ header: 'A', path: '@.a' }, { header: 'a', path: '@.b' }] }))).toEqual(['columns.1.header:repetida a'])
    expect(issues(json({ columns: [{ header: 'A', path: 'a.b' }] }))).toEqual(['columns.0.path:ruta a.b'])
    expect(issues(json({ columns: Array.from({ length: 61 }, (_, i) => ({ header: `c${i}`, path: '@.a' })) }))).toEqual(['columns:máx columnas'])
  })
})

describe('valores y cuerpo del API', () => {
  it('JSON y XML mandan rutas y columnas recortadas; Excel solo la hoja; los demás nada más', () => {
    expect(buildFormatConfig(json({ recordsPath: ' $.orders[*] ', columns: [{ header: ' Orden ', path: ' ^.ref ' }] }))).toEqual({
      kind: 'JSON', sheet: null, recordsPath: '$.orders[*]', linesPath: '@.items[*]', columns: [{ header: 'Orden', path: '^.ref' }],
    })
    expect(buildFormatConfig({ ...emptyFormat(), kind: 'EXCEL', sheet: ' Pedidos ' })).toEqual({ kind: 'EXCEL', sheet: 'Pedidos', recordsPath: null, linesPath: null, columns: [] })
    expect(buildFormatConfig({ ...json(), kind: 'CSV' })).toEqual({ kind: 'CSV', sheet: null, recordsPath: null, linesPath: null, columns: [] })
    expect(buildFormatConfig(json({ linesPath: ' ' })).linesPath).toBeNull()
  })

  it('parte de lo guardado y sabe si el formato no dice nada', () => {
    expect(formatValuesOf(null)).toEqual(emptyFormat())
    expect(formatValuesOf({ kind: 'XML', recordsPath: '$.a[*]', columns: [{ header: 'A', path: '@.x' }] })).toEqual({ kind: 'XML', sheet: '', recordsPath: '$.a[*]', linesPath: '', columns: [{ header: 'A', path: '@.x' }] })
    expect(formatIsPlain(emptyFormat())).toBe(true)
    expect(formatIsPlain({ ...emptyFormat(), sheet: 'x' })).toBe(false)
    expect(formatIsPlain(json())).toBe(false)
    expect(isStructured('JSON') && isStructured('XML') && !isStructured('EXCEL') && !isStructured('AUTO')).toBe(true)
  })
})
