// Exportar una tabla del portal (2026-10-23, pedido de Luis: «como en la aplicación del personal: se exporta lo que está filtrado», y «también a PDF, con el logo de la compañía o, si no tiene, el de
// Teikem»). Se traen TODAS las filas que cumplen el filtro (no solo las que se ven, hasta MAX_EXPORT_ROWS) y se bajan en el formato escogido: Excel (.xlsx), CSV o PDF. En la web y en la app instalada los tres;
// en la app se guarda el archivo y se abre el menú de compartir. Excel y PDF llevan arriba el logo, la compañía, el título, «Generado el …» y la oración de filtros, como los de la web del personal; el CSV lleva solo la tabla.
// Lo puro (armar los datos, el texto del CSV, el nombre del archivo, la oración de filtros) se prueba en tableExport.test.ts; Excel y PDF viven en exportFiles.web.ts (se cargan bajo demanda).

import { csvCell, csvFileName, saveCsv } from '../../features/reports/csv'
import { getPortalBrandInfo } from '../brand/portalBrand'
import { getLang, t, translate, type Lang } from '../i18n/i18n'

export type CsvValue = string | number | boolean | null | undefined
export type ExportFormat = 'xlsx' | 'csv' | 'pdf'

/** Formatos que ofrece toda tabla: Excel, CSV y PDF, en la web y en la app instalada (el estándar de Luis, 2026-10-24). */
export const exportFormats = (): readonly ExportFormat[] => ['xlsx', 'csv', 'pdf']

export interface CsvColumn<T> {
  title: string
  /** Valor exportado de la fila; sin él la columna no se exporta (botones, acciones). */
  csv?: (row: T) => CsvValue
}

/** Tabla lista para escribir en cualquier formato: encabezados, filas (texto, número o vacío) y qué columnas son numéricas (a la derecha en el PDF). */
export interface ExportData {
  headers: string[]
  rows: (string | number | null)[][]
  numeric: boolean[]
}

/** Lo que va arriba de un Excel o PDF (textos ya traducidos). */
export interface ExportHeading {
  company?: string | null
  title: string
  generatedAt: Date
  /** Oración de filtros («Filtros: …» / «Sin filtros»); null = sin línea. */
  filters?: string | null
  lang: Lang
}

export const MAX_EXPORT_ROWS = 5000
const TAKE = 100

/** Trae todas las páginas de una consulta del servidor con el filtro vigente (hasta MAX_EXPORT_ROWS). */
export async function fetchAll<T>(load: (skip: number, take: number) => Promise<{ items?: T[] | null; total?: number }>): Promise<{ items: T[]; truncated: boolean }> {
  const all: T[] = []
  let total = Infinity
  while (all.length < total && all.length < MAX_EXPORT_ROWS) {
    const page = await load(all.length, TAKE)
    const got = page.items ?? []
    total = page.total ?? got.length
    if (got.length === 0) break
    all.push(...got)
  }
  return { items: all.slice(0, MAX_EXPORT_ROWS), truncated: total > MAX_EXPORT_ROWS }
}

const text = (v: CsvValue, yes: string, no: string): string => (v === null || v === undefined ? '' : typeof v === 'boolean' ? (v ? yes : no) : String(v))

/** Texto del CSV: encabezados de las columnas exportables y una fila por registro. Los booleanos salen como `yes`/`no`. */
export function toCsvText<T>(columns: readonly CsvColumn<T>[], rows: readonly T[], yes = 'Sí', no = 'No'): string {
  const cols = columns.filter((c) => c.csv)
  const lines = [cols.map((c) => csvCell(c.title)).join(',')]
  for (const r of rows) lines.push(cols.map((c) => csvCell(text(c.csv!(r), yes, no))).join(','))
  return lines.join('\r\n') + '\r\n'
}

/** Los datos de la tabla para Excel y PDF: números como números (Excel los suma), booleanos en palabras, vacío como null. */
export function buildExportData<T>(columns: readonly CsvColumn<T>[], rows: readonly T[], yes = 'Sí', no = 'No'): ExportData {
  const cols = columns.filter((c) => c.csv)
  const cell = (v: CsvValue): string | number | null => (v === null || v === undefined || v === '' ? null : typeof v === 'number' ? (Number.isFinite(v) ? v : null) : typeof v === 'boolean' ? (v ? yes : no) : String(v))
  const body = rows.map((r) => cols.map((c) => cell(c.csv!(r))))
  return {
    headers: cols.map((c) => c.title),
    rows: body,
    // una columna es numérica si todo lo que trae es número
    numeric: cols.map((_, i) => body.some((r) => r[i] !== null) && body.every((r) => r[i] === null || typeof r[i] === 'number')),
  }
}

/** Día de hoy AAAA-MM-DD para el nombre del archivo. */
const today = () => {
  const d = new Date()
  const p = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
}

/** `teikem-mis-envios-2026-10-23.xlsx`: el título sin acentos ni símbolos, la fecha y la extensión del formato. */
export const exportFileName = (title: string, format: ExportFormat, day = today()) => csvFileName(title, day).replace(/\.csv$/, `.${format}`)

/** La oración de filtros del encabezado: «Filtros: A · B» o «Sin filtros». */
export function filtersSentence(parts: readonly (string | null | undefined | false)[]): string {
  const list = parts.filter((p): p is string => !!p)
  return list.length > 0 ? t('table.filters', { list: list.join(' · ') }) : t('table.noFilters')
}

/** «Generado el 23 de octubre de 2026, 10:15 a. m.» en el idioma de la persona. */
export function generatedText(date: Date, lang: Lang): string {
  const when = new Intl.DateTimeFormat(lang === 'en' ? 'en-US' : 'es-PR', { dateStyle: 'long', timeStyle: 'short' }).format(date)
  return translate(lang, 'table.generatedAt', { date: when })
}

export interface RunExportOptions<T> {
  format: ExportFormat
  /** Título del archivo y del encabezado (el de la pantalla). */
  title: string
  columns: readonly CsvColumn<T>[]
  rows: readonly T[]
  /** Oración de filtros (`filtersSentence`); sin ella no sale la línea. */
  filters?: string | null
}

/** Arma y baja (web) o guarda y comparte (app instalada) el archivo en el formato pedido: Excel, CSV o PDF. */
export async function runExport<T>(o: RunExportOptions<T>): Promise<void> {
  const yes = t('table.yes')
  const no = t('table.no')
  if (o.format === 'csv') {
    await saveCsv(exportFileName(o.title, 'csv'), toCsvText(o.columns, o.rows, yes, no))
    return
  }
  const data = buildExportData(o.columns, o.rows, yes, no)
  const heading: ExportHeading = { company: getPortalBrandInfo().companyName, title: o.title, generatedAt: new Date(), filters: o.filters ?? null, lang: getLang() }
  // `require` y no `import()`: el módulo de cada plataforma (exportFiles.ts en la app, exportFiles.web.ts en la web) es chico; jsPDF y SheetJS sí se cargan bajo demanda dentro de él
  const files = require('./exportFiles') as typeof import('./exportFiles')
  if (o.format === 'xlsx') await files.saveXlsx(data, exportFileName(o.title, 'xlsx'), heading)
  else await files.savePdf(data, exportFileName(o.title, 'pdf'), heading)
}

/** Día AAAA-MM-DD de una fecha ISO (para exportar fechas sin hora). */
export const csvDay = (iso: string | null | undefined) => (iso ? iso.slice(0, 10) : '')
