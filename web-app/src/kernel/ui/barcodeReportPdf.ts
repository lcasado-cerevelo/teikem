// Lote F14 — reporte imprimible de códigos de barras Code 128 (hoja carta vertical) para imprimir y escanear el papel con el
// lector del almacén cuando la mercancía o la posición no tienen etiqueta. Mismo encabezado y pie que los reportes de marca
// (`reportPdf.ts`: banda, título, "Generado el … por …", "Filtros aplicados", resumen, avisos y "Página X de Y"); el cuerpo
// es una REJILLA de 3, 2 o 1 columnas (diseño del dueño del 2026-10-03: menos páginas):
// - Cada celda es UN elemento: arriba la identificación (título en negrita —SKU o código de posición—, descripción en hasta
//   2 renglones y una línea gris pequeña) y debajo el código de barras del VALOR exacto (`row.value`) con el texto legible.
// - Columnas: en automático, 3 si TODOS los códigos caben a un módulo ≥ 0.25 mm con su zona de silencio; si no 2; si no 1
//   (`chooseBarcodeColumns`). Con "2 columnas", 2 o 1. Si se bajó de lo pedido, un aviso lo dice. "No cabe" solo para un
//   valor que ni en una columna cabe (sale su celda sin código y se lista en un aviso al final).
// - Grupos: cada uno abre con un título a todo el ancho; un título nunca queda solo al pie (si debajo no cabe al menos una
//   fila de celdas, pasa a la página siguiente) y si el grupo sigue en otra página se repite como "… (continuación)". Una
//   celda (una fila de celdas) nunca se parte entre dos páginas (`paginateBarcodeGrid`).
// - Barras como rectángulos vectoriales (no imágenes), zona de silencio de 10 módulos a cada lado, módulo de 0.33 mm (se
//   angosta hasta 0.25 mm si el valor es largo), barras de 12 mm. Los valores con caracteres que Code 128 no admite se
//   omiten (no salen como celda) y se listan en otro aviso al final.
// Todo lo que decide qué se pinta y dónde es puro y se prueba sin DOM (`planBarcodeReport`, `barcodeModuleWidth`,
// `chooseBarcodeColumns`, `paginateBarcodeGrid`, `barcodeReportSummary`, `barcodeEndNotices`); `renderBarcodeReportPdf`
// arma el documento en memoria y `downloadBarcodeReportPdf` lo descarga con el nombre de los demás reportes.
import type { jsPDF } from 'jspdf'
import { formatNumber } from '../format/format'
import { t as translate } from '../i18n'
import { code128Unsupported, encodeCode128, CODE128_QUIET_ZONE, type Code128Symbol } from './code128'
import { pdfSafeText } from './exportTable'
import {
  drawReportHeader,
  drawReportNotice,
  drawReportPageChrome,
  loadReportLogo,
  REPORT_COLORS,
  REPORT_LAYOUT,
  reportFileName,
  reportNoticeHeight,
  reportNoticeLines,
  type ReportFilter,
  type ReportSummaryItem,
} from './reportPdf'

/** Puntos por milímetro (el PDF se arma en puntos). */
export const PT_PER_MM = 72 / 25.4
/** Ancho de módulo buscado (0.33 mm) y el mínimo legible (0.25 mm). */
export const BARCODE_TARGET_MODULE_MM = 0.33
export const BARCODE_MIN_MODULE_MM = 0.25
/** Alto de las barras (mm). */
export const BARCODE_BAR_HEIGHT_MM = 12
/** Separación entre columnas de la rejilla y margen interior de cada celda (pt). */
export const BARCODE_GUTTER_PT = 10
export const BARCODE_CELL_PAD_PT = 5
/** Renglones máximos del título y de la descripción de una celda (lo demás se corta con "..."). */
export const BARCODE_TITLE_MAX_LINES = 2
export const BARCODE_DESCRIPTION_MAX_LINES = 2
/** Valores que se nombran en cada aviso del final; el resto va como "y N más". */
export const BARCODE_NOTICE_MAX_ITEMS = 200

/** Columnas pedidas: automático (3 → 2 → 1) o un máximo (la UI ofrece automático y 2; 1 sirve para comparar en pruebas). */
export type BarcodeColumnsOption = 'auto' | 1 | 2 | 3

export interface BarcodeReportRow {
  /** Lo que codifica el código de barras, exacto (SKU, código de posición). */
  value: string
  /** Primera línea en negrita; por defecto `value`. */
  title?: string
  /** Descripción (hasta 2 renglones). */
  description?: string | null
  /** Línea pequeña en gris (zona y almacén, "Inactivo"…). */
  meta?: string | null
}

export interface BarcodeReportGroup {
  /** Título del grupo (ya traducido, con su cantidad si se quiere). */
  title: string
  rows: readonly BarcodeReportRow[]
}

export interface BarcodeReportSpec {
  title: string
  subtitle?: string
  company?: string | null
  user?: string | null
  generatedAt?: Date
  locale: string
  filters: readonly ReportFilter[]
  /** Grupos en el orden en que se imprimen (cada uno con sus elementos ya ordenados). */
  groups: readonly BarcodeReportGroup[]
  /** Columnas de la rejilla (por defecto automático). */
  columns?: BarcodeColumnsOption
  /** Avisos de arriba (p. ej. la lista se cortó en el tope de lectura). */
  notices?: readonly string[]
  /** Texto sin elementos. */
  emptyText?: string
}

export type PlannedBarcodeCell =
  | { status: 'ok'; row: BarcodeReportRow; symbol: Code128Symbol; modulePt: number }
  | { status: 'tooWide'; row: BarcodeReportRow; symbol: Code128Symbol }

export interface PlannedBarcodeGroup {
  title: string
  cells: PlannedBarcodeCell[]
}

export interface BarcodeReportPlan {
  /** Columnas de la rejilla. */
  columns: 1 | 2 | 3
  /** Máximo de columnas que se pidió (3 en automático). */
  requestedColumns: 1 | 2 | 3
  /** Grupos con elementos (los vacíos, por omisiones, no salen). */
  groups: PlannedBarcodeGroup[]
  /** Valores que no caben ni en una columna (salen como celda sin código). */
  tooWide: string[]
  /** Valores omitidos por caracteres no admitidos (o vacíos), con esos caracteres. */
  unsupported: { value: string; chars: string[] }[]
}

// ---------------------------------------------------------------------------------------------------------------------
// Lógica pura
// ---------------------------------------------------------------------------------------------------------------------

/** Ancho útil (pt) para el código en una celda de una rejilla de `columns` columnas, en una página de `pageWidthPt`. */
export function barcodeCellCodeWidth(columns: number, pageWidthPt: number): number {
  const inner = pageWidthPt - REPORT_LAYOUT.margin * 2
  const cell = (inner - BARCODE_GUTTER_PT * (columns - 1)) / columns
  return cell - BARCODE_CELL_PAD_PT * 2
}

/**
 * Ancho de módulo (pt) para un símbolo de `symbolModules` módulos en `columnPt`, contando la zona de silencio a cada lado:
 * el buscado (0.33 mm) si cabe; si no, el que llene el ancho mientras no baje de 0.25 mm; null = no cabe.
 */
export function barcodeModuleWidth(symbolModules: number, columnPt: number): number | null {
  const target = BARCODE_TARGET_MODULE_MM * PT_PER_MM
  const min = BARCODE_MIN_MODULE_MM * PT_PER_MM
  const fit = columnPt / (symbolModules + 2 * CODE128_QUIET_ZONE)
  const w = Math.min(target, fit)
  return w + 1e-9 >= min ? w : null
}

/**
 * Columnas de la rejilla: la mayor (desde la pedida —3 en automático— hasta 1) en la que TODOS los símbolos caben a ≥ 0.25 mm.
 * Si ni en 1 caben todos, 1 (los que no caben salen "No cabe").
 */
export function chooseBarcodeColumns(symbolWidths: readonly number[], option: BarcodeColumnsOption, pageWidthPt: number): 1 | 2 | 3 {
  const max = option === 'auto' ? 3 : option
  for (let c = max; c >= 1; c--) {
    const width = barcodeCellCodeWidth(c, pageWidthPt)
    if (symbolWidths.every((w) => barcodeModuleWidth(w, width) !== null)) return c as 1 | 2 | 3
  }
  return 1
}

/** Clasifica los elementos de cada grupo (con código, "No cabe" u omitidos) y elige las columnas. */
export function planBarcodeReport(groups: readonly BarcodeReportGroup[], option: BarcodeColumnsOption, pageWidthPt: number): BarcodeReportPlan {
  const unsupported: BarcodeReportPlan['unsupported'] = []
  const encoded = groups.map((g) => ({
    title: g.title,
    items: g.rows.flatMap((row) => {
      const chars = code128Unsupported(row.value)
      if (row.value.length === 0 || chars.length > 0) {
        unsupported.push({ value: row.value, chars })
        return []
      }
      return [{ row, symbol: encodeCode128(row.value) }]
    }),
  }))
  const all = encoded.flatMap((g) => g.items)
  const columns = chooseBarcodeColumns(
    all.map((i) => i.symbol.width),
    option,
    pageWidthPt,
  )
  const codeWidth = barcodeCellCodeWidth(columns, pageWidthPt)
  const tooWide: string[] = []
  const planned = encoded
    .filter((g) => g.items.length > 0)
    .map((g) => ({
      title: g.title,
      cells: g.items.map(({ row, symbol }): PlannedBarcodeCell => {
        const modulePt = barcodeModuleWidth(symbol.width, codeWidth)
        if (modulePt === null) {
          tooWide.push(row.value)
          return { status: 'tooWide', row, symbol }
        }
        return { status: 'ok', row, symbol, modulePt }
      }),
    }))
  return { columns, requestedColumns: option === 'auto' ? 3 : option, groups: planned, tooWide, unsupported }
}

/** Una pieza de la rejilla ya ubicada en su página: título de grupo (o su continuación) o una fila de celdas. */
export type GridPlacement =
  | { kind: 'title'; group: number; continued: boolean; y: number; height: number }
  | { kind: 'row'; group: number; row: number; y: number; height: number }

/**
 * Reparte los grupos en páginas: cada grupo = un título (`titleHeight`) y sus filas de celdas (`rowHeights`). La primera
 * página tiene `firstAvailable` pt de cuerpo y las demás `pageAvailable`; `y` es relativa al inicio del cuerpo de su
 * página. Reglas: una fila nunca se parte; un título no queda al pie sin al menos una fila debajo (pasa con ella a la
 * página siguiente); un grupo que sigue en otra página la empieza con su título de continuación. Una fila más alta que
 * una página entera va sola en la suya.
 */
export function paginateBarcodeGrid(
  groups: readonly { rowHeights: readonly number[] }[],
  titleHeight: number,
  firstAvailable: number,
  pageAvailable: number,
): GridPlacement[][] {
  const pages: GridPlacement[][] = [[]]
  let used = 0
  let available = firstAvailable
  const newPage = () => {
    pages.push([])
    used = 0
    available = pageAvailable
  }
  const page = () => pages[pages.length - 1]
  groups.forEach((g, gi) => {
    g.rowHeights.forEach((h, ri) => {
      const first = ri === 0
      // el título va con su primera fila: si los dos no caben (y la página ya tiene algo), página nueva
      const need = (first ? titleHeight : 0) + h
      // (una página vacía solo se salta si es la primera, cuyo cuerpo es más corto por el encabezado)
      if (used + need > available && (page().length > 0 || available < pageAvailable)) newPage()
      if (first || page().length === 0) {
        page().push({ kind: 'title', group: gi, continued: !first, y: used, height: titleHeight })
        used += titleHeight
      }
      page().push({ kind: 'row', group: gi, row: ri, y: used, height: h })
      used += h
    })
  })
  return pages
}

type Translate = (key: string, params?: Record<string, string | number>) => string

/** Un carácter para el aviso: tal cual si es imprimible en el PDF (Latin-1), si no su código ("U+1F4E6"). */
function charLabel(ch: string): string {
  const c = ch.codePointAt(0) ?? 0
  if (c >= 0xa0 && c <= 0xff) return ch
  return `U+${c.toString(16).toUpperCase().padStart(4, '0')}`
}

/** Lista "A, B, C" o "A, B, C y N más" (hasta `BARCODE_NOTICE_MAX_ITEMS`). */
function listItems(items: readonly string[], t: Translate): string {
  if (items.length <= BARCODE_NOTICE_MAX_ITEMS) return items.join(', ')
  return t('ui.barcodeReport.more', { list: items.slice(0, BARCODE_NOTICE_MAX_ITEMS).join(', '), count: formatNumber(items.length - BARCODE_NOTICE_MAX_ITEMS) })
}

/** Avisos del final: valores que no caben (salen sin código) y valores omitidos con sus caracteres no admitidos. */
export function barcodeEndNotices(plan: Pick<BarcodeReportPlan, 'tooWide' | 'unsupported'>, t: Translate = translate): string[] {
  const out: string[] = []
  if (plan.tooWide.length > 0) {
    out.push(t('ui.barcodeReport.tooWideNotice', { count: formatNumber(plan.tooWide.length), list: listItems(plan.tooWide, t) }))
  }
  if (plan.unsupported.length > 0) {
    const items = plan.unsupported.map((u) =>
      u.value.length === 0 ? t('ui.barcodeReport.emptyValue') : t('ui.barcodeReport.unsupportedItem', { value: u.value, chars: u.chars.map(charLabel).join(' ') }),
    )
    out.push(t('ui.barcodeReport.unsupportedNotice', { count: formatNumber(plan.unsupported.length), list: listItems(items, t) }))
  }
  return out
}

/** Tarjetas del resumen: elementos del reporte, con código, que no caben y omitidos (naranja si hay alguno). */
export function barcodeReportSummary(plan: BarcodeReportPlan, t: Translate = translate): ReportSummaryItem[] {
  const cells = plan.groups.flatMap((g) => g.cells)
  const ok = cells.filter((c) => c.status === 'ok').length
  return [
    { label: t('ui.barcodeReport.summary.rows'), value: formatNumber(cells.length) },
    { label: t('ui.barcodeReport.summary.codes'), value: formatNumber(ok) },
    { label: t('ui.barcodeReport.summary.tooWide'), value: formatNumber(plan.tooWide.length), tone: plan.tooWide.length > 0 ? 'money' : 'flow' },
    { label: t('ui.barcodeReport.summary.omitted'), value: formatNumber(plan.unsupported.length), tone: plan.unsupported.length > 0 ? 'money' : 'flow' },
  ]
}

/** Avisos de arriba: los del reporte, las columnas si se bajó de lo pedido, y uno que remite a los avisos del final. */
export function barcodeTopNotices(spec: Pick<BarcodeReportSpec, 'notices'>, plan: BarcodeReportPlan, t: Translate = translate): string[] {
  const out = [...(spec.notices ?? [])]
  if (plan.columns < plan.requestedColumns && plan.groups.length > 0) {
    out.push(t(plan.columns === 1 ? 'ui.barcodeReport.columnsNoticeOne' : 'ui.barcodeReport.columnsNotice', { columns: plan.columns }))
  }
  if (plan.tooWide.length > 0 || plan.unsupported.length > 0) out.push(t('ui.barcodeReport.seeEndNotices'))
  return out
}

// ---------------------------------------------------------------------------------------------------------------------
// Dibujo
// ---------------------------------------------------------------------------------------------------------------------

const GROUP_TITLE_H = 22
/** Separación vertical entre filas de celdas y margen vertical dentro de la celda (pt). */
const ROW_GAP = 6
const CELL_PAD_Y = 4
const TITLE_SIZE = 9
const TITLE_LINE = 10.5
const DESC_SIZE = 7.5
const DESC_LINE = 9
const META_SIZE = 7
const META_LINE = 8.5
const VALUE_SIZE = 7.5
const VALUE_LINE = 9
const TEXT_GAP = 3
const BAR_GAP = 1.5

interface CellText {
  title: string[]
  description: string[]
  meta: string[]
}

/** Corta a `max` renglones, con "..." al final del último si sobraba texto (con la letra ya puesta en `doc`). */
function clampLines(doc: jsPDF, lines: string[], max: number, width: number): string[] {
  if (lines.length <= max) return lines
  const kept = lines.slice(0, max)
  let last = kept[max - 1]
  while (last.length > 0 && doc.getTextWidth(`${last}...`) > width) last = last.slice(0, -1)
  kept[max - 1] = `${last.trimEnd()}...`
  return kept
}

function cellText(doc: jsPDF, row: BarcodeReportRow, width: number): CellText {
  doc.setFont('helvetica', 'bold')
  doc.setFontSize(TITLE_SIZE)
  const title = clampLines(doc, doc.splitTextToSize(pdfSafeText(row.title ?? row.value), width) as string[], BARCODE_TITLE_MAX_LINES, width)
  doc.setFont('helvetica', 'normal')
  doc.setFontSize(DESC_SIZE)
  const description = row.description?.trim()
    ? clampLines(doc, doc.splitTextToSize(pdfSafeText(row.description.trim()), width) as string[], BARCODE_DESCRIPTION_MAX_LINES, width)
    : []
  doc.setFontSize(META_SIZE)
  const meta = row.meta?.trim() ? clampLines(doc, doc.splitTextToSize(pdfSafeText(row.meta.trim()), width) as string[], 2, width) : []
  return { title, description, meta }
}

function textHeight(text: CellText): number {
  return text.title.length * TITLE_LINE + text.description.length * DESC_LINE + text.meta.length * META_LINE
}

/** Alto de la parte del código (barras + valor, o el valor partido + "No cabe"). */
function codeBlockHeight(cell: PlannedBarcodeCell, valueLines: number): number {
  if (cell.status === 'ok') return BARCODE_BAR_HEIGHT_MM * PT_PER_MM + BAR_GAP + VALUE_LINE
  return valueLines * VALUE_LINE + 12
}

export interface RenderBarcodeReportOptions {
  logo?: string | null
  /** false = sin comprimir (pruebas). Por defecto true. */
  compress?: boolean
}

/** Arma el documento en memoria (no descarga). */
export async function renderBarcodeReportPdf(spec: BarcodeReportSpec, options: RenderBarcodeReportOptions = {}): Promise<jsPDF> {
  const { jsPDF: JsPdf } = await import('jspdf')
  const doc = new JsPdf({ orientation: 'portrait', unit: 'pt', format: 'letter', compress: options.compress ?? true })
  const W = doc.internal.pageSize.getWidth()
  const H = doc.internal.pageSize.getHeight()
  const M = REPORT_LAYOUT.margin
  const inner = W - M * 2
  const plan = planBarcodeReport(spec.groups, spec.columns ?? 'auto', W)
  const cols = plan.columns
  const cellW = (inner - BARCODE_GUTTER_PT * (cols - 1)) / cols
  const textW = cellW - BARCODE_CELL_PAD_PT * 2
  const { ink, muted, line, navy, noticeInk } = REPORT_COLORS

  doc.setProperties({ title: pdfSafeText(spec.title), subject: pdfSafeText(spec.subtitle ?? spec.title), author: pdfSafeText(spec.user ?? ''), creator: 'Teikem' })

  const startY = drawReportHeader(
    doc,
    {
      title: spec.title,
      subtitle: spec.subtitle,
      company: spec.company,
      user: spec.user,
      generatedAt: spec.generatedAt,
      locale: spec.locale,
      filters: spec.filters,
      summary: barcodeReportSummary(plan),
      notices: barcodeTopNotices(spec, plan),
    },
    options.logo,
  )

  const bodyBottom = H - REPORT_LAYOUT.footerHeight - 14
  const pageTop = REPORT_LAYOUT.thinBandHeight + 20

  // medidas de cada celda y de cada fila de celdas (alto = la celda más alta de la fila)
  const measured = plan.groups.map((g) => {
    const cells = g.cells.map((c) => {
      const text = cellText(doc, c.row, textW)
      doc.setFont('courier', 'normal')
      doc.setFontSize(VALUE_SIZE)
      const valueLines = c.status === 'ok' ? [c.row.value] : (doc.splitTextToSize(c.row.value, textW) as string[])
      const height = CELL_PAD_Y + 1 + textHeight(text) + TEXT_GAP + codeBlockHeight(c, valueLines.length) + CELL_PAD_Y
      return { cell: c, text, valueLines, height }
    })
    const rows: (typeof cells)[] = []
    for (let i = 0; i < cells.length; i += cols) rows.push(cells.slice(i, i + cols))
    return { title: g.title, rows, rowHeights: rows.map((r) => Math.max(...r.map((c) => c.height)) + ROW_GAP) }
  })
  const pages = paginateBarcodeGrid(measured, GROUP_TITLE_H, bodyBottom - startY, bodyBottom - pageTop)

  pages.forEach((placements, p) => {
    if (p > 0) doc.addPage('letter', 'portrait')
    const top = p === 0 ? startY : pageTop
    for (const item of placements) {
      const g = measured[item.group]
      const y = top + item.y
      if (item.kind === 'title') {
        // título del grupo a todo el ancho (con "(continuación)" si el grupo viene de la página anterior)
        const text = item.continued ? translate('ui.barcodeReport.continued', { title: g.title }) : g.title
        doc.setFillColor(232, 239, 251)
        doc.rect(M, y + 2, inner, GROUP_TITLE_H - 8, 'F')
        doc.setFont('helvetica', 'bold')
        doc.setFontSize(9)
        doc.setTextColor(...navy)
        doc.text((doc.splitTextToSize(pdfSafeText(text), inner - 16) as string[])[0] ?? '', M + 8, y + 12)
        continue
      }
      const cellH = item.height - ROW_GAP
      g.rows[item.row].forEach((m, ci) => {
        const x = M + ci * (cellW + BARCODE_GUTTER_PT)
        // recuadro fino de la celda (guía de corte); queda fuera de la zona de silencio
        doc.setDrawColor(...line)
        doc.setLineWidth(0.6)
        doc.roundedRect(x, y, cellW, cellH, 3, 3, 'S')
        const tx = x + BARCODE_CELL_PAD_PT
        let ty = y + CELL_PAD_Y + 1 + TITLE_LINE - 2.5
        doc.setFont('helvetica', 'bold')
        doc.setFontSize(TITLE_SIZE)
        doc.setTextColor(...ink)
        doc.text(m.text.title, tx, ty)
        ty += m.text.title.length * TITLE_LINE
        if (m.text.description.length > 0) {
          doc.setFont('helvetica', 'normal')
          doc.setFontSize(DESC_SIZE)
          doc.setTextColor(...ink)
          doc.text(m.text.description, tx, ty - 1.5)
          ty += m.text.description.length * DESC_LINE
        }
        if (m.text.meta.length > 0) {
          doc.setFont('helvetica', 'normal')
          doc.setFontSize(META_SIZE)
          doc.setTextColor(...muted)
          doc.text(m.text.meta, tx, ty - 2)
        }
        // el código va abajo de la celda, centrado (las celdas de una fila alinean sus códigos por abajo)
        const blockH = codeBlockHeight(m.cell, m.valueLines.length)
        const codeTop = y + cellH - CELL_PAD_Y - blockH
        const cx = x + cellW / 2
        doc.setFont('courier', 'normal')
        doc.setFontSize(VALUE_SIZE)
        doc.setTextColor(...ink)
        if (m.cell.status === 'ok') {
          const mw = m.cell.modulePt
          const barH = BARCODE_BAR_HEIGHT_MM * PT_PER_MM
          const x0 = cx - (m.cell.symbol.width * mw) / 2
          doc.setFillColor(0, 0, 0)
          for (const bar of m.cell.symbol.bars) doc.rect(x0 + bar.x * mw, codeTop, bar.width * mw, barH, 'F')
          doc.text(m.cell.row.value, cx, codeTop + barH + BAR_GAP + VALUE_LINE - 2, { align: 'center' })
        } else {
          doc.text(m.valueLines, cx, codeTop + VALUE_LINE - 2, { align: 'center' })
          doc.setFont('helvetica', 'bold')
          doc.setFontSize(7.5)
          doc.setTextColor(...noticeInk)
          doc.text(pdfSafeText(translate('ui.barcodeReport.tooWide')), cx, codeTop + m.valueLines.length * VALUE_LINE + 8, { align: 'center', maxWidth: textW })
        }
      })
    }
  })

  // cuerpo sin elementos: el texto de vacío; luego los avisos del final (si no caben, siguen en la página siguiente)
  const lastPage = pages[pages.length - 1]
  const lastItem = lastPage[lastPage.length - 1]
  let y = lastItem ? (pages.length === 1 ? startY : pageTop) + lastItem.y + lastItem.height : startY
  if (plan.groups.length === 0) {
    doc.setFont('helvetica', 'italic')
    doc.setFontSize(9)
    doc.setTextColor(...muted)
    doc.text(pdfSafeText(spec.emptyText ?? translate('ui.report.empty')), M + inner / 2, y + 16, { align: 'center' })
    y += 30
  }
  y += 4
  for (const notice of barcodeEndNotices(plan)) {
    let lines = reportNoticeLines(doc, notice)
    while (lines.length > 0) {
      const fit = Math.floor((bodyBottom - y - 10) / 11)
      if (fit < 1) {
        doc.addPage('letter', 'portrait')
        y = pageTop
        continue
      }
      const chunk = lines.slice(0, fit)
      drawReportNotice(doc, chunk, y)
      y += reportNoticeHeight(chunk.length) + 6
      lines = lines.slice(fit)
    }
  }

  drawReportPageChrome(doc, spec)
  return doc
}

/** Arma el reporte (con el logo si se puede rasterizar) y lo descarga como `reportFileName(spec)`. */
export async function downloadBarcodeReportPdf(spec: BarcodeReportSpec): Promise<void> {
  const logo = await loadReportLogo()
  const doc = await renderBarcodeReportPdf(spec, { logo })
  doc.save(reportFileName(spec, spec.generatedAt))
}
