import { api } from '../../kernel/api/client'
import { completeTask, fetchOpenPutawayTasks, fetchPutawaySuggestions, startTask } from './putawayApi'

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
  getMock.mockReset()
  postMock.mockReset()
})

describe('fetchOpenPutawayTasks', () => {
  it('pide solo PUTAWAY abiertas del almacén y mapea los campos', async () => {
    getMock.mockResolvedValueOnce(
      ok({ items: [{ id: 1, sku: 'A', productName: 'Uno', quantity: 3, toBinCode: 'B-1', assignedToUserId: 7 }] }),
    )
    const tasks = await fetchOpenPutawayTasks('wh-1')
    expect(getMock.mock.calls[0][1].params.query).toMatchObject({ warehousePublicId: 'wh-1', types: ['PUTAWAY'], includeClosed: false })
    expect(tasks).toEqual([{ id: 1, sku: 'A', productName: 'Uno', quantity: 3, toBinCode: 'B-1', assignedToUserId: 7 }])
  })
})

describe('fetchPutawaySuggestions', () => {
  it('mapea binId/binCode/reason', async () => {
    getMock.mockResolvedValueOnce(ok([{ binId: 9, binCode: 'C-1', reason: 'zona rápida' }]))
    expect(await fetchPutawaySuggestions(42)).toEqual([{ binId: 9, binCode: 'C-1', reason: 'zona rápida' }])
  })
})

describe('startTask / completeTask', () => {
  it('startTask llama a POST /start con el id de la tarea', async () => {
    postMock.mockResolvedValueOnce(ok({}))
    await startTask(42)
    expect(postMock.mock.calls[0][0]).toBe('/api/v1/warehouse-tasks/{id}/start')
    expect(postMock.mock.calls[0][1].params.path).toEqual({ id: 42 })
  })

  it('completeTask manda el destino y la cantidad', async () => {
    postMock.mockResolvedValueOnce(ok({}))
    await completeTask(42, 9, 3)
    expect(postMock.mock.calls[0][1].params.path).toEqual({ id: 42 })
    expect(postMock.mock.calls[0][1].body).toEqual({ toBinId: 9, quantity: 3 })
  })
})
