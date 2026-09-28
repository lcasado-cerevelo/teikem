import { buildBatchItems, matchExpectedLine, parseQty, remainingExpectedLines, type CapturedEntry, type ExpectedLine } from './countLogic'

describe('parseQty', () => {
  it('acepta números con coma o punto, incluyendo 0', () => {
    expect(parseQty('3')).toBe(3)
    expect(parseQty('2,5')).toBe(2.5)
    expect(parseQty('0')).toBe(0)
  })

  it('vacío o inválido da null', () => {
    expect(parseQty('')).toBeNull()
    expect(parseQty('  ')).toBeNull()
    expect(parseQty('abc')).toBeNull()
    expect(parseQty('-1')).toBeNull()
  })
})

describe('buildBatchItems', () => {
  it('una línea esperada va por lineId', () => {
    const entries: CapturedEntry[] = [{ lineId: 7, productPublicId: 'p1', sku: 'A', productName: 'A', countedQty: 3, isExtra: false }]
    expect(buildBatchItems(5, entries)).toEqual([{ lineId: 7, countedQty: 3 }])
  })

  it('un producto encontrado que no estaba en la lista va por binId + productPublicId', () => {
    const entries: CapturedEntry[] = [{ lineId: null, productPublicId: 'p9', sku: 'Z', productName: 'Z', countedQty: 1, isExtra: true }]
    expect(buildBatchItems(5, entries)).toEqual([{ binId: 5, productPublicId: 'p9', countedQty: 1 }])
  })
})

const EXPECTED: ExpectedLine[] = [
  { lineId: 1, productPublicId: 'p1', sku: 'A', productName: 'Uno', systemQty: 3 },
  { lineId: 2, productPublicId: 'p2', sku: 'B', productName: 'Dos', systemQty: 1 },
]

describe('remainingExpectedLines', () => {
  it('quita las líneas ya capturadas', () => {
    expect(remainingExpectedLines(EXPECTED, new Set([1]))).toEqual([EXPECTED[1]])
  })

  it('sin nada capturado, devuelve todas', () => {
    expect(remainingExpectedLines(EXPECTED, new Set())).toEqual(EXPECTED)
  })
})

describe('matchExpectedLine', () => {
  it('un producto esperado sin capturar devuelve su línea', () => {
    expect(matchExpectedLine(EXPECTED, new Set(), 'p1')).toEqual(EXPECTED[0])
  })

  it('un producto ya capturado se puede volver a elegir (corregir cantidad)', () => {
    expect(matchExpectedLine(EXPECTED, new Set([1]), 'p1')).toEqual(EXPECTED[0])
  })

  it('un producto que no está en la lista da null (se captura como extra)', () => {
    expect(matchExpectedLine(EXPECTED, new Set(), 'p9')).toBeNull()
  })
})
