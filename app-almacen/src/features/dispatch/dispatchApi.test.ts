import { __resetAllForTests } from 'expo-sqlite'

import { api } from '../../kernel/api/client'
import { __resetDbForTests } from '../../kernel/db/database'
import { countPending, listOutbox } from '../../kernel/sync/outbox'
import { fetchStockOptions, fetchClientsForOwnDispatch, fetchConsigneesForClient, resolveBinCodes, submitCollectAndPack } from './dispatchApi'
import type { PickLine } from './dispatchLogic'

jest.mock('../../kernel/api/client', () => {
  const actual = jest.requireActual('../../kernel/api/client')
  return { ...actual, api: { GET: jest.fn(), POST: jest.fn() } }
})

const getMock = api.GET as jest.Mock
const postMock = api.POST as jest.Mock

function ok(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) })
}

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  getMock.mockReset()
  postMock.mockReset()
})

describe('fetchConsigneesForClient', () => {
  it('mapea las ubicaciones del cliente a nombre y ciudad', async () => {
    getMock.mockResolvedValueOnce(ok([{ publicId: 'loc-1', name: 'Tienda Centro', city: 'San Juan' }]))
    const rows = await fetchConsigneesForClient('client-1')
    expect(getMock.mock.calls[0][1].params.query).toEqual({ clientId: 'client-1' })
    expect(rows).toEqual([{ publicId: 'loc-1', label: 'Tienda Centro · San Juan' }])
  })
})

const LINE_A: PickLine = { productPublicId: 'p1', sku: 'A', productName: 'A', quantity: 2, fromBinCode: 'B-1' }
const LINE_B: PickLine = { productPublicId: 'p2', sku: 'B', productName: 'B', quantity: 1, fromBinCode: 'B-2' }

/** Listado paginado de posiciones (Lote 1): sobre { total, skip, take, items }. */
function binPage(items: { id: number; code: string }[]) {
  return ok({ total: items.length, skip: 0, take: 200, items })
}

describe('resolveBinCodes', () => {
  it('resuelve un código por posición distinta y arma las líneas con el binId real', async () => {
    getMock.mockResolvedValueOnce(binPage([{ id: 10, code: 'B-1' }])).mockResolvedValueOnce(binPage([{ id: 20, code: 'B-2' }]))
    const result = await resolveBinCodes('wh-1', [LINE_A, LINE_B])
    expect(result.notFound).toEqual([])
    expect(result.lines).toEqual([
      { ...LINE_A, fromBinId: 10 },
      { ...LINE_B, fromBinId: 20 },
    ])
    expect(getMock).toHaveBeenCalledTimes(2)
  })

  it('un código repetido solo se resuelve una vez', async () => {
    getMock.mockResolvedValueOnce(binPage([{ id: 10, code: 'B-1' }]))
    const result = await resolveBinCodes('wh-1', [LINE_A, { ...LINE_A, productPublicId: 'p3' }])
    expect(getMock).toHaveBeenCalledTimes(1)
    expect(result.lines).toHaveLength(2)
    expect(result.lines.every((l) => l.fromBinId === 10)).toBe(true)
  })

  it('un código que no existe se reporta en notFound y no arma líneas', async () => {
    getMock.mockResolvedValueOnce(binPage([]))
    const result = await resolveBinCodes('wh-1', [LINE_A])
    expect(result.notFound).toEqual(['B-1'])
    expect(result.lines).toEqual([])
  })
})

describe('submitCollectAndPack', () => {
  const RESOLVED = [{ ...LINE_A, fromBinId: 10 }]

  it('si el API responde, no encola nada', async () => {
    postMock.mockResolvedValueOnce(ok({ batch: {}, order: {} }))
    const result = await submitCollectAndPack('wh-1', 'client-1', 'loc-1', 1, RESOLVED)
    expect(result).toEqual({ queued: false })
    expect(countPending()).toBe(0)
    expect(postMock.mock.calls[0][1].body).toMatchObject({ warehousePublicId: 'wh-1', lines: [{ productPublicId: 'p1', quantity: 2, binId: 10 }] })
  })

  it('sin red, encola el cuerpo armado como kind pack', async () => {
    postMock.mockResolvedValueOnce({ response: Response.error() })
    const result = await submitCollectAndPack('wh-1', 'client-1', 'loc-1', 1, RESOLVED)
    expect(result).toEqual({ queued: true })
    expect(countPending()).toBe(1)
    expect(listOutbox()[0].path).toBe('/api/v1/pick-batches/collect-and-pack')
  })

  it('un rechazo de negocio no se encola: se propaga para mostrarlo', async () => {
    postMock.mockResolvedValueOnce({ error: { title: 'Crédito excedido.', code: 'validation' }, response: new Response(null, { status: 400 }) })
    await expect(submitCollectAndPack('wh-1', 'client-1', 'loc-1', 1, RESOLVED)).rejects.toMatchObject({ title: 'Crédito excedido.' })
    expect(countPending()).toBe(0)
  })
})

describe('fetchClientsForOwnDispatch', () => {
  it('lista los clientes activos con nombre y código para elegir a quién se despacha inventario propio', async () => {
    getMock.mockResolvedValueOnce(ok([{ publicId: 'c-1', name: 'Farmacia Central', code: 'CLI-001' }, { publicId: 'c-2', name: 'Sin código', code: '' }]))
    const rows = await fetchClientsForOwnDispatch()
    expect(getMock.mock.calls[0][0]).toBe('/api/v1/clients')
    expect(getMock.mock.calls[0][1].params.query).toEqual({ includeInactive: false })
    expect(rows).toEqual([{ publicId: 'c-1', label: 'Farmacia Central · CLI-001' }, { publicId: 'c-2', label: 'Sin código' }])
  })
})

describe('fetchStockOptions', () => {
  it('pide el disponible del producto en el almacén y lo deja por posición y lote (sin las filas sin posición)', async () => {
    getMock.mockResolvedValueOnce(
      ok({
        items: [
          { binCode: 'A-01', zoneTypeCode: 'PICKING', lotNumber: 'L-3', expiryDate: '2027-01-31', qtyAvailable: 12 },
          { binCode: null, qtyAvailable: 9 },
          { binCode: 'B-02', qtyAvailable: 4 },
        ],
      }),
    )
    const rows = await fetchStockOptions('wh-1', 'p1')
    expect(getMock.mock.calls[0][0]).toBe('/api/v1/inventory/balances')
    expect(getMock.mock.calls[0][1].params.query).toEqual({ warehousePublicIds: ['wh-1'], productPublicIds: ['p1'], onlyAvailable: true, take: 200 })
    expect(rows).toEqual([
      { binCode: 'A-01', zoneTypeCode: 'PICKING', lotNumber: 'L-3', expiryDate: '2027-01-31', available: 12 },
      { binCode: 'B-02', zoneTypeCode: null, lotNumber: null, expiryDate: null, available: 4 },
    ])
  })
})
