import { __resetAllForTests } from 'expo-sqlite'

import { api } from '../../kernel/api/client'
import { __resetDbForTests } from '../../kernel/db/database'
import { listOutbox } from '../../kernel/sync/outbox'
import type { CapturedEntry } from './countLogic'
import { cancelCountOnline, enqueueFinishCount, fetchExpectedLines, startCountOnline } from './countApi'

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
    const entries: CapturedEntry[] = [{ lineId: 7, productPublicId: 'p1', sku: 'A', productName: 'Uno', countedQty: 3, isExtra: false }]
    enqueueFinishCount(42, 5, entries)
    const rows = listOutbox()
    expect(rows).toHaveLength(2)
    expect(rows[0]).toMatchObject({ kind: 'countBatch', method: 'PUT', path: '/api/v1/cycle-counts/42/lines/batch' })
    expect(JSON.parse(rows[0].body)).toEqual({ lines: [{ lineId: 7, countedQty: 3 }] })
    expect(rows[1]).toMatchObject({ kind: 'countFinish', method: 'POST', path: '/api/v1/cycle-counts/42/finish' })
    expect(JSON.parse(rows[1].body)).toEqual({})
  })
})
