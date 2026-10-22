// Ficha de la orden en el portal (Lote 126): «Factura procesada» con la fecha cuando la compañía ya procesó la factura del cliente contra la orden; sin fecha no hay fila.
jest.mock('expo-router', () => ({ useRouter: () => ({ push: jest.fn(), replace: jest.fn() }), useLocalSearchParams: () => ({ id: 'o1' }), Stack: { Screen: () => null } }))
jest.mock('../portalApi', () => ({ getOrder: jest.fn() }))

import { render, screen, waitFor } from '@testing-library/react-native'

import { setLang } from '../../kernel/i18n/i18n'
import { getOrder } from '../portalApi'
import { OrderDetailScreen } from './OrderDetailScreen'

const get = getOrder as jest.Mock

const ORDER = (invoiceProcessedAtUtc: string | null) => ({
  publicId: 'o1', waybillNumber: 'W-1', orderNumber: 'ORD-1', clientInvoiceNumber: 'FAC-1', serviceTypeLabel: 'Estándar', status: 'DELIVERED', statusLabel: 'Entregada', totalPieces: 2,
  createdAtUtc: '2026-10-18T12:00:00Z', delivery: { name: 'Tienda Norte', line1: 'Calle 5', city: 'Ponce' }, packages: [], timeline: [], deliveryProof: null, invoiceProcessedAtUtc,
})

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
})

describe('OrderDetailScreen — factura procesada (Lote 126)', () => {
  it('muestra cuándo se procesó la factura del cliente; sin fecha no muestra la fila', async () => {
    get.mockResolvedValue(ORDER('2026-10-18T22:00:00Z'))
    const first = await render(<OrderDetailScreen />)
    await waitFor(() => expect(get).toHaveBeenCalledWith('o1'))
    expect(await screen.findByText('Factura procesada', {}, { timeout: 3000 })).toBeTruthy()
    first.unmount()
    get.mockResolvedValue(ORDER(null))
    await render(<OrderDetailScreen />)
    expect(await screen.findByText('Tienda Norte', {}, { timeout: 3000 })).toBeTruthy()
    expect(screen.queryByText('Factura procesada')).toBeNull()
  })
})
