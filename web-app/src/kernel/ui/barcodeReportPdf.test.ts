// Lote F14 — reporte de códigos de barras en rejilla: ancho de módulo, elección de columnas (3 / 2 / 1), "No cabe" y
// omitidos, paginación (una fila de celdas nunca se parte; un título de grupo nunca queda solo al pie; continuación),
// avisos, y el documento completo en memoria (menos páginas que una fila por elemento).
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang } from '../i18n/i18n'
import {
  BARCODE_MIN_MODULE_MM,
  BARCODE_TARGET_MODULE_MM,
  PT_PER_MM,
  barcodeCellCodeWidth,
  barcodeEndNotices,
  barcodeModuleWidth,
  barcodeReportSummary,
  barcodeTopNotices,
  chooseBarcodeColumns,
  paginateBarcodeGrid,
  planBarcodeReport,
  renderBarcodeReportPdf,
  type BarcodeReportGroup,
  type BarcodeReportSpec,
} from './barcodeReportPdf'
import { encodeCode128 } from './code128'
import { reportFileName } from './reportPdf'

beforeAll(() => setLang('es'))

/** Carta vertical en puntos. */
const LETTER_W = 612
const letters = (n: number) => 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.repeat(4).slice(0, n)
const width = (v: string) => encodeCode128(v).width

describe('barcodeReportPdf · módulo y columnas', () => {
  it('módulo: 0.33 mm si cabe, más angosto hasta 0.25 mm, null por debajo', () => {
    const target = BARCODE_TARGET_MODULE_MM * PT_PER_MM
    const min = BARCODE_MIN_MODULE_MM * PT_PER_MM
    // 46 módulos + 20 de silencio en 200 pt: sobra espacio → el buscado
    expect(barcodeModuleWidth(46, 200)).toBeCloseTo(target, 6)
    // entre el buscado y el mínimo: llena la columna
    expect(barcodeModuleWidth(200, 160)).toBeCloseTo(160 / 220, 6)
    expect(barcodeModuleWidth(200, 160)!).toBeGreaterThanOrEqual(min)
    // por debajo del mínimo: no cabe
    expect(barcodeModuleWidth(400, 160)).toBeNull()
  })

  it('ancho útil por columnas en carta: 3 ≈ 163 pt, 2 = 255 pt, 1 = 530 pt', () => {
    expect(barcodeCellCodeWidth(3, LETTER_W)).toBeCloseTo(163.33, 1)
    expect(barcodeCellCodeWidth(2, LETTER_W)).toBeCloseTo(255, 1)
    expect(barcodeCellCodeWidth(1, LETTER_W)).toBeCloseTo(530, 1)
  })

  it('automático: 3 si todos caben, si no 2, si no 1 (15 / 16 / 27 / 28 letras en subconjunto B)', () => {
    expect(chooseBarcodeColumns([width(letters(15)), width('A-1')], 'auto', LETTER_W)).toBe(3)
    expect(chooseBarcodeColumns([width(letters(16)), width('A-1')], 'auto', LETTER_W)).toBe(2)
    expect(chooseBarcodeColumns([width(letters(27))], 'auto', LETTER_W)).toBe(2)
    expect(chooseBarcodeColumns([width(letters(28))], 'auto', LETTER_W)).toBe(1)
    expect(chooseBarcodeColumns([], 'auto', LETTER_W)).toBe(3)
    // los dígitos en subconjunto C ocupan la mitad: 30 dígitos caben en 3 columnas
    expect(chooseBarcodeColumns([width('1'.repeat(30))], 'auto', LETTER_W)).toBe(3)
    // "2 columnas": nunca 3; baja a 1 si hace falta
    expect(chooseBarcodeColumns([width('A-1')], 2, LETTER_W)).toBe(2)
    expect(chooseBarcodeColumns([width(letters(28))], 2, LETTER_W)).toBe(1)
  })

  it('plan: "No cabe" solo si ni en una columna cabe (62 letras sí, 63 no); omitidos los no admitidos; grupos vacíos fuera', () => {
    const groups: BarcodeReportGroup[] = [
      { title: 'A', rows: [{ value: letters(62) }, { value: letters(26) + letters(26) + letters(11) }, { value: 'CAFÉ-01' }] },
      { title: 'Solo omitidos', rows: [{ value: 'Ñ' }, { value: '' }] },
    ]
    const plan = planBarcodeReport(groups, 'auto', LETTER_W)
    expect(plan.columns).toBe(1)
    expect(plan.requestedColumns).toBe(3)
    expect(plan.groups.map((g) => g.title)).toEqual(['A'])
    expect(plan.groups[0].cells.map((c) => c.status)).toEqual(['ok', 'tooWide'])
    expect(plan.tooWide).toHaveLength(1)
    expect(plan.unsupported).toEqual([
      { value: 'CAFÉ-01', chars: ['É'] },
      { value: 'Ñ', chars: ['Ñ'] },
      { value: '', chars: [] },
    ])
    // el módulo de lo que cabe nunca baja de 0.25 mm
    const ok = plan.groups[0].cells[0]
    expect(ok.status === 'ok' && ok.modulePt >= BARCODE_MIN_MODULE_MM * PT_PER_MM - 1e-9).toBe(true)
  })

  it('avisos: arriba las columnas usadas y la remisión al final; al final la lista de "no caben" y de omitidos', () => {
    const plan = planBarcodeReport([{ title: 'G', rows: [{ value: letters(20) }, { value: 'X📦' }] }], 'auto', LETTER_W)
    expect(plan.columns).toBe(2)
    const top = barcodeTopNotices({ notices: ['Lista cortada'] }, plan)
    expect(top).toEqual([
      'Lista cortada',
      'Se usaron 2 columnas porque hay códigos largos que no caben más angostos.',
      'Hay valores sin código de barras (no caben u omitidos): vea los avisos al final del reporte.',
    ])
    expect(barcodeEndNotices(plan)).toEqual([
      'Omitidos porque tienen caracteres que el código de barras (Code 128) no admite (1): X📦 (no admite: U+1F4E6).',
    ])
    const wide = planBarcodeReport([{ title: 'G', rows: [{ value: letters(70) }] }], 'auto', LETTER_W)
    expect(barcodeEndNotices(wide)[0]).toBe(`No caben como código de barras legible en el ancho de la hoja (1); salen en la lista sin código: ${letters(70)}.`)
    expect(barcodeReportSummary(wide).map((s) => [s.label, s.value, s.tone])).toEqual([
      ['Elementos', '1', undefined],
      ['Con código', '0', undefined],
      ['No caben', '1', 'money'],
      ['Omitidos', '0', 'flow'],
    ])
    // con lo pedido (2 columnas) no hay aviso de columnas
    expect(barcodeTopNotices({}, planBarcodeReport([{ title: 'G', rows: [{ value: 'A-1' }] }], 2, LETTER_W))).toEqual([])
  })
})

describe('barcodeReportPdf · paginación', () => {
  /** Comprueba las reglas en cada página: nada pasa del cuerpo; un título nunca es lo último; la continuación abre página. */
  function checkPages(pages: ReturnType<typeof paginateBarcodeGrid>, first: number, rest: number) {
    pages.forEach((page, p) => {
      const available = p === 0 ? first : rest
      for (const item of page) if (page.length > 1 || item.kind === 'row') expect(item.y + item.height).toBeLessThanOrEqual(available + 1e-9)
      if (page.length > 0) expect(page[page.length - 1].kind).toBe('row')
      page.forEach((item, i) => {
        if (item.kind === 'title' && item.continued) expect(i).toBe(0)
        if (item.kind === 'title') expect(page[i + 1]?.kind).toBe('row')
      })
    })
  }

  it('una fila nunca se parte, el título va con su primera fila y el grupo sigue con "(continuación)"', () => {
    const groups = [{ rowHeights: [80, 80, 80] }, { rowHeights: [80, 80, 80, 80, 80, 80, 80, 80, 80] }]
    const pages = paginateBarcodeGrid(groups, 22, 300, 700)
    checkPages(pages, 300, 700)
    // página 1: título 0 + 3 filas = 262; el título 1 (22) + su fila (80) ya no caben (364 > 300) → pasan juntos
    expect(pages[0].map((i) => i.kind)).toEqual(['title', 'row', 'row', 'row'])
    expect(pages[1][0]).toMatchObject({ kind: 'title', group: 1, continued: false, y: 0 })
    // página 2: título + 8 filas (22 + 640 = 662 ≤ 700); la 9.ª sigue en la página 3 con el título de continuación
    expect(pages[1].filter((i) => i.kind === 'row')).toHaveLength(8)
    expect(pages[2][0]).toMatchObject({ kind: 'title', group: 1, continued: true })
    expect(pages[2][1]).toMatchObject({ kind: 'row', group: 1, row: 8 })
  })

  it('título que quedaría solo al pie pasa a la página siguiente; primera página sin espacio queda vacía', () => {
    // queda espacio para el título (30 ≥ 22) pero no para título + fila
    const pages = paginateBarcodeGrid([{ rowHeights: [70] }, { rowHeights: [70] }], 22, 122, 700)
    checkPages(pages, 122, 700)
    expect(pages[0].map((i) => i.kind)).toEqual(['title', 'row'])
    expect(pages[1].map((i) => i.kind)).toEqual(['title', 'row'])
    const tight = paginateBarcodeGrid([{ rowHeights: [80] }], 22, 50, 700)
    expect(tight[0]).toEqual([])
    expect(tight[1].map((i) => i.kind)).toEqual(['title', 'row'])
  })

  it('reparto con muchas filas de alturas distintas: siempre cumple las reglas', () => {
    const groups = Array.from({ length: 7 }, (_, g) => ({ rowHeights: Array.from({ length: 3 + ((g * 5) % 11) }, (_, r) => 70 + ((g + r) % 4) * 12) }))
    const pages = paginateBarcodeGrid(groups, 22, 420, 700)
    checkPages(pages, 420, 700)
    const rows = pages.flat().filter((i) => i.kind === 'row').length
    expect(rows).toBe(groups.reduce((a, g) => a + g.rowHeights.length, 0))
  })
})

describe('barcodeReportPdf · documento', () => {
  const spec = (groups: BarcodeReportGroup[], columns?: BarcodeReportSpec['columns']): BarcodeReportSpec => ({
    title: 'Códigos de barras de posiciones',
    subtitle: 'Un código por posición',
    company: 'Advance Logistics',
    user: 'Ana Pérez',
    generatedAt: new Date(2026, 9, 3, 14, 5),
    locale: 'es',
    filters: [{ label: 'Almacén', value: 'ALM-01 (Principal)' }],
    groups,
    columns,
  })
  const forty: BarcodeReportGroup[] = [
    { title: 'Grupo 01 (25)', rows: Array.from({ length: 25 }, (_, i) => ({ value: `A01-R${String(i + 1).padStart(2, '0')}`, meta: 'Zona RSV · Almacén ALM-01' })) },
    { title: 'Grupo 2 (15)', rows: Array.from({ length: 15 }, (_, i) => ({ value: `B2-${i + 1}`, meta: 'Zona PCK · Almacén ALM-01' })) },
  ]

  it('40 elementos: la rejilla usa menos páginas que una fila por elemento; encabezado, filtros, valores y "Página X de Y"', async () => {
    const grid = await renderBarcodeReportPdf(spec(forty), { compress: false })
    const single = await renderBarcodeReportPdf(spec(forty, 1), { compress: false })
    expect(grid.getNumberOfPages()).toBeLessThan(single.getNumberOfPages())
    expect(grid.getNumberOfPages()).toBeLessThanOrEqual(3)
    expect(single.getNumberOfPages()).toBeGreaterThanOrEqual(6)
    const text = grid.output()
    expect(text.startsWith('%PDF-')).toBe(true)
    expect(text).toContain('/MediaBox [0 0 612. 792.]')
    expect(text).toContain('FILTROS APLICADOS')
    expect(text).toContain('ALM-01 \\(Principal\\)')
    expect(text).toContain('(Grupo 01 \\(25\\))')
    expect(text).toContain('(A01-R25)')
    expect(text).toContain('(B2-15)')
    expect(text).toContain(`de ${grid.getNumberOfPages()}`)
    expect(reportFileName(spec(forty), new Date(2026, 9, 3, 14, 5))).toBe('codigos-de-barras-de-posiciones-advance-logistics-2026-10-03.pdf')
  })

  it('celda que no cabe: su valor y "No cabe"; vacío: el texto de vacío', async () => {
    const doc = await renderBarcodeReportPdf(spec([{ title: 'G', rows: [{ value: letters(70) }] }]), { compress: false })
    const text = doc.output()
    expect(text).toContain('No cabe: demasiado largo para un c')
    const empty = await renderBarcodeReportPdf({ ...spec([]), emptyText: 'Ninguna posición cumple los filtros.' }, { compress: false })
    expect(empty.getNumberOfPages()).toBe(1)
    expect(empty.output()).toContain('Ninguna posici')
  })
})
