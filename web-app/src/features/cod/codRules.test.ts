// Pruebas de las reglas puras de la pantalla COD (Lote 85).
import { describe, expect, it } from 'vitest'
import { cleanCode, fileKind } from './barcodeReader'
import {
  codesError,
  collectFormError,
  countResults,
  needsCheckNumber,
  parseAmount,
  parseCodes,
  reconcilableAmount,
  resultTone,
  rowsFromManual,
  rowsFromScanned,
  runSource,
  toCollectRequest,
  toReconcileRequest,
  type CodeRow,
} from './codRules'

const form = (over = {}) => ({ method: 'CASH', amount: '40', checkNumber: '', reference: '', notes: '', ...over })

describe('cobro', () => {
  it('el monto acepta coma, hasta 2 decimales y mayor que cero', () => {
    expect(parseAmount('12,5')).toBe(12.5)
    expect(parseAmount('0')).toBeNull()
    expect(parseAmount('1.005')).toBeNull()
    expect(parseAmount('abc')).toBeNull()
    expect(parseAmount('')).toBeNull()
  })
  it('valida método, monto, tope de lo pendiente y número de cheque', () => {
    expect(collectFormError(form({ method: '' }), 100)).toBe('method')
    expect(collectFormError(form({ amount: '' }), 100)).toBe('amount')
    expect(collectFormError(form({ amount: '150' }), 100)).toBe('exceeds')
    expect(collectFormError(form({ method: 'CHECK' }), 100)).toBe('checkNumber')
    expect(collectFormError(form({ method: 'CHECK', checkNumber: '1234' }), 100)).toBeNull()
    expect(collectFormError(form({ reference: 'r'.repeat(81) }), 100)).toBe('reference')
    expect(collectFormError(form({ notes: 'n'.repeat(301) }), 100)).toBe('notes')
    expect(collectFormError(form(), 100)).toBeNull()
    expect(needsCheckNumber('COMPANY_CHECK')).toBe(true)
    expect(needsCheckNumber('CASH')).toBe(false)
  })
  it('arma la solicitud sin campos vacíos', () => {
    expect(toCollectRequest(form({ amount: '12,5', reference: ' ATH-1 ' }))).toEqual({ method: 'CASH', amount: 12.5, checkNumber: undefined, reference: 'ATH-1', notes: undefined })
  })
})

describe('códigos', () => {
  it('los escritos a mano se separan por línea, coma, punto y coma o espacio', () => {
    expect(parseCodes('FAC-1\nFAC-2, FAC-3;FAC-4   FAC-5\n\n')).toEqual(['FAC-1', 'FAC-2', 'FAC-3', 'FAC-4', 'FAC-5'])
    expect(parseCodes('  ')).toEqual([])
  })
  it('las filas llevan su origen (archivo y página, o Manual) y una clave propia', () => {
    expect(rowsFromScanned([{ code: 'A', source: 'h.pdf p1', format: 'Code128' }], 5)).toEqual([{ key: 5, code: 'A', source: 'h.pdf p1' }])
    expect(rowsFromManual('A B', 1)).toEqual([{ key: 1, code: 'A', source: 'Manual' }, { key: 2, code: 'B', source: 'Manual' }])
  })
  it('la lista pide al menos uno y no más de 2000', () => {
    const row = (k: number): CodeRow => ({ key: k, code: 'C' + k, source: 'Manual' })
    expect(codesError([])).toBe('none')
    expect(codesError([row(1)])).toBeNull()
    expect(codesError(Array.from({ length: 2001 }, (_, i) => row(i)))).toBe('tooMany')
  })
  it('el cuerpo y el origen de la corrida', () => {
    const rows: CodeRow[] = [{ key: 1, code: 'A', source: 'h1.pdf p1' }, { key: 2, code: 'B', source: 'h1.pdf p2' }, { key: 3, code: 'C', source: 'Manual' }]
    expect(runSource(rows)).toBe('h1.pdf, Manual')
    expect(toReconcileRequest(rows, 'cli-1', 'h1.pdf, Manual', true)).toEqual({
      codes: [{ code: 'A', source: 'h1.pdf p1' }, { code: 'B', source: 'h1.pdf p2' }, { code: 'C', source: 'Manual' }],
      source: 'h1.pdf, Manual',
      clientPublicId: 'cli-1',
      dryRun: true,
      mode: 'AUTO',
    })
    expect(toReconcileRequest(rows, null, '', true, 'BOTH').mode).toBe('BOTH')
    expect(toReconcileRequest(rows, null, '', false)).toMatchObject({ clientPublicId: undefined, source: undefined, dryRun: false })
  })
})

describe('resultados', () => {
  it('el tono de cada resultado, el conteo y lo que se cuadra', () => {
    expect(['RECONCILED', 'ALREADY_RECONCILED', 'DUPLICATE', 'NOT_FOUND', 'AMBIGUOUS'].map(resultTone)).toEqual(['ok', 'info', 'info', 'bad', 'warn'])
    const items = [
      { code: 'A', result: 'RECONCILED', reconciled: 60, collections: 1, codAmount: 60, message: '' },
      { code: 'B', result: 'RECONCILED', reconciled: 40, collections: 1, codAmount: 40, message: '' },
      { code: 'C', result: 'NOT_FOUND', reconciled: 0, collections: 0, codAmount: 0, message: '' },
    ]
    expect(countResults(items)).toEqual({ RECONCILED: 2, NOT_FOUND: 1 })
    expect(reconcilableAmount(items)).toBe(100)
  })
})

describe('archivos', () => {
  it('reconoce PDF e imágenes por tipo o por extensión', () => {
    expect(fileKind({ name: 'a.PDF', type: '' })).toBe('pdf')
    expect(fileKind({ name: 'a', type: 'application/pdf' })).toBe('pdf')
    expect(fileKind({ name: 'a.jpeg', type: '' })).toBe('image')
    expect(fileKind({ name: 'a', type: 'image/png' })).toBe('image')
    expect(fileKind({ name: 'a.docx', type: '' })).toBeNull()
  })
  it('limpia lo que lee el lector: sin controles ni espacios', () => {
    expect(cleanCode(' FAC-00001\r\n\u0000')).toBe('FAC-00001')
  })
})
