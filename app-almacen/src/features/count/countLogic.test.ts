import {
  binOptionLabel,
  defaultBinOption,
  blankAsZero,
  buildBatchItems,
  canConfirmProductCount,
  hasAnyCountedQty,
  hasNothingToConfirm,
  isAllBlank,
  filterProductRows,
  findListedRow,
  matchExpectedLine,
  parseQty,
  PRODUCT_COUNT_SEARCH_THRESHOLD,
  productCountBlocker,
  productCountConfirmBlock,
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
    expect(summarizeProductCount(['3', '', '  ', '0', 'abc', '-1'])).toEqual({ blanks: 2, invalid: 2, filled: 2, total: 6 })
    expect(summarizeProductCount(['1', '2,5'])).toEqual({ blanks: 0, invalid: 0, filled: 2, total: 2 })
  })

  it('el botón Confirmar está encendido salvo con una cantidad inválida (los demás bloqueos se explican al tocarlo)', () => {
    expect(canConfirmProductCount(summarizeProductCount(['', '', '']))).toBe(true)
    expect(canConfirmProductCount(summarizeProductCount(['2', '']))).toBe(true)
    expect(canConfirmProductCount(summarizeProductCount(['2', 'x']))).toBe(false)
    expect(canConfirmProductCount(summarizeProductCount([]))).toBe(true)
    expect(hasNothingToConfirm(summarizeProductCount([]))).toBe(true)
    expect(hasNothingToConfirm(summarizeProductCount(['']))).toBe(false)
  })

  // decisión del dueño 4 (docs/decisiones-del-dueno-2026-10-03.md): al menos un número escrito; 0 vale
  it('todo en blanco → bloqueado (allBlank), sin importar cuántas filas', () => {
    expect(productCountConfirmBlock(summarizeProductCount(['']))).toBe('allBlank')
    expect(productCountConfirmBlock(summarizeProductCount(['', '  ', '']))).toBe('allBlank')
    expect(isAllBlank(summarizeProductCount(['', '']))).toBe(true)
  })

  it('solo ceros → permitido', () => {
    expect(productCountConfirmBlock(summarizeProductCount(['0']))).toBeNull()
    expect(productCountConfirmBlock(summarizeProductCount(['0', '0', '0,0']))).toBeNull()
    expect(isAllBlank(summarizeProductCount(['0', '0']))).toBe(false)
  })

  it('un número y el resto en blanco → permitido (los blancos viajan como 0)', () => {
    expect(productCountConfirmBlock(summarizeProductCount(['', '5', '']))).toBeNull()
    expect(productCountConfirmBlock(summarizeProductCount(['', '', '0']))).toBeNull()
  })

  it('sin filas → empty (aviso del conteo vacío); algo inválido → invalid (antes que todo en blanco)', () => {
    expect(productCountConfirmBlock(summarizeProductCount([]))).toBe('empty')
    expect(productCountConfirmBlock(summarizeProductCount(['x', '']))).toBe('invalid')
    expect(productCountConfirmBlock(summarizeProductCount(['x', '3']))).toBe('invalid')
    expect(isAllBlank(summarizeProductCount([]))).toBe(false)
    expect(isAllBlank(summarizeProductCount(['x']))).toBe(false)
  })

  it('misma regla sobre lo guardado (null = en blanco)', () => {
    expect(hasAnyCountedQty([])).toBe(false)
    expect(hasAnyCountedQty([null, null])).toBe(false)
    expect(hasAnyCountedQty([null, 0])).toBe(true)
    expect(hasAnyCountedQty([7])).toBe(true)
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

describe('conteo abierto (Lote 24)', () => {
  const A = { binId: 1, binCode: 'A-01', zoneCode: 'PCK', lotId: null, lotNumber: null }
  const B = { binId: 2, binCode: 'B-02', zoneCode: 'RES', lotId: 7, lotNumber: 'L-7' }

  it('la posición por defecto solo existe con UNA opción; con varias o ninguna se elige', () => {
    expect(defaultBinOption([A])).toBe(A)
    expect(defaultBinOption([A, B])).toBeNull()
    expect(defaultBinOption([])).toBeNull()
  })

  it('el texto de la opción lleva código, lote (si lo hay) y zona', () => {
    const t = (key: string, params?: Record<string, string | number>) => (key === 'count.rowLot' ? `Lote ${params?.lot}` : key)
    expect(binOptionLabel(A, t)).toBe('A-01 · PCK')
    expect(binOptionLabel(B, t)).toBe('B-02 · Lote L-7 · RES')
    expect(binOptionLabel({ ...A, zoneCode: '' }, t)).toBe('A-01')
  })

  it('una línea nueva del conteo abierto viaja por posición + producto (+ lote por número), sin lineId', () => {
    expect(
      buildBatchItems([
        { lineId: null, productPublicId: 'p1', sku: 'S', productName: 'N', countedQty: 4, isExtra: true, binId: 10 },
        { lineId: null, productPublicId: 'p2', sku: 'T', productName: 'M', countedQty: 1, isExtra: true, binId: 11, lotNumber: 'L-9' },
      ]),
    ).toEqual([
      { binId: 10, productPublicId: 'p1', countedQty: 4 },
      { binId: 11, productPublicId: 'p2', countedQty: 1, lot: { number: 'L-9' } },
    ])
  })
})
