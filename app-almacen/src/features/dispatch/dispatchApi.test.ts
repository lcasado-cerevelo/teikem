import { __resetAllForTests } from 'expo-sqlite'

import { api } from '../../kernel/api/client'
import { __resetDbForTests, getDb } from '../../kernel/db/database'
import { countPending, listOutbox } from '../../kernel/sync/outbox'
import { fetchStockOptions, queueManualIssue, fetchClientsForOwnDispatch, fetchConsigneesForClient, resolveBinCodes, submitCollectAndPack } from './dispatchApi'
import { addLocalPickLine, getOpenPick, startLocalPick } from './localPick'
import type { PickLine } from './dispatchLogic'
import { mapExitRow, readStockExit, replaceStockExit } from './stockExit'

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
  const ROW = { productPublicId: 'p1', binId: 5, binCode: 'A-01', zoneCode: 'PCK', zoneTypeCode: 'PICKING', lotId: 2, lotNumber: 'L-3', expiryDate: '2027-01-31', available: 12, rank: 1 }

  it('en línea pide el orden de salida del producto al servidor, lo deja en el aparato y devuelve lo que contestó (con su rank)', async () => {
    getMock.mockResolvedValueOnce(ok({ total: 2, skip: 0, take: 500, items: [ROW, { ...ROW, binId: 6, binCode: 'B-02', lotId: null, lotNumber: null, expiryDate: null, available: 4, rank: 2 }] }))
    const result = await fetchStockOptions('wh-1', 'p1')
    expect(getMock.mock.calls[0][0]).toBe('/api/v1/inventory/exit-options')
    expect(getMock.mock.calls[0][1].params.query).toEqual({ warehousePublicId: 'wh-1', productPublicIds: ['p1'], take: 500 })
    expect(result.source).toBe('server')
    expect(result.options.map((o) => [o.binCode, o.rank, o.available])).toEqual([['A-01', 1, 12], ['B-02', 2, 4]])
    // la copia del aparato quedó al día para ese producto
    expect(readStockExit('wh-1', 'p1').map((r) => r.binCode)).toEqual(['A-01', 'B-02'])
  })

  it('sin señal usa la copia del aparato (la que bajó la sincronización); sin copia no se sabe (none)', async () => {
    getMock.mockRejectedValue(new TypeError('Network request failed'))
    expect(await fetchStockOptions('wh-1', 'p1')).toEqual({ options: [], source: 'none' })
    getDb().runSync("INSERT INTO sync_watermark (resource, since_utc, last_run_utc) VALUES ('stockExit:wh-1', 'x', 'y')")
    replaceStockExit('wh-1', [mapExitRow(ROW)])
    const offline = await fetchStockOptions('wh-1', 'p1')
    expect(offline.source).toBe('device')
    expect(offline.options.map((o) => [o.binCode, o.lotNumber, o.rank])).toEqual([['A-01', 'L-3', 1]])
    // un producto sin existencia en la copia: no hay de dónde sacarlo (no es "no se sabe")
    expect((await fetchStockOptions('wh-1', 'otro')).options).toEqual([])
  })
})

describe('queueManualIssue (despacho manual, 2026-10-11)', () => {
  const LINES = [{ productPublicId: 'p1', sku: 'A', productName: 'A', quantity: 2, fromBinCode: 'A-01', fromBinId: 5 }]

  function seedOpenPick() {
    const id = startLocalPick('wh-1', null)
    addLocalPickLine(id, { productPublicId: 'p1', sku: 'A', productName: 'A', quantity: 2, fromBinCode: 'A-01' })
  }

  it('encola POST /manual-issues con motivo, nota recortada y las líneas, sin llamar al servidor, y cierra el despacho local en el mismo paso', () => {
    seedOpenPick()
    const id = queueManualIssue('wh-1', 'SAMPLE', '  Feria de salud  ', LINES)
    expect(postMock).not.toHaveBeenCalled()
    const row = listOutbox()[0]
    expect(row.id).toBe(id)
    expect([row.kind, row.method, row.path, row.status]).toEqual(['manualIssue', 'POST', '/api/v1/manual-issues', 'pending'])
    expect(JSON.parse(row.body)).toEqual({
      warehousePublicId: 'wh-1',
      lines: [{ productPublicId: 'p1', quantity: 2, binId: 5 }],
      reasonCode: 'SAMPLE',
      note: 'Feria de salud',
    })
    expect(row.idempotency_key).toMatch(/^app-/)
    expect(getOpenPick()).toBeNull()
    expect(countPending()).toBe(1)
  })

  it('una nota en blanco viaja como null', () => {
    queueManualIssue('wh-1', 'OTHER', '   ', LINES)
    expect(JSON.parse(listOutbox()[0].body).note).toBeNull()
  })
})
