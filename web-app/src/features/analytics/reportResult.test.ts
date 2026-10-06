// Lote F18 (Rentas F-R2) — resultado de una vista del motor de Análisis: texto de cada celda por el tipo de su columna, valor
// para ordenar y totales de una vista agrupada (sin recalcular nada).
import { describe, expect, it } from 'vitest'
import { reportCellText, reportSortValue, reportTotalsItems, type ReportFormatters, type ReportResultColumn } from './reportResult'

const fmt: ReportFormatters = { number: (n) => `n${n}`, money: (n) => `$${n}`, dateTime: (v) => `d(${v})`, yes: 'Sí', no: 'No' }
const col = (key: string, type: ReportResultColumn['type'], isMoney = false): ReportResultColumn => ({ key, label: key.toUpperCase(), type, isMoney })

describe('reportResult', () => {
  it('texto por tipo: número, dinero, booleano, fecha, texto y vacío', () => {
    expect(reportCellText(3, col('a', 'Number'), fmt)).toBe('n3')
    expect(reportCellText('3.5', col('a', 'Number'), fmt)).toBe('n3.5')
    expect(reportCellText(45, col('a', 'Number', true), fmt)).toBe('$45')
    expect(reportCellText(true, col('b', 'Bool'), fmt)).toBe('Sí')
    expect(reportCellText(false, col('b', 'Bool'), fmt)).toBe('No')
    expect(reportCellText('2026-10-05', col('c', 'Date'), fmt)).toBe('d(2026-10-05)')
    expect(reportCellText('Hospital', col('d', 'Text'), fmt)).toBe('Hospital')
    expect(reportCellText(null, col('d', 'Text'), fmt)).toBe('—')
    expect(reportCellText('', col('a', 'Number'), fmt)).toBe('—')
  })

  it('valor para ordenar: números como números, booleanos 1/0, vacíos null', () => {
    expect(reportSortValue({ a: '10' }, col('a', 'Number'))).toBe(10)
    expect(reportSortValue({ a: 2 }, col('a', 'Text'))).toBe(2)
    expect(reportSortValue({ b: true }, col('b', 'Bool'))).toBe(1)
    expect(reportSortValue({}, col('a', 'Text'))).toBeNull()
    expect(reportSortValue({ c: '2026-10-05' }, col('c', 'Date'))).toBe('2026-10-05')
  })

  it('totales: una cifra por columna con valor, en el orden de las columnas', () => {
    const result = { columns: [col('ClientName', 'Text'), col('count', 'Number'), col('sum_UnitsOnRent', 'Number')], rows: [], totals: { count: 3, sum_UnitsOnRent: 5 }, total: 2 }
    expect(reportTotalsItems(result, fmt)).toEqual([
      { key: 'count', label: 'COUNT', value: 'n3' },
      { key: 'sum_UnitsOnRent', label: 'SUM_UNITSONRENT', value: 'n5' },
    ])
    expect(reportTotalsItems({ ...result, totals: null }, fmt)).toEqual([])
  })
})
