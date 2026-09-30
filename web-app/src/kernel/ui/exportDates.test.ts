import { describe, expect, it } from 'vitest'
import * as XLSX from 'xlsx'
import {
  buildExportData,
  buildXlsxWorkbook,
  excelDateSerial,
  exportDateIso,
  exportDateText,
  isExportDate,
  toCsv,
  toExportDate,
  type ExportableColumn,
} from './exportTable'

// Pedido del dueño: en Excel y en CSV una fecha se debe leer como FECHA (y fecha y hora si tiene hora), no como texto.

describe('toExportDate', () => {
  it('fecha sola del API → día de calendario sin hora', () => {
    const d = toExportDate('2026-09-30')
    expect(d && d.withTime).toBe(false)
    expect(d && exportDateIso(d)).toBe('2026-09-30')
  })
  it('fecha y hora del API (UTC sin zona) → hora de Puerto Rico', () => {
    const d = toExportDate('2026-09-30T18:03:00')
    expect(d?.withTime).toBe(true)
    expect(d && exportDateIso(d)).toBe('2026-09-30 14:03:00')
  })
  it('a medianoche UTC todavía es el día anterior en Puerto Rico', () => {
    const d = toExportDate('2026-10-01T03:30:00Z')
    expect(d && exportDateIso(d)).toBe('2026-09-30 23:30:00')
  })
  it('lo que no es fecha no se convierte', () => {
    expect(toExportDate('REC-00001')).toBeNull()
    expect(toExportDate('2026-09')).toBeNull()
    expect(toExportDate(12)).toBeNull()
    expect(toExportDate(new Date('x'))).toBeNull()
  })
})

describe('número de serie de Excel', () => {
  it('día 2026-09-30 = 46295 y la hora es la fracción del día', () => {
    const day = toExportDate('2026-09-30')
    const withTime = toExportDate('2026-09-30T18:00:00Z')
    expect(day && excelDateSerial(day)).toBe(46295)
    expect(withTime && excelDateSerial(withTime)).toBeCloseTo(46295 + 14 / 24, 9)
  })
})

interface Row {
  number: string
  createdAtUtc: string
  expected: string
}
const ROWS: Row[] = [{ number: 'REC-00001', createdAtUtc: '2026-09-30T18:03:00', expected: '2026-10-02' }]
const COLS: ExportableColumn<Row>[] = [
  { header: 'Número', cell: (r) => r.number },
  // la celda pinta la fecha formateada; el valor de orden es la ISO del API → se exporta como fecha
  { header: 'Creado', cell: () => '30 sept 2026, 14:03', sortValue: (r) => r.createdAtUtc },
  { header: 'Llegada', cell: () => '2 oct 2026', exportValue: (r) => r.expected },
]

describe('exportación de tablas con fechas', () => {
  it('las columnas de fecha salen como fecha, no como el texto pintado', () => {
    const data = buildExportData(COLS, ROWS)
    expect(data.rows[0][0]).toBe('REC-00001')
    expect(isExportDate(data.rows[0][1])).toBe(true)
    expect(isExportDate(data.rows[0][2])).toBe(true)
  })
  it('CSV: fecha estándar que Excel lee como fecha (con hora si la tiene)', () => {
    const csv = toCsv(buildExportData(COLS, ROWS))
    expect(csv.split('\r\n')[1]).toBe('REC-00001,2026-09-30 14:03:00,2026-10-02')
  })
  it('Excel: celda numérica con formato de fecha (y de fecha y hora)', () => {
    const wb = buildXlsxWorkbook(XLSX, buildExportData(COLS, ROWS), { title: 'Recibos' })
    const ws = wb.Sheets[wb.SheetNames[0]]
    const range = XLSX.utils.decode_range(ws['!ref'] ?? 'A1')
    // la última fila es la del recibo
    const created = ws[XLSX.utils.encode_cell({ r: range.e.r, c: 1 })]
    const expected = ws[XLSX.utils.encode_cell({ r: range.e.r, c: 2 })]
    expect(created.t).toBe('n')
    expect(created.z).toBe('yyyy-mm-dd hh:mm')
    expect(created.v).toBeCloseTo(46295 + (14 * 60 + 3) / 1440, 9)
    expect(expected.t).toBe('n')
    expect(expected.z).toBe('yyyy-mm-dd')
    expect(expected.v).toBe(46297)
  })
  it('PDF: texto legible en el idioma', () => {
    const d = toExportDate('2026-09-30')
    expect(d && exportDateText(d, 'es')).toMatch(/30 sept 2026/)
  })
})
