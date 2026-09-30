// Reportes PDF del kit (Lote 12): formato de números por columna, armado del cuerpo (grupos, cebra, subtotales y totales
// con la etiqueta fundida), orientación, nombre de archivo y el documento completo generado en memoria (sin descargar).
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang } from '../i18n/i18n'
import {
  buildReportBody,
  formatReportValue,
  isNumericFormat,
  renderReportPdf,
  reportFileName,
  reportOrientation,
  type ReportColumn,
  type ReportSpec,
} from './reportPdf'

beforeAll(() => setLang('es'))

const COLUMNS: ReportColumn[] = [{ header: 'SKU' }, { header: 'Producto' }, { header: 'Total', format: 'quantity' }, { header: 'Valor', format: 'money' }]

describe('reportPdf · lógica pura', () => {
  it('formatReportValue: separadores del idioma por formato; texto tal cual; vacío = ""', () => {
    expect(formatReportValue(1234567.5, 'quantity', 'en')).toBe('1,234,567.5')
    expect(formatReportValue(1234567.5, 'quantity', 'es')).toBe('1,234,567.5')
    expect(formatReportValue(12.5, 'money', 'en')).toBe('$12.50')
    expect(formatReportValue(0.12345, 'unitCost', 'en')).toBe('$0.1235')
    expect(formatReportValue(3, 'unitCost', 'en')).toBe('$3.00')
    expect(formatReportValue(5, 'signed', 'en')).toBe('+5')
    expect(formatReportValue(-2, 'signed', 'en')).toBe('-2')
    expect(formatReportValue(0, 'signed', 'en')).toBe('0')
    expect(formatReportValue('—', 'money', 'en')).toBe('—')
    expect(formatReportValue(null, 'money', 'en')).toBe('')
    expect(formatReportValue(Number.NaN, 'money', 'en')).toBe('')
    expect(formatReportValue(7, 'text', 'en')).toBe('7')
    expect(isNumericFormat('text')).toBe(false)
    expect(isNumericFormat(undefined)).toBe(false)
    expect(isNumericFormat('signed')).toBe(true)
  })

  it('buildReportBody: grupo, filas con índice para la cebra, subtotal con la etiqueta fundida y total al final', () => {
    const rows = buildReportBody({
      locale: 'en',
      columns: COLUMNS,
      sections: [
        { title: 'Médico (2)', rows: [['A-1', 'Uno', 1000, 12.5], ['A-2', 'Dos', 2, '—']], subtotal: ['Subtotal Médico', null, 1002, 12.5] },
        { title: 'Vacía', rows: [] },
      ],
      totals: [['Total general', null, 1002, 12.5]],
    })
    expect(rows.map((r) => r.kind)).toEqual(['group', 'data', 'data', 'subtotal', 'total'])
    expect(rows[0].cells).toEqual([{ text: 'Médico (2)', colSpan: 4, align: 'left' }])
    expect(rows[1].index).toBe(0)
    expect(rows[2].index).toBe(1)
    expect(rows[1].cells.map((c) => c.text)).toEqual(['A-1', 'Uno', '1,000', '$12.50'])
    expect(rows[1].cells.map((c) => c.align)).toEqual(['left', 'left', 'right', 'right'])
    expect(rows[2].cells[3].text).toBe('—')
    expect(rows[3].cells).toEqual([
      { text: 'Subtotal Médico', colSpan: 2, align: 'left' },
      { text: '1,002', colSpan: 1, align: 'right' },
      { text: '$12.50', colSpan: 1, align: 'right' },
    ])
    expect(rows[4].cells[0]).toEqual({ text: 'Total general', colSpan: 2, align: 'left' })
  })

  it('buildReportBody sin filas: una sola fila "empty" con el texto', () => {
    const rows = buildReportBody({ locale: 'es', columns: COLUMNS, sections: [{ rows: [] }], emptyText: 'Nada' })
    expect(rows).toEqual([{ kind: 'empty', cells: [{ text: 'Nada', colSpan: 4, align: 'left' }] }])
  })

  it('orientación: horizontal con más de 6 columnas (o la pedida); nombre de archivo con título, compañía y fecha', () => {
    expect(reportOrientation({ columns: COLUMNS })).toBe('portrait')
    expect(reportOrientation({ columns: [...COLUMNS, ...COLUMNS] })).toBe('landscape')
    expect(reportOrientation({ columns: COLUMNS, orientation: 'landscape' })).toBe('landscape')
    expect(reportFileName({ title: 'Reporte de inventario', company: 'Advance Depot' }, new Date(2026, 8, 30))).toBe(
      'reporte-de-inventario-advance-depot-2026-09-30.pdf',
    )
    expect(reportFileName({ title: 'Reporte de ajustes', company: null }, new Date(2026, 0, 5))).toBe('reporte-de-ajustes-2026-01-05.pdf')
  })
})

describe('reportPdf · documento', () => {
  it('genera el PDF en memoria: varias páginas con encabezado, filtros, "Página X de Y" y "Generado con Teikem"', async () => {
    const spec: ReportSpec = {
      title: 'Reporte de inventario',
      subtitle: 'Inventario al momento',
      company: 'Advance Depot',
      user: 'Ana Pérez',
      generatedAt: new Date(2026, 8, 30, 14, 5),
      locale: 'es',
      filters: [{ label: 'Almacén', value: 'ALM-01 · Principal' }],
      columns: COLUMNS,
      sections: [{ title: 'Médico', rows: Array.from({ length: 90 }, (_, i) => [`SKU-${i}`, `Producto ${i}`, i * 10, i * 1.5]), subtotal: ['Subtotal', null, 40050, 6007.5] }],
      totals: [['Total general', null, 40050, 6007.5]],
      summary: [{ label: 'Productos', value: '90' }],
      notices: ['Aviso de prueba → con flecha'],
    }
    const doc = await renderReportPdf(spec, { logo: null, compress: false })
    const pages = doc.getNumberOfPages()
    expect(pages).toBeGreaterThan(1)
    const text = doc.output()
    expect(text.startsWith('%PDF-')).toBe(true)
    expect(text).toContain('TEIKEM')
    expect(text).toContain('Advance Depot')
    expect(text).toContain(`de ${pages}`)
    expect(text).toContain('Generado con Teikem')
    expect(text).toContain('FILTROS APLICADOS')
    // fuera de Latin-1 se reemplaza (la flecha pasa a "->")
    expect(text).toContain('->')
  })
})
