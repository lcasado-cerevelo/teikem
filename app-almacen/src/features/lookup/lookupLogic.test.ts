import {
  aggregateBinContents,
  BIN_CONTENT_MAX_LOTS_SHOWN,
  binCacheKey,
  cacheKey,
  filterBinContents,
  lotsToShow,
  minutesAgo,
  type BalanceRow,
} from './lookupLogic'

describe('cacheKey', () => {
  it('normaliza mayúsculas y espacios', () => {
    expect(cacheKey('wh-1', ' abc-1 ')).toBe('wh-1:ABC-1')
  })

  it('el mismo almacén y código dan la misma clave', () => {
    expect(cacheKey('wh-1', 'abc')).toBe(cacheKey('wh-1', 'ABC'))
  })

  it('almacenes distintos dan claves distintas', () => {
    expect(cacheKey('wh-1', 'abc')).not.toBe(cacheKey('wh-2', 'abc'))
  })
})

describe('minutesAgo', () => {
  it('calcula los minutos transcurridos', () => {
    const now = new Date('2026-01-01T12:10:00Z')
    expect(minutesAgo('2026-01-01T12:00:00Z', now)).toBe(10)
  })

  it('nunca da negativo (reloj adelantado)', () => {
    const now = new Date('2026-01-01T12:00:00Z')
    expect(minutesAgo('2026-01-01T12:05:00Z', now)).toBe(0)
  })
})

// ------------------------------------------------------------------ Lote A8: lo que hay en una posición

function row(id: number, productPublicId: string, sku: string, lotNumber: string | null, qtyOnHand: number, qtyAvailable = qtyOnHand): BalanceRow {
  return { id, binCode: 'A-01', productPublicId, sku, productName: `Producto ${sku}`, lotNumber, qtyOnHand, qtyAvailable }
}

describe('aggregateBinContents', () => {
  it('junta los lotes del mismo producto en una fila con la suma y la lista de lotes', () => {
    const items = aggregateBinContents([row(1, 'p1', 'TOR-1', 'L-A', 10, 8), row(2, 'p1', 'TOR-1', 'L-B', 5, 5), row(3, 'p2', 'TUE-2', null, 3, 1)])
    expect(items).toEqual([
      { productPublicId: 'p1', sku: 'TOR-1', productName: 'Producto TOR-1', lots: ['L-A', 'L-B'], qtyOnHand: 15, qtyAvailable: 13 },
      { productPublicId: 'p2', sku: 'TUE-2', productName: 'Producto TUE-2', lots: [], qtyOnHand: 3, qtyAvailable: 1 },
    ])
  })

  it('un lote repetido (dos filas del mismo lote) se nombra una vez y se suma', () => {
    const [item] = aggregateBinContents([row(1, 'p1', 'A', 'L-1', 2), row(2, 'p1', 'A', 'L-1', 3)])
    expect(item.lots).toEqual(['L-1'])
    expect(item.qtyOnHand).toBe(5)
  })

  it('ordena por SKU sin distinguir mayúsculas', () => {
    const items = aggregateBinContents([row(1, 'p3', 'zeta', null, 1), row(2, 'p1', 'Alfa', null, 1), row(3, 'p2', 'BETA', null, 1)])
    expect(items.map((i) => i.sku)).toEqual(['Alfa', 'BETA', 'zeta'])
  })

  it('sin filas, lista vacía', () => {
    expect(aggregateBinContents([])).toEqual([])
  })
})

describe('lotsToShow', () => {
  it('pocos lotes: todos', () => {
    expect(lotsToShow(['A', 'B'])).toEqual({ shown: ['A', 'B'], more: 0 })
  })

  it(`más de ${BIN_CONTENT_MAX_LOTS_SHOWN}: los primeros y cuántos más`, () => {
    expect(lotsToShow(['A', 'B', 'C', 'D', 'E'])).toEqual({ shown: ['A', 'B', 'C'], more: 2 })
  })
})

describe('filterBinContents', () => {
  const items = aggregateBinContents([row(1, 'p1', 'TOR-1', 'L-77', 1), row(2, 'p2', 'TUE-2', null, 1)])

  it('filtra por SKU, nombre o lote sin distinguir mayúsculas', () => {
    expect(filterBinContents(items, 'tue').map((i) => i.sku)).toEqual(['TUE-2'])
    expect(filterBinContents(items, 'producto tor').map((i) => i.sku)).toEqual(['TOR-1'])
    expect(filterBinContents(items, 'l-77').map((i) => i.sku)).toEqual(['TOR-1'])
  })

  it('sin texto, todas', () => {
    expect(filterBinContents(items, '  ')).toHaveLength(2)
  })
})

describe('binCacheKey', () => {
  it('es distinta de la búsqueda libre del mismo texto', () => {
    expect(binCacheKey('wh-1', 'a-01')).toBe('wh-1:BIN:A-01')
    expect(binCacheKey('wh-1', 'A-01')).not.toBe(cacheKey('wh-1', 'A-01'))
  })
})
