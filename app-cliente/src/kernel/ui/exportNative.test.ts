// Excel, CSV y PDF en la APP INSTALADA (celulares y tabletas; Luis, 2026-10-24: «el estándar es CSV, PDF y Excel»): cada formato se guarda como archivo y se abre el menú de compartir. Los módulos nativos
// (archivos, compartir, imprimir) se simulan; lo que se prueba es qué se escribe, con qué nombre y tipo, y que el PDF lleve el logo, el título y los colores de la compañía.
const mockWrites: { name: string; content: string | Uint8Array }[] = []
const mockCopies: { from: string; to: string }[] = []

jest.mock('expo-file-system', () => {
  class File {
    uri: string
    exists = false
    constructor(...parts: (string | { uri: string })[]) {
      this.uri = parts.map((p) => (typeof p === 'string' ? p : p.uri)).join('/')
    }
    create() {}
    delete() {}
    write(content: string | Uint8Array) {
      mockWrites.push({ name: this.uri.split('/').pop() ?? '', content })
    }
    async copy(target: { uri: string }) {
      mockCopies.push({ from: this.uri, to: target.uri })
    }
    async base64() {
      return 'AAAA'
    }
  }
  return { File, Paths: { cache: { uri: 'file:///cache' } } }
})
jest.mock('expo-sharing', () => ({ isAvailableAsync: jest.fn(async () => true), shareAsync: jest.fn(async () => undefined) }))
jest.mock('expo-print', () => ({ printToFileAsync: jest.fn(async () => ({ uri: 'file:///tmp/print-1.pdf' })) }))
jest.mock('expo-asset', () => ({ Asset: { fromModule: () => ({ downloadAsync: async () => undefined, localUri: 'file:///assets/teikem.png', uri: 'http://x/teikem.png' }) } }))
jest.mock('../brand/portalBrand', () => ({
  getPortalBrandInfo: () => ({ companyName: 'Colmados Acme', logoSlots: [], logoUri: null }),
  loadPdfLogoSource: jest.fn(async () => ({ src: 'data:image/png;base64,QUJD', plate: null })),
}))

import * as Print from 'expo-print'
import * as Sharing from 'expo-sharing'

import { setLang } from '../i18n/i18n'
import { buildPdfHtml, escapeHtml, isLandscape, pageSize } from './exportPdfHtml'
import { sheetRows } from './exportWorkbook'
import { exportFormats, runExport, type ExportData, type ExportHeading } from './tableExport'

const rows = [{ n: 'Farmacia <Norte> & Cía', q: 2, d: '2026-10-23' }, { n: 'Colmado Sur', q: 10.5, d: '' }]
const columns = [{ title: 'Nombre', csv: (r: (typeof rows)[number]) => r.n }, { title: 'Piezas', csv: (r: (typeof rows)[number]) => r.q }, { title: 'Fecha', csv: (r: (typeof rows)[number]) => r.d }]
const share = Sharing.shareAsync as jest.Mock

beforeEach(() => {
  setLang('es')
  mockWrites.length = 0
  mockCopies.length = 0
  jest.clearAllMocks()
})

describe('formatos', () => {
  it('toda tabla ofrece Excel, CSV y PDF también en la app instalada', () => {
    expect(exportFormats()).toEqual(['xlsx', 'csv', 'pdf'])
  })
})

describe('runExport en la app instalada', () => {
  it('Excel: guarda un .xlsx de verdad (un zip) y abre compartir con su tipo', async () => {
    await runExport({ format: 'xlsx', title: 'Mis envíos', columns, rows })
    expect(mockWrites).toHaveLength(1)
    expect(mockWrites[0].name).toMatch(/^teikem-mis-envios-\d{4}-\d{2}-\d{2}\.xlsx$/)
    const bytes = mockWrites[0].content as Uint8Array
    expect(bytes).toBeInstanceOf(Uint8Array)
    expect([bytes[0], bytes[1]]).toEqual([0x50, 0x4b]) // «PK»
    expect(share).toHaveBeenCalledWith(expect.stringMatching(/\.xlsx$/), expect.objectContaining({ mimeType: expect.stringContaining('spreadsheetml') }))
  })

  it('CSV: guarda un .csv con la marca UTF-8 y el encabezado, y abre compartir', async () => {
    await runExport({ format: 'csv', title: 'Mis envíos', columns, rows })
    expect(mockWrites[0].name).toMatch(/\.csv$/)
    expect(String(mockWrites[0].content)).toBe('﻿Nombre,Piezas,Fecha\r\nFarmacia <Norte> & Cía,2,2026-10-23\r\nColmado Sur,10.5,\r\n')
    expect(share).toHaveBeenCalledWith(expect.stringMatching(/\.csv$/), expect.objectContaining({ mimeType: 'text/csv' }))
  })

  it('PDF: arma la página con el logo, la compañía, el título y los filtros; la imprime a PDF y la comparte con el nombre bueno', async () => {
    await runExport({ format: 'pdf', title: 'Mis envíos', columns, rows, filters: 'Filtros: Buscar «col»' })
    const print = Print.printToFileAsync as jest.Mock
    expect(print).toHaveBeenCalledTimes(1)
    const { html, width, height } = print.mock.calls[0][0] as { html: string; width: number; height: number }
    expect(html).toContain('data:image/png;base64,QUJD') // el logo de la compañía
    expect(html).toContain('Colmados Acme')
    expect(html).toContain('<h1>Mis envíos</h1>')
    expect(html).toContain('Filtros: Buscar «col»')
    expect(html).toContain('Farmacia &lt;Norte&gt; &amp; Cía') // el texto se escapa
    expect({ width, height }).toEqual({ width: 595, height: 842 }) // 3 columnas: vertical
    expect(mockCopies).toEqual([{ from: 'file:///tmp/print-1.pdf', to: expect.stringMatching(/teikem-mis-envios-\d{4}-\d{2}-\d{2}\.pdf$/) }])
    expect(share).toHaveBeenCalledWith(expect.stringMatching(/\.pdf$/), expect.objectContaining({ mimeType: 'application/pdf' }))
  })

  it('si el aparato no puede compartir archivos, el error llega a la pantalla', async () => {
    ;(Sharing.isAvailableAsync as jest.Mock).mockResolvedValueOnce(false)
    await expect(runExport({ format: 'xlsx', title: 'X', columns, rows })).rejects.toThrow('no puede compartir')
  })
})

describe('el PDF en HTML', () => {
  const heading: ExportHeading = { company: 'Acme', title: 'Informe', generatedAt: new Date(2026, 9, 24, 10, 15), filters: null, lang: 'es' }
  const colors = { navy: [3, 31, 73] as [number, number, number], onNavy: [255, 255, 255] as [number, number, number], flow: [243, 113, 35] as [number, number, number], zebra: [243, 244, 247] as [number, number, number] }
  const wide: ExportData = { headers: ['a', 'b', 'c', 'd', 'e', 'f'], rows: [['x', 1, null, 'y', 'z', 2]], numeric: [false, true, false, false, false, true] }

  it('más de 5 columnas sale horizontal y los números van a la derecha; con los colores de la compañía', () => {
    expect(isLandscape(wide)).toBe(true)
    expect(pageSize(wide)).toEqual({ width: 842, height: 595 })
    const html = buildPdfHtml(wide, heading, { colors, logo: null, plate: false })
    expect(html).toContain('size: A4 landscape')
    expect(html).toContain('<th class="n">b</th>')
    expect(html).toContain('<td class="n">1</td>')
    expect(html).toContain('background: rgb(3, 31, 73)')
    expect(html).toContain('rgb(243, 113, 35)')
    expect(html).not.toContain('<img')
  })
  it('un logo de letras claras va sobre una placa del color de la compañía', () => {
    const html = buildPdfHtml(wide, heading, { colors, logo: 'data:image/png;base64,QQ==', plate: true })
    expect(html).toContain('class="logo plate"')
  })
  it('escapa el HTML', () => {
    expect(escapeHtml('<a href="x">&</a>')).toBe('&lt;a href=&quot;x&quot;&gt;&amp;&lt;/a&gt;')
  })
})

describe('las filas de la hoja de Excel', () => {
  it('encabezado, línea en blanco, títulos y datos; las fechas AAAA-MM-DD pasan a fecha', () => {
    const data: ExportData = { headers: ['Nombre', 'Fecha'], rows: [['A', '2026-10-23'], ['B', null]], numeric: [false, false] }
    const { aoa, headerRow } = sheetRows(data, { company: 'Acme', title: 'Informe', generatedAt: new Date(2026, 9, 24, 10, 15), filters: 'Sin filtros', lang: 'es' })
    expect(aoa[0]).toEqual(['Acme'])
    expect(aoa[1]).toEqual(['Informe'])
    expect(aoa[headerRow]).toEqual(['Nombre', 'Fecha'])
    expect(aoa[headerRow + 1][1]).toBeInstanceOf(Date)
    expect(aoa[headerRow + 2]).toEqual(['B', null])
  })
})
