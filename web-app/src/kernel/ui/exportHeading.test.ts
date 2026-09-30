// Encabezado de las exportaciones (pedido del dueño del producto, 2026-09-30): PDF (tabla y agrupado) y Excel con la
// compañía, el título, "Generado el …" y la oración de filtros; el CSV sigue siendo solo la tabla.
import * as XLSX from 'xlsx'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { setLang } from '../i18n/i18n'
import { exportChildren } from './exportChildren'
import { buildGroupedExportData, flattenGroupedExport, renderGroupedPdf } from './exportGrouped'
import {
  buildExportData,
  buildXlsxWorkbook,
  exportGeneratedText,
  exportHeadingLines,
  exportTable,
  renderTablePdf,
  toCsv,
  xlsxSheetRows,
  type ExportableColumn,
  type ExportData,
} from './exportTable'

beforeAll(() => setLang('es'))

const WHEN = new Date(2026, 8, 30, 10, 15)
const DATA: ExportData = {
  headers: ['Número', 'Estatus', 'Cantidad'],
  numeric: [false, false, true],
  rows: [
    ['REC-1', 'Recibiendo', 12],
    ['REC-2', 'Esperado', null],
  ],
}
const FILTERS = 'Filtros: Almacén ALM-DEPOT · Estatus Recibiendo · Creado del 01/09/2026 al 30/09/2026'
const HEADING = { company: 'Advance Logistics', title: 'Recibos', generatedAt: WHEN, filters: FILTERS, locale: 'es-PR' }

describe('exportHeadingLines', () => {
  it('orden: compañía → título → "Generado el …" → filtros; sin compañía, título ni filtros solo va la fecha', () => {
    const lines = exportHeadingLines(HEADING)
    expect(lines.map((l) => l.kind)).toEqual(['company', 'title', 'generated', 'filters'])
    expect(lines[2].text).toBe(exportGeneratedText(WHEN, 'es-PR'))
    expect(lines[2].text).toMatch(/^Generado el 30 de septiembre de 2026/)
    expect(exportHeadingLines({ company: ' ', title: null, filters: null, generatedAt: WHEN }).map((l) => l.kind)).toEqual(['generated'])
  })
})

describe('PDF de tabla', () => {
  it('compañía arriba del título, luego "Generado el …" y la oración de filtros, y la tabla', async () => {
    const doc = await renderTablePdf(DATA, { ...HEADING, compress: false })
    const text = doc.output()
    const at = (s: string) => text.indexOf(s)
    expect(at('Advance Logistics')).toBeGreaterThan(-1)
    expect(at('Advance Logistics')).toBeLessThan(at('(Recibos)'))
    expect(at('(Recibos)')).toBeLessThan(at('Generado el'))
    expect(at('Generado el')).toBeLessThan(at('Filtros: Almac'))
    expect(at('Filtros: Almac')).toBeLessThan(at('REC-1'))
  })

  it('sin compañía ni filtros: solo título y fecha', async () => {
    const text = (await renderTablePdf(DATA, { title: 'Recibos', generatedAt: WHEN, locale: 'es-PR', compress: false })).output()
    expect(text).toContain('Generado el')
    expect(text).not.toContain('Filtros')
    expect(text).not.toContain('Advance')
  })
})

describe('PDF agrupado', () => {
  interface Line {
    sku: string
  }
  interface Doc {
    number: string
    lines: Line[]
  }
  const cols: ExportableColumn<Doc>[] = [{ header: 'Número', cell: (d) => d.number }]
  const children = exportChildren<Doc, Line>({ children: (d) => d.lines, columns: [{ header: 'SKU', cell: (l) => l.sku }], emptyText: 'Sin líneas' })

  it('mismo encabezado: compañía, título, fecha y filtros antes del primer bloque', async () => {
    const data = buildGroupedExportData(cols, [{ number: 'REC-9', lines: [{ sku: 'A-1' }] }], children)
    const text = (await renderGroupedPdf(data, { ...HEADING, compress: false })).output()
    const at = (s: string) => text.indexOf(s)
    expect(at('Advance Logistics')).toBeGreaterThan(-1)
    expect(at('Advance Logistics')).toBeLessThan(at('Generado el'))
    expect(at('Generado el')).toBeLessThan(at('Filtros: Almac'))
    expect(at('Filtros: Almac')).toBeLessThan(at('REC-9'))
  })

  it('Excel agrupado: encabezado, fila en blanco y una fila por línea', () => {
    const data = buildGroupedExportData(cols, [{ number: 'REC-9', lines: [{ sku: 'A-1' }, { sku: 'A-2' }] }], children)
    const { rows, headerRow } = xlsxSheetRows(flattenGroupedExport(data), HEADING)
    expect(headerRow).toBe(5)
    expect(rows.slice(headerRow)).toEqual([
      ['Número', 'SKU'],
      ['REC-9', 'A-1'],
      ['REC-9', 'A-2'],
    ])
  })
})

describe('Excel', () => {
  it('filas del encabezado (compañía, título, fecha, filtros), una en blanco y luego la tabla', () => {
    const { rows, headerRow } = xlsxSheetRows(DATA, HEADING)
    expect(rows.slice(0, headerRow)).toEqual([['Advance Logistics'], ['Recibos'], [exportGeneratedText(WHEN, 'es-PR')], [FILTERS], []])
    expect(rows[headerRow]).toEqual(DATA.headers)
    expect(rows.slice(headerRow + 1)).toEqual(DATA.rows)
    // sin filtros ni compañía: título, fecha, blanco
    expect(xlsxSheetRows(DATA, { title: 'Recibos', generatedAt: WHEN }).headerRow).toBe(3)
  })

  it('el libro: celdas del encabezado, números como números, autofiltro en la fila de encabezados y ancho por la tabla', () => {
    const wb = buildXlsxWorkbook(XLSX, DATA, HEADING)
    const ws = wb.Sheets[wb.SheetNames[0]]
    expect(wb.SheetNames[0]).toBe('Recibos')
    expect(ws.A1.v).toBe('Advance Logistics')
    expect(ws.A2.v).toBe('Recibos')
    expect(String(ws.A3.v)).toMatch(/^Generado el/)
    expect(ws.A4.v).toBe(FILTERS)
    expect(ws.A5).toBeUndefined()
    expect(ws.A6.v).toBe('Número')
    expect(ws.C7).toMatchObject({ t: 'n', v: 12 })
    expect(ws['!autofilter']).toEqual({ ref: 'A6:C8' })
    // la oración larga del encabezado no ensancha la columna A (solo la tabla cuenta)
    expect(ws['!cols']?.[0]?.wch).toBeLessThan(20)
    // se puede escribir y volver a leer
    const back = XLSX.read(XLSX.write(wb, { type: 'array', bookType: 'xlsx' }), { type: 'array' })
    expect(XLSX.utils.sheet_to_json(back.Sheets[back.SheetNames[0]], { header: 1 })[5]).toEqual(['Número', 'Estatus', 'Cantidad'])
  })
})

describe('CSV sin cambios', () => {
  afterEach(() => vi.restoreAllMocks())

  it('exportTable csv: solo encabezados y filas (sin compañía, título, fecha ni filtros)', async () => {
    let blob: Blob | null = null
    const createUrl = vi.fn((b: Blob) => {
      blob = b
      return 'blob:x'
    })
    const g = globalThis as unknown as { URL: { createObjectURL?: unknown; revokeObjectURL?: unknown } }
    const prevCreate = g.URL.createObjectURL
    const prevRevoke = g.URL.revokeObjectURL
    g.URL.createObjectURL = createUrl
    g.URL.revokeObjectURL = () => {}
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    try {
      const cols: ExportableColumn<{ n: string }>[] = [{ header: 'Número', cell: (r) => r.n }]
      const rows = [{ n: 'REC-1' }]
      await exportTable('csv', cols, rows, { title: 'Recibos', company: 'Advance Logistics', filters: FILTERS, date: WHEN })
      expect(click).toHaveBeenCalled()
      const text = await (blob as unknown as Blob).text()
      // (Blob.text() ya quita el BOM al decodificar UTF-8)
      expect(text.replace(/^﻿/, '')).toBe(toCsv(buildExportData(cols, rows)))
      expect(text).not.toContain('Advance')
      expect(text).not.toContain('Filtros')
    } finally {
      g.URL.createObjectURL = prevCreate
      g.URL.revokeObjectURL = prevRevoke
    }
  })
})
