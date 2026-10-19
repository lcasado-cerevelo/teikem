// Facturas del portal (Lote 119): la lista (sin acciones de pago), el filtro por estatus, «Cargar más» y la ficha con líneas y pagos. Solo lectura.
const mockRouter = { push: jest.fn() }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('../portalApi', () => ({ listInvoices: jest.fn(), PAGE_SIZE: 20 }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { listInvoices } from '../portalApi'
import { InvoicesScreen } from './InvoicesScreen'
import { invoiceTone } from './invoiceRules'

const list = listInvoices as jest.Mock

const ITEM = (over: object = {}) => ({ publicId: 'f1', number: 'FAC-00001', status: 'ISSUED', statusLabel: 'Emitida', issueDate: '2026-10-01', dueDate: '2026-10-31', currency: 'USD', total: 110, paid: 30, balance: 80, ...over })

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
})

describe('invoiceTone', () => {
  it('pagada = ok, vencida = peligro, anulada = apagada y lo demás informativa', () => {
    expect(invoiceTone('PAID')).toBe('ok')
    expect(invoiceTone('OVERDUE')).toBe('danger')
    expect(invoiceTone('VOID')).toBe('neutral')
    expect(invoiceTone('ISSUED')).toBe('info')
    expect(invoiceTone(null)).toBe('info')
  })
})

describe('InvoicesScreen', () => {
  it('lista las facturas con el total y el saldo, avisa que es solo consulta y no ofrece pagar', async () => {
    list.mockResolvedValue({ items: [ITEM(), ITEM({ publicId: 'f2', number: 'FAC-00002', status: 'PAID', statusLabel: 'Pagada', paid: 110, balance: 0 })], total: 2, skip: 0, take: 20 })
    await render(<InvoicesScreen />)
    expect(await screen.findByText('FAC-00001')).toBeTruthy()
    expect(screen.getByText('FAC-00002')).toBeTruthy()
    expect(screen.getByText(/Aquí solo puede consultarlas/)).toBeTruthy()
    expect(screen.getAllByText(/Saldo:/)).toHaveLength(1)                   // la pagada no muestra saldo
    expect(screen.queryByText(/Pagar/i)).toBeNull()
    expect(list).toHaveBeenCalledWith('', 0, 20)
  })

  it('el filtro pide solo ese estatus y tocar una factura abre su ficha', async () => {
    list.mockResolvedValue({ items: [ITEM()], total: 1, skip: 0, take: 20 })
    await render(<InvoicesScreen />)
    await screen.findByText('FAC-00001')
    await fireEvent.press(screen.getByTestId('invoices-filter-OVERDUE'))
    await waitFor(() => expect(list).toHaveBeenLastCalledWith('OVERDUE', 0, 20))
    await fireEvent.press(await screen.findByText('FAC-00001'))
    expect(mockRouter.push).toHaveBeenCalledWith({ pathname: '/invoice/[id]', params: { id: 'f1' } })
  })

  it('sin facturas lo dice, y un error del servidor se muestra con Reintentar', async () => {
    list.mockResolvedValueOnce({ items: [], total: 0, skip: 0, take: 20 })
    const first = await render(<InvoicesScreen />)
    expect(await screen.findByText('No hay facturas.')).toBeTruthy()
    first.unmount()
    list.mockRejectedValueOnce(new ApiError(403, { title: "El módulo 'BILLING' no está habilitado para esta compañía." }))
    await render(<InvoicesScreen />)
    expect(await screen.findByText(/no está habilitado/)).toBeTruthy()
    expect(screen.getByText('Reintentar')).toBeTruthy()
  })
})
