// Lote A6 — "Actualizar el conteo" tras el 409: GET /cycle-counts/{id} → estado y líneas vigentes (sin cantidades esperadas).
import { api } from '../../kernel/api/client'
import { fetchCountForRejection } from './countRejectionApi'

jest.mock('../../kernel/api/client', () => {
  const actual = jest.requireActual('../../kernel/api/client')
  return { ...actual, api: { GET: jest.fn() } }
})

const getMock = api.GET as jest.Mock

function ok(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) })
}
function fail(status: number, error: unknown) {
  return Promise.resolve({ error, response: new Response(null, { status }) })
}

beforeEach(() => getMock.mockReset())

describe('fetchCountForRejection', () => {
  it('pide el conteo por su id y devuelve estado, número y líneas vigentes sin la cantidad esperada', async () => {
    getMock.mockReturnValue(
      ok({
        count: { id: 300, number: 'CC-0300', statusCode: 'OPEN', isActive: true },
        isBlind: false,
        lines: [
          { id: 1, sku: 'SKU-A', productName: 'Tornillo', binCode: 'A-01', systemQty: 9, countedQty: 5, wasCorrected: true, correctedByName: 'Beto' },
          { id: 2, sku: 'SKU-B', productName: 'Tuerca', binCode: 'A-02', lotNumber: 'L1', systemQty: 3, countedQty: null },
        ],
      }),
    )
    const result = await fetchCountForRejection(300)
    expect(getMock).toHaveBeenCalledWith('/api/v1/cycle-counts/{id}', { params: { path: { id: 300 } } })
    expect(result).toEqual({
      kind: 'ok',
      state: 'open',
      number: 'CC-0300',
      lines: [
        { lineId: 1, sku: 'SKU-A', productName: 'Tornillo', binCode: 'A-01', lotNumber: null, countedQty: 5, wasCorrected: true, correctedByName: 'Beto' },
        { lineId: 2, sku: 'SKU-B', productName: 'Tuerca', binCode: 'A-02', lotNumber: 'L1', countedQty: null, wasCorrected: false, correctedByName: null },
      ],
    })
    // la cantidad esperada (systemQty) no viaja a la pantalla aunque el servidor la mande
    expect(JSON.stringify(result)).not.toContain('systemQty')
  })

  it('Contado y reconciliado', async () => {
    getMock.mockReturnValueOnce(ok({ count: { id: 300, number: 'CC-0300', statusCode: 'COUNTED' }, lines: [] }))
    expect(await fetchCountForRejection(300)).toMatchObject({ kind: 'ok', state: 'counted' })
    getMock.mockReturnValueOnce(ok({ count: { id: 300, number: 'CC-0300', statusCode: 'RECONCILED_VARIANCE' }, lines: [] }))
    expect(await fetchCountForRejection(300)).toMatchObject({ kind: 'ok', state: 'closed' })
  })

  it('404 o dado de baja → notFound', async () => {
    getMock.mockReturnValueOnce(fail(404, { title: 'Conteo no encontrado.', status: 404, code: 'not_found' }))
    expect(await fetchCountForRejection(300)).toEqual({ kind: 'notFound' })
    getMock.mockReturnValueOnce(ok({ count: { id: 300, statusCode: 'OPEN', isActive: false }, lines: [] }))
    expect(await fetchCountForRejection(300)).toEqual({ kind: 'notFound' })
  })

  it('sin señal → offline; otro error → su mensaje tal cual', async () => {
    getMock.mockReturnValueOnce(Promise.resolve({ response: Response.error() }))
    expect(await fetchCountForRejection(300)).toEqual({ kind: 'offline' })
    getMock.mockReturnValueOnce(fail(403, { title: 'No tiene permiso para esta acción.', status: 403, code: 'forbidden' }))
    expect(await fetchCountForRejection(300)).toEqual({ kind: 'error', message: 'No tiene permiso para esta acción.' })
  })
})
