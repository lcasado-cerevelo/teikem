import { __resetAllForTests } from 'expo-sqlite'

import { api } from '../api/client'
import { __resetDbForTests, getDb } from '../db/database'
import { countPending, discardRow, enqueue, listOutbox, retryRow, runOutbox } from './outbox'

jest.mock('../api/client', () => {
  const actual = jest.requireActual('../api/client')
  return { ...actual, api: { POST: jest.fn(), PUT: jest.fn() } }
})

const postMock = api.POST as jest.Mock
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const putMock = (api as any).PUT as jest.Mock

beforeEach(() => {
  __resetAllForTests()
  __resetDbForTests()
  postMock.mockReset()
  putMock.mockReset()
})

function ok(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) })
}
function problem(status: number, body: { title?: string; code?: string }) {
  return Promise.resolve({ error: body, response: new Response(null, { status }) })
}
function networkFailure() {
  return Promise.resolve({ response: Response.error() })
}

describe('cola de salida', () => {
  it('envía las operaciones en el orden en que se crearon (FIFO) con su propia Idempotency-Key', async () => {
    enqueue({ kind: 'receipt', body: { a: 1 } })
    enqueue({ kind: 'receipt', body: { a: 2 } })
    const seenKeys: string[] = []
    postMock.mockImplementation((_path, opts) => {
      seenKeys.push(opts.headers['Idempotency-Key'])
      return ok({ publicId: 'r-' + opts.headers['Idempotency-Key'] })
    })

    const result = await runOutbox()

    expect(result).toEqual({ sent: 2, rejected: 0, stoppedForNetwork: false, remaining: 0 })
    expect(seenKeys).toHaveLength(2)
    expect(seenKeys[0]).not.toBe(seenKeys[1])
    const rows = listOutbox()
    expect(rows.map((r) => r.status)).toEqual(['sent', 'sent'])
  })

  it('un rechazo de negocio (409) marca la fila como rechazada y sigue con la siguiente', async () => {
    enqueue({ kind: 'receipt', body: { a: 1 } })
    enqueue({ kind: 'receipt', body: { a: 2 } })
    postMock
      .mockImplementationOnce(() => problem(409, { title: 'La clave de idempotencia ya se usó con otro contenido.', code: 'conflict' }))
      .mockImplementationOnce(() => ok({ publicId: 'r-2' }))

    const result = await runOutbox()

    expect(result).toEqual({ sent: 1, rejected: 1, stoppedForNetwork: false, remaining: 0 })
    const rows = listOutbox()
    expect(rows[0].status).toBe('rejected')
    expect(rows[0].last_error).toBe('La clave de idempotencia ya se usó con otro contenido.')
    expect(rows[1].status).toBe('sent')
  })

  it('sin red se detiene en la primera y deja las demás pendientes', async () => {
    enqueue({ kind: 'receipt', body: { a: 1 } })
    enqueue({ kind: 'receipt', body: { a: 2 } })
    postMock.mockImplementation(() => networkFailure())

    const result = await runOutbox()

    expect(result).toEqual({ sent: 0, rejected: 0, stoppedForNetwork: true, remaining: 2 })
    expect(postMock).toHaveBeenCalledTimes(1)
    expect(listOutbox().every((r) => r.status === 'pending')).toBe(true)
  })

  it('un 500 cuenta el intento pero no rechaza la fila, y sigue con la siguiente (documentos independientes)', async () => {
    enqueue({ kind: 'receipt', body: { a: 1 } })
    enqueue({ kind: 'receipt', body: { a: 2 } })
    postMock
      .mockImplementationOnce(() => problem(500, { title: 'Error interno.', code: 'internal' }))
      .mockImplementationOnce(() => ok({ publicId: 'r-2' }))

    const result = await runOutbox()

    expect(result).toEqual({ sent: 1, rejected: 0, stoppedForNetwork: false, remaining: 1 })
    const rows = listOutbox()
    expect(rows[0].status).toBe('pending')
    expect(rows[0].attempts).toBe(1)
    expect(rows[1].status).toBe('sent')
  })

  it('reintentar una fila rechazada la vuelve a poner pendiente', async () => {
    const id = enqueue({ kind: 'receipt', body: { a: 1 } })
    postMock.mockImplementationOnce(() => problem(404, { title: 'No existe.', code: 'not_found' }))
    await runOutbox()
    expect(listOutbox()[0].status).toBe('rejected')

    retryRow(id)
    expect(listOutbox()[0].status).toBe('pending')

    postMock.mockImplementationOnce(() => ok({ publicId: 'r-1' }))
    const result = await runOutbox()
    expect(result.sent).toBe(1)
  })

  it('descartar una fila la elimina de la cola', () => {
    const id = enqueue({ kind: 'receipt', body: { a: 1 } })
    expect(countPending()).toBe(1)
    discardRow(id)
    expect(countPending()).toBe(0)
    expect(listOutbox()).toHaveLength(0)
  })

  it('countPending refleja solo las pendientes', async () => {
    enqueue({ kind: 'receipt', body: { a: 1 } })
    enqueue({ kind: 'receipt', body: { a: 2 } })
    expect(countPending()).toBe(2)
    postMock.mockImplementation(() => ok({ publicId: 'x' }))
    await runOutbox()
    expect(countPending()).toBe(0)
  })
})

describe('enqueue', () => {
  it('guarda el cuerpo como JSON y la ruta según el tipo (receipt, pack: ruta fija)', () => {
    const id = enqueue({ kind: 'receipt', body: { warehousePublicId: 'wh-1' } })
    const row = getDb().getFirstSync<{ path: string; method: string; body: string }>('SELECT path, method, body FROM outbox WHERE id = ?', [id])
    expect(row?.path).toBe('/api/v1/receipts')
    expect(row?.method).toBe('POST')
    expect(JSON.parse(row?.body ?? '{}')).toEqual({ warehousePublicId: 'wh-1' })

    const packId = enqueue({ kind: 'pack', body: { warehousePublicId: 'wh-1' } })
    const packRow = getDb().getFirstSync<{ path: string }>('SELECT path FROM outbox WHERE id = ?', [packId])
    expect(packRow?.path).toBe('/api/v1/pick-batches/collect-and-pack')
  })

  it('countBatch/countFinish exigen path (la ruta lleva el id del conteo, solo se sabe en línea)', () => {
    expect(() => enqueue({ kind: 'countBatch', body: {} })).toThrow(/hace falta 'path'/i)

    const id = enqueue({ kind: 'countBatch', body: { lines: [] }, path: '/api/v1/cycle-counts/42/lines/batch' })
    const row = getDb().getFirstSync<{ path: string; method: string }>('SELECT path, method FROM outbox WHERE id = ?', [id])
    expect(row).toEqual({ path: '/api/v1/cycle-counts/42/lines/batch', method: 'PUT' })

    const finishId = enqueue({ kind: 'countFinish', body: {}, path: '/api/v1/cycle-counts/42/finish' })
    const finishRow = getDb().getFirstSync<{ method: string }>('SELECT method FROM outbox WHERE id = ?', [finishId])
    expect(finishRow?.method).toBe('POST')
  })

  it('runOutbox manda countBatch con PUT a la ruta guardada', async () => {
    enqueue({ kind: 'countBatch', body: { lines: [{ lineId: 1, countedQty: 3 }] }, path: '/api/v1/cycle-counts/42/lines/batch' })
    putMock.mockResolvedValueOnce(ok({ count: { id: 42 } }))

    const result = await runOutbox()

    expect(result.sent).toBe(1)
    expect(putMock).toHaveBeenCalledTimes(1)
    expect(putMock.mock.calls[0][0]).toBe('/api/v1/cycle-counts/42/lines/batch')
    expect(putMock.mock.calls[0][1].body).toEqual({ lines: [{ lineId: 1, countedQty: 3 }] })
  })
})
