// Lote F16 — etiquetas de posición: tamaños exactos de página (4×2, 4×4, 4×6 en pt) y el giro de la página, margen de
// seguridad, código de barras (módulo entre 0.25 mm y el máximo, a todo el ancho con su zona de silencio; "No cabe" y no
// admitidos con el aviso de siempre), ajuste del código en texto (llena el ancho, mínimo legible, 2 renglones si no cabe,
// barras nunca bajo el mínimo) y el documento en memoria (una página por posición, barras vectoriales exactas, datos).
import { beforeAll, describe, expect, it } from 'vitest'
import { setLang } from '../i18n/i18n'
import { BARCODE_MIN_MODULE_MM, PT_PER_MM } from './barcodeReportPdf'
import {
  BIN_LABEL_CODE_MAX_PT,
  BIN_LABEL_CODE_MIN_PT,
  BIN_LABEL_MAX_MODULE_MM,
  BIN_LABEL_MIN_BAR_MM,
  BIN_LABEL_SAFE_MARGIN_PT,
  BIN_LABEL_SIZE_KEYS,
  BIN_LABEL_SIZES,
  binLabelCode,
  binLabelDetailsText,
  binLabelFileName,
  binLabelInner,
  binLabelLayout,
  binLabelNotices,
  binLabelPage,
  planBinLabels,
  renderBinLabelsPdf,
  type BinLabelSize,
  type BinLabelSpec,
} from './binLabelPdf'
import { CODE128_QUIET_ZONE, encodeCode128 } from './code128'

beforeAll(() => setLang('es'))

const MIN_MODULE = BARCODE_MIN_MODULE_MM * PT_PER_MM
const MAX_MODULE = BIN_LABEL_MAX_MODULE_MM * PT_PER_MM
const MIN_BAR = BIN_LABEL_MIN_BAR_MM * PT_PER_MM
/** Un código de `n` letras (subconjunto B: nada de corridas de dígitos que lo acorten). */
const letters = (n: number) => 'ABCDEFGHJKLMNPQRSTUVWXYZ'.repeat(5).slice(0, n)

describe('binLabelPdf · tamaños', () => {
  it('página del tamaño exacto de la etiqueta (pt): 4×2 = 288×144, 4×4 = 288×288, 4×6 = 288×432; girar = la misma página con /Rotate 90', () => {
    expect(binLabelPage('4x2')).toEqual({ width: 288, height: 144, rotate: 0 })
    expect(binLabelPage('4x4')).toEqual({ width: 288, height: 288, rotate: 0 })
    expect(binLabelPage('4x6')).toEqual({ width: 288, height: 432, rotate: 0 })
    expect(binLabelPage('4x2', 'rotate')).toEqual({ width: 288, height: 144, rotate: 90 })
    expect(binLabelPage('4x6', 'rotate')).toEqual({ width: 288, height: 432, rotate: 90 })
    expect(BIN_LABEL_SIZE_KEYS).toEqual(['4x2', '4x4', '4x6'])
    expect(BIN_LABEL_SIZES['4x2'].shape).toBe('landscape')
    expect(BIN_LABEL_SIZES['4x4'].shape).toBe('square')
    expect(BIN_LABEL_SIZES['4x6'].shape).toBe('portrait')
  })

  it('margen de seguridad de 0.1 in (7.2 pt) a cada lado', () => {
    expect(BIN_LABEL_SAFE_MARGIN_PT).toBeCloseTo(7.2, 9)
    expect(binLabelInner('4x2')).toEqual({ width: 288 - 14.4, height: 144 - 14.4 })
    expect(binLabelInner('4x6').height).toBeCloseTo(432 - 14.4, 9)
  })
})

describe('binLabelPdf · código de barras', () => {
  const width = binLabelInner('4x2').width

  it('a todo el ancho con su zona de silencio, entre 0.25 mm y el máximo; "No cabe" bajo 0.25 mm', () => {
    // corto: el módulo máximo (no crece más)
    const short = binLabelCode('A1', width)
    expect(short.status).toBe('ok')
    if (short.status === 'ok') expect(short.modulePt).toBeCloseTo(MAX_MODULE, 6)
    // mediano: llena el ancho exacto (símbolo + 10 módulos de silencio a cada lado)
    const mid = binLabelCode('A-01-02-03-RACK', width)
    expect(mid.status).toBe('ok')
    if (mid.status === 'ok') {
      expect((mid.symbol.width + 2 * CODE128_QUIET_ZONE) * mid.modulePt).toBeCloseTo(width, 6)
      expect(mid.modulePt).toBeLessThan(MAX_MODULE)
      expect(mid.modulePt).toBeGreaterThanOrEqual(MIN_MODULE)
    }
    // el más largo que cabe y el primero que no: la frontera es el mínimo de 0.25 mm (el de los demás reportes)
    let n = 10
    while (binLabelCode(letters(n + 1), width).status === 'ok') n++
    const last = binLabelCode(letters(n), width)
    expect(last.status === 'ok' && last.modulePt).toBeGreaterThanOrEqual(MIN_MODULE - 1e-9)
    const first = binLabelCode(letters(n + 1), width)
    expect(first.status).toBe('tooWide')
    expect((encodeCode128(letters(n + 1)).width + 2 * CODE128_QUIET_ZONE) * MIN_MODULE).toBeGreaterThan(width)
  })

  it('caracteres que Code 128 no admite, o vacío: sin símbolo (solo texto)', () => {
    expect(binLabelCode('AÑO-1', width)).toEqual({ status: 'unsupported', chars: ['Ñ'] })
    expect(binLabelCode('', width).status).toBe('unsupported')
  })

  it('plan: una etiqueta por posición en el orden recibido; avisos de "No cabe" y no admitidos con los textos de siempre', () => {
    const long = letters(60)
    const plan = planBinLabels([{ code: 'A-01' }, { code: long }, { code: 'AÑO' }, { code: 'B-02', details: [{ label: 'Zona', value: 'RSV' }], key: 7 }], '4x4')
    expect(plan.labels.map((l) => l.code)).toEqual(['A-01', long, 'AÑO', 'B-02'])
    expect(plan.labels.map((l) => l.barcode.status)).toEqual(['ok', 'tooWide', 'unsupported', 'ok'])
    expect(plan.labels[3]).toMatchObject({ bin: 3, key: 7, details: [{ label: 'Zona', value: 'RSV' }] })
    expect(plan.tooWide).toEqual([long])
    expect(plan.unsupported).toEqual([{ value: 'AÑO', chars: ['Ñ'] }])
    expect(plan.withoutCode).toBe(2)
    const notices = binLabelNotices(plan)
    expect(notices[0]).toMatch(/^No caben como código de barras legible/)
    expect(notices[0]).toContain(long)
    expect(notices[1]).toMatch(/^Omitidos porque tienen caracteres que el código de barras \(Code 128\) no admite \(1\)/)
    // el ancho útil es el mismo en los tres tamaños (4 in): lo que cabe en uno cabe en los otros
    expect(planBinLabels([{ code: long }], '4x2').tooWide).toEqual([long])
    expect(planBinLabels([{ code: 'A-01' }], '4x6').withoutCode).toBe(0)
  })
})

describe('binLabelPdf · acomodo', () => {
  // ancho de "A-01-02-03" a 1 pt en Helvetica negrita (≈ 5.9): lo mide jsPDF en el dibujo
  const unit = 5.9
  const sym = encodeCode128('A-01-02-03').width

  it('todo dentro del margen de seguridad: barras arriba, código debajo, datos al final, sin encimarse', () => {
    for (const size of BIN_LABEL_SIZE_KEYS) {
      const l = binLabelLayout(size, { symbolModules: sym, codeUnitWidth: unit, detailLines: 2 })
      const page = binLabelPage(size)
      expect(l.barTop).toBeCloseTo(BIN_LABEL_SAFE_MARGIN_PT, 9)
      expect(l.barLeft).toBeGreaterThanOrEqual(BIN_LABEL_SAFE_MARGIN_PT - 1e-9)
      expect(l.barLeft + l.symbolWidth).toBeLessThanOrEqual(page.width - BIN_LABEL_SAFE_MARGIN_PT + 1e-9)
      // centrado
      expect(l.barLeft - BIN_LABEL_SAFE_MARGIN_PT).toBeCloseTo(page.width - BIN_LABEL_SAFE_MARGIN_PT - (l.barLeft + l.symbolWidth), 6)
      expect(l.codeTop).toBeGreaterThan(l.barTop + l.barHeight)
      expect(l.detailTop).toBeGreaterThan(l.codeTop + l.codeSize)
      const bottom = l.detailTop + l.detailLines * l.detailSize * 1.2
      expect(bottom).toBeLessThanOrEqual(page.height - BIN_LABEL_SAFE_MARGIN_PT + 1e-6)
      expect(l.barHeight).toBeGreaterThanOrEqual(MIN_BAR - 1e-9)
    }
  })

  it('crece con la etiqueta: barras y letra de 4×2 < 4×4 < 4×6 (o igual si el ancho ya manda)', () => {
    const [a, b, c] = (['4x2', '4x4', '4x6'] as BinLabelSize[]).map((s) => binLabelLayout(s, { symbolModules: sym, codeUnitWidth: unit, detailLines: 1 }))
    expect(a.barHeight).toBeLessThan(b.barHeight)
    expect(b.barHeight).toBeLessThan(c.barHeight)
    expect(a.codeSize).toBeLessThanOrEqual(b.codeSize)
    expect(b.codeSize).toBeLessThanOrEqual(c.codeSize)
    expect(a.detailSize).toBeLessThanOrEqual(c.detailSize)
    // el ancho no cambia (4 in): el módulo es el mismo en los tres
    expect(a.modulePt).toBeCloseTo(c.modulePt ?? 0, 9)
  })

  it('código en texto: llena el ancho (sin cortarse) entre el mínimo legible y el máximo; si ni al mínimo cabe, 2 renglones', () => {
    const iw = binLabelInner('4x6').width
    const fits = binLabelLayout('4x6', { symbolModules: sym, codeUnitWidth: unit, detailLines: 0 })
    expect(fits.codeLines).toBe(1)
    expect(fits.codeSize * unit).toBeCloseTo(iw, 6)
    // muy corto: el máximo del tamaño (proporcional a su alto), nunca más de BIN_LABEL_CODE_MAX_PT
    const tiny = binLabelLayout('4x6', { symbolModules: sym, codeUnitWidth: 1, detailLines: 0 })
    expect(tiny.codeSize).toBeLessThanOrEqual(BIN_LABEL_CODE_MAX_PT)
    expect(tiny.codeSize).toBeLessThan(iw)
    const small = binLabelLayout('4x2', { symbolModules: sym, codeUnitWidth: 1, detailLines: 0 })
    expect(small.codeSize).toBeLessThan(tiny.codeSize)
    // larguísimo: la letra mínima en 2 renglones
    const long = binLabelLayout('4x2', { symbolModules: null, codeUnitWidth: 60, detailLines: 2 })
    expect(long.codeLines).toBe(2)
    expect(long.codeSize).toBe(BIN_LABEL_CODE_MIN_PT)
    expect(long.barHeight).toBeGreaterThanOrEqual(MIN_BAR - 1e-9)
  })

  it('las barras nunca bajan del mínimo: la letra del código se achica antes (4×2 con 2 renglones de datos)', () => {
    const l = binLabelLayout('4x2', { symbolModules: sym, codeUnitWidth: 0.5, detailLines: 2 })
    expect(l.barHeight).toBeGreaterThanOrEqual(MIN_BAR - 1e-9)
    expect(l.codeSize).toBeGreaterThanOrEqual(BIN_LABEL_CODE_MIN_PT)
  })

  it('sin símbolo: módulo null y ancho 0 (el motivo va en el lugar de las barras)', () => {
    const l = binLabelLayout('4x4', { symbolModules: null, codeUnitWidth: unit, detailLines: 1 })
    expect(l.modulePt).toBeNull()
    expect(l.symbolWidth).toBe(0)
  })

  it('datos de la ubicación: "Etiqueta valor" separados por " · " (vacíos fuera); nombre del archivo con el tamaño', () => {
    expect(binLabelDetailsText([{ label: 'Almacén', value: 'ALM-01' }, { label: 'Zona', value: 'RSV' }, { label: '', value: '' }])).toBe('Almacén ALM-01 · Zona RSV')
    expect(binLabelFileName({ title: 'Etiquetas de posición', company: 'Advance Logistics', size: '4x6', generatedAt: new Date(Date.UTC(2026, 9, 4, 15)) })).toBe(
      'etiquetas-de-posicion-4x6-advance-logistics-2026-10-04.pdf',
    )
  })
})

describe('binLabelPdf · documento', () => {
  const spec = (size: BinLabelSize, bins: BinLabelSpec['bins'], orientation: BinLabelSpec['orientation'] = 'auto'): BinLabelSpec => ({
    title: 'Etiquetas de posición',
    company: 'Advance Logistics',
    locale: 'es',
    size,
    orientation,
    bins,
  })
  const pageTexts = (doc: Awaited<ReturnType<typeof renderBinLabelsPdf>>['doc']) =>
    (doc.internal as unknown as { pages: (string[] | undefined)[] }).pages.slice(1).map((p) => (p ?? []).join('\n'))
  const mediaBoxes = (pdf: string) => [...pdf.matchAll(/\/MediaBox \[([^\]]+)\]/g)].map((m) => m[1].split(/\s+/).map(Number))

  it.each(['4x2', '4x4', '4x6'] as const)('%s: una página por posición, del tamaño exacto, sin giro', async (size) => {
    const bins = [{ code: 'A-01' }, { code: 'A-02' }, { code: 'B-10' }]
    const { doc, plan } = await renderBinLabelsPdf(spec(size, bins), { compress: false })
    expect(doc.getNumberOfPages()).toBe(3)
    expect(plan.labels).toHaveLength(3)
    const pdf = doc.output()
    const { width, height } = binLabelPage(size)
    expect(mediaBoxes(pdf)).toEqual([
      [0, 0, width, height],
      [0, 0, width, height],
      [0, 0, width, height],
    ])
    expect(pdf).not.toContain('/Rotate')
  })

  it('girar 90°: la misma página (MediaBox) con /Rotate 90 en cada una; el contenido no cambia', async () => {
    const bins = [{ code: 'A-01' }, { code: 'A-02' }]
    const { doc } = await renderBinLabelsPdf(spec('4x2', bins, 'rotate'), { compress: false })
    const pdf = doc.output()
    expect(mediaBoxes(pdf)).toEqual([
      [0, 0, 288, 144],
      [0, 0, 288, 144],
    ])
    expect(pdf.match(/\/Rotate 90/g)).toHaveLength(2)
    const { doc: plain } = await renderBinLabelsPdf(spec('4x2', bins), { compress: false })
    expect(pageTexts(doc)).toEqual(pageTexts(plain))
  })

  it('cada etiqueta: tantas barras como su código (vectoriales), el código en texto y los datos de la ubicación', async () => {
    const details = [
      { label: 'Almacén', value: 'ALM-01' },
      { label: 'Zona', value: 'RSV' },
      { label: 'Pasillo', value: '01' },
    ]
    const { doc } = await renderBinLabelsPdf(spec('4x4', [{ code: 'A-01-02', details }, { code: 'R1' }]), { compress: false })
    const [first, second] = pageTexts(doc)
    expect((first.match(/ re\nf/g) ?? []).length).toBe(encodeCode128('A-01-02').bars.length)
    expect(first).toContain('(A-01-02) Tj')
    expect(first).toContain('(Almacén ALM-01 · Zona RSV · Pasillo 01) Tj')
    expect((second.match(/ re\nf/g) ?? []).length).toBe(encodeCode128('R1').bars.length)
    expect(second).not.toContain('Almac')
    // todo en negro (la térmica no imprime grises)
    for (const p of [first, second]) expect(p).not.toMatch(/\b0\.\d+ g\b/)
  })

  it('sin código de barras: sin barras, el motivo y el código en texto (no admitido y "No cabe")', async () => {
    const long = letters(60)
    const { doc, plan } = await renderBinLabelsPdf(spec('4x2', [{ code: 'AÑO-1' }, { code: long }]), { compress: false })
    expect(doc.getNumberOfPages()).toBe(2)
    expect(plan.withoutCode).toBe(2)
    const [unsupported, wide] = pageTexts(doc)
    expect(unsupported).not.toMatch(/ re\nf/)
    expect(unsupported).toContain('Sin código de barras: Code 128 no')
    expect(unsupported).toContain('(AÑO-1) Tj')
    expect(wide).not.toMatch(/ re\nf/)
    expect(wide).toContain('No cabe')
    expect(wide).toContain(long.slice(0, 20))
  })
})
