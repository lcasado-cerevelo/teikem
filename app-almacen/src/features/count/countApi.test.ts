import { __resetAllForTests } from 'expo-sqlite'

import { api } from '../../kernel/api/client'
import { __resetDbForTests } from '../../kernel/db/database'
import { listOutbox } from '../../kernel/sync/outbox'
import type { CapturedEntry } from './countLogic'
import { getDb } from '../../kernel/db/database'
import {
  ApiError,
  cancelCountOnline,
  createProvisionalBin,
  enqueueFinishCount,
  fetchExpectedLines,
  fetchProductBins,
  fetchZones,
  findLocalBin,
  startCountOnline,
  startOpenCountOnline,
  startProductCountOnline,
} from './countApi'

jest.mock('../../kernel/api/client', () => {
  const actual = jest.requireActual('../../kernel/api/client')
  return { ...actual, api: { GET: jest.fn(), POST: jest.fn(), DELETE: jest.fn() } }
})

const getMock = api.GET as jest.Mock
const postMock = api.POST as jest.Mock
const deleteMock = api.DELETE as jest.Mock

function ok(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) })
}

function fail(status: number, error: unknown) {
  return Promise.resolve({ error, response: new Response(null, { status }) })
}

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  getMock.mockReset()
  postMock.mockReset()
  deleteMock.mockReset()
})

describe('startCountOnline', () => {
  it('arma el conteo y sus líneas esperadas de la respuesta del servidor', async () => {
    postMock.mockResolvedValueOnce(
      ok({
        count: { id: 42 },
        isBlind: true,
        lines: [{ id: 7, productPublicId: 'p1', sku: 'A', productName: 'Uno', systemQty: 3 }],
      }),
    )
    const result = await startCountOnline('wh-1', 5)
    expect(postMock.mock.calls[0][1].body).toEqual({ warehousePublicId: 'wh-1', binIds: [5] })
    expect(result).toEqual({
      countId: 42,
      isBlind: true,
      expectedLines: [{ lineId: 7, productPublicId: 'p1', sku: 'A', productName: 'Uno', systemQty: 3 }],
    })
  })

  it('conteo a ciegas: systemQty ausente se mapea a null', async () => {
    postMock.mockResolvedValueOnce(ok({ count: { id: 42 }, isBlind: true, lines: [{ id: 7, productPublicId: 'p1', sku: 'A', productName: 'Uno' }] }))
    const result = await startCountOnline('wh-1', 5)
    expect(result.expectedLines[0].systemQty).toBeNull()
  })
})

describe('fetchExpectedLines', () => {
  it('vuelve a pedir las líneas esperadas de un conteo ya abierto (resume tras reabrir la app)', async () => {
    getMock.mockResolvedValueOnce(ok({ count: { id: 42 }, isBlind: false, lines: [{ id: 7, productPublicId: 'p1', sku: 'A', productName: 'Uno', systemQty: 3 }] }))
    const lines = await fetchExpectedLines(42)
    expect(getMock.mock.calls[0][1].params.path).toEqual({ id: 42 })
    expect(lines).toEqual([{ lineId: 7, productPublicId: 'p1', sku: 'A', productName: 'Uno', systemQty: 3 }])
  })
})

describe('cancelCountOnline', () => {
  it('llama DELETE con el id del conteo', async () => {
    deleteMock.mockResolvedValueOnce(ok(undefined))
    await cancelCountOnline(42)
    expect(deleteMock.mock.calls[0][1].params.path).toEqual({ id: 42 })
  })
})

describe('enqueueFinishCount', () => {
  it('encola el lote y el cierre en orden, con la ruta del conteo', () => {
    const entries: CapturedEntry[] = [{ lineId: 7, productPublicId: 'p1', sku: 'A', productName: 'Uno', countedQty: 3, isExtra: false, binId: 5 }]
    enqueueFinishCount(42, entries)
    const rows = listOutbox()
    expect(rows).toHaveLength(2)
    expect(rows[0]).toMatchObject({ kind: 'countBatch', method: 'PUT', path: '/api/v1/cycle-counts/42/lines/batch' })
    expect(JSON.parse(rows[0].body)).toEqual({ lines: [{ lineId: 7, countedQty: 3 }] })
    expect(rows[1]).toMatchObject({ kind: 'countFinish', method: 'POST', path: '/api/v1/cycle-counts/42/finish' })
    expect(JSON.parse(rows[1].body)).toEqual({})
  })
})

describe('enqueueFinishCount — conteo por producto', () => {
  it('un solo lote con las líneas de varias posiciones y la fila nueva de "Otra posición", y luego el cierre', () => {
    const base = { productPublicId: 'p1', sku: 'A', productName: 'Uno' }
    enqueueFinishCount(50, [
      { ...base, lineId: 1, countedQty: 2, isExtra: false, binId: 10 },
      { ...base, lineId: 2, countedQty: 0, isExtra: false, binId: 11 },
      { ...base, lineId: null, countedQty: 5, isExtra: true, binId: 99, lotNumber: 'L-1', lotExpiryDate: null },
    ])
    const rows = listOutbox()
    expect(rows.map((r) => r.kind)).toEqual(['countBatch', 'countFinish'])
    expect(JSON.parse(rows[0].body)).toEqual({
      lines: [
        { lineId: 1, countedQty: 2 },
        { lineId: 2, countedQty: 0 },
        { binId: 99, productPublicId: 'p1', countedQty: 5, lot: { number: 'L-1' } },
      ],
    })
  })
})

describe('startProductCountOnline', () => {
  it('manda el producto sin posiciones y arma una fila por posición y lote', async () => {
    postMock.mockResolvedValueOnce(
      ok({
        count: { id: 77, originCode: 'PRODUCT' },
        isBlind: false,
        lines: [
          { id: 1, productPublicId: 'p1', sku: 'A', productName: 'Uno', systemQty: 4, binId: 10, binCode: 'A-01', lotId: null, lotNumber: null },
          { id: 2, productPublicId: 'p1', sku: 'A', productName: 'Uno', systemQty: 1, binId: 11, binCode: 'B-02', lotId: 5, lotNumber: 'L-5', binIsProvisional: true },
        ],
      }),
    )
    const result = await startProductCountOnline('wh-1', 'p1')
    expect(postMock.mock.calls[0][0]).toBe('/api/v1/cycle-counts')
    expect(postMock.mock.calls[0][1].body).toEqual({ warehousePublicId: 'wh-1', productPublicIds: ['p1'], allowEmpty: true })
    expect(result.countId).toBe(77)
    expect(result.isBlind).toBe(false)
    expect(result.lines).toEqual([
      { lineId: 1, productPublicId: 'p1', sku: 'A', productName: 'Uno', systemQty: 4, binId: 10, binCode: 'A-01', lotId: null, lotNumber: null, binIsProvisional: false },
      { lineId: 2, productPublicId: 'p1', sku: 'A', productName: 'Uno', systemQty: 1, binId: 11, binCode: 'B-02', lotId: 5, lotNumber: 'L-5', binIsProvisional: true },
    ])
  })

  it('un producto sin existencia abre un conteo vacío (allowEmpty): sin líneas', async () => {
    postMock.mockResolvedValueOnce(ok({ count: { id: 78, originCode: 'PRODUCT', lineCount: 0 }, isBlind: true, lines: [] }))
    const result = await startProductCountOnline('wh-1', 'p1')
    expect(postMock.mock.calls[0][1].body).toEqual({ warehousePublicId: 'wh-1', productPublicIds: ['p1'], allowEmpty: true })
    expect(result).toEqual({ countId: 78, isBlind: true, lines: [] })
  })

  it('un error del servidor llega con su mensaje', async () => {
    postMock.mockResolvedValueOnce(fail(404, { title: 'Producto no encontrado.', status: 404 }))
    const err = await startProductCountOnline('wh-1', 'p1').catch((e: unknown) => e)
    expect(err).toBeInstanceOf(ApiError)
    expect((err as ApiError).title).toBe('Producto no encontrado.')
  })
})

describe('"Otra posición"', () => {
  it('zonas: las activas del servidor', async () => {
    getMock.mockResolvedValueOnce(
      ok([
        { id: 1, code: 'PCK', name: 'Picking', isActive: true },
        { id: 2, code: 'OLD', name: 'Vieja', isActive: false },
      ]),
    )
    const result = await fetchZones('wh-1')
    expect(getMock.mock.calls[0][0]).toBe('/api/v1/warehouses/{publicId}/zones')
    expect(result).toEqual({ zones: [{ id: 1, code: 'PCK', name: 'Picking' }], fromLocal: false })
  })

  it('zonas: si el servidor no responde (o niega el permiso), las de las posiciones sincronizadas', async () => {
    const db = getDb()
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_id, zone_code, zone_name, is_active) VALUES (1, 'A-01', 'wh-1', 3, 'RES', 'Reserva', 1)")
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_id, zone_code, zone_name, is_active) VALUES (2, 'A-02', 'wh-1', 3, 'RES', 'Reserva', 1)")
    db.runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_id, zone_code, zone_name, is_active) VALUES (3, 'X-01', 'wh-2', 9, 'OTR', 'Otro', 1)")
    getMock.mockResolvedValueOnce(fail(403, { title: 'Prohibido', status: 403 }))
    expect(await fetchZones('wh-1')).toEqual({ zones: [{ id: 3, code: 'RES', name: 'Reserva' }], fromLocal: true })
  })

  it('crea la posición provisional con la zona y solo los datos escritos', async () => {
    postMock.mockResolvedValueOnce(ok({ id: 501, code: 'A01-R02', zoneId: 1, isProvisional: true }))
    const bin = await createProvisionalBin(77, { zoneId: 1, code: '  ', aisle: 'A01', rack: 'R02', level: '', position: undefined })
    expect(postMock.mock.calls[0][0]).toBe('/api/v1/cycle-counts/{id}/bins')
    expect(postMock.mock.calls[0][1].params.path).toEqual({ id: 77 })
    expect(postMock.mock.calls[0][1].body).toEqual({ zoneId: 1, aisle: 'A01', rack: 'R02' })
    expect(bin).toEqual({ id: 501, code: 'A01-R02', isProvisional: true })
  })

  it('el 409 del servidor llega con su mensaje exacto', async () => {
    postMock.mockResolvedValueOnce(fail(409, { title: 'Ya existe una posición con ese código en el almacén.', status: 409 }))
    const err = await createProvisionalBin(77, { zoneId: 1, code: 'A-01' }).catch((e: unknown) => e)
    expect(err).toBeInstanceOf(ApiError)
    expect((err as ApiError).status).toBe(409)
    expect((err as ApiError).title).toBe('Ya existe una posición con ese código en el almacén.')
  })

  it('una posición ya sincronizada se encuentra por código sin distinguir mayúsculas', () => {
    getDb().runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_id, is_active) VALUES (8, 'C-03', 'wh-1', 1, 1)")
    expect(findLocalBin('wh-1', 'c-03')).toEqual({ id: 8, code: 'C-03', isProvisional: false })
    expect(findLocalBin('wh-2', 'C-03')).toBeNull()
  })

  it('una posición sincronizada como provisional conserva su marca de "pendiente de revisión"', () => {
    getDb().runSync("INSERT INTO bin (id, code, warehouse_public_id, zone_id, is_active, is_provisional) VALUES (9, 'Z-09', 'wh-1', 1, 1, 1)")
    expect(findLocalBin('wh-1', 'z-09')).toEqual({ id: 9, code: 'Z-09', isProvisional: true })
  })
})

describe('conteo abierto (Lote 24)', () => {
  it('abre un conteo vacío: allowEmpty y nada más (ni producto ni posición)', async () => {
    postMock.mockResolvedValueOnce(ok({ count: { id: 900 }, isBlind: false, lines: [] }))
    expect(await startOpenCountOnline('wh-1')).toEqual({ countId: 900, isBlind: false })
    expect(postMock.mock.calls[0][0]).toBe('/api/v1/cycle-counts')
    expect(postMock.mock.calls[0][1].body).toEqual({ warehousePublicId: 'wh-1', allowEmpty: true })
  })

  it('dónde está el producto: una opción por posición y lote, sin cantidades', async () => {
    getMock.mockResolvedValueOnce(
      ok({ bins: [{ binId: 5, binCode: 'A-01', zoneCode: 'PCK', lotId: 2, lotNumber: 'L-2', lineId: null }, { binId: 6, binCode: 'B-02', zoneCode: 'RES' }] }),
    )
    const options = await fetchProductBins(900, 'p1')
    expect(getMock.mock.calls[0][0]).toBe('/api/v1/cycle-counts/{id}/product-bins')
    expect(getMock.mock.calls[0][1].params).toEqual({ path: { id: 900 }, query: { productPublicId: 'p1' } })
    expect(options).toEqual([
      { binId: 5, binCode: 'A-01', zoneCode: 'PCK', lotId: 2, lotNumber: 'L-2' },
      { binId: 6, binCode: 'B-02', zoneCode: 'RES', lotId: null, lotNumber: null },
    ])
  })
})
