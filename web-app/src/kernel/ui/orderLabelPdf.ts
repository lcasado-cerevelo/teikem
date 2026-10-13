// Lote F-G1 — label de envío en PDF vectorial (una página del tamaño EXACTO de la etiqueta por paquete), para imprimir directo con
// QZ Tray o con el diálogo del navegador. Reusa la página, el margen de seguridad y el módulo de barras de las etiquetas de posición
// (F16: `binLabelPage`, `BIN_LABEL_SAFE_MARGIN_PT`, `sheetModuleWidth`) y el codificador Code 128 (F14).
// Pinta el plan de `orderLabelLayout.ts`: el renglón de arriba pegado arriba, el de abajo pegado abajo y el del medio centrado en lo
// que queda; en cada renglón las tres celdas de texto (izquierda, centro, derecha) y, debajo, sus códigos de barras a TODO el ancho
// con su zona de silencio y el texto legible. Todo en NEGRO puro, letra mínima 6 pt; el COD en caja negra con letra blanca.
// Si el contenido no cabe, se achica proporcionalmente (texto hasta 6 pt, barras hasta 8 mm); si ni así cabe se avisa «El label no
// cabe en …» y se dibuja igual (lo que sobra se corta).
import type { jsPDF } from 'jspdf'
import { BIN_LABEL_SAFE_MARGIN_PT, binLabelPage, type BinLabelSize } from './binLabelPdf'
import { PT_PER_MM } from './barcodeReportPdf'
import { sheetModuleWidth } from './binSheetPdf'
import { CODE128_QUIET_ZONE, encodeCode128 } from './code128'
import { pdfSafeText } from './exportTable'
import { planLabel, type LabelLayout, type LabelRowPlan, type LabelSize, type LabelTexts, type OrderLabelData } from './orderLabelLayout'

/** Tamaños de etiqueta de una impresora (dbo.Printer.MediaSize). */
export const LABEL_MEDIA = ['4X6', '4X4', '4X2'] as const
export type LabelMedia = (typeof LABEL_MEDIA)[number]

/** Letra de cada tamaño del constructor (pt) y alto de las barras (pt). */
export const LABEL_TEXT_PT: Record<LabelSize, number> = { sm: 8, md: 11, lg: 15 }
export const LABEL_BAR_PT: Record<LabelSize, number> = { sm: 30, md: 44, lg: 62 }
export const LABEL_MIN_TEXT_PT = 6
export const LABEL_MIN_BAR_PT = 8 * PT_PER_MM
export const LABEL_CAPTION_PT = 8
export const LABEL_MAX_MODULE_MM = 1
const LINE = 1.2
const GAP = 4
const PAD_INVERSE = 2

export const toBinLabelSize = (m: string | null | undefined): BinLabelSize => (m === '4X4' ? '4x4' : m === '4X2' ? '4x2' : '4x6')

/** Medidas de texto que el plan necesita (jsPDF en el dibujo; una función fija en pruebas). */
export interface LabelMeasure {
  /** Renglones de `text` con la letra y ancho dados. */
  split: (text: string, fontPt: number, bold: boolean, width: number) => string[]
}

export interface PlacedText {
  x: number
  align: 'left' | 'center' | 'right'
  y: number
  fontPt: number
  bold: boolean
  inverse: boolean
  lines: string[]
}
export interface PlacedBarcode {
  value: string
  caption: string
  top: number
  barHeight: number
  captionPt: number
  modulePt: number | null
}
export interface LabelPlacement {
  page: { width: number; height: number }
  texts: PlacedText[]
  barcodes: PlacedBarcode[]
  scale: number
  fits: boolean
  /** Códigos que no caben a módulo legible o con caracteres que Code 128 no admite (salen en texto). */
  withoutBars: string[]
}

/**
 * Coloca el plan en la página (puro; las medidas de texto llegan en `measure`). Devuelve la escala aplicada (1 = sin achicar) y si
 * cupo. Columnas de un tercio del ancho útil; las barras a todo el ancho útil.
 */
export function placeLabel(plan: LabelRowPlan[], media: LabelMedia, measure: LabelMeasure): LabelPlacement {
  const page = binLabelPage(toBinLabelSize(media))
  const m = BIN_LABEL_SAFE_MARGIN_PT
  const iw = page.width - 2 * m
  const ih = page.height - 2 * m
  const colW = iw / 3

  const rowHeight = (row: LabelRowPlan, s: number) => {
    const cellH = (['l', 'c', 'r'] as const).map((c) =>
      row.cells[c].reduce((h, it, i) => {
        const pt = Math.max(LABEL_MIN_TEXT_PT, LABEL_TEXT_PT[it.size] * s)
        const n = it.lines.flatMap((l) => measure.split(l, pt, it.bold, colW - (it.inverse ? 2 * PAD_INVERSE : 0))).length
        return h + (i > 0 ? GAP * s : 0) + n * pt * LINE + (it.inverse ? 2 * PAD_INVERSE : 0)
      }, 0),
    )
    const text = Math.max(...cellH)
    const bars = row.barcodes.reduce((h, b) => h + GAP * s + Math.max(LABEL_MIN_BAR_PT, LABEL_BAR_PT[b.size] * s) + Math.max(LABEL_MIN_TEXT_PT, LABEL_CAPTION_PT * s) * LINE, 0)
    return { text, total: text + bars }
  }
  const totalAt = (s: number) => {
    const hs = plan.map((r) => rowHeight(r, s).total)
    const used = hs.filter((h) => h > 0).length
    return hs.reduce((a, b) => a + b, 0) + Math.max(0, used - 1) * GAP * 2 * s
  }

  let scale = 1
  if (totalAt(1) > ih) {
    // búsqueda binaria de la mayor escala que cabe (los mínimos de letra y barras pueden impedirlo)
    let lo = 0.2
    let hi = 1
    for (let i = 0; i < 20; i++) {
      const mid = (lo + hi) / 2
      if (totalAt(mid) <= ih) lo = mid
      else hi = mid
    }
    scale = lo
  }
  const fits = totalAt(scale) <= ih + 0.01

  const texts: PlacedText[] = []
  const barcodes: PlacedBarcode[] = []
  const withoutBars: string[] = []
  const heights = plan.map((r) => rowHeight(r, scale))
  const gapRow = GAP * 2 * scale
  const top = m
  const bottomEdge = page.height - m
  const tH = heights[0].total
  const bH = heights[2].total
  const mH = heights[1].total
  const tTop = top
  const bTop = bottomEdge - bH
  const midSpaceTop = tTop + tH + (tH > 0 ? gapRow : 0)
  const midSpaceBottom = bTop - (bH > 0 ? gapRow : 0)
  const mTop = Math.max(midSpaceTop, midSpaceTop + (midSpaceBottom - midSpaceTop - mH) / 2)
  const rowTops = [tTop, mTop, bTop]

  plan.forEach((row, ri) => {
    const y0 = rowTops[ri]
    ;(['l', 'c', 'r'] as const).forEach((c, ci) => {
      let y = y0
      row.cells[c].forEach((it, i) => {
        const pt = Math.max(LABEL_MIN_TEXT_PT, LABEL_TEXT_PT[it.size] * scale)
        if (i > 0) y += GAP * scale
        const w = colW - (it.inverse ? 2 * PAD_INVERSE : 0)
        const lines = it.lines.flatMap((l) => measure.split(l, pt, it.bold, w))
        const align = c === 'l' ? 'left' : c === 'r' ? 'right' : 'center'
        const x = c === 'l' ? m + ci * colW + (it.inverse ? PAD_INVERSE : 0) : c === 'r' ? m + (ci + 1) * colW - (it.inverse ? PAD_INVERSE : 0) : m + ci * colW + colW / 2
        texts.push({ x, align, y: y + (it.inverse ? PAD_INVERSE : 0), fontPt: pt, bold: it.bold, inverse: it.inverse, lines })
        y += lines.length * pt * LINE + (it.inverse ? 2 * PAD_INVERSE : 0)
      })
    })
    let by = y0 + heights[ri].text
    for (const b of row.barcodes) {
      by += GAP * scale
      const barHeight = Math.max(LABEL_MIN_BAR_PT, LABEL_BAR_PT[b.size] * scale)
      const captionPt = Math.max(LABEL_MIN_TEXT_PT, LABEL_CAPTION_PT * scale)
      let modulePt: number | null = null
      if (b.encodable) modulePt = sheetModuleWidth(encodeCode128(b.value).width, iw, LABEL_MAX_MODULE_MM)
      if (modulePt === null) withoutBars.push(b.value)
      barcodes.push({ value: b.value, caption: b.caption, top: by, barHeight, captionPt, modulePt })
      by += barHeight + captionPt * LINE
    }
  })
  return { page: { width: page.width, height: page.height }, texts, barcodes, scale, fits, withoutBars }
}

export interface OrderLabelSpec {
  title: string
  media: LabelMedia
  /** true = la página con `/Rotate 90` (impresora que alimenta de lado); con QZ Tray se gira en la impresión. */
  rotate?: boolean
  layout: LabelLayout
  texts: LabelTexts
  labels: readonly OrderLabelData[]
}

export interface OrderLabelsResult {
  doc: jsPDF
  pages: number
  /** Etiquetas que no cupieron ni achicadas. */
  notFitting: number
  /** Valores sin barras (no caben o no se admiten): salen en texto. */
  withoutBars: string[]
}

type JsPdfInternals = { internal: { write: (s: string) => void; events: { subscribe: (topic: string, cb: () => void) => unknown } } }

function drawPlacement(doc: jsPDF, p: LabelPlacement) {
  const m = BIN_LABEL_SAFE_MARGIN_PT
  const iw = p.page.width - 2 * m
  doc.setTextColor(0, 0, 0)
  for (const t of p.texts) {
    doc.setFont('helvetica', t.bold ? 'bold' : 'normal')
    doc.setFontSize(t.fontPt)
    if (t.inverse) {
      const w = Math.max(...t.lines.map((l) => doc.getTextWidth(l))) + 2 * PAD_INVERSE
      const h = t.lines.length * t.fontPt * LINE + 2 * PAD_INVERSE
      const left = t.align === 'left' ? t.x - PAD_INVERSE : t.align === 'right' ? t.x - w + PAD_INVERSE : t.x - w / 2
      doc.setFillColor(0, 0, 0)
      doc.rect(left, t.y - PAD_INVERSE, w, h, 'F')
      doc.setTextColor(255, 255, 255)
    }
    t.lines.forEach((l, i) => doc.text(l, t.x, t.y + t.fontPt * 0.86 + i * t.fontPt * LINE, { align: t.align }))
    doc.setTextColor(0, 0, 0)
  }
  for (const b of p.barcodes) {
    if (b.modulePt !== null) {
      const symbol = encodeCode128(b.value)
      const total = (symbol.width + 2 * CODE128_QUIET_ZONE) * b.modulePt
      const x0 = m + (iw - total) / 2 + CODE128_QUIET_ZONE * b.modulePt
      doc.setFillColor(0, 0, 0)
      for (const bar of symbol.bars) doc.rect(x0 + bar.x * b.modulePt, b.top, bar.width * b.modulePt, b.barHeight, 'F')
    } else {
      // sin barras posibles: el valor grande en su lugar
      doc.setFont('helvetica', 'bold')
      doc.setFontSize(Math.min(18, b.barHeight / 2))
      doc.text(pdfSafeText(b.value), m + iw / 2, b.top + b.barHeight / 2, { align: 'center', baseline: 'middle' })
    }
    doc.setFont('helvetica', 'normal')
    doc.setFontSize(b.captionPt)
    doc.text(pdfSafeText(b.caption), m + iw / 2, b.top + b.barHeight + b.captionPt * 0.95, { align: 'center' })
  }
}

/** Arma el PDF de los labels (uno por paquete, en el orden recibido) en memoria. */
export async function renderOrderLabelsPdf(spec: OrderLabelSpec, options: { compress?: boolean } = {}): Promise<OrderLabelsResult> {
  const { jsPDF: JsPdf } = await import('jspdf')
  const page = binLabelPage(toBinLabelSize(spec.media), spec.rotate ? 'rotate' : 'auto')
  const orientation = page.width > page.height ? 'landscape' : 'portrait'
  const format: [number, number] = [page.width, page.height]
  const doc = new JsPdf({ orientation, unit: 'pt', format, compress: options.compress ?? true })
  doc.setProperties({ title: pdfSafeText(spec.title), creator: 'Teikem' })
  if (page.rotate) {
    const internals = (doc as unknown as JsPdfInternals).internal
    internals.events.subscribe('putPage', () => internals.write(`/Rotate ${page.rotate}`))
  }
  const measure: LabelMeasure = {
    split: (text, fontPt, bold, width) => {
      doc.setFont('helvetica', bold ? 'bold' : 'normal')
      doc.setFontSize(fontPt)
      return doc.splitTextToSize(pdfSafeText(text), width) as string[]
    },
  }
  let notFitting = 0
  const withoutBars: string[] = []
  spec.labels.forEach((data, i) => {
    if (i > 0) doc.addPage(format, orientation)
    const placed = placeLabel(planLabel(spec.layout, data, spec.texts), spec.media, measure)
    if (!placed.fits) notFitting++
    withoutBars.push(...placed.withoutBars)
    drawPlacement(doc, placed)
  })
  return { doc, pages: Math.max(1, spec.labels.length), notFitting, withoutBars }
}
