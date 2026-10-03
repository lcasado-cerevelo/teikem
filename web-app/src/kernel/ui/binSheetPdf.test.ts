// Lote F15 — hoja de posición: qué codifica cada producto (código de barras → SKU → solo texto), una posición por hoja y
// partes de 10 ("Hoja 2 de 2"), posiciones vacías, reparto del alto entre los N productos (mínimo/máximo, siempre cabe),
// medidas del bloque (modo, módulo ≥ 0.25 mm, barras dentro de sus límites, "No cabe") y el documento en memoria.
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang } from '../i18n/i18n'
import { BARCODE_MIN_MODULE_MM, PT_PER_MM } from './barcodeReportPdf'
import {
  BIN_SHEET_BLOCK_GAP_PT,
  BIN_SHEET_BODY_BOTTOM,
  BIN_SHEET_BODY_TOP,
  BIN_SHEET_CODE_MAX_WIDTH_PT,
  BIN_SHEET_INNER_PT,
  BIN_SHEET_MAX_BAR_MM,
  BIN_SHEET_MAX_BAR_SIDE_MM,
  BIN_SHEET_MAX_BLOCK_PT,
  BIN_SHEET_MAX_MODULE_MM,
  BIN_SHEET_MAX_PRODUCTS,
  BIN_SHEET_MIN_BAR_MM,
  BIN_SHEET_MIN_BLOCK_PT,
  BIN_SHEET_NOTICE_MAX_LINES,
  BIN_SHEET_SIDE_TEXT_MIN_PT,
  BIN_SHEET_STACKED_MIN_BLOCK_PT,
  binSheetBlockHeight,
  binSheetBlockLayout,
  binSheetBodyHeight,
  binSheetCodeChoice,
  binSheetNotices,
  chunk,
  fitFontSize,
  planBinSheets,
  planSheetHeader,
  renderBinSheetsPdf,
  sheetModuleWidth,
  type BinSheetBin,
  type BinSheetSpec,
} from './binSheetPdf'
import { encodeCode128 } from './code128'
import { reportFileName } from './reportPdf'

beforeAll(() => setLang('es'))

const letters = (n: number) => 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.repeat(4).slice(0, n)
const products = (n: number, prefix = 'SKU') => Array.from({ length: n }, (_, i) => ({ sku: `${prefix}-${String(i + 1).padStart(2, '0')}`, name: `Producto ${i + 1}`, barcode: `75012345${String(i).padStart(4, '0')}` }))
const MIN_MODULE = BARCODE_MIN_MODULE_MM * PT_PER_MM
const MAX_MODULE = BIN_SHEET_MAX_MODULE_MM * PT_PER_MM

describe('binSheetPdf · código de cada producto', () => {
  it('módulo: el máximo (1 mm) si sobra, el que llena el ancho, null por debajo de 0.25 mm', () => {
    expect(sheetModuleWidth(46, 500)).toBeCloseTo(MAX_MODULE, 6)
    expect(sheetModuleWidth(200, 300)).toBeCloseTo(300 / 220, 6)
    expect(sheetModuleWidth(800, 300)).toBeNull()
    // el mínimo es el mismo del reporte de códigos de barras
    expect(sheetModuleWidth(200, MIN_MODULE * 220)).toBeCloseTo(MIN_MODULE, 6)
  })

  it('usa el código de barras del producto; sin él, el SKU exacto; si ninguno sirve, solo texto', () => {
    expect(binSheetCodeChoice({ sku: 'TORN-01', barcode: '7501234567890' })).toMatchObject({ status: 'ok', source: 'barcode', value: '7501234567890' })
    expect(binSheetCodeChoice({ sku: 'TORN-01', barcode: null })).toMatchObject({ status: 'ok', source: 'sku', value: 'TORN-01' })
    expect(binSheetCodeChoice({ sku: 'TORN-01', barcode: '   ' })).toMatchObject({ status: 'ok', source: 'sku', value: 'TORN-01' })
    // código de barras con un carácter que Code 128 no admite → el SKU
    expect(binSheetCodeChoice({ sku: 'TORN-01', barcode: 'CAFÉ' })).toMatchObject({ status: 'ok', source: 'sku', value: 'TORN-01' })
    // código de barras demasiado largo para la hoja → el SKU
    expect(binSheetCodeChoice({ sku: 'TORN-01', barcode: letters(70) })).toMatchObject({ status: 'ok', source: 'sku' })
    // los dos con caracteres no admitidos → solo texto, con los caracteres del primero
    expect(binSheetCodeChoice({ sku: 'AÑO-1', barcode: 'CAFÉ' })).toEqual({ status: 'unsupported', value: 'CAFÉ', chars: ['É'] })
    expect(binSheetCodeChoice({ sku: '', barcode: '' })).toEqual({ status: 'unsupported', value: '', chars: [] })
    // solo el SKU y demasiado largo → "No cabe"
    expect(binSheetCodeChoice({ sku: letters(70) })).toMatchObject({ status: 'tooWide', source: 'sku' })
    // 62 letras caben a todo el ancho (como en el reporte de códigos), 63 no
    expect(binSheetCodeChoice({ sku: letters(62) }).status).toBe('ok')
    expect(binSheetCodeChoice({ sku: letters(26) + letters(26) + letters(11) }).status).toBe('tooWide')
  })

  it('el código de la posición: módulo ≤ 0.5 mm; sin caracteres admitidos, sin símbolo', () => {
    const h = planSheetHeader('A01-R01-N1-P01')
    expect(h.symbol?.text).toBe('A01-R01-N1-P01')
    expect(h.modulePt!).toBeLessThanOrEqual(0.5 * PT_PER_MM + 1e-9)
    expect(planSheetHeader('PASILLO-Ñ')).toEqual({ symbol: null, modulePt: null, chars: ['Ñ'] })
  })
})

describe('binSheetPdf · hojas', () => {
  const bin = (code: string, n: number, extra: Partial<BinSheetBin> = {}): BinSheetBin => ({ code, products: products(n, code), ...extra })

  it('una posición por hoja, siempre; partes de 10 con "Hoja i de n" (11 → 10 + 1, 20 → 10 + 10, 21 → 10 + 10 + 1)', () => {
    expect(BIN_SHEET_MAX_PRODUCTS).toBe(10)
    const plan = planBinSheets([bin('A-1', 1), bin('A-2', 11), bin('A-3', 20), bin('A-4', 21), bin('A-5', 10)])
    expect(plan.pages.map((p) => [p.code, p.part, p.parts, p.products.length])).toEqual([
      ['A-1', 1, 1, 1],
      ['A-2', 1, 2, 10],
      ['A-2', 2, 2, 1],
      ['A-3', 1, 2, 10],
      ['A-3', 2, 2, 10],
      ['A-4', 1, 3, 10],
      ['A-4', 2, 3, 10],
      ['A-4', 3, 3, 1],
      ['A-5', 1, 1, 10],
    ])
    // cada hoja tiene productos de UNA sola posición
    for (const p of plan.pages) expect(p.products.every((x) => x.sku.startsWith(`${p.code}-`))).toBe(true)
    expect(plan.printedBins).toEqual([0, 1, 2, 3, 4])
    // el orden de los productos se respeta al partir
    expect(plan.pages[2].products[0].sku).toBe('A-2-11')
    expect(chunk([1, 2, 3], 2)).toEqual([[1, 2], [3]])
  })

  it('posiciones vacías: se omiten por defecto; con includeEmpty salen con una hoja "Sin productos"', () => {
    const bins = [bin('B-1', 0), bin('B-2', 2), bin('B-3', 0)]
    const off = planBinSheets(bins)
    expect(off.pages.map((p) => p.code)).toEqual(['B-2'])
    expect(off.omittedEmpty).toEqual(['B-1', 'B-3'])
    expect(off.printedBins).toEqual([1])
    const on = planBinSheets(bins, { includeEmpty: true })
    expect(on.pages.map((p) => [p.code, p.empty, p.parts])).toEqual([
      ['B-1', true, 1],
      ['B-2', false, 1],
      ['B-3', true, 1],
    ])
    expect(on.omittedEmpty).toEqual([])
    expect(planBinSheets([bin('B-1', 0)]).pages).toEqual([])
  })

  it('avisos del pie por hoja: productos sin código (no admitidos y "No cabe") y el código de la posición', () => {
    const plan = planBinSheets([
      { code: 'C-1', products: [{ sku: 'OK-1' }, { sku: 'AÑO', name: 'Con eñe' }, { sku: letters(70) }] },
      { code: 'C-Ñ', products: [{ sku: 'OK-2' }] },
    ])
    expect(plan.withoutCode).toBe(2)
    expect(plan.pages[0].tooWide).toEqual([letters(70)])
    expect(plan.pages[0].unsupported).toEqual([{ value: 'AÑO', chars: ['Ñ'] }])
    expect(binSheetNotices(plan.pages[0])).toEqual([
      `No caben como código de barras legible en el ancho de la hoja (1); salen en la lista sin código: ${letters(70)}.`,
      'Omitidos porque tienen caracteres que el código de barras (Code 128) no admite (1): AÑO (no admite: Ñ).',
    ])
    expect(plan.pages[1].unsupported).toEqual([{ value: 'C-Ñ', chars: ['Ñ'] }])
    expect(binSheetNotices(plan.pages[1])).toHaveLength(1)
  })
})

describe('binSheetPdf · tamaño adaptable', () => {
  const body = binSheetBodyHeight(0)

  it('cuerpo: entre el encabezado y el pie; el aviso del pie lo achica (hasta 4 renglones)', () => {
    expect(body).toBe(BIN_SHEET_BODY_BOTTOM - BIN_SHEET_BODY_TOP)
    expect(binSheetBodyHeight(2)).toBeLessThan(body)
    expect(binSheetBodyHeight(9)).toBe(binSheetBodyHeight(BIN_SHEET_NOTICE_MAX_LINES))
  })

  it('reparto: 1 producto = el máximo; 2 a 5 más grandes que 10; nunca bajo el mínimo; siempre caben en el cuerpo', () => {
    const heights = Array.from({ length: 10 }, (_, i) => binSheetBlockHeight(i + 1, body))
    expect(heights[0]).toBe(BIN_SHEET_MAX_BLOCK_PT)
    for (let n = 2; n <= 10; n++) expect(heights[n - 1]).toBeLessThanOrEqual(heights[n - 2])
    for (let n = 2; n <= 5; n++) expect(heights[n - 1]).toBeGreaterThan(heights[9] * 1.8)
    for (const lines of [0, 1, 4]) {
      const available = binSheetBodyHeight(lines)
      for (let n = 1; n <= 10; n++) {
        const h = binSheetBlockHeight(n, available)
        expect(h).toBeGreaterThanOrEqual(BIN_SHEET_MIN_BLOCK_PT)
        expect(n * h + (n - 1) * BIN_SHEET_BLOCK_GAP_PT).toBeLessThanOrEqual(available + 1e-9)
      }
    }
    expect(binSheetBlockHeight(0, body)).toBe(0)
  })

  it('bloque: 1 o 2 productos con el código debajo; 3 a 10 a la derecha; barras y módulo dentro de sus límites', () => {
    const symbol = encodeCode128('7501234567890').width
    const minBar = BIN_SHEET_MIN_BAR_MM * PT_PER_MM
    const maxBar = BIN_SHEET_MAX_BAR_MM * PT_PER_MM
    const layouts = Array.from({ length: 10 }, (_, i) => binSheetBlockLayout(binSheetBlockHeight(i + 1, body), symbol))
    expect(layouts.map((l) => l.mode)).toEqual(['stacked', 'stacked', 'side', 'side', 'side', 'side', 'side', 'side', 'side', 'side'])
    for (const l of layouts) {
      expect(l.modulePt!).toBeGreaterThanOrEqual(MIN_MODULE - 1e-9)
      expect(l.modulePt!).toBeLessThanOrEqual(MAX_MODULE + 1e-9)
      expect(l.barHeight).toBeGreaterThanOrEqual(minBar - 1e-9)
      expect(l.barHeight).toBeLessThanOrEqual((l.mode === 'stacked' ? maxBar : BIN_SHEET_MAX_BAR_SIDE_MM * PT_PER_MM) + 1e-9)
      // el código con su zona de silencio cabe en el ancho
      expect(l.codeWidth).toBeLessThanOrEqual(BIN_SHEET_INNER_PT - l.pad * 2 + 1e-9)
      if (l.mode === 'side') {
        expect(l.textWidth).toBeGreaterThanOrEqual(BIN_SHEET_SIDE_TEXT_MIN_PT - 1e-9)
        // barras + valor legible caben en el alto del bloque
        expect(l.barHeight + l.valueSize * 1.2 + 2 + l.pad * 2).toBeLessThanOrEqual(l.height + 1e-9)
      }
    }
    // con menos productos, barras y SKU más grandes (o iguales)
    for (let i = 1; i < 10; i++) {
      expect(layouts[i].barHeight).toBeLessThanOrEqual(layouts[i - 1].barHeight + 1e-9)
      expect(layouts[i].skuSize).toBeLessThanOrEqual(layouts[i - 1].skuSize + 1e-9)
    }
    expect(layouts[0].skuSize).toBeGreaterThan(layouts[9].skuSize * 2)
    expect(layouts[0].barHeight).toBeGreaterThan(layouts[9].barHeight * 2)
    expect(BIN_SHEET_STACKED_MIN_BLOCK_PT).toBeLessThanOrEqual(layouts[1].height)
    expect(BIN_SHEET_STACKED_MIN_BLOCK_PT).toBeGreaterThan(layouts[2].height)
  })

  it('un código largo que no cabe a la derecha baja a todo el ancho ("wide"); sin símbolo, el recuadro del aviso', () => {
    const h = binSheetBlockHeight(10, body)
    const long = encodeCode128(letters(50)).width
    const wide = binSheetBlockLayout(h, long)
    expect(wide.mode).toBe('wide')
    expect(wide.modulePt!).toBeGreaterThanOrEqual(MIN_MODULE - 1e-9)
    expect(wide.codeWidth).toBeLessThanOrEqual(BIN_SHEET_INNER_PT - wide.pad * 2 + 1e-9)
    // lo que cabe a todo el ancho de un bloque alto también cabe aquí (no hay "No cabe" que dependa del alto)
    expect(BIN_SHEET_INNER_PT - wide.pad * 2).toBeGreaterThanOrEqual(BIN_SHEET_CODE_MAX_WIDTH_PT)
    expect(wide.barHeight + wide.skuSize * 1.15 + 2 + wide.pad * 2).toBeLessThanOrEqual(h + 1e-9)
    const none = binSheetBlockLayout(h, null)
    expect(none.mode).toBe('side')
    expect(none.modulePt).toBeNull()
    expect(none.textWidth).toBeGreaterThanOrEqual(BIN_SHEET_SIDE_TEXT_MIN_PT)
  })

  it('letra que cabe: la mayor entre el mínimo y el máximo', () => {
    expect(fitFontSize(5, 100, 30, 10)).toBe(20)
    expect(fitFontSize(1, 100, 30, 10)).toBe(30)
    expect(fitFontSize(50, 100, 30, 10)).toBe(10)
  })
})

describe('binSheetPdf · documento', () => {
  const spec = (bins: BinSheetBin[], includeEmpty = false): BinSheetSpec => ({
    title: 'Hojas de posición',
    company: 'Advance Logistics',
    warehouse: 'ALM-01 · Almacén principal',
    locale: 'es',
    printedAt: new Date(Date.UTC(2026, 9, 3, 18, 5)),
    bins,
    includeEmpty,
  })
  /** Texto de cada página (sin comprimir). */
  const pageTexts = (doc: Awaited<ReturnType<typeof renderBinSheetsPdf>>['doc']) =>
    (doc.internal as unknown as { pages: (string[] | undefined)[] }).pages.slice(1).map((p) => (p ?? []).join('\n'))

  it('una hoja carta vertical por posición (y por cada 10 productos), encabezado, detalle, "Hoja 2 de 2" y pie', async () => {
    const bins: BinSheetBin[] = [
      { code: 'A01-R01-N1-P01', details: [{ label: 'Zona', value: 'RSV' }, { label: 'Pasillo', value: '01' }], products: products(11, 'X') },
      { code: 'GENERAL', products: [{ sku: 'SOLO-1', name: 'Un producto con un nombre bastante largo que no cabe en el ancho de la hoja y se recorta con puntos suspensivos al final '.repeat(4), barcode: '' }] },
      { code: 'B-02', products: [] },
    ]
    const { doc, plan } = await renderBinSheetsPdf(spec(bins), { compress: false })
    expect(doc.getNumberOfPages()).toBe(3)
    expect(plan.pages).toHaveLength(3)
    const text = doc.output()
    expect(text.startsWith('%PDF-')).toBe(true)
    expect(text).toContain('/MediaBox [0 0 612. 792.]')
    const pages = pageTexts(doc)
    expect(pages[0]).toContain('(A01-R01-N1-P01)')
    expect(pages[0]).toContain('(Hoja 1 de 2)')
    expect(pages[0]).toContain('(Zona RSV  ·  Pasillo 01)')
    expect(pages[1]).toContain('(A01-R01-N1-P01)')
    expect(pages[1]).toContain('(Hoja 2 de 2)')
    expect(pages[1]).toContain('(X-11)')
    expect(pages[1]).not.toContain('(X-10)')
    expect(pages[2]).toContain('(GENERAL)')
    expect(pages[2]).not.toContain('Hoja 1 de 1')
    // el producto sin código de barras lleva el SKU en el símbolo (texto legible debajo, en courier)
    expect(pages[2].match(/\(SOLO-1\) Tj/g)?.length).toBe(2)
    expect(pages[2]).toContain('...')
    for (const p of pages) {
      // fecha y hora con los formatos de la compañía (Puerto Rico) y su zona: 18:05 UTC = 2:05 p. m.
      expect(p).toMatch(/Impresa el 10\/03\/2026 2:05\sp\.\sm\./)
      expect(p).toContain('Almac')
    }
    // la posición vacía no sale sin el interruptor
    expect(text).not.toContain('(B-02)')
    expect(reportFileName(spec(bins), spec(bins).printedAt)).toBe('hojas-de-posicion-advance-logistics-2026-10-03.pdf')
  })

  it('con "Incluir posiciones vacías": la hoja "Sin productos"; aviso al pie si un producto no tiene código', async () => {
    const { doc } = await renderBinSheetsPdf(spec([{ code: 'B-02', products: [] }, { code: 'C-1', products: [{ sku: 'AÑO', name: 'Eñe' }] }], true), { compress: false })
    expect(doc.getNumberOfPages()).toBe(2)
    const [empty, withNotice] = pageTexts(doc)
    expect(empty).toContain('(B-02)')
    expect(empty).toContain('(Sin productos)')
    expect(withNotice).toContain('Sin c')
    expect(withNotice).toContain('Omitidos porque tienen caracteres')
  })

  it('cada hoja dibuja tantas barras como su código (vectoriales): 1 producto = barras de la posición + las del producto', async () => {
    const { doc } = await renderBinSheetsPdf(spec([{ code: 'R1', products: [{ sku: 'P-1', barcode: '12345678' }] }]), { compress: false })
    const page = pageTexts(doc)[0]
    const rects = (page.match(/ re\nf/g) ?? []).length
    expect(rects).toBe(encodeCode128('R1').bars.length + encodeCode128('12345678').bars.length)
  })
})
