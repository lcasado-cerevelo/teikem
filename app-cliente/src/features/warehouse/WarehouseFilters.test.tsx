// Almacén del portal (Luis, 2026-10-24: ampliar la búsqueda): movimientos, llegadas y salidas tienen un filtro de fecha que llega al servidor, y las llegadas tienen buscador. El API se simula.
jest.mock('../portalApi', () => ({
  PAGE_SIZE: 20,
  listStock: jest.fn(),
  listWarehouseProducts: jest.fn(),
  listMovements: jest.fn(),
  listArrivals: jest.fn(),
  listExits: jest.fn(),
  getValuation: jest.fn(),
  cancelArrival: jest.fn(),
  cancelExit: jest.fn(),
}))
jest.mock('../capabilities', () => ({
  useOffer: () => ({ transport: false, warehouse: true, canSeeValue: false, canCreateProducts: false, canCreateArrivals: true, canCreateExits: true, companyName: 'Acme', known: true }),
}))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { setLang } from '../../kernel/i18n/i18n'
import { listArrivals, listExits, listMovements, listStock } from '../portalApi'
import { WarehouseScreen } from './WarehouseScreen'

const page = { items: [], total: 0 }

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  ;(listStock as jest.Mock).mockResolvedValue({ ...page, totalOnHand: 0, totalAvailable: 0 })
  ;(listMovements as jest.Mock).mockResolvedValue(page)
  ;(listExits as jest.Mock).mockResolvedValue(page)
  ;(listArrivals as jest.Mock).mockResolvedValue([])
})

/** Abre el desplegable de rango y escoge una opción (la versión del teléfono abre una hoja con las opciones). */
const pickRange = async (testId: string, preset: string) => {
  await fireEvent.press(screen.getByTestId(`${testId}-preset`))
  await fireEvent.press(await screen.findByTestId(`${testId}-preset-opt-${preset}`))
}

const open = async (label: string) => {
  await render(<WarehouseScreen />)
  await fireEvent.press(await screen.findByText(label))
}

describe('Almacén: filtros de fecha y búsqueda ampliada', () => {
  it('las existencias se buscan también por lote y almacén (lo dice el buscador)', async () => {
    await render(<WarehouseScreen />)
    expect(await screen.findByPlaceholderText('SKU, nombre, código de barras, lote o almacén')).toBeTruthy()
  })

  it('movimientos: escoger «Hoy» manda el rango al servidor', async () => {
    await open('Movimientos')
    await waitFor(() => expect(listMovements).toHaveBeenCalled())
    expect((listMovements as jest.Mock).mock.calls[0][3]).toEqual({ from: '', to: '' }) // al abrir, sin rango
    expect(await screen.findByTestId('movements-range')).toBeTruthy()
    await pickRange('movements-range', 'today')
    await waitFor(() => {
      const last = (listMovements as jest.Mock).mock.calls.at(-1)
      expect(last?.[3].from).toMatch(/^\d{4}-\d{2}-\d{2}$/)
      expect(last?.[3].to).toBe(last?.[3].from)
    })
  })

  it('salidas: tienen su filtro de fecha y su buscador con lo que busca el servidor', async () => {
    await open('Salidas')
    await waitFor(() => expect(listExits).toHaveBeenCalled())
    expect(await screen.findByTestId('exits-range')).toBeTruthy()
    expect(screen.getByPlaceholderText('Número, referencia, destino o producto')).toBeTruthy()
    await pickRange('exits-range', 'last7')
    await waitFor(() => {
      const last = (listExits as jest.Mock).mock.calls.at(-1)
      expect(last?.[3].from).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    })
  })

  it('llegadas: ahora tienen buscador y filtro de fecha esperada, y los mandan al servidor', async () => {
    await open('Llegadas')
    await waitFor(() => expect(listArrivals).toHaveBeenCalled())
    expect((listArrivals as jest.Mock).mock.calls[0]).toEqual(['', { from: '', to: '' }])
    expect(await screen.findByTestId('arrivals-range')).toBeTruthy()
    await fireEvent.changeText(screen.getByTestId('arrivals-search'), 'OC-77')
    await waitFor(() => expect((listArrivals as jest.Mock).mock.calls.at(-1)?.[0]).toBe('OC-77'), { timeout: 3000 })
    await pickRange('arrivals-range', 'thisMonth')
    await waitFor(() => {
      const last = (listArrivals as jest.Mock).mock.calls.at(-1)
      expect(last?.[0]).toBe('OC-77')
      expect(last?.[1].from).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    })
  })
})
