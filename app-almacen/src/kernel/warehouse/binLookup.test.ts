import { api } from '../api/client'
import { findBinByCode } from './binLookup'

jest.mock('../api/client', () => {
  const actual = jest.requireActual('../api/client')
  return { ...actual, api: { GET: jest.fn() } }
})

const getMock = api.GET as jest.Mock

function ok(data: unknown) {
  return Promise.resolve({ data, response: new Response(null, { status: 200 }) })
}

beforeEach(() => {
  getMock.mockReset()
})

describe('findBinByCode', () => {
  it('encuentra la posición que coincide exactamente con el código (sin importar mayúsculas)', async () => {
    getMock.mockResolvedValueOnce(
      ok([
        { id: 5, code: 'a-01' },
        { id: 6, code: 'A-02' },
      ]),
    )
    expect(await findBinByCode('wh-1', 'A-01')).toEqual({ id: 5, code: 'a-01' })
    expect(getMock.mock.calls[0][1].params.path).toEqual({ publicId: 'wh-1' })
    expect(getMock.mock.calls[0][1].params.query).toEqual({ search: 'A-01' })
  })

  it('sin coincidencia exacta, devuelve null', async () => {
    getMock.mockResolvedValueOnce(ok([{ id: 6, code: 'A-02' }]))
    expect(await findBinByCode('wh-1', 'A-99')).toBeNull()
  })
})
