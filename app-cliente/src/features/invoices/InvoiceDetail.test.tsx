// Ficha de una factura del portal (Lote 119): encabezado con lo pagado y el saldo, líneas y pagos recibidos; ninguna acción (solo lectura); una factura ajena o inexistente muestra el error.
jest.mock('expo-router', () => ({ useRouter: () => ({ push: jest.fn(), replace: jest.fn() }), useLocalSearchParams: () => ({ id: 'f1' }), Stack: { Screen: () => null } }))
jest.mock('../portalApi', () => ({ getInvoice: jest.fn() }))

import { render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { getInvoice } from '../portalApi'
import { InvoiceDetailScreen } from './InvoiceDetailScreen'

const get = getInvoice as jest.Mock

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
})

describe('InvoiceDetailScreen', () => {
  it('muestra el encabezado con lo pagado y el saldo, las líneas y los pagos recibidos', async () => {
    get.mockResolvedValue({
      publicId: 'f1', number: 'FAC-00001', status: 'ISSUED', statusLabel: 'Emitida', issueDate: '2026-10-01', dueDate: '2026-10-31', currency: 'USD', subtotal: 100, taxAmount: 10, total: 110, paid: 30, credited: 0, balance: 80,
      lines: [{ description: 'Entregas de octubre', quantity: 2, unitAmount: 50, lineSubtotal: 100, taxAmount: 10, orderNumber: 'ORD-00007' }],
      payments: [{ date: '2026-10-05', amount: 30, methodLabel: 'Efectivo', reference: 'R-1' }],
    })
    await render(<InvoiceDetailScreen />)
    await waitFor(() => expect(get).toHaveBeenCalledWith('f1'))
    expect(await screen.findByText('Entregas de octubre', {}, { timeout: 3000 })).toBeTruthy()
    expect(screen.getByText('Pagos recibidos')).toBeTruthy()
    expect(screen.getByText(/Efectivo/)).toBeTruthy()
    expect(screen.getByText(/envío ORD-00007/)).toBeTruthy()
    expect(screen.queryByText(/Pagar|Marcar/i)).toBeNull()                // ninguna acción: solo lectura
    expect(get).toHaveBeenCalledWith('f1')
  })

  it('una factura que no existe (o de otro cliente) muestra el error', async () => {
    get.mockRejectedValue(new ApiError(404, { title: 'Factura no encontrada.' }))
    await render(<InvoiceDetailScreen />)
    await waitFor(() => expect(get).toHaveBeenCalledWith('f1'))
    expect(await screen.findByText('Factura no encontrada.', {}, { timeout: 3000 })).toBeTruthy()
  })
})
