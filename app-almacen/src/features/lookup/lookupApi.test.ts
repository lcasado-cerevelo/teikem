import { __resetAllForTests } from 'expo-sqlite'

import { api } from '../../kernel/api/client'
import { __resetDbForTests } from '../../kernel/db/database'
import { searchBalances } from './lookupApi'

jest.mock('../../kernel/api/client', () => {
  const actual = jest.requireActual('../../kernel/api/client')
  return { ...actual, api: { GET: jest.fn() } }
})

const getMock = api.GET as jest.Mock

function ok(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) })
}

const ITEM = { id: 1, binCode: 'B-5', productPublicId: 'p1', sku: 'A', productName: 'Uno', lotNumber: null, qtyOnHand: 10, qtyAvailable: 8 }

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  getMock.mockReset()
})

describe('searchBalances', () => {
  it('busca por productPublicId cuando el producto ya se conoce localmente', async () => {
    getMock.mockResolvedValueOnce(ok({ items: [ITEM] }))
    const result = await searchBalances('wh-1', 'ABC', 'p1')
    expect(getMock.mock.calls[0][1].params.query).toEqual({ warehousePublicIds: ['wh-1'], productPublicIds: ['p1'], take: 50 })
    expect(result.fromCache).toBe(false)
    expect(result.rows).toEqual([{ id: 1, binCode: 'B-5', productPublicId: 'p1', sku: 'A', productName: 'Uno', lotNumber: null, qtyOnHand: 10, qtyAvailable: 8 }])
  })

  it('sin producto local, busca por texto libre (cubre posición)', async () => {
    getMock.mockResolvedValueOnce(ok({ items: [] }))
    await searchBalances('wh-1', 'B-5', null)
    expect(getMock.mock.calls[0][1].params.query).toEqual({ warehousePublicIds: ['wh-1'], search: 'B-5', take: 50 })
  })

  it('guarda la respuesta y una segunda consulta sin red la recupera de la caché', async () => {
    getMock.mockResolvedValueOnce(ok({ items: [ITEM] }))
    await searchBalances('wh-1', 'ABC', 'p1')

    getMock.mockResolvedValueOnce({ response: Response.error() })
    const result = await searchBalances('wh-1', 'ABC', 'p1')
    expect(result.fromCache).toBe(true)
    expect(result.rows).toHaveLength(1)
    expect(result.fetchedAtUtc).toBeTruthy()
  })

  it('sin red y sin nada guardado para ese código, propaga el error', async () => {
    getMock.mockResolvedValueOnce({ response: Response.error() })
    await expect(searchBalances('wh-1', 'ABC', 'p1')).rejects.toMatchObject({ code: 'network' })
  })

  it('una segunda consulta con red reemplaza la caché anterior', async () => {
    getMock.mockResolvedValueOnce(ok({ items: [ITEM] }))
    await searchBalances('wh-1', 'ABC', 'p1')

    getMock.mockResolvedValueOnce(ok({ items: [] }))
    const result = await searchBalances('wh-1', 'ABC', 'p1')
    expect(result.fromCache).toBe(false)
    expect(result.rows).toEqual([])
  })
})
