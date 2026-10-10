import { __resetAllForTests } from 'expo-sqlite'

import { api } from '../../kernel/api/client'
import { __resetDbForTests, getDb } from '../../kernel/db/database'
import { fetchBinContents, resolveLookupBin, searchBalances } from './lookupApi'

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
    expect(result.rows).toEqual([{ id: 1, binId: null, lotId: null, zoneTypeCode: null, binCode: 'B-5', productPublicId: 'p1', sku: 'A', productName: 'Uno', lotNumber: null, qtyOnHand: 10, qtyAvailable: 8 }])
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

// ------------------------------------------------------------------ Lote A8: lo que hay en una posición

function insertBin(id: number, code: string, active: 0 | 1, warehouse = 'wh-1') {
  getDb().runSync('INSERT INTO bin (id, code, warehouse_public_id, is_active) VALUES (?, ?, ?, ?)', [id, code, warehouse, active])
}

describe('resolveLookupBin', () => {
  it('posición sincronizada activa: la toma sin preguntar al servidor', async () => {
    insertBin(5, 'A-01-01', 1)
    await expect(resolveLookupBin('wh-1', 'a-01-01')).resolves.toEqual({ kind: 'bin', bin: { id: 5, code: 'A-01-01' } })
    expect(getMock).not.toHaveBeenCalled()
  })

  it('no está en el aparato: la busca en el servidor por código exacto', async () => {
    getMock.mockResolvedValueOnce(ok({ total: 2, items: [{ id: 8, code: 'B-02-01' }, { id: 9, code: 'B-02' }] }))
    await expect(resolveLookupBin('wh-1', 'B-02')).resolves.toEqual({ kind: 'bin', bin: { id: 9, code: 'B-02', freeQty: null } })
    expect(getMock.mock.calls[0][0]).toBe('/api/v1/warehouses/{publicId}/bins')
  })

  it('el servidor no la tiene: none (la pantalla sigue con la búsqueda libre)', async () => {
    getMock.mockResolvedValueOnce(ok({ total: 0, items: [] }))
    await expect(resolveLookupBin('wh-1', 'LOTE-9')).resolves.toEqual({ kind: 'none' })
  })

  it('solo existe dada de baja en el aparato y el servidor no la encuentra activa: inactive', async () => {
    insertBin(6, 'OLD-01', 0)
    getMock.mockResolvedValueOnce(ok({ total: 0, items: [] }))
    await expect(resolveLookupBin('wh-1', 'old-01')).resolves.toEqual({ kind: 'inactive', code: 'OLD-01' })
  })

  it('sin señal y sin la posición en el aparato: none (no lanza)', async () => {
    getMock.mockResolvedValueOnce({ response: Response.error() })
    await expect(resolveLookupBin('wh-1', 'C-03')).resolves.toEqual({ kind: 'none' })
  })

  it('una posición de otro almacén no cuenta', async () => {
    insertBin(7, 'A-01-01', 1, 'wh-2')
    getMock.mockResolvedValueOnce(ok({ total: 0, items: [] }))
    await expect(resolveLookupBin('wh-1', 'A-01-01')).resolves.toEqual({ kind: 'none' })
  })
})

describe('fetchBinContents', () => {
  const BIN = { id: 5, code: 'A-01-01' }

  it('pide los saldos por el id de la posición (no por texto libre)', async () => {
    getMock.mockResolvedValueOnce(ok({ total: 1, items: [ITEM] }))
    const result = await fetchBinContents('wh-1', BIN)
    expect(getMock.mock.calls[0][0]).toBe('/api/v1/inventory/balances')
    expect(getMock.mock.calls[0][1].params.query).toEqual({ warehousePublicIds: ['wh-1'], binIds: [5], skip: 0, take: 200 })
    expect(result).toMatchObject({ fromCache: false, truncated: false })
    expect(result.rows).toHaveLength(1)
  })

  it('posición vacía: lista vacía, no es un error', async () => {
    getMock.mockResolvedValueOnce(ok({ total: 0, items: [] }))
    await expect(fetchBinContents('wh-1', BIN)).resolves.toMatchObject({ rows: [], truncated: false })
  })

  it('lee las páginas siguientes hasta el total', async () => {
    const page = (start: number, n: number) => Array.from({ length: n }, (_, i) => ({ ...ITEM, id: start + i }))
    getMock.mockResolvedValueOnce(ok({ total: 250, items: page(0, 200) })).mockResolvedValueOnce(ok({ total: 250, items: page(200, 50) }))
    const result = await fetchBinContents('wh-1', BIN)
    expect(result.rows).toHaveLength(250)
    expect(getMock.mock.calls[1][1].params.query.skip).toBe(200)
    expect(result.truncated).toBe(false)
  })

  it('más de 5 páginas: se queda con las primeras y lo marca', async () => {
    const page = Array.from({ length: 200 }, (_, i) => ({ ...ITEM, id: i }))
    for (let i = 0; i < 5; i++) getMock.mockResolvedValueOnce(ok({ total: 1500, items: page }))
    const result = await fetchBinContents('wh-1', BIN)
    expect(getMock).toHaveBeenCalledTimes(5)
    expect(result.rows).toHaveLength(1000)
    expect(result.truncated).toBe(true)
  })

  it('sin señal: la última respuesta de esa misma posición; sin nada guardado, el error de red', async () => {
    getMock.mockResolvedValueOnce({ response: Response.error() })
    await expect(fetchBinContents('wh-1', BIN)).rejects.toMatchObject({ code: 'network' })

    getMock.mockResolvedValueOnce(ok({ total: 1, items: [ITEM] }))
    await fetchBinContents('wh-1', BIN)
    getMock.mockResolvedValueOnce({ response: Response.error() })
    const cached = await fetchBinContents('wh-1', BIN)
    expect(cached.fromCache).toBe(true)
    expect(cached.rows).toHaveLength(1)
  })

  it('la caché de la posición no se mezcla con la búsqueda libre del mismo texto', async () => {
    getMock.mockResolvedValueOnce(ok({ items: [ITEM] }))
    await searchBalances('wh-1', 'A-01-01', null)
    getMock.mockResolvedValueOnce({ response: Response.error() })
    await expect(fetchBinContents('wh-1', BIN)).rejects.toMatchObject({ code: 'network' })
  })

  it('un error del servidor (403, 500) se propaga tal cual', async () => {
    getMock.mockResolvedValueOnce({
      error: { title: 'No tiene permiso para esta acción.' },
      response: new Response(JSON.stringify({ title: 'No tiene permiso para esta acción.' }), { status: 403 }),
    })
    await expect(fetchBinContents('wh-1', BIN)).rejects.toMatchObject({ title: 'No tiene permiso para esta acción.' })
  })
})
