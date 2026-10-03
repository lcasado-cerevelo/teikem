// Reportes PDF de presentación (Lote 12), generados 100 % en el cliente con jsPDF + jspdf-autotable (import dinámico, como
// exportTable.ts: no inflan el paquete inicial). Un reporte se describe con `ReportSpec` (título, compañía, usuario,
// fecha, filtros legibles, columnas, secciones con subtotal, filas de total, resumen y avisos) y se pinta con el mismo
// formato siempre:
// - Página 1: banda azul marino de la marca (símbolo de Teikem rasterizado desde `public/brand/` si se puede, "TEIKEM" y
//   el lema; a la derecha la compañía y la fecha), título y subtítulo, "Generado el … por …", recuadro "Filtros
//   aplicados", tarjetas de resumen y avisos. Páginas siguientes: banda delgada con la compañía y el título.
// - Tabla con encabezado oscuro que se repite en cada página, filas cebra, números a la derecha con los separadores del
//   idioma, fila de grupo por sección, subtotal por sección y total general en azul marino.
// - Pie en todas las páginas: "Generado con Teikem · compañía" y "Página X de Y".
// Todo lo que decide qué se pinta es puro y se prueba sin DOM (formatReportValue, buildReportBody, reportOrientation,
// reportFileName); `renderReportPdf` arma el documento en memoria y `downloadReportPdf` lo descarga.
// Las fuentes estándar de jsPDF solo cubren Latin-1: todo texto pasa por `pdfSafeText`.
import type { jsPDF } from 'jspdf'
import { formatDateLongTime, formatMoney, formatNumber } from '../format/format'
import { t as translate } from '../i18n'
import { BRAND_SYMBOL_SRC } from './brandAssets'
import { exportFileName, pdfSafeText } from './exportTable'

/** Valor de una celda del reporte: número (se formatea según la columna), texto tal cual o vacío. */
export type ReportValue = string | number | null | undefined

/**
 * Formato de una columna: `text` (a la izquierda); los demás van a la derecha con los separadores del idioma:
 * `quantity` (hasta 3 decimales), `signed` (cantidad con signo: +5 / -3), `money` (2 decimales) y `unitCost` (2 a 4).
 */
export type ReportFormat = 'text' | 'quantity' | 'signed' | 'money' | 'unitCost'

export interface ReportColumn {
  header: string
  format?: ReportFormat
  /** true = la columna no parte su texto (fechas cortas); las numéricas ya no lo parten. */
  noWrap?: boolean
}

/** Bloque de filas; con `title` lleva una fila de grupo arriba; `subtotal` = fila de subtotal al final (misma forma que una fila). */
export interface ReportSection {
  title?: string
  rows: ReportValue[][]
  subtotal?: ReportValue[]
}

/** Un filtro aplicado, ya legible (nombres, no ids). */
export interface ReportFilter {
  label: string
  value: string
}

/** Tarjeta del resumen (valor ya formateado); `money` = cifra en naranja de la marca. */
export interface ReportSummaryItem {
  label: string
  value: string
  tone?: 'flow' | 'money'
}

export interface ReportSpec {
  title: string
  subtitle?: string
  /** Compañía (tenant activo). */
  company?: string | null
  /** Usuario que genera el reporte. */
  user?: string | null
  generatedAt?: Date
  /** Idioma de los números y fechas ('es' | 'en'). */
  locale: string
  /** Filtros aplicados; vacío = "Sin filtros". */
  filters: readonly ReportFilter[]
  columns: readonly ReportColumn[]
  sections: readonly ReportSection[]
  /** Filas de total general (total, o entradas/salidas/neto). En subtotales y totales, el primer valor es la etiqueta y los
   *  vacíos que la siguen se funden con ella. */
  totals?: readonly ReportValue[][]
  summary?: readonly ReportSummaryItem[]
  /** Avisos al lector (lista truncada, datos que el sistema no tiene…). */
  notices?: readonly string[]
  /** Texto cuando no hay filas (por defecto `ui.report.empty`). */
  emptyText?: string
  /** Por defecto: horizontal con más de 6 columnas. */
  orientation?: 'portrait' | 'landscape'
}

// ---------------------------------------------------------------------------------------------------------------------
// Lógica pura
// ---------------------------------------------------------------------------------------------------------------------

/** true = columna numérica (a la derecha). */
export function isNumericFormat(format: ReportFormat | undefined): boolean {
  return format !== undefined && format !== 'text'
}

/**
 * Texto de una celda: números con los separadores de la compañía (miles siempre) según el formato; dinero con su símbolo
 * y decimales ("$1,234.50"); texto tal cual; vacío = ''.
 */
export function formatReportValue(value: ReportValue, format: ReportFormat | undefined, locale: string): string {
  if (value === null || value === undefined) return ''
  if (typeof value === 'string') return value
  if (!Number.isFinite(value)) return ''
  if (!format || format === 'text') return String(value)
  if (format === 'money') return formatMoney(value, locale)
  if (format === 'unitCost') return formatMoney(value, locale, { unitPrice: true })
  return formatNumber(value, format === 'signed' ? { signDisplay: 'exceptZero' } : {})
}

/** Orientación: la pedida o, si no, horizontal con más de 6 columnas. */
export function reportOrientation(spec: Pick<ReportSpec, 'columns' | 'orientation'>): 'portrait' | 'landscape' {
  return spec.orientation ?? (spec.columns.length > 6 ? 'landscape' : 'portrait')
}

/** Nombre del archivo: título + compañía sin acentos + fecha local, p. ej. `reporte-de-inventario-advance-depot-2026-09-30.pdf`. */
export function reportFileName(spec: Pick<ReportSpec, 'title' | 'company'>, date: Date = new Date()): string {
  return exportFileName([spec.title, spec.company].filter(Boolean).join(' '), 'pdf', date)
}

/** Fecha y hora de generación: mes en el idioma del reporte, hora y zona de la compañía ("30 de septiembre de 2026, 2:05 p. m."). */
export function formatReportDate(date: Date, locale: string): string {
  return formatDateLongTime(date, locale)
}

export type ReportRowKind = 'group' | 'data' | 'subtotal' | 'total' | 'empty'

export interface ReportBodyCell {
  text: string
  colSpan: number
  align: 'left' | 'right'
}

export interface ReportBodyRow {
  kind: ReportRowKind
  cells: ReportBodyCell[]
  /** Filas de datos: posición dentro de su sección (para la cebra). */
  index?: number
}

/** Celdas de una fila de subtotal/total: la etiqueta (primer valor) se funde con los vacíos que la siguen. */
function labelledCells(values: readonly ReportValue[], columns: readonly ReportColumn[], locale: string): ReportBodyCell[] {
  const cells: ReportBodyCell[] = []
  let span = 1
  while (span < columns.length && (values[span] === null || values[span] === undefined || values[span] === '')) span++
  cells.push({ text: formatReportValue(values[0], 'text', locale), colSpan: span, align: 'left' })
  for (let i = span; i < columns.length; i++) {
    const col = columns[i]
    cells.push({ text: formatReportValue(values[i], col.format, locale), colSpan: 1, align: isNumericFormat(col.format) ? 'right' : 'left' })
  }
  return cells
}

/**
 * Filas del cuerpo en el orden en que se pintan: por sección, su fila de grupo (si tiene título), sus datos y su subtotal;
 * al final los totales. Sin ninguna fila de datos, una sola fila `empty` con `emptyText`.
 */
export function buildReportBody(spec: Pick<ReportSpec, 'columns' | 'sections' | 'totals' | 'locale' | 'emptyText'>): ReportBodyRow[] {
  const { columns, locale } = spec
  const n = columns.length
  const out: ReportBodyRow[] = []
  const dataCount = spec.sections.reduce((acc, s) => acc + s.rows.length, 0)
  if (dataCount === 0) {
    out.push({ kind: 'empty', cells: [{ text: spec.emptyText ?? '', colSpan: n, align: 'left' }] })
  } else {
    for (const section of spec.sections) {
      if (section.rows.length === 0) continue
      if (section.title) out.push({ kind: 'group', cells: [{ text: section.title, colSpan: n, align: 'left' }] })
      section.rows.forEach((row, index) => {
        out.push({
          kind: 'data',
          index,
          cells: columns.map((c, i) => ({
            text: formatReportValue(row[i], c.format, locale),
            colSpan: 1,
            align: isNumericFormat(c.format) ? 'right' : 'left',
          })),
        })
      })
      if (section.subtotal) out.push({ kind: 'subtotal', cells: labelledCells(section.subtotal, columns, locale) })
    }
  }
  for (const total of spec.totals ?? []) out.push({ kind: 'total', cells: labelledCells(total, columns, locale) })
  return out
}

// ---------------------------------------------------------------------------------------------------------------------
// Logo: el símbolo SVG de la marca rasterizado a PNG (jsPDF no dibuja SVG)
// ---------------------------------------------------------------------------------------------------------------------

/** Rasteriza una imagen (SVG incluido) a PNG `px`×`px` con un canvas. null si no se puede (sin DOM, error, 3 s sin cargar). */
export function rasterizeImage(src: string, px: number, timeoutMs = 3000): Promise<string | null> {
  if (typeof document === 'undefined' || typeof Image === 'undefined') return Promise.resolve(null)
  return new Promise((resolve) => {
    const img = new Image()
    const timer = setTimeout(() => resolve(null), timeoutMs)
    img.onload = () => {
      clearTimeout(timer)
      try {
        const canvas = document.createElement('canvas')
        canvas.width = px
        canvas.height = px
        const ctx = canvas.getContext('2d')
        if (!ctx) return resolve(null)
        ctx.drawImage(img, 0, 0, px, px)
        resolve(canvas.toDataURL('image/png'))
      } catch {
        resolve(null)
      }
    }
    img.onerror = () => {
      clearTimeout(timer)
      resolve(null)
    }
    img.src = src
  })
}

let logoPromise: Promise<string | null> | null = null

/** Símbolo de Teikem en PNG (una sola vez por sesión); null = el reporte va solo con texto. */
export function loadReportLogo(): Promise<string | null> {
  logoPromise ??= rasterizeImage(BRAND_SYMBOL_SRC, 192).then((png) => {
    if (!png) logoPromise = null
    return png
  })
  return logoPromise
}

// ---------------------------------------------------------------------------------------------------------------------
// Dibujo
// ---------------------------------------------------------------------------------------------------------------------

type Rgb = [number, number, number]
const NAVY: Rgb = [11, 44, 102]
const BLUE: Rgb = [31, 111, 229]
const ORANGE: Rgb = [255, 106, 26]
const ORANGE_INK: Rgb = [185, 74, 12]
const INK: Rgb = [24, 33, 54]
const MUTED: Rgb = [91, 104, 128]
const LINE: Rgb = [221, 228, 240]
const HEAD: Rgb = [24, 33, 54]
const ZEBRA: Rgb = [245, 247, 250]
const GROUP_BG: Rgb = [232, 239, 251]
const SUBTOTAL_BG: Rgb = [236, 240, 247]
const PANEL_BG: Rgb = [247, 249, 252]
const BAND_TEXT: Rgb = [176, 200, 240]
const NOTICE_BG: Rgb = [255, 246, 232]
const NOTICE_INK: Rgb = [140, 84, 10]

const MARGIN = 36
const BAND_H = 72
const THIN_BAND_H = 28
const FOOTER_H = 30

/** Medidas compartidas con otros reportes de marca (p. ej. `barcodeReportPdf.ts`), en puntos. */
export const REPORT_LAYOUT = { margin: MARGIN, thinBandHeight: THIN_BAND_H, footerHeight: FOOTER_H } as const
/** Colores de la marca en los reportes (RGB). */
export const REPORT_COLORS = { navy: NAVY, blue: BLUE, orange: ORANGE, ink: INK, muted: MUTED, line: LINE, noticeBg: NOTICE_BG, noticeInk: NOTICE_INK } as const

/** Lo que pinta el encabezado de la página 1 de un reporte de marca. */
export type ReportHeaderSpec = Pick<ReportSpec, 'title' | 'subtitle' | 'company' | 'user' | 'generatedAt' | 'locale' | 'filters' | 'summary' | 'notices'>

export interface RenderReportOptions {
  /** PNG (data URL) del símbolo de la marca; null/omitido = encabezado solo con texto. */
  logo?: string | null
  /** false = sin comprimir (pruebas: el texto queda legible en el PDF). Por defecto true. */
  compress?: boolean
}

/** Arma el documento en memoria (no descarga). */
export async function renderReportPdf(spec: ReportSpec, options: RenderReportOptions = {}): Promise<jsPDF> {
  const [{ jsPDF: JsPdf }, { autoTable }] = await Promise.all([import('jspdf'), import('jspdf-autotable')])
  const safe = (s: string) => pdfSafeText(s)
  const doc = new JsPdf({ orientation: reportOrientation(spec), unit: 'pt', format: 'a4', compress: options.compress ?? true })
  const y = drawReportHeader(doc, spec, options.logo)

  // ---- tabla ----
  const rows = buildReportBody({ ...spec, emptyText: spec.emptyText ?? translate('ui.report.empty') })
  const cellPadding = { top: 4.5, bottom: 4.5, left: 5, right: 5 }
  const columnStyles: Record<number, { halign?: 'right'; cellWidth: 'wrap' }> = {}
  spec.columns.forEach((c, i) => {
    if (isNumericFormat(c.format)) columnStyles[i] = { halign: 'right', cellWidth: 'wrap' }
    else if (c.noWrap) columnStyles[i] = { cellWidth: 'wrap' }
  })
  // con varias filas de total (entradas / salidas / neto) solo la última va en azul marino; las demás, como subtotal
  const lastTotal = rows.map((r) => r.kind).lastIndexOf('total')
  const body = rows.map((row, rowIndex) =>
    row.cells.map((cell) => {
      const base = { content: safe(cell.text), colSpan: cell.colSpan }
      const kind = row.kind === 'total' && rowIndex !== lastTotal ? 'subtotal' : row.kind
      switch (kind) {
        case 'group':
          return { ...base, styles: { fillColor: GROUP_BG, textColor: NAVY, fontStyle: 'bold' as const, fontSize: 8.5, halign: 'left' as const } }
        case 'subtotal':
          return {
            ...base,
            styles: {
              fillColor: SUBTOTAL_BG,
              textColor: INK,
              fontStyle: 'bold' as const,
              halign: cell.align,
              lineWidth: { top: 0.8, bottom: 0, left: 0, right: 0 },
              lineColor: [170, 182, 204] as Rgb,
            },
          }
        case 'total':
          return { ...base, styles: { fillColor: NAVY, textColor: [255, 255, 255] as Rgb, fontStyle: 'bold' as const, fontSize: 9, halign: cell.align } }
        case 'empty':
          return { ...base, styles: { textColor: MUTED, fontStyle: 'italic' as const, halign: 'center' as const, cellPadding: 12 } }
        default:
          return {
            ...base,
            styles: {
              fillColor: (row.index ?? 0) % 2 === 1 ? ZEBRA : ([255, 255, 255] as Rgb),
              halign: cell.align,
              lineWidth: { top: 0, bottom: 0.5, left: 0, right: 0 },
              lineColor: LINE,
            },
          }
      }
    }),
  )
  autoTable(doc, {
    head: [spec.columns.map((c) => ({ content: safe(c.header), styles: { halign: isNumericFormat(c.format) ? ('right' as const) : ('left' as const) } }))],
    body,
    startY: y,
    theme: 'plain',
    margin: { left: MARGIN, right: MARGIN, top: THIN_BAND_H + 20, bottom: FOOTER_H + 14 },
    styles: { font: 'helvetica', fontSize: 8, cellPadding, textColor: INK, overflow: 'linebreak', valign: 'middle' },
    headStyles: { fillColor: HEAD, textColor: [255, 255, 255], fontStyle: 'bold', fontSize: 8 },
    columnStyles,
    rowPageBreak: 'avoid',
    showHead: 'everyPage',
  })

  drawReportPageChrome(doc, spec)
  return doc
}

/**
 * Página 1 de un reporte de marca: banda azul marino (símbolo si hay `logo`, "TEIKEM", lema, compañía y fecha), título,
 * subtítulo, "Generado el … por …", recuadro "Filtros aplicados", tarjetas de resumen y avisos. Devuelve la `y` (pt) donde
 * empieza el cuerpo. Lo usan `renderReportPdf` y el reporte de códigos de barras (Lote F14).
 */
export function drawReportHeader(doc: jsPDF, spec: ReportHeaderSpec, logo?: string | null): number {
  const safe = (s: string) => pdfSafeText(s)
  const W = doc.internal.pageSize.getWidth()
  const inner = W - MARGIN * 2
  const generatedAt = spec.generatedAt ?? new Date()
  const when = formatReportDate(generatedAt, spec.locale)
  const company = spec.company?.trim() || ''

  // ---- banda de la marca ----
  doc.setFillColor(...NAVY)
  doc.rect(0, 0, W, BAND_H, 'F')
  doc.setFillColor(...BLUE)
  doc.rect(0, BAND_H, W, 3, 'F')
  doc.setFillColor(...ORANGE)
  doc.rect(0, BAND_H, 96, 3, 'F')
  let brandX = MARGIN
  if (logo) {
    try {
      doc.addImage(logo, 'PNG', MARGIN - 4, 14, 44, 44)
      brandX = MARGIN + 48
    } catch {
      // un PNG ilegible no impide el reporte: queda solo el texto
    }
  }
  doc.setTextColor(255, 255, 255)
  doc.setFont('helvetica', 'bold')
  doc.setFontSize(19)
  doc.text('TEIKEM', brandX, 38)
  doc.setFont('helvetica', 'normal')
  doc.setFontSize(8)
  doc.setTextColor(...BAND_TEXT)
  doc.text(safe(translate('ui.report.tagline')).toUpperCase(), brandX + 1, 52)
  if (company) {
    doc.setFont('helvetica', 'bold')
    doc.setFontSize(13)
    doc.setTextColor(255, 255, 255)
    const line = (doc.splitTextToSize(safe(company), inner / 2) as string[])[0] ?? ''
    doc.text(line, W - MARGIN, 34, { align: 'right' })
  }
  doc.setFont('helvetica', 'normal')
  doc.setFontSize(8.5)
  doc.setTextColor(...BAND_TEXT)
  doc.text(safe(when), W - MARGIN, 50, { align: 'right' })

  // ---- título ----
  let y = BAND_H + 32
  doc.setTextColor(...INK)
  doc.setFont('helvetica', 'bold')
  doc.setFontSize(19)
  const titleLines = doc.splitTextToSize(safe(spec.title), inner) as string[]
  doc.text(titleLines, MARGIN, y)
  y += (titleLines.length - 1) * 22 + 16
  if (spec.subtitle) {
    doc.setFont('helvetica', 'normal')
    doc.setFontSize(10)
    doc.setTextColor(...MUTED)
    const sub = doc.splitTextToSize(safe(spec.subtitle), inner) as string[]
    doc.text(sub, MARGIN, y)
    y += sub.length * 12.5
  }
  doc.setFontSize(8.5)
  doc.setTextColor(...MUTED)
  const meta = [translate('ui.report.generatedAt', { date: when }), spec.user ? translate('ui.report.generatedBy', { user: spec.user }) : '']
    .filter(Boolean)
    .join(' ')
  doc.text(safe(meta), MARGIN, y + 2)
  y += 16

  // ---- filtros aplicados ----
  doc.setFontSize(8.5)
  doc.setFont('helvetica', 'bold')
  // columna de etiquetas del ancho de la más larga (entre 60 y 150 pt); los valores envuelven en el resto
  const labelW = Math.min(150, Math.max(60, ...spec.filters.map((f) => doc.getTextWidth(safe(f.label)) + 10)))
  doc.setFont('helvetica', 'normal')
  const valueW = inner - 24 - labelW
  const filterLines = spec.filters.map((f) => ({
    label: safe(f.label),
    lines: doc.splitTextToSize(safe(f.value), valueW) as string[],
  }))
  const noFilters = doc.splitTextToSize(safe(translate('ui.report.noFilters')), inner - 24) as string[]
  const bodyLines = spec.filters.length > 0 ? filterLines.reduce((acc, f) => acc + f.lines.length, 0) : noFilters.length
  const boxH = 24 + bodyLines * 11 + 6
  doc.setFillColor(...PANEL_BG)
  doc.setDrawColor(...LINE)
  doc.setLineWidth(0.8)
  doc.roundedRect(MARGIN, y, inner, boxH, 5, 5, 'FD')
  doc.setFillColor(...BLUE)
  doc.rect(MARGIN, y + 6, 2.5, boxH - 12, 'F')
  doc.setFont('helvetica', 'bold')
  doc.setFontSize(7.5)
  doc.setTextColor(...BLUE)
  doc.text(safe(translate('ui.report.filtersTitle')).toUpperCase(), MARGIN + 12, y + 15)
  let fy = y + 28
  doc.setFontSize(8.5)
  if (spec.filters.length === 0) {
    doc.setFont('helvetica', 'normal')
    doc.setTextColor(...MUTED)
    doc.text(noFilters, MARGIN + 12, fy)
  } else {
    for (const f of filterLines) {
      doc.setFont('helvetica', 'bold')
      doc.setTextColor(...INK)
      doc.text(f.label, MARGIN + 12, fy)
      doc.setFont('helvetica', 'normal')
      doc.setTextColor(...MUTED)
      doc.text(f.lines, MARGIN + 12 + labelW, fy)
      fy += f.lines.length * 11
    }
  }
  y += boxH + 12

  // ---- resumen ----
  const summary = spec.summary ?? []
  if (summary.length > 0) {
    const gap = 8
    const cardW = (inner - gap * (summary.length - 1)) / summary.length
    summary.forEach((s, i) => {
      const x = MARGIN + i * (cardW + gap)
      doc.setFillColor(255, 255, 255)
      doc.setDrawColor(...LINE)
      doc.setLineWidth(0.8)
      doc.roundedRect(x, y, cardW, 42, 5, 5, 'FD')
      doc.setFillColor(...(s.tone === 'money' ? ORANGE : BLUE))
      doc.rect(x + 8, y + 7, 14, 2, 'F')
      doc.setFont('helvetica', 'normal')
      doc.setFontSize(7)
      doc.setTextColor(...MUTED)
      doc.text((doc.splitTextToSize(safe(s.label).toUpperCase(), cardW - 16) as string[])[0] ?? '', x + 8, y + 19)
      doc.setFont('helvetica', 'bold')
      doc.setFontSize(12.5)
      doc.setTextColor(...(s.tone === 'money' ? ORANGE_INK : INK))
      doc.text((doc.splitTextToSize(safe(s.value), cardW - 16) as string[])[0] ?? '', x + 8, y + 34)
    })
    y += 42 + 12
  }

  // ---- avisos ----
  for (const notice of spec.notices ?? []) y += drawReportNotice(doc, reportNoticeLines(doc, notice), y) + 6
  y += 4

  return y
}

/** Renglones de un aviso al ancho del cuerpo (texto ya seguro para el PDF). */
export function reportNoticeLines(doc: jsPDF, notice: string): string[] {
  const inner = doc.internal.pageSize.getWidth() - MARGIN * 2
  doc.setFont('helvetica', 'normal')
  doc.setFontSize(8.5)
  return doc.splitTextToSize(pdfSafeText(notice), inner - 22) as string[]
}

/** Alto (pt) de un aviso de `lines` renglones. */
export function reportNoticeHeight(lines: number): number {
  return lines * 11 + 10
}

/** Recuadro de aviso (fondo crema, filete naranja) a todo el ancho del cuerpo en `y`; devuelve su alto. */
export function drawReportNotice(doc: jsPDF, lines: readonly string[], y: number): number {
  const inner = doc.internal.pageSize.getWidth() - MARGIN * 2
  const h = reportNoticeHeight(lines.length)
  doc.setFont('helvetica', 'normal')
  doc.setFontSize(8.5)
  doc.setFillColor(...NOTICE_BG)
  doc.rect(MARGIN, y, inner, h, 'F')
  doc.setFillColor(...ORANGE)
  doc.rect(MARGIN, y, 2.5, h, 'F')
  doc.setTextColor(...NOTICE_INK)
  doc.text([...lines], MARGIN + 12, y + 13)
  return h
}

/** Banda delgada con la compañía y el título en las páginas 2+ y pie "Generado con Teikem · compañía" / "Página X de Y" en
 *  todas. Se llama al final, con todas las páginas ya armadas. */
export function drawReportPageChrome(doc: jsPDF, spec: Pick<ReportSpec, 'title' | 'company'>): void {
  const safe = (s: string) => pdfSafeText(s)
  const W = doc.internal.pageSize.getWidth()
  const H = doc.internal.pageSize.getHeight()
  const inner = W - MARGIN * 2
  const company = spec.company?.trim() || ''
  const pages = doc.getNumberOfPages()
  for (let i = 1; i <= pages; i++) {
    doc.setPage(i)
    if (i > 1) {
      doc.setFillColor(...NAVY)
      doc.rect(0, 0, W, THIN_BAND_H, 'F')
      doc.setFillColor(...BLUE)
      doc.rect(0, THIN_BAND_H, W, 2, 'F')
      doc.setFont('helvetica', 'bold')
      doc.setFontSize(10)
      doc.setTextColor(255, 255, 255)
      doc.text('TEIKEM', MARGIN, 18)
      doc.setFont('helvetica', 'normal')
      doc.setFontSize(8.5)
      doc.setTextColor(...BAND_TEXT)
      const right = [company, spec.title].filter(Boolean).join(' · ')
      doc.text((doc.splitTextToSize(safe(right), inner - 80) as string[])[0] ?? '', W - MARGIN, 18, { align: 'right' })
    }
    doc.setDrawColor(...LINE)
    doc.setLineWidth(0.6)
    doc.line(MARGIN, H - FOOTER_H, W - MARGIN, H - FOOTER_H)
    doc.setFont('helvetica', 'normal')
    doc.setFontSize(7.5)
    doc.setTextColor(...MUTED)
    const left = [translate('ui.report.footer'), company].filter(Boolean).join(' · ')
    doc.text((doc.splitTextToSize(safe(left), inner - 110) as string[])[0] ?? '', MARGIN, H - FOOTER_H + 13)
    doc.text(safe(translate('ui.report.page', { page: i, pages })), W - MARGIN, H - FOOTER_H + 13, { align: 'right' })
  }
}

/** Arma el reporte (con el logo si se puede rasterizar) y lo descarga como `reportFileName(spec)`. */
export async function downloadReportPdf(spec: ReportSpec): Promise<void> {
  const logo = await loadReportLogo()
  const doc = await renderReportPdf(spec, { logo })
  doc.save(reportFileName(spec, spec.generatedAt))
}
