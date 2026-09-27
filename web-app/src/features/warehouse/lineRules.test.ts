import { describe, expect, it } from 'vitest'
import { ApiError } from '../../kernel/api/problem'
import {
  countLineIssues,
  countLotIssue,
  firstOtherOwner,
  lineErrorsByIndex,
  parseSerials,
  pickDuplicateAcrossLines,
  pickLineIssues,
  receiptLineIssues,
  remapProblemFields,
} from './lineRules'

const codes = (issues: { field: string; code: string }[]) => issues.map((i) => `${i.field}:${i.code}`)

describe('parseSerials', () => {
  it('separa por renglón, coma o punto y coma y descarta vacíos', () => {
    expect(parseSerials(' A1 \nB2,,C3;\n')).toEqual(['A1', 'B2', 'C3'])
    expect(parseSerials('')).toEqual([])
  })
})

describe('receiptLineIssues (ReceiptRules)', () => {
  const base = { sku: 'SKU-1', lot: '', serials: [] as string[] }
  it('cantidad obligatoria, no negativa, 3 decimales', () => {
    expect(codes(receiptLineIssues({ ...base, trackingTypeCode: 'NONE', receivedQty: null }))).toEqual(['receivedQty:receivedQtyRequired'])
    expect(codes(receiptLineIssues({ ...base, trackingTypeCode: 'NONE', receivedQty: -1 }))).toEqual(['receivedQty:receivedQtyNegative'])
    expect(codes(receiptLineIssues({ ...base, trackingTypeCode: 'NONE', receivedQty: 1.2345 }))).toEqual(['receivedQty:qtyDecimals'])
  })
  it('LOT exige lote si se recibió algo; 0 sin lote es válido', () => {
    expect(codes(receiptLineIssues({ ...base, trackingTypeCode: 'LOT', receivedQty: 5 }))).toEqual(['lot:lotRequired'])
    expect(receiptLineIssues({ ...base, trackingTypeCode: 'LOT', receivedQty: 0 })).toEqual([])
  })
  it('SERIAL: entera e igual al número de series; serie repetida', () => {
    expect(codes(receiptLineIssues({ ...base, trackingTypeCode: 'SERIAL', receivedQty: 1.5 }))).toEqual(['receivedQty:serialInteger'])
    const mismatch = receiptLineIssues({ ...base, trackingTypeCode: 'SERIAL', receivedQty: 3, serials: ['A', 'B'] })
    expect(mismatch).toEqual([{ field: 'serialNumbers', code: 'serialCountMismatch', params: { sku: 'SKU-1', qty: '3', n: 2 } }])
    expect(codes(receiptLineIssues({ ...base, trackingTypeCode: 'SERIAL', receivedQty: 2, serials: ['A', 'a'] }))).toEqual([
      'serialNumbers:serialDuplicated',
    ])
    expect(receiptLineIssues({ ...base, trackingTypeCode: 'SERIAL', receivedQty: 2, serials: ['A', 'B'] })).toEqual([])
  })
  it('NONE no admite lote ni series', () => {
    expect(codes(receiptLineIssues({ ...base, trackingTypeCode: 'NONE', receivedQty: 1, lot: 'L1', serials: ['X'] }))).toEqual([
      'lot:lotNotAllowed',
      'serialNumbers:serialsNotAllowed',
    ])
  })
})

describe('countLineIssues (CycleCountRules.Capture)', () => {
  it('cantidad negativa y series en producto sin serie', () => {
    expect(codes(countLineIssues({ sku: 'S', trackingTypeCode: 'NONE', countedQty: -2, serials: [] }))).toEqual(['countedQty:countedNegative'])
    expect(codes(countLineIssues({ sku: 'S', trackingTypeCode: 'LOT', countedQty: 1, serials: ['X'] }))).toEqual(['serialNumbers:serialsNotAllowed'])
    expect(countLineIssues({ sku: 'S', trackingTypeCode: 'NONE', countedQty: null, serials: [] })).toEqual([])
  })
  it('SERIAL valida solo las series', () => {
    expect(codes(countLineIssues({ sku: 'S', trackingTypeCode: 'SERIAL', countedQty: null, serials: ['A', 'A'] }))).toEqual([
      'serialNumbers:countSerialDuplicated',
    ])
  })
  it('lote de la línea agregada a mano', () => {
    expect(countLotIssue('LOT', 'S', false)?.code).toBe('countLotRequired')
    expect(countLotIssue('NONE', 'S', true)?.code).toBe('lotNotAllowed')
    expect(countLotIssue('SERIAL', 'S', false)).toBeNull()
  })
})

describe('pickLineIssues (PickBatchRules)', () => {
  it('cantidad > 0 y series según seguimiento', () => {
    expect(codes(pickLineIssues({ sku: 'S', trackingTypeCode: 'NONE', quantity: 0, hasLot: false, serials: [] }))).toEqual(['quantity:pickQtyRequired'])
    expect(codes(pickLineIssues({ sku: 'S', trackingTypeCode: 'SERIAL', quantity: 1, hasLot: false, serials: [] }))).toEqual([
      'serialNumbers:pickSerialRequired',
    ])
    expect(codes(pickLineIssues({ sku: 'S', trackingTypeCode: 'SERIAL', quantity: 2, hasLot: false, serials: ['A'] }))).toEqual([
      'serialNumbers:pickSerialMismatch',
    ])
    expect(codes(pickLineIssues({ sku: 'S', trackingTypeCode: 'NONE', quantity: 1, hasLot: true, serials: ['A'] }))).toEqual([
      'serialNumbers:pickSerialNotAllowed',
      'lotId:pickLotNotTracked',
    ])
  })
  it('serie repetida entre líneas y un solo dueño', () => {
    expect(pickDuplicateAcrossLines([['A', 'B'], ['C'], ['b']])).toEqual([2, 'b'])
    expect(pickDuplicateAcrossLines([['A'], ['B']])).toBeNull()
    expect(firstOtherOwner([null, undefined, null, 'c1'])).toBe(3)
    expect(firstOtherOwner(['c1', 'c1'])).toBeNull()
  })
})

describe('errores del servidor', () => {
  const err = new ApiError(400, { title: 'Datos inválidos', code: 'validation', errors: { 'lines[0].countedQty': ['x'], 'lines[2]': ['y'], line: ['z'] } })
  it('renombra campos para ponerlos bajo el control', () => {
    const mapped = remapProblemFields(err, (k) => (k === 'line' ? 'serialNumbers' : k.replace(/^lines\[\d+\]\./, ''))) as ApiError
    expect(mapped).toBeInstanceOf(ApiError)
    expect(mapped.errors).toEqual({ countedQty: ['x'], 'lines[2]': ['y'], serialNumbers: ['z'] })
    expect(mapped.code).toBe('validation')
  })
  it('agrupa por índice de línea', () => {
    expect(lineErrorsByIndex(err)).toEqual({ 0: ['x'], 2: ['y'] })
    expect(lineErrorsByIndex(new Error('x'))).toEqual({})
  })
})
