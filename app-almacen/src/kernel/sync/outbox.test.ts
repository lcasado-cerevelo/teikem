import { __resetAllForTests } from 'expo-sqlite'

import { api } from '../api/client'
import { __resetDbForTests, getDb } from '../db/database'
import { listSkippedNotices } from '../../features/count/countSkipped'
import { countPending, discardRow, enqueue, listOutbox, outboxResult, outboxStatus, retryRow, runOutbox } from './outbox'

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

  describe('lote de conteo parcial (Lote A7)', () => {
    const PATH = '/api/v1/cycle-counts/300/lines/batch'
    const BODY = { lines: [{ lineId: 1, countedQty: 4 }, { lineId: 2, countedQty: 6 }] }
    const SKIPPED = { lineId: 2, binCode: 'A-01', sku: 'SKU-B', lotNumber: null, sentQty: 6, currentQty: 5, reasonCode: 'CORRECTED_BY_SUPERVISOR', message: 'x' }

    it('200 con skippedLines: la fila queda enviada y se guarda el aviso', async () => {
      const id = enqueue({ kind: 'countBatch', body: BODY, path: PATH })
      putMock.mockImplementation(() => ok({ count: { id: 300 }, lines: [], skippedLines: [SKIPPED] }))

      const result = await runOutbox()

      expect(result).toEqual({ sent: 1, rejected: 0, stoppedForNetwork: false, remaining: 0 })
      expect(listOutbox().map((r) => [r.id, r.status, r.last_error])).toEqual([[id, 'sent', null]])
      const notices = listSkippedNotices()
      expect(notices).toHaveLength(1)
      expect(notices[0]).toMatchObject({ outboxId: id, countId: 300 })
      expect(notices[0].lines).toEqual([{ lineId: 2, sku: 'SKU-B', binCode: 'A-01', lotNumber: null, sentQty: 6, currentQty: 5 }])
    })

    it('200 sin skippedLines (o null o vacío): no hay aviso', async () => {
      enqueue({ kind: 'countBatch', body: BODY, path: PATH })
      enqueue({ kind: 'countBatch', body: BODY, path: PATH })
      enqueue({ kind: 'countBatch', body: BODY, path: PATH })
      putMock
        .mockImplementationOnce(() => ok({ lines: [] }))
        .mockImplementationOnce(() => ok({ lines: [], skippedLines: null }))
        .mockImplementationOnce(() => ok({ lines: [], skippedLines: [] }))
      await runOutbox()
      expect(listOutbox().map((r) => r.status)).toEqual(['sent', 'sent', 'sent'])
      expect(listSkippedNotices()).toEqual([])
    })

    it('solo los lotes de conteo generan aviso (un recibo con skippedLines inesperado no)', async () => {
      enqueue({ kind: 'receipt', body: { a: 1 } })
      postMock.mockImplementation(() => ok({ skippedLines: [SKIPPED] }))
      await runOutbox()
      expect(listSkippedNotices()).toEqual([])
    })

    it('el 409 residual (todas corregidas) sigue siendo un rechazo y no genera aviso', async () => {
      enqueue({ kind: 'countBatch', body: BODY, path: PATH })
      putMock.mockImplementation(() =>
        problem(409, { title: 'La línea ya fue corregida por el supervisor; no se puede volver a capturar. Renglón(es) del lote: 1 (SKU-B). No se guardó nada.', code: 'conflict' }),
      )
      const result = await runOutbox()
      expect(result.rejected).toBe(1)
      expect(listOutbox()[0].status).toBe('rejected')
      expect(listSkippedNotices()).toEqual([])
    })
  })
})

describe('cola de salida — despacho manual (2026-10-11)', () => {
  it('manualIssue va por POST /api/v1/manual-issues con su Idempotency-Key, en orden con lo demás, y guarda la respuesta (número DMA)', async () => {
    enqueue({ kind: 'receipt', body: { a: 1 } })
    const id = enqueue({ kind: 'manualIssue', body: { reasonCode: 'SAMPLE' }, projection: [] })
    const seen: Array<[string, string]> = []
    postMock.mockImplementation((path, opts) => {
      seen.push([path, opts.headers['Idempotency-Key']])
      return ok(path === '/api/v1/manual-issues' ? { number: 'DMA-00012' } : {})
    })
    expect(outboxResult(id)).toBeNull()
    await runOutbox()
    expect(seen.map((x) => x[0])).toEqual(['/api/v1/receipts', '/api/v1/manual-issues'])
    expect(seen[1][1]).toBe(listOutbox()[1].idempotency_key)
    expect(outboxStatus(id)).toEqual({ status: 'sent', error: null })
    expect(outboxResult(id)).toEqual({ number: 'DMA-00012' })
    expect(listOutbox()[1].projection_json).toBe('[]')
  })

  it('un 409 con código propio del servidor (insufficient_stock) es un rechazo: no se queda pendiente para siempre', async () => {
    const id = enqueue({ kind: 'manualIssue', body: {} })
    postMock.mockImplementationOnce(() => problem(409, { title: 'Inventario insuficiente de A en A-01: disponible 1, solicitado 2.', code: 'insufficient_stock' }))
    const result = await runOutbox()
    expect(result).toMatchObject({ rejected: 1, remaining: 0 })
    expect(outboxStatus(id)).toEqual({ status: 'rejected', error: 'Inventario insuficiente de A en A-01: disponible 1, solicitado 2.' })
  })

  it('un 403 de módulo apagado (module_disabled) también es un rechazo; un 500 sigue pendiente', async () => {
    const a = enqueue({ kind: 'manualIssue', body: {} })
    const b = enqueue({ kind: 'manualIssue', body: {} })
    postMock
      .mockImplementationOnce(() => problem(403, { title: "El módulo 'WMS_LOTSERIAL' no está habilitado para esta compañía.", code: 'module_disabled' }))
      .mockImplementationOnce(() => problem(500, { title: 'Error interno.' }))
    await runOutbox()
    expect(outboxStatus(a)?.status).toBe('rejected')
    expect(outboxStatus(b)?.status).toBe('pending')
  })
})
