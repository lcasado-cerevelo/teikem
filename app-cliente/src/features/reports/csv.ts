// CSV de un informe: UTF-8 con marca de orden de bytes (Excel lo abre bien con acentos), comas, comillas dobles escapadas, números sin formato (para que Excel los sume) y fechas AAAA-MM-DD.
import { Platform, Share } from 'react-native'

import type { CellValue, ReportColumn, ReportRow } from './reportDefs'
import { totalsOf } from './reportDefs'

export function csvCell(v: CellValue): string {
  if (v === null || v === undefined) return ''
  const s = String(v)
  return /[",\r\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s
}

/** Texto del CSV: encabezados traducidos, una fila por registro y, si alguna columna suma, una fila de totales. */
export function toCsv(columns: readonly ReportColumn[], headers: readonly string[], rows: readonly ReportRow[], totalLabel: string): string {
  const lines = [headers.map(csvCell).join(',')]
  for (const r of rows) lines.push(columns.map((c) => csvCell(r[c.key] ?? null)).join(','))
  const totals = totalsOf(columns, rows)
  if (Object.keys(totals).length > 0) lines.push(columns.map((c, i) => (i === 0 ? csvCell(totalLabel) : c.key in totals ? csvCell(totals[c.key]) : '')).join(','))
  return lines.join('\r\n') + '\r\n'
}

/** Nombre del archivo: informe y fecha, sin caracteres raros. */
export function csvFileName(report: string, day: string): string {
  return `teikem-${report.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-')}-${day}.csv`
}

/** Baja el archivo (web) o lo comparte/guarda (celular). */
export async function saveCsv(fileName: string, csv: string): Promise<void> {
  if (Platform.OS === 'web') {
    const blob = new Blob(['\uFEFF', csv], { type: 'text/csv;charset=utf-8' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = fileName
    document.body.appendChild(a)
    a.click()
    a.remove()
    URL.revokeObjectURL(url)
    return
  }
  await Share.share({ message: csv, title: fileName })
}
