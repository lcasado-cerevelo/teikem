import {
  blankAsZero,
  buildBatchItems,
  canConfirmProductCount,
  filterProductRows,
  findListedRow,
  matchExpectedLine,
  parseQty,
  PRODUCT_COUNT_SEARCH_THRESHOLD,
  productCountBlocker,
  remainingExpectedLines,
  resolveBinCode,
  showProductSearch,
  summarizeProductCount,
  type CapturedEntry,
  type ExpectedLine,
} from './countLogic'

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
    const entries: CapturedEntry[] = [{ lineId: 7, productPublicId: 'p1', sku: 'A', productName: 'A', countedQty: 3, isExtra: false, binId: 5 }]
    expect(buildBatchItems(entries)).toEqual([{ lineId: 7, countedQty: 3 }])
  })

  it('un producto encontrado que no estaba en la lista va por binId + productPublicId', () => {
    const entries: CapturedEntry[] = [{ lineId: null, productPublicId: 'p9', sku: 'Z', productName: 'Z', countedQty: 1, isExtra: true, binId: 5 }]
    expect(buildBatchItems(entries)).toEqual([{ binId: 5, productPublicId: 'p9', countedQty: 1 }])
  })

  it('conteo por producto: cada línea lleva su propia posición; las filas nuevas van con su lote por número', () => {
    const base = { productPublicId: 'p1', sku: 'A', productName: 'A' }
    const entries: CapturedEntry[] = [
      { ...base, lineId: 11, countedQty: 4, isExtra: false, binId: 1 },
      { ...base, lineId: 12, countedQty: 0, isExtra: false, binId: 2 },
      { ...base, lineId: 13, countedQty: 2.5, isExtra: false, binId: 3 },
      { ...base, lineId: null, countedQty: 6, isExtra: true, binId: 90, lotNumber: ' L-7 ', lotExpiryDate: '2027-01-31' },
      { ...base, lineId: null, countedQty: 1, isExtra: true, binId: 91, lotNumber: 'L-8', lotExpiryDate: null },
      { ...base, lineId: null, countedQty: 0, isExtra: true, binId: 92 },
    ]
    expect(buildBatchItems(entries)).toEqual([
      { lineId: 11, countedQty: 4 },
      { lineId: 12, countedQty: 0 },
      { lineId: 13, countedQty: 2.5 },
      { binId: 90, productPublicId: 'p1', countedQty: 6, lot: { number: 'L-7', expiryDate: '2027-01-31' } },
      { binId: 91, productPublicId: 'p1', countedQty: 1, lot: { number: 'L-8' } },
      { binId: 92, productPublicId: 'p1', countedQty: 0 },
    ])
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

describe('conteo por producto — reglas de captura', () => {
  it('un espacio en blanco es cero', () => {
    expect(blankAsZero(null)).toBe(0)
    expect(blankAsZero(0)).toBe(0)
    expect(blankAsZero(7)).toBe(7)
  })

  it('el resumen cuenta los blancos (se toman como 0) y lo que no es una cantidad', () => {
    expect(summarizeProductCount(['3', '', '  ', '0', 'abc', '-1'])).toEqual({ blanks: 2, invalid: 2, total: 6 })
    expect(summarizeProductCount(['1', '2,5'])).toEqual({ blanks: 0, invalid: 0, total: 2 })
  })

  it('Confirmar se puede con blancos (son 0) pero no con una cantidad inválida ni con la lista vacía', () => {
    expect(canConfirmProductCount(summarizeProductCount(['', '', '']))).toBe(true)
    expect(canConfirmProductCount(summarizeProductCount(['2', '']))).toBe(true)
    expect(canConfirmProductCount(summarizeProductCount(['2', 'x']))).toBe(false)
    expect(canConfirmProductCount(summarizeProductCount([]))).toBe(false)
  })

  it('el buscador aparece solo con más de 6 posiciones (constante ajustable)', () => {
    expect(PRODUCT_COUNT_SEARCH_THRESHOLD).toBe(6)
    expect(showProductSearch(1)).toBe(false)
    expect(showProductSearch(6)).toBe(false)
    expect(showProductSearch(7)).toBe(true)
    expect(showProductSearch(3, 2)).toBe(true)
  })

  it('el buscador filtra por posición o por lote, sin distinguir mayúsculas', () => {
    const rows = [
      { binCode: 'A-01-01', lotNumber: null },
      { binCode: 'B-02-01', lotNumber: 'L-77' },
      { binCode: 'C-03-01', lotNumber: 'X-1' },
    ]
    expect(filterProductRows(rows, '')).toHaveLength(3)
    expect(filterProductRows(rows, 'b-02')).toEqual([rows[1]])
    expect(filterProductRows(rows, 'l-7')).toEqual([rows[1]])
    expect(filterProductRows(rows, 'zzz')).toEqual([])
  })

  it('guarda de serie: un producto con serie no se cuenta por producto; con lote o sin seguimiento sí', () => {
    expect(productCountBlocker({ trackingTypeCode: 'SERIAL' })).toBe('serial')
    expect(productCountBlocker({ trackingTypeCode: 'LOT' })).toBeNull()
    expect(productCountBlocker({ trackingTypeCode: 'NONE' })).toBeNull()
  })
})

describe('conteo por producto — "Otra posición"', () => {
  const EMPTY = { code: '', aisle: '', rack: '', level: '', position: '' }

  it('el código escrito manda; si no hay, se compone de las partes como el servidor', () => {
    expect(resolveBinCode({ ...EMPTY, code: ' x-9 ' })).toBe('X-9')
    expect(resolveBinCode({ ...EMPTY, code: 'Z1', aisle: 'a01' })).toBe('Z1')
    expect(resolveBinCode({ code: '', aisle: 'a01', rack: 'r02', level: 'n3', position: 'p04' })).toBe('A01-R02-N3-P04')
    expect(resolveBinCode({ ...EMPTY, aisle: 'a01', level: 'n3' })).toBe('A01-N3')
    expect(resolveBinCode(EMPTY)).toBeNull()
  })

  it('detecta una posición (y lote) que ya está en la lista, para no mandar la misma línea dos veces', () => {
    const rows = [
      { binCode: 'A-01', lotNumber: null },
      { binCode: 'B-02', lotNumber: 'L-1' },
    ]
    expect(findListedRow(rows, 'a-01', null)).toBe(rows[0])
    expect(findListedRow(rows, 'B-02', 'l-1')).toBe(rows[1])
    expect(findListedRow(rows, 'B-02', 'L-2')).toBeNull()
    expect(findListedRow(rows, 'C-03', null)).toBeNull()
  })
})
