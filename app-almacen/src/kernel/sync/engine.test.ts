import { __resetAllForTests } from 'expo-sqlite'

import { api } from '../api/client'
import { __resetDbForTests } from '../db/database'
import { __resetSyncEngineForTests, getLastSync, runSync } from './engine'
import { enqueue } from './outbox'

jest.mock('../api/client', () => {
  const actual = jest.requireActual('../api/client')
  return { ...actual, api: { GET: jest.fn(), POST: jest.fn() } }
})

const getMock = api.GET as jest.Mock
const postMock = api.POST as jest.Mock

function emptyPage() {
  return Promise.resolve({ data: { items: [], nextCursor: null, serverTimeUtc: new Date().toISOString() }, response: new Response(null, { status: 200 }) })
}

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  __resetSyncEngineForTests()
  getMock.mockReset().mockImplementation(() => emptyPage())
  postMock.mockReset()
})

describe('runSync', () => {
  it('manda la cola y luego baja los tres recursos de Recibir', async () => {
    enqueue({ kind: 'receipt', body: { a: 1 } })
    postMock.mockResolvedValueOnce({ data: { header: {} }, response: new Response(null, { status: 200 }) })

    const summary = await runSync()

    expect(summary.error).toBeNull()
    expect(summary.outbox).toEqual({ sent: 1, rejected: 0, stoppedForNetwork: false, remaining: 0 })
    expect(summary.download.map((d) => d.resource)).toEqual(['products', 'purchaseOrders', 'asns'])
    expect(getLastSync()).toBe(summary)
  })

  it('si la cola se detiene por falta de red, no intenta bajar (para no gastar la señal en vano)', async () => {
    enqueue({ kind: 'receipt', body: { a: 1 } })
    postMock.mockResolvedValueOnce({ response: Response.error() })

    const summary = await runSync()

    expect(summary.outbox.stoppedForNetwork).toBe(true)
    expect(summary.download).toEqual([])
    expect(getMock).not.toHaveBeenCalled()
  })

  it('dos llamadas a la vez comparten el mismo ciclo (una sola pasada en vuelo)', async () => {
    let resolvePost: (v: unknown) => void = () => undefined
    postMock.mockReturnValueOnce(new Promise((resolve) => { resolvePost = resolve }))
    enqueue({ kind: 'receipt', body: { a: 1 } })

    const first = runSync()
    const second = runSync()
    expect(second).toBe(first)
    resolvePost({ data: {}, response: new Response(null, { status: 200 }) })
    await first
  })
})
