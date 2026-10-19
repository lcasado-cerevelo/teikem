// Ficha de la orden en el portal (Lote 119): el comprobante de entrega — quién recibió y cuándo, que lo registró el personal (sin nombre del usuario) y el enlace al mapa solo si se encontró la ubicación.
jest.mock('expo-router', () => ({ useLocalSearchParams: () => ({ id: 'o1' }), Stack: { Screen: () => null } }))
jest.mock('../portalApi', () => ({ getOrder: jest.fn() }))

import { render, screen, waitFor } from '@testing-library/react-native'

import { setLang } from '../../kernel/i18n/i18n'
import { getOrder } from '../portalApi'
import { OrderDetailScreen } from './OrderDetailScreen'

const get = getOrder as jest.Mock

const ORDER = (proof: object | null) => ({
  publicId: 'o1', waybillNumber: 'W-1', orderNumber: 'ORD-1', clientInvoiceNumber: 'FAC-1', serviceTypeLabel: 'Estándar', status: 'DELIVERED', statusLabel: 'Entregada', totalPieces: 2, createdAtUtc: '2026-10-18T10:00:00Z',
  delivery: { name: 'Tienda Norte', line1: 'Calle 5', city: 'Ponce' }, packages: [], timeline: [], deliveryProof: proof,
})

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
})

describe('OrderDetailScreen — comprobante de entrega', () => {
  it('muestra quién recibió y cuándo y que lo registró el personal, con el mapa si hay ubicación', async () => {
    get.mockResolvedValue(ORDER({ deliveredAtUtc: '2026-10-18T20:00:00Z', recipientName: 'Ana Rivera', registeredByStaff: true, signedByRecipient: false, latitude: 18.4655, longitude: -66.1057 }))
    await render(<OrderDetailScreen />)
    await waitFor(() => expect(get).toHaveBeenCalledWith('o1'))
    expect(await screen.findByText('Comprobante de entrega', {}, { timeout: 3000 })).toBeTruthy()
    expect(screen.getByText('Ana Rivera')).toBeTruthy()
    expect(screen.getByText('La entrega la registró el personal de la compañía; el consignatario no firmó.')).toBeTruthy()
    expect(screen.getByTestId('order-proof-map')).toBeTruthy()
  })

  it('sin ubicación no hay enlace al mapa y sin comprobante no hay tarjeta', async () => {
    get.mockResolvedValue(ORDER({ deliveredAtUtc: '2026-10-18T20:00:00Z', recipientName: 'Ana Rivera', registeredByStaff: true, signedByRecipient: false, latitude: null, longitude: null }))
    const first = await render(<OrderDetailScreen />)
    expect(await screen.findByText('Ana Rivera', {}, { timeout: 3000 })).toBeTruthy()
    expect(screen.queryByTestId('order-proof-map')).toBeNull()
    first.unmount()
    get.mockResolvedValue(ORDER(null))
    await render(<OrderDetailScreen />)
    expect(await screen.findByText('Tienda Norte', {}, { timeout: 3000 })).toBeTruthy()
    expect(screen.queryByText('Comprobante de entrega')).toBeNull()
  })
})
