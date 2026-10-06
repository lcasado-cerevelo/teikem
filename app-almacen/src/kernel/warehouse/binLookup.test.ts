import { api } from '../api/client'
import { BIN_LOOKUP_PAGE, findBinByCode, freeQtyOf } from './binLookup'

jest.mock('../api/client', () => {
  const actual = jest.requireActual('../api/client')
  return { ...actual, api: { GET: jest.fn() } }
})

const getMock = api.GET as jest.Mock

function ok(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) })
}

/** Sobre paginado del listado de posiciones (Lote 1). */
function page(items: { id: number; code: string; maxCapacityQty?: number | null; qtyOnHand?: number }[], total = items.length, skip = 0) {
  return ok({ total, skip, take: BIN_LOOKUP_PAGE, items })
}

beforeEach(() => {
  getMock.mockReset()
})

describe('findBinByCode', () => {
  it('encuentra la posición que coincide exactamente con el código (sin importar mayúsculas)', async () => {
    getMock.mockResolvedValueOnce(
      page([
        { id: 6, code: 'A-01-B' },
        { id: 5, code: 'a-01' },
      ]),
    )
    expect(await findBinByCode('wh-1', 'A-01')).toEqual({ id: 5, code: 'a-01', freeQty: null })
    expect(getMock).toHaveBeenCalledTimes(1)
    expect(getMock.mock.calls[0][1].params.path).toEqual({ publicId: 'wh-1' })
    expect(getMock.mock.calls[0][1].params.query).toEqual({ search: 'A-01', skip: 0, take: BIN_LOOKUP_PAGE })
  })

  it('sin coincidencia exacta, devuelve null (una sola página: no hay más que leer)', async () => {
    getMock.mockResolvedValueOnce(page([{ id: 6, code: 'A-02' }]))
    expect(await findBinByCode('wh-1', 'A-99')).toBeNull()
    expect(getMock).toHaveBeenCalledTimes(1)
  })

  it('si la exacta no viene en la primera página, sigue con la siguiente (skip)', async () => {
    const first = Array.from({ length: BIN_LOOKUP_PAGE }, (_, i) => ({ id: 100 + i, code: `XA-01-${i}` }))
    getMock.mockResolvedValueOnce(page(first, BIN_LOOKUP_PAGE + 1)).mockResolvedValueOnce(page([{ id: 7, code: 'A-01' }], BIN_LOOKUP_PAGE + 1, BIN_LOOKUP_PAGE))
    expect(await findBinByCode('wh-1', 'a-01')).toEqual({ id: 7, code: 'A-01', freeQty: null })
    expect(getMock).toHaveBeenCalledTimes(2)
    expect(getMock.mock.calls[1][1].params.query).toEqual({ search: 'a-01', skip: BIN_LOOKUP_PAGE, take: BIN_LOOKUP_PAGE })
  })

  it('código vacío: null sin consultar', async () => {
    expect(await findBinByCode('wh-1', '  ')).toBeNull()
    expect(getMock).not.toHaveBeenCalled()
  })
})

describe('cupo libre de la posición', () => {
  it('freeQtyOf: cupo − existencia (nunca negativo); sin cupo = null', () => {
    expect(freeQtyOf(25, 10)).toBe(15)
    expect(freeQtyOf(25, 30)).toBe(0)
    expect(freeQtyOf(25, undefined)).toBe(25)
    expect(freeQtyOf(null, 10)).toBeNull()
    expect(freeQtyOf(undefined, 10)).toBeNull()
  })

  it('findBinByCode trae el espacio libre del listado', async () => {
    getMock.mockResolvedValueOnce(page([{ id: 5, code: 'A-01', maxCapacityQty: 25, qtyOnHand: 10 }]))
    expect(await findBinByCode('wh-1', 'A-01')).toEqual({ id: 5, code: 'A-01', freeQty: 15 })
  })
})
