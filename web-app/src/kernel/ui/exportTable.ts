// Exportación de tablas del kit a CSV, Excel (.xlsx) y PDF, 100 % en el cliente, sobre las filas que la tabla tiene
// cargadas. Las funciones de armado (buildExportData, toCsv, exportFileName…) son puras y se prueban sin DOM; las de
// descarga cargan SheetJS / jsPDF bajo demanda (import dinámico) para no inflar el paquete inicial.
import type { jsPDF } from 'jspdf'
import { isValidElement, type ReactNode } from 'react'
import { parseApiDate } from '../api/dates'
import { TENANT_TIME_ZONE } from '../api/tenantZone'
import { t as translate } from '../i18n/i18n'
import type { ExportChildren } from './exportChildren'

export type ExportFormat = 'xlsx' | 'csv' | 'pdf'
export const EXPORT_FORMATS: readonly ExportFormat[] = ['xlsx', 'csv', 'pdf']

/**
 * Fecha exportada (pedido del dueño: Excel y CSV deben leerla como FECHA, no como texto). `withTime` = fecha y hora (se
 * muestra en la hora de la compañía, Puerto Rico); sin hora = día de calendario.
 */
export interface ExportDate {
  kind: 'date'
  value: Date
  withTime: boolean
}

/** Valor de una celda exportada: texto, número (Excel lo trata como número), fecha o vacío. */
export type ExportCell = string | number | ExportDate | null

export function isExportDate(v: unknown): v is ExportDate {
  return typeof v === 'object' && v !== null && (v as ExportDate).kind === 'date'
}

// fecha ISO del API: "2026-09-30", "2026-09-30T14:03:00(.123)(Z|±hh:mm)"
const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/
const ISO_DATE_TIME = /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:[zZ]|[+-]\d{2}:?\d{2})?$/

/** Una fecha (Date o cadena ISO del API) como celda de fecha; null si no lo es. */
export function toExportDate(v: unknown): ExportDate | null {
  if (v instanceof Date) return Number.isNaN(v.getTime()) ? null : { kind: 'date', value: v, withTime: true }
  if (typeof v !== 'string') return null
  const s = v.trim()
  if (ISO_DATE.test(s)) {
    const [y, m, d] = s.split('-').map(Number)
    return { kind: 'date', value: new Date(y, m - 1, d), withTime: false }
  }
  if (ISO_DATE_TIME.test(s)) {
    const date = parseApiDate(s.replace(' ', 'T'))
    return Number.isNaN(date.getTime()) ? null : { kind: 'date', value: date, withTime: true }
  }
  return null
}

/** Año, mes, día, hora, minuto y segundo de la fecha: con hora, en la zona de la compañía; sin hora, tal cual. */
function dateParts(d: ExportDate): [number, number, number, number, number, number] {
  if (!d.withTime) return [d.value.getFullYear(), d.value.getMonth() + 1, d.value.getDate(), 0, 0, 0]
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: TENANT_TIME_ZONE,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(d.value)
  const get = (type: string) => Number(parts.find((p) => p.type === type)?.value ?? 0)
  return [get('year'), get('month'), get('day'), get('hour'), get('minute'), get('second')]
}

/** Texto estándar que Excel y cualquier programa leen como fecha: "2026-09-30" o "2026-09-30 14:03:00". */
export function exportDateIso(d: ExportDate): string {
  const [y, m, day, h, mi, sec] = dateParts(d)
  const p = (n: number) => String(n).padStart(2, '0')
  const date = `${y}-${p(m)}-${p(day)}`
  return d.withTime ? `${date} ${p(h)}:${p(mi)}:${p(sec)}` : date
}

/** Número de serie de Excel (días desde 1899-12-30, con la hora como fracción del día). */
export function excelDateSerial(d: ExportDate): number {
  const [y, m, day, h, mi, sec] = dateParts(d)
  return (Date.UTC(y, m - 1, day, h, mi, sec) - Date.UTC(1899, 11, 30)) / 86_400_000
}

/** Formato de número de Excel para la celda: fecha o fecha y hora. */
export function excelDateFormat(d: ExportDate): string {
  return d.withTime ? 'yyyy-mm-dd hh:mm' : 'yyyy-mm-dd'
}

/** Texto legible de la fecha para el PDF, en el idioma de la interfaz ("30 sept 2026, 14:03"). */
export function exportDateText(d: ExportDate, locale?: string): string {
  return new Intl.DateTimeFormat(
    locale,
    d.withTime ? { dateStyle: 'medium', timeStyle: 'short', timeZone: TENANT_TIME_ZONE } : { dateStyle: 'medium' },
  ).format(d.value)
}
/** Lo que puede devolver `sortValue`/`exportValue` de una columna. */
export type ExportRawValue = string | number | boolean | Date | null | undefined

/** Lo que la exportación necesita de una columna (`DataColumn<T>` lo cumple tal cual). */
export interface ExportableColumn<T> {
  header: string
  cell: (row: T) => ReactNode
  sortValue?: (row: T) => ExportRawValue
  /** Valor exportado explícito; gana sobre el texto de la celda y sobre `sortValue`. */
  exportValue?: (row: T) => ExportRawValue
  /** false = la columna no se exporta (casillas de selección, columnas solo visuales). */
  exportable?: boolean
  align?: 'start' | 'end'
  /** true = número con signo (+5 / -3) en el PDF agrupado (`exportChildren`); Excel/CSV lo guardan como número. */
  signed?: boolean
}

export interface ExportData {
  headers: string[]
  /** true = columna numérica (alineada a la derecha en el PDF). */
  numeric: boolean[]
  /** true = número con signo (lo usa el PDF agrupado; opcional). */
  signed?: boolean[]
  rows: ExportCell[][]
}

export interface ExportOptions {
  /** Idioma de la interfaz (para leer números formateados y fechas). */
  locale?: string
  /** Texto de los booleanos. */
  yes?: string
  no?: string
}

/** Marcadores que las pantallas pintan para "sin dato": se exportan como celda vacía. */
const EMPTY_MARKS = new Set(['—', '–', '-'])

/**
 * Texto plano de un ReactNode: cadenas y números tal cual, elementos por sus `children` (separando bloques con un
 * espacio). Un componente sin `children` (p. ej. `<StatusChip label=…/>`) no aporta texto: ahí decide `sortValue`.
 */
export function nodeToText(node: ReactNode): string {
  const parts: string[] = []
  const walk = (n: ReactNode): void => {
    if (n === null || n === undefined || typeof n === 'boolean') return
    if (typeof n === 'string' || typeof n === 'number' || typeof n === 'bigint') {
      parts.push(String(n))
      return
    }
    if (Array.isArray(n)) {
      n.forEach(walk)
      return
    }
    if (isValidElement<{ children?: ReactNode }>(n)) {
      parts.push(' ')
      walk(n.props.children)
      parts.push(' ')
    }
  }
  walk(node)
  return parts.join('').replace(/\s+/g, ' ').trim()
}

/** Lee un número formateado en `locale` ("12.345,5" en es, "12,345.5" en en, "+5"); null si no es un número. */
export function parseLocaleNumber(text: string, locale?: string): number | null {
  const parts = new Intl.NumberFormat(locale).formatToParts(12345.6)
  const group = parts.find((p) => p.type === 'group')?.value ?? ','
  const decimal = parts.find((p) => p.type === 'decimal')?.value ?? '.'
  let s = text.replace(/[\s  ]/g, '').split(group).join('')
  if (decimal !== '.') s = s.split(decimal).join('.')
  s = s.replace(/^\+/, '').replace(/^−/, '-')
  if (!/^-?\d+(\.\d+)?$/.test(s)) return null
  return Number(s)
}

function normalizeRaw(v: ExportRawValue, opts: ExportOptions): ExportCell {
  if (v === null || v === undefined || v === '') return null
  if (typeof v === 'boolean') return v ? (opts.yes ?? 'true') : (opts.no ?? 'false')
  const date = toExportDate(v)
  if (date) return date
  if (typeof v === 'number') return Number.isFinite(v) ? v : null
  if (v instanceof Date) return null // fecha inválida (las válidas ya salieron como fecha)
  return v
}

/**
 * Valor exportable de una celda, en este orden:
 * 1. `exportValue(row)` si la columna lo define;
 * 2. lo que se ve: el texto de `cell(row)` ("—" = vacío). Si `sortValue` es un número y el texto es ese mismo número
 *    formateado ("1.234", "+5"), se exporta el número (Excel puede sumarlo);
 * 3. si la celda no tiene texto (un componente sin children, un ícono), `sortValue(row)`.
 */
export function exportCellValue<T>(col: ExportableColumn<T>, row: T, opts: ExportOptions = {}): ExportCell {
  if (col.exportValue) return normalizeRaw(col.exportValue(row), opts)
  const node = col.cell(row)
  if (typeof node === 'number') return Number.isFinite(node) ? node : null
  const text = nodeToText(node)
  const sortRaw = col.sortValue?.(row)
  // columna de fecha: su valor de orden es la fecha (Date o ISO del API) → se exporta como FECHA, no como el texto pintado
  const sortDate = toExportDate(sortRaw)
  if (sortDate && text && !EMPTY_MARKS.has(text)) return sortDate
  if (text) {
    if (EMPTY_MARKS.has(text)) return null
    if (typeof sortRaw === 'number' && Number.isFinite(sortRaw)) {
      const parsed = parseLocaleNumber(text, opts.locale)
      if (parsed !== null && Math.abs(parsed - sortRaw) < 1e-9) return sortRaw
    }
    return text
  }
  return normalizeRaw(sortRaw, opts)
}

/** Encabezados y filas planas de la tabla (sin las columnas `exportable: false` ni columnas sin encabezado ni datos). */
export function buildExportData<T>(
  columns: readonly ExportableColumn<T>[],
  rows: readonly T[],
  opts: ExportOptions = {},
): ExportData {
  const cols = columns.filter((c) => c.exportable !== false)
  const matrix = rows.map((row) => cols.map((c) => exportCellValue(c, row, opts)))
  // una columna sin encabezado y sin ningún dato (casilla de selección, ícono) no aporta nada al archivo
  const keep = cols.map((c, i) => c.header.trim() !== '' || matrix.some((r) => r[i] !== null))
  return {
    headers: cols.filter((_, i) => keep[i]).map((c) => c.header),
    numeric: cols.filter((_, i) => keep[i]).map((c) => c.align === 'end'),
    signed: cols.filter((_, i) => keep[i]).map((c) => c.signed === true),
    rows: matrix.map((r) => r.filter((_, i) => keep[i])),
  }
}

/** Una celda CSV (RFC 4180): entre comillas si trae separador, comillas o saltos de línea; comillas duplicadas. */
export function csvCell(value: ExportCell): string {
  if (value === null) return ''
  if (typeof value === 'number') return String(value)
  if (isExportDate(value)) return exportDateIso(value)
  let s = value
  // inyección de fórmulas: un texto que empieza con = + - @ (y no es un número) se abre como fórmula en Excel
  if (/^[=+\-@\t\r]/.test(s) && !/^[+-]?\d[\d.,]*$/.test(s)) s = `'${s}`
  return /[",\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
}

/** CSV con coma como separador y CRLF entre filas (sin BOM: lo agrega la descarga para que Excel lea UTF-8). */
export function toCsv(data: ExportData): string {
  return [data.headers, ...data.rows].map((r) => r.map(csvCell).join(',')).join('\r\n')
}

/** Nombre de archivo: base sin acentos ni símbolos + fecha local, p. ej. `almacenes-2026-09-29.xlsx`. */
export function exportFileName(base: string | null | undefined, ext: ExportFormat, date: Date = new Date()): string {
  const slug = (base ?? '')
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 60)
  const pad = (n: number) => String(n).padStart(2, '0')
  const day = `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
  return `${slug || 'export'}-${day}.${ext}`
}

/** Las fuentes estándar de jsPDF solo cubren Latin-1: reemplaza lo demás por su equivalente más cercano. */
export function pdfSafeText(s: string): string {
  return s
    .replace(/[‒-―−]/g, '-')
    .replace(/→/g, '->')
    .replace(/←/g, '<-')
    .replace(/…/g, '...')
    .replace(/[‘’]/g, "'")
    .replace(/[“”]/g, '"')
    .replace(/•/g, '·')
    .replace(/[ -   ]/g, ' ')
    .replace(/[▲▼]/g, '')
    // fuera de Latin-1 (U+0000–U+00FF): el rango empieza a propósito en el carácter de control 0
    // oxlint-disable-next-line no-control-regex
    .replace(/[^\u0000-ÿ]/g, '?')
}

/** Nombre de hoja válido para Excel: sin `[]:*?/\` y hasta 31 caracteres. */
export function sheetName(title: string | null | undefined): string {
  const clean = (title ?? '').replace(/[[\]:*?/\\]/g, ' ').trim().slice(0, 31)
  return clean || 'Sheet1'
}

function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = fileName
  a.rel = 'noopener'
  document.body.appendChild(a)
  a.click()
  a.remove()
  // se libera después de que el navegador tome el enlace
  setTimeout(() => URL.revokeObjectURL(url), 0)
}

export function downloadCsv(data: ExportData, fileName: string): void {
  // BOM: sin él Excel abre el CSV como ANSI y rompe acentos y eñes
  downloadBlob(new Blob(['﻿', toCsv(data)], { type: 'text/csv;charset=utf-8' }), fileName)
}

// ---------------------------------------------------------------------------------------------------------------------
// Encabezado de los archivos (PDF y Excel; el CSV no lleva): compañía → título → "Generado el …" → oración de filtros
// ---------------------------------------------------------------------------------------------------------------------

/** Lo que va arriba de la tabla en un PDF o Excel exportado (textos ya traducidos). */
export interface ExportHeadingSpec {
  /** Compañía activa (la pone el shell con `me.tenantName`; `useExportHeading`). */
  company?: string | null
  title?: string | null
  /** Fecha de generación (por defecto ahora). */
  generatedAt?: Date
  /** Oración de filtros ("Filtros: …" / "Sin filtros"); null = sin línea (tabla sin barra o en un modal). */
  filters?: string | null
  locale?: string
}

export type ExportHeadingKind = 'company' | 'title' | 'generated' | 'filters'

/** "Generado el 30 de septiembre de 2026, 10:15 a. m." en el idioma de la interfaz. */
export function exportGeneratedText(date: Date, locale?: string): string {
  const when = new Intl.DateTimeFormat(locale, { dateStyle: 'long', timeStyle: 'short' }).format(date)
  return translate('ui.report.generatedAt', { date: when })
}

/** Renglones del encabezado en orden (sin los vacíos): compañía, título, "Generado el …" y filtros. */
export function exportHeadingLines(spec: ExportHeadingSpec): { kind: ExportHeadingKind; text: string }[] {
  const lines: { kind: ExportHeadingKind; text: string }[] = []
  const company = spec.company?.trim()
  const title = spec.title?.trim()
  const filters = spec.filters?.trim()
  if (company) lines.push({ kind: 'company', text: company })
  if (title) lines.push({ kind: 'title', text: title })
  lines.push({ kind: 'generated', text: exportGeneratedText(spec.generatedAt ?? new Date(), spec.locale) })
  if (filters) lines.push({ kind: 'filters', text: filters })
  return lines
}

type Rgb = [number, number, number]
const PDF_INK: Rgb = [24, 33, 54]
const PDF_MUTED: Rgb = [91, 104, 128]

/**
 * Dibuja el encabezado en la página actual desde `y` (compañía en negrita 10, título en negrita 13, fecha y filtros con
 * la misma letra, 8 gris; los filtros largos parten renglón al ancho `maxWidth`) y devuelve la `y` donde empieza la
 * tabla. Lo comparten el PDF de `DataTable` y el agrupado (`exportGrouped.ts`).
 */
export function drawPdfHeading(doc: jsPDF, spec: ExportHeadingSpec, x: number, y: number, maxWidth: number): number {
  let cy = y
  for (const line of exportHeadingLines(spec)) {
    const text = pdfSafeText(line.text)
    if (line.kind === 'company') {
      doc.setFont('helvetica', 'bold')
      doc.setFontSize(10)
      doc.setTextColor(...PDF_MUTED)
      doc.text(text, x, cy + 8)
      cy += 14
    } else if (line.kind === 'title') {
      doc.setFont('helvetica', 'bold')
      doc.setFontSize(13)
      doc.setTextColor(...PDF_INK)
      doc.text(text, x, cy + 10)
      cy += 17
    } else {
      doc.setFont('helvetica', 'normal')
      doc.setFontSize(8)
      doc.setTextColor(...PDF_MUTED)
      const wrapped = doc.splitTextToSize(text, maxWidth) as string[]
      doc.text(wrapped, x, cy + 7)
      cy += 10 * wrapped.length + 1
    }
  }
  doc.setTextColor(0, 0, 0)
  return cy + 6
}

/** Filas de la hoja de Excel: el encabezado (un renglón por línea), una fila en blanco y la tabla. `headerRow` = índice
 *  (base 0) de la fila de encabezados de la tabla. */
export function xlsxSheetRows(data: ExportData, heading: ExportHeadingSpec): { rows: ExportCell[][]; headerRow: number } {
  const top: ExportCell[][] = exportHeadingLines(heading).map((l) => [l.text])
  top.push([])
  return { rows: [...top, data.headers, ...data.rows], headerRow: top.length }
}

/** Arma el libro de Excel en memoria (hoja con el encabezado, una fila en blanco y la tabla); no descarga. */
export function buildXlsxWorkbook(XLSX: typeof import('xlsx'), data: ExportData, heading: ExportHeadingSpec) {
  const { rows, headerRow } = xlsxSheetRows(data, heading)
  // las fechas van como número de serie de Excel con formato de fecha: Excel las lee como FECHA (ordenar, filtrar, restar)
  const dates: { r: number; c: number; z: string }[] = []
  const plain = rows.map((r, ri) =>
    r.map((v, ci) => {
      if (!isExportDate(v)) return v
      dates.push({ r: ri, c: ci, z: excelDateFormat(v) })
      return excelDateSerial(v)
    }),
  )
  const ws = XLSX.utils.aoa_to_sheet(plain)
  for (const d of dates) {
    const cell = ws[XLSX.utils.encode_cell({ r: d.r, c: d.c })]
    if (cell) {
      cell.t = 'n'
      cell.z = d.z
    }
  }
  // ancho de columna aproximado al contenido más largo de la TABLA (tope 60 caracteres): el encabezado no ensancha la A
  const table = rows.slice(headerRow)
  ws['!cols'] = data.headers.map((_, i) => ({
    wch: Math.min(60, Math.max(8, ...table.map((r) => (isExportDate(r[i]) ? 18 : String(r[i] ?? '').length + 2)))),
  }))
  // SheetJS (edición comunidad) no escribe estilos ni paneles inmovilizados: los encabezados no van en negrita ni se
  // congelan; el autofiltro sí se escribe y marca la fila de encabezados (flechas de filtro de Excel)
  if (data.headers.length > 0) {
    ws['!autofilter'] = {
      ref: XLSX.utils.encode_range({ s: { r: headerRow, c: 0 }, e: { r: headerRow + data.rows.length, c: data.headers.length - 1 } }),
    }
  }
  const wb = XLSX.utils.book_new()
  XLSX.utils.book_append_sheet(wb, ws, sheetName(heading.title))
  return wb
}

export async function downloadXlsx(data: ExportData, fileName: string, heading: ExportHeadingSpec = {}): Promise<void> {
  const XLSX = await import('xlsx')
  XLSX.writeFile(buildXlsxWorkbook(XLSX, data, heading), fileName, { compression: true })
}

export interface RenderTablePdfOptions extends ExportHeadingSpec {
  /** false = sin comprimir (pruebas: el texto queda legible en el PDF). Por defecto true. */
  compress?: boolean
}

/** Arma el PDF de la tabla en memoria (A4; horizontal con más de 5 columnas): encabezado y tabla; no descarga. */
export async function renderTablePdf(data: ExportData, options: RenderTablePdfOptions = {}): Promise<jsPDF> {
  const [{ jsPDF: JsPdf }, { autoTable }] = await Promise.all([import('jspdf'), import('jspdf-autotable')])
  // más de 5 columnas: horizontal
  const doc = new JsPdf({
    orientation: data.headers.length > 5 ? 'landscape' : 'portrait',
    unit: 'pt',
    format: 'a4',
    compress: options.compress ?? true,
  })
  const margin = 32
  const startY = drawPdfHeading(doc, options, margin, margin - 6, doc.internal.pageSize.getWidth() - margin * 2)
  const columnStyles: Record<number, { halign: 'right' }> = {}
  data.numeric.forEach((n, i) => {
    if (n) columnStyles[i] = { halign: 'right' }
  })
  autoTable(doc, {
    head: [data.headers.map(pdfSafeText)],
    body: data.rows.map((r) => r.map((v) => (v === null ? '' : typeof v === 'number' ? String(v) : isExportDate(v) ? pdfSafeText(exportDateText(v, options.locale)) : pdfSafeText(v)))),
    startY,
    margin: { left: margin, right: margin, top: margin, bottom: margin },
    styles: { font: 'helvetica', fontSize: 8, cellPadding: 3, overflow: 'linebreak' },
    headStyles: { fillColor: [38, 50, 72], textColor: 255, fontStyle: 'bold' },
    alternateRowStyles: { fillColor: [245, 247, 250] },
    columnStyles,
    didDrawPage: () => {
      const page = doc.getNumberOfPages()
      const { width, height } = doc.internal.pageSize
      doc.setFont('helvetica', 'normal')
      doc.setFontSize(8)
      doc.text(String(page), width - margin, height - margin / 2, { align: 'right' })
    },
  })
  return doc
}

export async function downloadPdf(data: ExportData, fileName: string, heading: ExportHeadingSpec = {}): Promise<void> {
  const doc = await renderTablePdf(data, heading)
  doc.save(fileName)
}

/** Opciones de `exportTable`: formato de valores + título, fecha, compañía y oración de filtros (PDF y Excel). */
export interface ExportTableOptions extends ExportOptions {
  /** Nombre del archivo, de la hoja y título del PDF/Excel. */
  title?: string | null
  date?: Date
  /** Compañía activa (arriba del título). */
  company?: string | null
  /** Oración de filtros bajo "Generado el …" (null = sin línea). */
  filters?: string | null
}

/** Encabezado de PDF/Excel a partir de las opciones de `exportTable`. */
export function headingFromOptions(opts: ExportTableOptions): ExportHeadingSpec {
  return { company: opts.company, title: opts.title, generatedAt: opts.date, filters: opts.filters, locale: opts.locale }
}

/**
 * Arma y descarga el archivo en el formato pedido. `title` da nombre al archivo, a la hoja y al encabezado del PDF.
 * PDF y Excel llevan arriba la compañía, el título, "Generado el …" y la oración de filtros (`company`/`filters`: los
 * arma `useExportHeading` en `DataTable`/`ListPager`); el CSV lleva solo la tabla.
 * Con `children` (filas hijas, `exportChildren` de exportGrouped.ts) la exportación es agrupada: Excel/CSV con una fila
 * por hija repitiendo la madre y PDF con un bloque por madre (banda + tablita de hijas).
 */
export async function exportTable<T>(
  format: ExportFormat,
  columns: readonly ExportableColumn<T>[],
  rows: readonly T[],
  opts: ExportTableOptions & { children?: ExportChildren<T> } = {},
): Promise<void> {
  if (opts.children) {
    const { exportGroupedTable } = await import('./exportGrouped')
    await exportGroupedTable(format, columns, rows, opts.children, opts)
    return
  }
  const data = buildExportData(columns, rows, opts)
  const fileName = exportFileName(opts.title, format, opts.date)
  if (format === 'csv') downloadCsv(data, fileName)
  else if (format === 'xlsx') await downloadXlsx(data, fileName, headingFromOptions(opts))
  else await downloadPdf(data, fileName, headingFromOptions(opts))
}

