// Exportación agrupada del kit: filas madre con filas hijas (p. ej. recibos con sus líneas). Es opcional y se activa con
// `exportChildren` en `ListPager` / `DataTable` (o en `exportTable`); sin él todo sigue igual.
// - Excel y CSV: una fila POR HIJA repitiendo los datos de la madre (columnas de la madre + columnas de las hijas); una
//   madre sin hijas sale como una fila con las columnas de hija vacías (`flattenGroupedExport`).
// - PDF (carta horizontal): un bloque por madre con una banda de encabezado (las primeras `titleColumns` columnas de la
//   madre como título y el resto como "etiqueta: valor") y debajo una tablita con sus hijas (encabezado oscuro que se
//   repite si el bloque cruza de página, cebra, números a la derecha con separadores del idioma y signo en las columnas
//   `signed`); una madre sin hijas muestra `emptyText`. Arriba, el encabezado de `drawPdfHeading` (compañía, título,
//   "Generado el …" y filtros); pie con el título y "Página X de Y" en cada página. El Excel lleva el mismo encabezado.
// Las hijas se preparan con `exportChildren` (exportChildren.ts, liviano: lo importan las pantallas); este módulo lo carga
// `exportTable` bajo demanda. Las funciones de armado (buildGroupedExportData, flattenGroupedExport, formatExportNumber)
// son puras y se prueban sin DOM; `renderGroupedPdf` arma el documento en memoria y `exportGroupedTable` descarga.
import type { jsPDF } from 'jspdf'
import { numberLocale, t as translate } from '../i18n'
import type { ExportChildren } from './exportChildren'
import {
  buildExportData,
  downloadCsv,
  downloadXlsx,
  drawPdfHeading,
  exportFileName,
  headingFromOptions,
  pdfSafeText,
  type ExportableColumn,
  exportDateText,
  isExportDate,
  type ExportCell,
  type ExportData,
  type ExportFormat,
  type ExportHeadingSpec,
  type ExportOptions,
  type ExportTableOptions,
} from './exportTable'

/** Un campo de la banda del PDF ("Tipo: Ciego"). */
export interface GroupedField {
  label: string
  value: ExportCell
  numeric: boolean
  signed: boolean
}

/** Bloque de una madre: título (valores de las columnas de título), campos de la banda y filas de sus hijas. */
export interface GroupedBlock {
  title: ExportCell[]
  fields: GroupedField[]
  /** Fila de la madre tal cual (para Excel/CSV). */
  parent: ExportCell[]
  children: ExportCell[][]
}

export interface GroupedExportData {
  /** Encabezados de la madre (sin columnas `exportable: false`). */
  parent: Pick<ExportData, 'headers' | 'numeric'> & { signed: boolean[] }
  child: { headers: string[]; numeric: boolean[]; signed: boolean[] }
  blocks: GroupedBlock[]
  emptyText: string
}

/** Arma los bloques: la madre con las reglas de `buildExportData` y sus hijas con las de `exportChildren`. */
export function buildGroupedExportData<T>(
  columns: readonly ExportableColumn<T>[],
  rows: readonly T[],
  children: ExportChildren<T>,
  opts: ExportOptions = {},
): GroupedExportData {
  const parent = buildExportData(columns, rows, opts)
  const signed = parent.signed ?? parent.headers.map(() => false)
  const titleCount = Math.min(children.titleColumns, parent.headers.length)
  const blocks = rows.map((row, i) => {
    const cells = parent.rows[i]
    return {
      title: cells.slice(0, titleCount),
      fields: parent.headers.slice(titleCount).map((label, j) => ({
        label,
        value: cells[titleCount + j],
        numeric: parent.numeric[titleCount + j],
        signed: signed[titleCount + j],
      })),
      parent: cells,
      children: children.rows(row, opts),
    }
  })
  return {
    parent: { headers: parent.headers, numeric: parent.numeric, signed },
    child: { headers: children.headers, numeric: children.numeric, signed: children.signed },
    blocks,
    emptyText: children.emptyText,
  }
}

/** Excel/CSV: una fila por hija con los datos de la madre repetidos; una madre sin hijas = una fila con las de hija vacías. */
export function flattenGroupedExport(data: GroupedExportData): ExportData {
  const blank = data.child.headers.map((): ExportCell => null)
  return {
    headers: [...data.parent.headers, ...data.child.headers],
    numeric: [...data.parent.numeric, ...data.child.numeric],
    signed: [...data.parent.signed, ...data.child.signed],
    rows: data.blocks.flatMap((b) => (b.children.length === 0 ? [[...b.parent, ...blank]] : b.children.map((c) => [...b.parent, ...c]))),
  }
}

/** Número del PDF con los separadores del idioma (hasta 3 decimales); `signed` = "+5" / "-3" (0 sin signo). */
export function formatExportNumber(value: number, locale: string | undefined, signed = false): string {
  return new Intl.NumberFormat(numberLocale(locale), { maximumFractionDigits: 3, useGrouping: 'always', signDisplay: signed ? 'exceptZero' : 'auto' }).format(value)
}

/** Texto de una celda del PDF: números formateados, texto seguro para Latin-1, vacío = `empty`. */
export function groupedCellText(value: ExportCell, locale: string | undefined, signed: boolean, empty = ''): string {
  if (value === null) return empty
  if (typeof value === 'number') return formatExportNumber(value, locale, signed)
  if (isExportDate(value)) return pdfSafeText(exportDateText(value, locale))
  return pdfSafeText(value)
}

// ---------------------------------------------------------------------------------------------------------------------
// PDF
// ---------------------------------------------------------------------------------------------------------------------

type Rgb = [number, number, number]
// mismos tonos que el PDF de `downloadPdf` (encabezado oscuro, cebra) y la banda de grupo de `reportPdf`
const HEAD: Rgb = [38, 50, 72]
const ZEBRA: Rgb = [245, 247, 250]
const BAND_BG: Rgb = [232, 239, 251]
const BAND_INK: Rgb = [11, 44, 102]
const FIELD_BG: Rgb = [247, 249, 252]
const INK: Rgb = [24, 33, 54]
const MUTED: Rgb = [91, 104, 128]
const LINE: Rgb = [221, 228, 240]

const MARGIN = 32
const FOOTER_H = 24
/** Pares "etiqueta: valor" por renglón de la banda. */
const FIELDS_PER_ROW = 4
/** Ancho de la etiqueta de cada par de la banda (pt). */
const BAND_LABEL_W = 96
/** Ancho mínimo de una columna numérica de las hijas (pt). */
const NUMBER_MIN_W = 64

/** Encabezado (compañía, título, fecha y filtros, como `drawPdfHeading`) y compresión. */
export interface RenderGroupedOptions extends ExportHeadingSpec {
  /** false = sin comprimir (pruebas: el texto queda legible en el PDF). Por defecto true. */
  compress?: boolean
}

interface AutoTableDoc {
  lastAutoTable?: { finalY?: number } | false
}

/** Arma el PDF agrupado en memoria (carta horizontal); no descarga. */
export async function renderGroupedPdf(data: GroupedExportData, options: RenderGroupedOptions = {}): Promise<jsPDF> {
  const [{ jsPDF: JsPdf }, { autoTable }] = await Promise.all([import('jspdf'), import('jspdf-autotable')])
  const locale = options.locale
  const doc = new JsPdf({ orientation: 'landscape', unit: 'pt', format: 'letter', compress: options.compress ?? true })
  const W = doc.internal.pageSize.getWidth()
  const H = doc.internal.pageSize.getHeight()
  const bottom = FOOTER_H + 14
  const finalY = () => {
    const last = (doc as unknown as AutoTableDoc).lastAutoTable
    return last && typeof last.finalY === 'number' ? last.finalY : MARGIN
  }

  // ---- compañía, título, fecha de generación y filtros (página 1; el mismo encabezado que el PDF de las tablas) ----
  let y = drawPdfHeading(doc, options, MARGIN, MARGIN - 6, W - MARGIN * 2)

  const childCols = data.child.headers.length
  // números a la derecha, sin partir y con un ancho mínimo (no quedan pegados a la columna de texto de al lado)
  const numberPadding = { top: 3, bottom: 3, left: 3, right: 12 }
  const childStyles: Record<number, { halign: 'right'; cellWidth: 'wrap'; minCellWidth: number; cellPadding: typeof numberPadding }> = {}
  data.child.numeric.forEach((n, i) => {
    if (n) childStyles[i] = { halign: 'right', cellWidth: 'wrap', minCellWidth: NUMBER_MIN_W, cellPadding: numberPadding }
  })
  // banda con anchos fijos: todos los bloques alinean sus pares "etiqueta: valor"
  const pairCols = FIELDS_PER_ROW * 2
  const pairW = (W - MARGIN * 2) / FIELDS_PER_ROW
  const bandStyles: Record<number, { fontStyle?: 'bold'; textColor?: Rgb; cellWidth: number; fontSize?: number }> = {}
  for (let i = 0; i < pairCols; i += 2) {
    bandStyles[i] = { fontStyle: 'bold', textColor: MUTED, cellWidth: BAND_LABEL_W, fontSize: 7.5 }
    bandStyles[i + 1] = { cellWidth: pairW - BAND_LABEL_W }
  }

  if (data.blocks.length === 0) {
    doc.setFont('helvetica', 'italic')
    doc.setFontSize(9)
    doc.setTextColor(...MUTED)
    doc.text(pdfSafeText(translate('ui.report.empty')), MARGIN, y + 16)
  }

  for (const block of data.blocks) {
    const fieldRows = Math.ceil(block.fields.length / FIELDS_PER_ROW)
    // la banda no se separa de su primera hija: si no cabe (banda + encabezado + un renglón), página nueva
    const needed = 20 + fieldRows * 15 + 36
    if (y + needed > H - bottom) {
      doc.addPage()
      y = MARGIN
    }

    // ---- banda: título + "etiqueta: valor" ----
    const titleText = block.title.map((v) => groupedCellText(v, locale, false)).filter(Boolean).join('  ·  ')
    const band: { content: string; colSpan?: number; styles?: Record<string, unknown> }[][] = [
      [{ content: titleText, colSpan: pairCols, styles: { fillColor: BAND_BG, textColor: BAND_INK, fontStyle: 'bold', fontSize: 10, cellPadding: { top: 5, bottom: 5, left: 6, right: 6 } } }],
    ]
    for (let r = 0; r < fieldRows; r++) {
      const cells: { content: string }[] = []
      for (let k = 0; k < FIELDS_PER_ROW; k++) {
        const f = block.fields[r * FIELDS_PER_ROW + k]
        cells.push({ content: f ? pdfSafeText(f.label) : '' })
        cells.push({ content: f ? groupedCellText(f.value, locale, f.signed, '-') : '' })
      }
      band.push(cells)
    }
    autoTable(doc, {
      body: band,
      startY: y,
      theme: 'plain',
      margin: { left: MARGIN, right: MARGIN, top: MARGIN, bottom },
      styles: { font: 'helvetica', fontSize: 8, cellPadding: { top: 2.5, bottom: 2.5, left: 6, right: 4 }, textColor: INK, fillColor: FIELD_BG, overflow: 'linebreak' },
      columnStyles: bandStyles,
      pageBreak: 'avoid',
      rowPageBreak: 'avoid',
    })

    // ---- hijas ----
    if (block.children.length === 0) {
      autoTable(doc, {
        body: [[{ content: pdfSafeText(data.emptyText), colSpan: Math.max(1, childCols) }]],
        startY: finalY(),
        theme: 'plain',
        margin: { left: MARGIN, right: MARGIN, top: MARGIN, bottom },
        styles: { font: 'helvetica', fontSize: 8, fontStyle: 'italic', textColor: MUTED, cellPadding: { top: 5, bottom: 5, left: 6, right: 6 }, lineWidth: { top: 0, bottom: 0.5, left: 0, right: 0 }, lineColor: LINE },
      })
    } else {
      autoTable(doc, {
        head: [
          data.child.headers.map((h, i) => ({
            content: pdfSafeText(h),
            styles: data.child.numeric[i] ? { halign: 'right' as const, cellPadding: numberPadding } : { halign: 'left' as const },
          })),
        ],
        body: block.children.map((r) => r.map((v, i) => groupedCellText(v, locale, data.child.signed[i]))),
        startY: finalY(),
        margin: { left: MARGIN, right: MARGIN, top: MARGIN, bottom },
        styles: { font: 'helvetica', fontSize: 8, cellPadding: 3, overflow: 'linebreak', textColor: INK },
        headStyles: { fillColor: HEAD, textColor: 255, fontStyle: 'bold' },
        alternateRowStyles: { fillColor: ZEBRA },
        columnStyles: childStyles,
        showHead: 'everyPage',
        rowPageBreak: 'avoid',
      })
    }
    y = finalY() + 14
  }

  // ---- pie en todas las páginas: título y "Página X de Y" ----
  const pages = doc.getNumberOfPages()
  for (let i = 1; i <= pages; i++) {
    doc.setPage(i)
    doc.setDrawColor(...LINE)
    doc.setLineWidth(0.6)
    doc.line(MARGIN, H - FOOTER_H, W - MARGIN, H - FOOTER_H)
    doc.setFont('helvetica', 'normal')
    doc.setFontSize(7.5)
    doc.setTextColor(...MUTED)
    if (options.title) doc.text(pdfSafeText(options.title), MARGIN, H - FOOTER_H + 12)
    doc.text(pdfSafeText(translate('ui.report.page', { page: i, pages })), W - MARGIN, H - FOOTER_H + 12, { align: 'right' })
  }
  return doc
}

/** Arma y descarga la exportación agrupada en el formato pedido (lo llama `exportTable` cuando recibe `children`). */
export async function exportGroupedTable<T>(
  format: ExportFormat,
  columns: readonly ExportableColumn<T>[],
  rows: readonly T[],
  children: ExportChildren<T>,
  opts: ExportTableOptions = {},
): Promise<void> {
  const data = buildGroupedExportData(columns, rows, children, opts)
  const fileName = exportFileName(opts.title, format, opts.date)
  // CSV: solo la tabla; Excel: encabezado (compañía, título, fecha, filtros), fila en blanco y una fila por hija
  if (format === 'csv') downloadCsv(flattenGroupedExport(data), fileName)
  else if (format === 'xlsx') await downloadXlsx(flattenGroupedExport(data), fileName, headingFromOptions(opts))
  else {
    const doc = await renderGroupedPdf(data, headingFromOptions(opts))
    doc.save(fileName)
  }
}
