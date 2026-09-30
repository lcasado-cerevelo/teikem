// Exportación agrupada del kit (filas madre con hijas): armado de bloques, aplanado para Excel/CSV (una fila por hija,
// madre sin hijas = una fila con las de hija vacías), números con signo y el PDF en memoria (carta horizontal, banda por
// madre, "Sin líneas", "Página X de Y").
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang } from '../i18n/i18n'
import { exportChildren } from './exportChildren'
import { buildGroupedExportData, flattenGroupedExport, formatExportNumber, groupedCellText, renderGroupedPdf } from './exportGrouped'
import { toCsv, type ExportableColumn } from './exportTable'

beforeAll(() => setLang('es'))

interface Line {
  sku: string
  qty: number
  diff: number
  lot?: string
}
interface Doc {
  number: string
  status: string
  who: string
  total: number
  lines: Line[] | null
}

const DOC_COLS: ExportableColumn<Doc>[] = [
  { header: 'Número', cell: (d) => d.number },
  { header: 'Estatus', cell: (d) => d.status },
  { header: 'Remitente', cell: (d) => d.who },
  { header: 'Diferencia', cell: (d) => String(d.total), exportValue: (d) => d.total, align: 'end', signed: true },
  { header: '', cell: () => null, exportable: false },
]
const LINE_COLS: ExportableColumn<Line>[] = [
  { header: 'SKU', cell: (l) => l.sku },
  { header: 'Recibido', cell: (l) => String(l.qty), exportValue: (l) => l.qty, align: 'end' },
  { header: 'Diferencia', cell: (l) => String(l.diff), exportValue: (l) => l.diff, align: 'end', signed: true },
  { header: 'Lote', cell: (l) => l.lot ?? '' },
]
const DOCS: Doc[] = [
  { number: 'REC-1', status: 'Completado con diferencia', who: 'Proveedor Uno', total: -2, lines: [{ sku: 'A', qty: 3, diff: -2 }, { sku: 'B', qty: 1234, diff: 0, lot: 'L1' }] },
  { number: 'REC-2', status: 'Esperado', who: 'Cliente', total: 0, lines: [] },
  { number: 'REC-3', status: 'Recibiendo', who: '', total: 1, lines: null },
]
const children = exportChildren<Doc, Line>({ children: (d) => d.lines, columns: LINE_COLS, emptyText: 'Sin líneas', titleColumns: 2 })

describe('exportGrouped · armado', () => {
  it('exportChildren: encabezados, numéricas y con signo de las hijas; celdas por madre ([] sin hijas)', () => {
    expect(children.headers).toEqual(['SKU', 'Recibido', 'Diferencia', 'Lote'])
    expect(children.numeric).toEqual([false, true, true, false])
    expect(children.signed).toEqual([false, false, true, false])
    expect(children.rows(DOCS[0], {})).toEqual([
      ['A', 3, -2, null],
      ['B', 1234, 0, 'L1'],
    ])
    expect(children.rows(DOCS[1], {})).toEqual([])
    expect(children.rows(DOCS[2], {})).toEqual([])
  })

  it('bloques: título con las primeras columnas, el resto como campos (sin columnas no exportables) y las hijas', () => {
    const data = buildGroupedExportData(DOC_COLS, DOCS, children, { locale: 'es' })
    expect(data.parent.headers).toEqual(['Número', 'Estatus', 'Remitente', 'Diferencia'])
    expect(data.blocks).toHaveLength(3)
    expect(data.blocks[0].title).toEqual(['REC-1', 'Completado con diferencia'])
    expect(data.blocks[0].fields).toEqual([
      { label: 'Remitente', value: 'Proveedor Uno', numeric: false, signed: false },
      { label: 'Diferencia', value: -2, numeric: true, signed: true },
    ])
    expect(data.blocks[0].children).toHaveLength(2)
    expect(data.blocks[1].children).toEqual([])
    expect(data.emptyText).toBe('Sin líneas')
  })

  it('Excel/CSV: una fila por hija repitiendo la madre; madre sin hijas = una fila con las de hija vacías', () => {
    const flat = flattenGroupedExport(buildGroupedExportData(DOC_COLS, DOCS, children, { locale: 'es' }))
    expect(flat.headers).toEqual(['Número', 'Estatus', 'Remitente', 'Diferencia', 'SKU', 'Recibido', 'Diferencia', 'Lote'])
    expect(flat.rows).toEqual([
      ['REC-1', 'Completado con diferencia', 'Proveedor Uno', -2, 'A', 3, -2, null],
      ['REC-1', 'Completado con diferencia', 'Proveedor Uno', -2, 'B', 1234, 0, 'L1'],
      ['REC-2', 'Esperado', 'Cliente', 0, null, null, null, null],
      ['REC-3', 'Recibiendo', null, 1, null, null, null, null],
    ])
    expect(toCsv(flat).split('\r\n')[3]).toBe('REC-2,Esperado,Cliente,0,,,,')
  })

  it('números del PDF: separadores del idioma y signo solo en las columnas con signo (0 sin signo)', () => {
    expect(formatExportNumber(1234.5, 'es')).toBe('1,234.5')
    expect(formatExportNumber(5, 'en', true)).toBe('+5')
    expect(formatExportNumber(-3, 'es', true)).toBe('-3')
    expect(formatExportNumber(0, 'es', true)).toBe('0')
    expect(groupedCellText(null, 'es', false, '-')).toBe('-')
    expect(groupedCellText('Almacén → A', 'es', false)).toBe('Almacén -> A')
  })
})

describe('exportGrouped · PDF', () => {
  it('carta horizontal, un bloque por madre con su banda, "Sin líneas" y "Página X de Y" en cada página', async () => {
    const many: Doc[] = Array.from({ length: 30 }, (_, i) => ({
      number: `REC-${100 + i}`,
      status: 'Completado',
      who: `Proveedor ${i}`,
      total: i % 3 === 0 ? -1 : 0,
      lines: i % 5 === 4 ? [] : Array.from({ length: 4 }, (_, j) => ({ sku: `S${i}-${j}`, qty: j + 1, diff: j === 0 ? 2 : 0 })),
    }))
    const data = buildGroupedExportData(DOC_COLS, many, children, { locale: 'es' })
    const doc = await renderGroupedPdf(data, { title: 'Recibos', locale: 'es', compress: false })
    expect(doc.internal.pageSize.getWidth()).toBeCloseTo(792, 0)
    expect(doc.internal.pageSize.getHeight()).toBeCloseTo(612, 0)
    const pages = doc.getNumberOfPages()
    expect(pages).toBeGreaterThan(1)
    const text = doc.output()
    expect(text.startsWith('%PDF-')).toBe(true)
    expect(text).toContain('REC-100')
    expect(text).toContain('REC-129')
    // la "í" va como byte Latin-1 (o escapada): se busca el texto alrededor
    expect(text).toMatch(/\(Sin l.{1,4}neas\)/)
    expect(text).toContain('+2')
    expect(text).toContain(`de ${pages}`)
  })

  it('sin madres: el PDF sale igual, con el texto de "sin registros"', async () => {
    const doc = await renderGroupedPdf(buildGroupedExportData(DOC_COLS, [], children), { title: 'Recibos', locale: 'es', compress: false })
    expect(doc.getNumberOfPages()).toBe(1)
    expect(doc.output()).toContain('No hay registros')
  })
})
