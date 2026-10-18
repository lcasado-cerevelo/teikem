// Envíos del consignatario (Lote 117d): lista con barra de progreso y búsqueda por conduce, ficha sin precios; solo lectura (no hay forma de crear una orden).
const mockRouter = { push: jest.fn(), replace: jest.fn() }
const mockParams: { id?: string } = { id: 'ord-1' }
jest.mock('expo-router', () => ({
  useRouter: () => mockRouter,
  useLocalSearchParams: () => mockParams,
  Stack: { Screen: () => null },
}))
jest.mock('../portalApi', () => ({ PAGE_SIZE: 20, listConsigneeOrders: jest.fn(), getConsigneeOrder: jest.fn() }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { getConsigneeOrder, listConsigneeOrders } from '../portalApi'
import { ConsigneeOrderDetailScreen } from './ConsigneeOrderDetailScreen'
import { ConsigneeOrdersScreen } from './ConsigneeOrdersScreen'

const list = listConsigneeOrders as jest.Mock
const get = getConsigneeOrder as jest.Mock

const ITEM = (id: string, waybill: string) => ({
  publicId: id, waybillNumber: waybill, serviceTypeLabel: 'Estándar', totalPieces: 2, status: 'IN_TRANSIT', statusLabel: 'En ruta', createdAtUtc: '2026-10-18T12:00:00Z', promisedDate: null,
  progress: { step: 3, total: 4, state: 'IN_PROGRESS', statusCode: 'IN_TRANSIT', statusLabel: 'En ruta' }, trackToken: 'tok',
})

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
})

describe('ConsigneeOrdersScreen', () => {
  it('lista los envíos con su avance, abre la ficha y no ofrece crear órdenes', async () => {
    list.mockResolvedValue({ items: [ITEM('o1', 'W-1'), ITEM('o2', 'W-2')], total: 2, skip: 0, take: 20 })
    await render(<ConsigneeOrdersScreen />)
    expect(await screen.findByText('Conduce W-1')).toBeTruthy()
    expect(screen.getByText('Conduce W-2')).toBeTruthy()
    expect(screen.getAllByRole('progressbar').length).toBe(2)
    expect(screen.queryByTestId('orders-new')).toBeNull()
    expect(screen.queryByText(/Nueva orden/i)).toBeNull()
    await fireEvent.press(screen.getByText('Conduce W-1'))
    expect(mockRouter.push).toHaveBeenCalledWith({ pathname: '/order/[id]', params: { id: 'o1' } })
  })

  it('buscar por conduce vuelve a pedir la lista', async () => {
    list.mockResolvedValue({ items: [], total: 0, skip: 0, take: 20 })
    await render(<ConsigneeOrdersScreen />)
    expect(await screen.findByText('Todavía no hay envíos para usted.')).toBeTruthy()
    await fireEvent.changeText(screen.getByTestId('shipments-search'), 'W-9')
    await waitFor(() => expect(list).toHaveBeenLastCalledWith('W-9', 0, 20))
  })

  it('un error del servidor se muestra', async () => {
    list.mockRejectedValue(new ApiError(403, { title: 'Este espacio es solo para consignatarios con acceso.' }))
    await render(<ConsigneeOrdersScreen />)
    expect(await screen.findByText('Este espacio es solo para consignatarios con acceso.')).toBeTruthy()
  })
})

describe('ConsigneeOrderDetailScreen', () => {
  it('muestra el envío con su dirección, paquetes y línea de tiempo, sin precios ni factura', async () => {
    get.mockResolvedValue({
      publicId: 'ord-1', waybillNumber: 'W-1', serviceTypeLabel: 'Estándar', status: 'IN_TRANSIT', statusLabel: 'En ruta', totalPieces: 2, promisedDate: null, createdAtUtc: '2026-10-18T12:00:00Z',
      delivery: { name: 'Tienda Norte', line1: 'Calle 1', line2: null, city: 'Ponce', state: 'PR', postalCode: '00716' },
      packages: [{ description: 'Caja', pieces: 2, weightKg: null }],
      timeline: [{ status: 'SUBMITTED', statusLabel: 'Sometida', atUtc: '2026-10-18T10:00:00Z' }, { status: 'IN_TRANSIT', statusLabel: 'En ruta', atUtc: '2026-10-18T12:00:00Z' }],
      progress: { step: 3, total: 4, state: 'IN_PROGRESS', statusCode: 'IN_TRANSIT', statusLabel: 'En ruta' }, stepLabels: ['Sometida', 'Recibida', 'En ruta', 'Completada'],
    })
    await render(<ConsigneeOrderDetailScreen />)
    expect(await screen.findByText('Tienda Norte')).toBeTruthy()
    expect(get).toHaveBeenCalledWith('ord-1')
    expect(screen.getByText('2 × Caja')).toBeTruthy()
    expect(screen.getAllByText('Sometida').length).toBeGreaterThan(0)
    expect(screen.queryByText(/COD|Factura|Invoice/)).toBeNull()
  })

  it('un envío que no es suyo responde no encontrado', async () => {
    get.mockRejectedValue(new ApiError(404, { title: 'Orden no encontrada.' }))
    await render(<ConsigneeOrderDetailScreen />)
    expect(await screen.findByText('Orden no encontrada.')).toBeTruthy()
  })
})
