// Lote F16 — etiquetas de posición (PDF vectorial) para impresoras térmicas de etiquetas (Zebra y similares): UNA etiqueta
// por posición y CADA etiqueta es UNA página del tamaño exacto de la etiqueta, sin márgenes de página que el driver tenga
// que recortar. Tamaños (pulgadas, ancho × alto): 4×2 apaisada (288×144 pt), 4×4 cuadrada (288×288 pt) y 4×6 vertical
// (288×432 pt). Genérico: el código de la posición se imprime tal cual (no se asume ningún formato de pasillo ni de piso).
// Contenido de cada etiqueta, de arriba abajo, dentro de un margen interno de seguridad constante
// (`BIN_LABEL_SAFE_MARGIN_IN` = 0.1 in a cada lado):
// - el código de barras Code 128 del código de la posición, HORIZONTAL y lo más grande posible: a todo el ancho útil con
//   su zona de silencio (`CODE128_QUIET_ZONE` módulos a cada lado), módulo entre `BARCODE_MIN_MODULE_MM` (0.25 mm, el
//   mismo de los demás reportes) y `BIN_LABEL_MAX_MODULE_MM`; el alto de las barras es lo que queda después del texto
//   (al menos `BIN_LABEL_MIN_BAR_MM`). Un código que ni a 0.25 mm cabe en el ancho sale "No cabe" (solo texto) y uno con
//   caracteres que Code 128 no admite, "Sin código de barras" (solo texto); los dos se listan al final del proceso con los
//   mismos textos del reporte de códigos de barras (`barcodeEndNotices`);
// - debajo, el código en texto grande: la letra se ajusta para llenar el ancho sin cortarse (`fitFontSize`), entre
//   `BIN_LABEL_CODE_MIN_PT` (mínimo legible; si ni así cabe, en 2 renglones) y un máximo proporcional al alto de la etiqueta;
// - y en letra más pequeña los datos de la ubicación que existan ("Almacén ALM-01 · Zona RSV · Pasillo 01…", hasta 2
//   renglones, recortados con "...").
// Todo en NEGRO puro (texto incluido): la impresora térmica no tiene grises y un gris se imprime tramado, ilegible en
// letra chica.
// Orientación: 'auto' = la página tal cual (ancho × alto del tamaño); 'rotate' = la MISMA página con `/Rotate 90` en su
// diccionario (para impresoras que alimentan la etiqueta de lado): el visor y el driver la giran; el contenido no se
// vuelve a acomodar a mano.
// Lo que decide qué se pinta y dónde es puro y se prueba sin DOM (`binLabelPage`, `planBinLabels`, `binLabelLayout`,
// `binLabelDetailSize`); `renderBinLabelsPdf` arma el documento en memoria y `downloadBinLabelsPdf` lo descarga.
// Las etiquetas no tienen estado: imprimirlas no marca nada (a diferencia de las hojas de posición del Lote F15).
import type { jsPDF } from 'jspdf'
import { t as translate } from '../i18n'
import { PT_PER_MM, barcodeEndNotices, clampLines } from './barcodeReportPdf'
import { fitFontSize, sheetModuleWidth, type BinSheetDetail } from './binSheetPdf'
import { CODE128_QUIET_ZONE, code128Unsupported, encodeCode128, isCode128Encodable, type Code128Symbol } from './code128'
import { pdfSafeText } from './exportTable'
import { reportFileName } from './reportPdf'

type Translate = (key: string, params?: Record<string, string | number>) => string

/** Puntos por pulgada (unidad del PDF). */
export const PT_PER_IN = 72

/** Tamaños de etiqueta, en el orden del selector. */
export const BIN_LABEL_SIZE_KEYS = ['4x2', '4x4', '4x6'] as const
export type BinLabelSize = (typeof BIN_LABEL_SIZE_KEYS)[number]

export interface BinLabelSizeInfo {
  /** Ancho y alto en pulgadas (ancho = a lo largo del cabezal de la impresora). */
  widthIn: number
  heightIn: number
  /** Ancho y alto en centímetros, redondeados para el texto del selector ("10 × 5 cm"). */
  widthCm: number
  heightCm: number
  /** Forma de la página: apaisada, cuadrada o vertical. */
  shape: 'landscape' | 'square' | 'portrait'
}

export const BIN_LABEL_SIZES: Record<BinLabelSize, BinLabelSizeInfo> = {
  '4x2': { widthIn: 4, heightIn: 2, widthCm: 10, heightCm: 5, shape: 'landscape' },
  '4x4': { widthIn: 4, heightIn: 4, widthCm: 10, heightCm: 10, shape: 'square' },
  '4x6': { widthIn: 4, heightIn: 6, widthCm: 10, heightCm: 15, shape: 'portrait' },
}

/** Orientación: tal cual, o la página girada 90° (`/Rotate 90`). */
export const BIN_LABEL_ORIENTATIONS = ['auto', 'rotate'] as const
export type BinLabelOrientation = (typeof BIN_LABEL_ORIENTATIONS)[number]

/** Margen interno de seguridad a cada lado (pulgadas y pt): nada se dibuja fuera de él (el borde de corte de la etiqueta). */
export const BIN_LABEL_SAFE_MARGIN_IN = 0.1
export const BIN_LABEL_SAFE_MARGIN_PT = BIN_LABEL_SAFE_MARGIN_IN * PT_PER_IN
/** Módulo máximo del código de barras (mm), el mismo de los productos de la hoja de posición: un código muy corto no
 *  ocupa todo el ancho (barras más gruesas no ayudan al lector) y queda centrado. */
export const BIN_LABEL_MAX_MODULE_MM = 1
/** Alto mínimo de las barras (mm): el texto se achica antes que las barras bajen de aquí. */
export const BIN_LABEL_MIN_BAR_MM = 8
/** Letra del código en texto: mínimo legible y máximo absoluto (pt); el máximo de cada tamaño es proporcional a su alto. */
export const BIN_LABEL_CODE_MIN_PT = 10
export const BIN_LABEL_CODE_MAX_PT = 96
/** Proporción del alto útil que puede ocupar el código en texto (un renglón). */
export const BIN_LABEL_CODE_HEIGHT_SHARE = 0.24
/** Renglones máximos del código en texto (si ni a la letra mínima cabe en uno) y de los datos de la ubicación. */
export const BIN_LABEL_CODE_MAX_LINES = 2
export const BIN_LABEL_DETAIL_MAX_LINES = 2
/** Letra de los datos de la ubicación (pt): proporcional al alto útil, entre estos límites. */
export const BIN_LABEL_DETAIL_MIN_PT = 7
export const BIN_LABEL_DETAIL_MAX_PT = 14
/** Separación vertical entre barras, código y datos (pt): proporcional al alto útil, entre estos límites. */
export const BIN_LABEL_GAP_MIN_PT = 3
export const BIN_LABEL_GAP_MAX_PT = 12

/** Interlineado del código (letra grande) y de los datos. */
const CODE_LINE = 1.15
const DETAIL_LINE = 1.2
/** Letra del motivo cuando no hay código de barras ("No cabe…", "Sin código de barras…"). */
const REASON_MIN_PT = 8
const REASON_MAX_PT = 14

const clamp = (v: number, min: number, max: number) => Math.min(max, Math.max(min, v))

export interface BinLabelBin {
  /** Código de la posición, tal cual (también es el valor de su código de barras). */
  code: string
  /** Almacén, zona, pasillo, rack, nivel, posición… (solo los que existen), en ese orden. */
  details?: readonly BinSheetDetail[]
  /** Identificador de quien llama (p. ej. el id de la posición). */
  key?: string | number
}

export interface BinLabelSpec {
  /** Título del documento y base del nombre del archivo ("Etiquetas de posición"); el tamaño se agrega solo. */
  title: string
  company?: string | null
  locale: string
  size: BinLabelSize
  orientation?: BinLabelOrientation
  /** Fecha del nombre del archivo (por defecto ahora). */
  generatedAt?: Date
  bins: readonly BinLabelBin[]
}

// ---------------------------------------------------------------------------------------------------------------------
// Lógica pura
// ---------------------------------------------------------------------------------------------------------------------

/** Página de un tamaño (pt): ancho × alto de la etiqueta (MediaBox) y el giro (`/Rotate`) según la orientación. */
export function binLabelPage(size: BinLabelSize, orientation: BinLabelOrientation = 'auto'): { width: number; height: number; rotate: 0 | 90 } {
  const s = BIN_LABEL_SIZES[size]
  return { width: s.widthIn * PT_PER_IN, height: s.heightIn * PT_PER_IN, rotate: orientation === 'rotate' ? 90 : 0 }
}

/** Ancho y alto útiles (pt): la página menos el margen de seguridad a cada lado. */
export function binLabelInner(size: BinLabelSize): { width: number; height: number } {
  const p = binLabelPage(size)
  return { width: p.width - 2 * BIN_LABEL_SAFE_MARGIN_PT, height: p.height - 2 * BIN_LABEL_SAFE_MARGIN_PT }
}

/** Letra de los datos de la ubicación de un tamaño (pt). */
export function binLabelDetailSize(size: BinLabelSize): number {
  return clamp(binLabelInner(size).height * 0.055, BIN_LABEL_DETAIL_MIN_PT, BIN_LABEL_DETAIL_MAX_PT)
}

/** Código de barras de una etiqueta: con símbolo, "No cabe" o no admitido (solo texto). */
export type BinLabelCode =
  | { status: 'ok'; symbol: Code128Symbol; modulePt: number }
  | { status: 'tooWide'; symbol: Code128Symbol; modulePt: null }
  | { status: 'unsupported'; chars: string[] }

/**
 * El código de barras del código de la posición en `widthPt`: el módulo que llena el ancho (con la zona de silencio), a
 * lo más `BIN_LABEL_MAX_MODULE_MM`; "No cabe" si haría falta menos de `BARCODE_MIN_MODULE_MM`; vacío o con caracteres
 * fuera de Code 128, no admitido.
 */
export function binLabelCode(code: string, widthPt: number): BinLabelCode {
  if (!isCode128Encodable(code)) return { status: 'unsupported', chars: code128Unsupported(code) }
  const symbol = encodeCode128(code)
  const modulePt = sheetModuleWidth(symbol.width, widthPt, BIN_LABEL_MAX_MODULE_MM)
  return modulePt === null ? { status: 'tooWide', symbol, modulePt: null } : { status: 'ok', symbol, modulePt }
}

/** Una etiqueta ya decidida. */
export interface PlannedBinLabel {
  /** Índice de la posición en `spec.bins`. */
  bin: number
  code: string
  details: readonly BinSheetDetail[]
  key?: string | number
  barcode: BinLabelCode
}

export interface BinLabelPlan {
  size: BinLabelSize
  orientation: BinLabelOrientation
  /** Una por posición, en el orden recibido (= una página cada una). */
  labels: PlannedBinLabel[]
  /** Códigos demasiado largos para un código de barras legible en el ancho de la etiqueta (salen solo en texto). */
  tooWide: string[]
  /** Códigos con caracteres que Code 128 no admite (salen solo en texto). */
  unsupported: { value: string; chars: string[] }[]
  /** Etiquetas sin código de barras (no caben + no admitidos). */
  withoutCode: number
}

/** Una etiqueta por posición (todas, también las que salen solo en texto) y los avisos de las que no llevan código. */
export function planBinLabels(bins: readonly BinLabelBin[], size: BinLabelSize, orientation: BinLabelOrientation = 'auto'): BinLabelPlan {
  const width = binLabelInner(size).width
  const tooWide: string[] = []
  const unsupported: { value: string; chars: string[] }[] = []
  const labels = bins.map((b, bin): PlannedBinLabel => {
    const barcode = binLabelCode(b.code, width)
    if (barcode.status === 'tooWide') tooWide.push(b.code)
    if (barcode.status === 'unsupported') unsupported.push({ value: b.code, chars: barcode.chars })
    return { bin, code: b.code, details: b.details ?? [], key: b.key, barcode }
  })
  return { size, orientation, labels, tooWide, unsupported, withoutCode: tooWide.length + unsupported.length }
}

/** Avisos de las etiquetas sin código de barras (los mismos textos del reporte de códigos de barras). */
export function binLabelNotices(plan: Pick<BinLabelPlan, 'tooWide' | 'unsupported'>, t: Translate = translate): string[] {
  return barcodeEndNotices(plan, t)
}

/** Medidas de una etiqueta (pt, desde la esquina superior izquierda de la página, como jsPDF). */
export interface BinLabelLayout {
  pageWidth: number
  pageHeight: number
  margin: number
  innerWidth: number
  innerHeight: number
  /** Separación entre barras, código y datos. */
  gap: number
  /** Ancho de módulo; null = sin símbolo (no cabe o no se admite: el motivo va en el lugar de las barras). */
  modulePt: number | null
  /** Ancho del símbolo con su zona de silencio (0 sin símbolo). */
  symbolWidth: number
  /** Esquina izquierda de la zona de silencio izquierda (el símbolo va centrado). */
  barLeft: number
  barTop: number
  barHeight: number
  codeSize: number
  codeLines: number
  codeTop: number
  detailSize: number
  detailLines: number
  detailTop: number
}

export interface BinLabelLayoutInput {
  /** Módulos del símbolo (sin zona de silencio); null = sin símbolo. */
  symbolModules: number | null
  /** Ancho del código en texto a 1 pt (`doc.getStringUnitWidth`). */
  codeUnitWidth: number
  /** Renglones de los datos de la ubicación (0 = no hay; se acota a `BIN_LABEL_DETAIL_MAX_LINES`). */
  detailLines: number
}

/**
 * Medidas de una etiqueta: abajo los datos (letra `binLabelDetailSize`), encima el código en texto (la mayor letra que
 * llene el ancho, entre el mínimo legible y `BIN_LABEL_CODE_HEIGHT_SHARE` del alto; si ni al mínimo cabe, 2 renglones al
 * mínimo; y nunca tan grande que las barras bajen de `BIN_LABEL_MIN_BAR_MM`), y arriba las barras con todo el alto que
 * quede, a todo el ancho útil (módulo de `binLabelCode`), centradas.
 */
export function binLabelLayout(size: BinLabelSize, input: BinLabelLayoutInput): BinLabelLayout {
  const page = binLabelPage(size)
  const m = BIN_LABEL_SAFE_MARGIN_PT
  const iw = page.width - 2 * m
  const ih = page.height - 2 * m
  const gap = clamp(ih * 0.04, BIN_LABEL_GAP_MIN_PT, BIN_LABEL_GAP_MAX_PT)
  const detailSize = binLabelDetailSize(size)
  const detailLines = clamp(Math.floor(input.detailLines), 0, BIN_LABEL_DETAIL_MAX_LINES)
  const detailH = detailLines > 0 ? detailLines * detailSize * DETAIL_LINE + gap : 0
  const oneLine = input.codeUnitWidth * BIN_LABEL_CODE_MIN_PT <= iw + 0.01
  const codeLines = oneLine ? 1 : BIN_LABEL_CODE_MAX_LINES
  const minBar = BIN_LABEL_MIN_BAR_MM * PT_PER_MM
  const codeMax = clamp(ih * BIN_LABEL_CODE_HEIGHT_SHARE, BIN_LABEL_CODE_MIN_PT, BIN_LABEL_CODE_MAX_PT)
  const byWidth = oneLine ? fitFontSize(input.codeUnitWidth, iw, codeMax, BIN_LABEL_CODE_MIN_PT) : BIN_LABEL_CODE_MIN_PT
  const byHeight = (ih - detailH - gap - minBar) / (codeLines * CODE_LINE)
  const codeSize = Math.max(BIN_LABEL_CODE_MIN_PT, Math.min(byWidth, byHeight))
  const codeH = codeLines * codeSize * CODE_LINE
  const barHeight = ih - detailH - gap - codeH
  const modulePt = input.symbolModules === null ? null : sheetModuleWidth(input.symbolModules, iw, BIN_LABEL_MAX_MODULE_MM)
  const symbolWidth = modulePt !== null && input.symbolModules !== null ? (input.symbolModules + 2 * CODE128_QUIET_ZONE) * modulePt : 0
  const codeTop = m + barHeight + gap
  return {
    pageWidth: page.width,
    pageHeight: page.height,
    margin: m,
    innerWidth: iw,
    innerHeight: ih,
    gap,
    modulePt,
    symbolWidth,
    barLeft: m + (iw - symbolWidth) / 2,
    barTop: m,
    barHeight,
    codeSize,
    codeLines,
    codeTop,
    detailSize,
    detailLines,
    detailTop: codeTop + codeH + gap,
  }
}

/** Texto de los datos de la ubicación ("Almacén ALM-01 · Zona RSV · Pasillo 01"). */
export function binLabelDetailsText(details: readonly BinSheetDetail[]): string {
  return details
    .map((d) => `${d.label} ${d.value}`.trim())
    .filter(Boolean)
    .join(' · ')
}

/** Nombre del archivo: "etiquetas-de-posicion-4x2-<compañía>-<día>.pdf" (el día de la compañía). */
export function binLabelFileName(spec: Pick<BinLabelSpec, 'title' | 'company' | 'size' | 'generatedAt'>): string {
  return reportFileName({ title: `${spec.title} ${spec.size}`, company: spec.company }, spec.generatedAt ?? new Date())
}

// ---------------------------------------------------------------------------------------------------------------------
// Dibujo
// ---------------------------------------------------------------------------------------------------------------------

/** Una etiqueta en la página actual. */
function drawLabel(doc: jsPDF, size: BinLabelSize, label: PlannedBinLabel) {
  const code = pdfSafeText(label.code)
  const inner = binLabelInner(size)
  const symbol = label.barcode.status === 'ok' ? label.barcode.symbol : null

  // renglones de los datos (con la letra del tamaño) y ancho del código a 1 pt, para las medidas
  const detailSize = binLabelDetailSize(size)
  doc.setFont('helvetica', 'normal')
  doc.setFontSize(detailSize)
  const detailsText = pdfSafeText(binLabelDetailsText(label.details))
  const details = detailsText ? clampLines(doc, doc.splitTextToSize(detailsText, inner.width) as string[], BIN_LABEL_DETAIL_MAX_LINES, inner.width) : []
  doc.setFont('helvetica', 'bold')
  const layout = binLabelLayout(size, { symbolModules: symbol ? symbol.width : null, codeUnitWidth: doc.getStringUnitWidth(code), detailLines: details.length })
  const cx = layout.margin + layout.innerWidth / 2

  // ---- barras (vectoriales: rectángulos), o el motivo si no hay código de barras ----
  if (symbol && layout.modulePt !== null) {
    const x0 = layout.barLeft + CODE128_QUIET_ZONE * layout.modulePt
    doc.setFillColor(0, 0, 0)
    for (const bar of symbol.bars) doc.rect(x0 + bar.x * layout.modulePt, layout.barTop, bar.width * layout.modulePt, layout.barHeight, 'F')
  } else {
    const reason =
      label.barcode.status === 'unsupported'
        ? translate('ui.binSheet.noCode', { chars: label.barcode.chars.map((c) => pdfSafeText(c) || '?').join(' ') || '-' })
        : translate('ui.barcodeReport.tooWide')
    const reasonSize = clamp(layout.barHeight / 6, REASON_MIN_PT, REASON_MAX_PT)
    doc.setFont('helvetica', 'bold')
    doc.setFontSize(reasonSize)
    doc.setTextColor(0, 0, 0)
    const lines = clampLines(doc, doc.splitTextToSize(pdfSafeText(reason), layout.innerWidth) as string[], 3, layout.innerWidth)
    const top = layout.barTop + Math.max(0, (layout.barHeight - lines.length * reasonSize * DETAIL_LINE) / 2)
    lines.forEach((l, i) => doc.text(l, cx, top + reasonSize * 0.86 + i * reasonSize * DETAIL_LINE, { align: 'center' }))
  }

  // ---- código en texto grande, centrado ----
  doc.setFont('helvetica', 'bold')
  doc.setFontSize(layout.codeSize)
  doc.setTextColor(0, 0, 0)
  const codeLines = layout.codeLines === 1 ? [code] : clampLines(doc, doc.splitTextToSize(code, layout.innerWidth) as string[], BIN_LABEL_CODE_MAX_LINES, layout.innerWidth)
  codeLines.forEach((l, i) => doc.text(l, cx, layout.codeTop + layout.codeSize * 0.86 + i * layout.codeSize * CODE_LINE, { align: 'center' }))

  // ---- datos de la ubicación, más pequeños ----
  if (details.length > 0) {
    doc.setFont('helvetica', 'normal')
    doc.setFontSize(layout.detailSize)
    doc.setTextColor(0, 0, 0)
    details.forEach((l, i) => doc.text(l, cx, layout.detailTop + layout.detailSize * 0.86 + i * layout.detailSize * DETAIL_LINE, { align: 'center' }))
  }
}

export interface RenderBinLabelsOptions {
  /** false = sin comprimir (pruebas: el texto queda legible en el PDF). Por defecto true. */
  compress?: boolean
}

/** jsPDF con `internal.write` y `internal.events` (no están en sus tipos públicos). */
type JsPdfInternals = { internal: { write: (s: string) => void; events: { subscribe: (topic: string, cb: () => void) => unknown } } }

/**
 * Arma el documento en memoria (no descarga): una página del tamaño exacto de la etiqueta por posición, con `/Rotate 90`
 * en cada página si la orientación es 'rotate'. Sin posiciones: una sola página en blanco (quien llama no debería pedirlo).
 */
export async function renderBinLabelsPdf(spec: BinLabelSpec, options: RenderBinLabelsOptions = {}): Promise<{ doc: jsPDF; plan: BinLabelPlan }> {
  const { jsPDF: JsPdf } = await import('jspdf')
  const orientation = spec.orientation ?? 'auto'
  const page = binLabelPage(spec.size, orientation)
  // jsPDF intercambia ancho y alto si la orientación no coincide con el formato: se le dice la que es (4×4 = vertical)
  const pageOrientation = page.width > page.height ? 'landscape' : 'portrait'
  const format: [number, number] = [page.width, page.height]
  const doc = new JsPdf({ orientation: pageOrientation, unit: 'pt', format, compress: options.compress ?? true })
  doc.setProperties({ title: pdfSafeText(`${spec.title} ${spec.size}`), subject: pdfSafeText(spec.title), creator: 'Teikem' })
  if (page.rotate) {
    // el giro es de la PÁGINA (`/Rotate 90` en su diccionario, junto a /MediaBox), no del contenido
    const internals = (doc as unknown as JsPdfInternals).internal
    internals.events.subscribe('putPage', () => internals.write(`/Rotate ${page.rotate}`))
  }
  const plan = planBinLabels(spec.bins, spec.size, orientation)
  plan.labels.forEach((label, i) => {
    if (i > 0) doc.addPage(format, pageOrientation)
    drawLabel(doc, spec.size, label)
  })
  return { doc, plan }
}

/** Arma las etiquetas y las descarga como `binLabelFileName(spec)`; devuelve el plan (para los avisos de la pantalla). */
export async function downloadBinLabelsPdf(spec: BinLabelSpec): Promise<BinLabelPlan> {
  const { doc, plan } = await renderBinLabelsPdf(spec)
  doc.save(binLabelFileName(spec))
  return plan
}
