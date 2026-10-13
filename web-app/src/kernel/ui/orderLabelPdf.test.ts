// Lote F-G1 — label en PDF: una página del tamaño EXACTO de la etiqueta por paquete, giro con /Rotate 90, texto y barras en negro, y el
// aviso «no cabe» cuando ni achicado entra.
import { describe, expect, it } from 'vitest'
import { BUILT_IN_LAYOUT, LABEL_KEYS, sampleLabelData, type LabelLayout, type LabelTexts } from './orderLabelLayout'
import { LABEL_MIN_TEXT_PT, placeLabel, renderOrderLabelsPdf } from './orderLabelPdf'
import { planLabel } from './orderLabelLayout'

const TEXTS: LabelTexts = { from: 'De:', to: 'A:', waybill: 'Waybill', invoice: 'Factura', cod: 'COD', packageOf: 'Paquete {i} de {n}' }
const DATA = sampleLabelData('Advance Logistics', '$125.82')
const mediaBoxes = (pdf: string) => [...pdf.matchAll(/\/MediaBox \[([^\]]+)\]/g)].map((m) => m[1].split(/\s+/).map(Number))
/** Medida fija: 0.5 pt por carácter por punto de letra. */
const measure = { split: (text: string, pt: number, _bold: boolean, width: number) => {
  const per = Math.max(1, Math.floor(width / (pt * 0.5)))
  const out: string[] = []
  for (let i = 0; i < text.length; i += per) out.push(text.slice(i, i + per))
  return out.length ? out : ['']
} }

describe('renderOrderLabelsPdf', () => {
  it('4×6: una página de 288×432 pt por paquete, con el número del paquete y «Paquete i de N»', async () => {
    const labels = [1, 2, 3].map((i) => ({ ...DATA, packageNumber: `PQT-0001${i}`, packageIndex: i }))
    const { doc, pages, notFitting } = await renderOrderLabelsPdf({ title: 'Labels', media: '4X6', layout: BUILT_IN_LAYOUT, texts: TEXTS, labels }, { compress: false })
    expect(pages).toBe(3)
    expect(doc.getNumberOfPages()).toBe(3)
    expect(notFitting).toBe(0)
    const pdf = doc.output()
    expect(mediaBoxes(pdf)).toEqual([[0, 0, 288, 432], [0, 0, 288, 432], [0, 0, 288, 432]])
    expect(pdf).toContain('(PQT-00012 · Paquete 2 de 3) Tj')
    expect(pdf).not.toContain('/Rotate')
  })

  it('4×2 girada: misma página con /Rotate 90; 4×4 cuadrada', async () => {
    const r = await renderOrderLabelsPdf({ title: 'L', media: '4X2', rotate: true, layout: BUILT_IN_LAYOUT, texts: TEXTS, labels: [DATA] }, { compress: false })
    const pdf = r.doc.output()
    expect(mediaBoxes(pdf)).toEqual([[0, 0, 288, 144]])
    expect(pdf).toContain('/Rotate 90')
    const sq = await renderOrderLabelsPdf({ title: 'L', media: '4X4', layout: BUILT_IN_LAYOUT, texts: TEXTS, labels: [DATA] }, { compress: false })
    expect(mediaBoxes(sq.doc.output())).toEqual([[0, 0, 288, 288]])
  })

  it('todo grande en 4×2 no cabe ni achicado: se avisa (y se dibuja igual)', async () => {
    const big = Object.fromEntries(LABEL_KEYS.map((k) => [k, { ...BUILT_IN_LAYOUT[k], on: true, size: 'lg', asBarcode: k === 'waybill' || k === 'invoice' }])) as LabelLayout
    const r = await renderOrderLabelsPdf({ title: 'L', media: '4X2', layout: big, texts: TEXTS, labels: [DATA] }, { compress: false })
    expect(r.notFitting).toBe(1)
    expect(r.doc.getNumberOfPages()).toBe(1)
  })
})

describe('placeLabel', () => {
  it('arriba pegado arriba, abajo pegado abajo, barras a todo el ancho; letra nunca menor de 6 pt', () => {
    const p = placeLabel(planLabel(BUILT_IN_LAYOUT, DATA, TEXTS), '4X6', measure)
    expect(p.fits).toBe(true)
    expect(p.scale).toBe(1)
    expect(Math.min(...p.texts.map((t) => t.y))).toBeCloseTo(7.2) // margen de seguridad de 0.1 in
    expect(p.barcodes).toHaveLength(1)
    expect(p.barcodes[0].modulePt).not.toBeNull()
    expect(p.texts.every((t) => t.fontPt >= LABEL_MIN_TEXT_PT)).toBe(true)
    // COD (abajo der.) alineado a la derecha y en caja negra
    const cod = p.texts.find((t) => t.inverse)!
    expect(cod.align).toBe('right')
    expect(cod.y).toBeGreaterThan(300)
  })

  it('si no cabe a tamaño normal, achica en vez de cortar', () => {
    const big = Object.fromEntries(LABEL_KEYS.map((k) => [k, { ...BUILT_IN_LAYOUT[k], on: true, size: 'lg' }])) as LabelLayout
    const p = placeLabel(planLabel(big, DATA, TEXTS), '4X4', measure)
    expect(p.scale).toBeLessThan(1)
  })
})
