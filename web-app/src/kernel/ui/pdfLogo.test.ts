// Logo de la compañía en los PDF (Lote 188): el reporte de marca y las tablas llevan el logo subido; sin él, el reporte conserva el de Teikem.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang } from '../i18n/i18n'
import { renderTablePdf } from './exportTable'
import { renderReportPdf, type ReportSpec } from './reportPdf'
import { fitLogo, type PdfLogo } from './pdfLogo'

beforeAll(() => setLang('es'))

// PNG de 1×1 válido
const PNG = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=='
const LOGO: PdfLogo = { dataUrl: PNG, width: 300, height: 100, plate: null }
const SPEC: ReportSpec = {
  title: 'Inventario',
  company: 'Advance Logistics',
  locale: 'es',
  filters: [{ label: 'Almacén', value: 'ALM-01' }],
  columns: [{ header: 'A', format: 'text' }] as unknown as ReportSpec['columns'],
  sections: [{ title: 'Uno', rows: [['x']] }],
}

describe('logo de la compañía en los PDF', () => {
  it('el reporte con el logo de la compañía no lleva el «TEIKEM» de la marca; sin logo sí', async () => {
    const withLogo = (await renderReportPdf(SPEC, { logo: LOGO, compress: false })).output()
    const without = (await renderReportPdf(SPEC, { logo: null, compress: false })).output()
    expect(withLogo).not.toContain('(TEIKEM)')
    expect(withLogo).toContain('Advance Logistics')
    expect(without).toContain('TEIKEM')
  })

  it('la tabla exportada dibuja el logo arriba del encabezado', async () => {
    const data = { headers: ['A'], numeric: [false], rows: [['x']] }
    const plain = await renderTablePdf(data, { company: 'Advance Logistics', title: 'Recibos', logo: null, compress: false })
    const logo = await renderTablePdf(data, { company: 'Advance Logistics', title: 'Recibos', logo: LOGO, compress: false })
    expect(logo.output().length).toBeGreaterThan(plain.output().length)
    expect(logo.output()).toContain('/Subtype /Image')
    expect(plain.output()).not.toContain('/Subtype /Image')
  })

  it('el logo se ajusta a la caja sin deformarse', () => {
    expect(fitLogo({ width: 300, height: 100 }, 190, 46)).toEqual({ w: 138, h: 46 })
    expect(fitLogo({ width: 100, height: 100 }, 190, 46)).toEqual({ w: 46, h: 46 })
  })
})
