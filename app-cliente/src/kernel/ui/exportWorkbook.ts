// El libro de Excel de una tabla del portal, igual en la web y en la app instalada (2026-10-24, pedido de Luis: Excel, CSV y PDF en todas partes): encabezado (compañía, título, «Generado el …», filtros),
// una fila de títulos con filtro automático y las filas; las fechas AAAA-MM-DD salen como fecha de Excel. Cada plataforma escribe el libro a su manera (la web lo baja; el aparato lo guarda y lo comparte).
import { generatedText, type ExportData, type ExportHeading } from './tableExport'

const ISO_DAY = /^\d{4}-\d{2}-\d{2}$/

/** Texto de fecha AAAA-MM-DD → Date a mediodía local (Excel la guarda como fecha). */
const dayToDate = (s: string) => {
  const [y, m, d] = s.split('-').map(Number)
  return new Date(y, m - 1, d, 12)
}

type XlsxModule = typeof import('xlsx')

/** Las filas de la hoja: encabezado, línea en blanco, títulos y datos. `headerRow` es la fila (0 = primera) de los títulos. */
export function sheetRows(data: ExportData, heading: ExportHeading): { aoa: (string | number | Date | null)[][]; headerRow: number } {
  const aoa: (string | number | Date | null)[][] = []
  if (heading.company?.trim()) aoa.push([heading.company.trim()])
  aoa.push([heading.title])
  aoa.push([generatedText(heading.generatedAt, heading.lang)])
  if (heading.filters?.trim()) aoa.push([heading.filters.trim()])
  aoa.push([])
  const headerRow = aoa.length
  aoa.push(data.headers)
  for (const r of data.rows) aoa.push(r.map((v) => (typeof v === 'string' && ISO_DAY.test(v) ? dayToDate(v) : v)))
  return { aoa, headerRow }
}

/** El libro listo para escribir (`XLSX.writeFile` en la web, `XLSX.write` en el aparato). */
export function buildWorkbook(XLSX: XlsxModule, data: ExportData, heading: ExportHeading) {
  const { aoa, headerRow } = sheetRows(data, heading)
  const ws = XLSX.utils.aoa_to_sheet(aoa, { cellDates: true, dateNF: 'yyyy-mm-dd' })
  // anchos por el contenido (tope 50), encabezados con filtro automático
  ws['!cols'] = data.headers.map((h, i) => ({ wch: Math.min(50, Math.max(h.length, 8, ...data.rows.map((r) => (r[i] === null ? 0 : String(r[i]).length))) + 2) }))
  if (data.headers.length > 0 && data.rows.length > 0) ws['!autofilter'] = { ref: XLSX.utils.encode_range({ s: { r: headerRow, c: 0 }, e: { r: headerRow + data.rows.length, c: data.headers.length - 1 } }) }
  const wb = XLSX.utils.book_new()
  XLSX.utils.book_append_sheet(wb, ws, heading.title.replace(/[\\/?*[\]:]/g, ' ').slice(0, 31) || 'Hoja1')
  return wb
}
