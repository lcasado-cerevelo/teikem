// Nueva orden: reglas de validación y armado, y la pantalla (solo lectura, escoger/escribir consignatario, errores, envío).
const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn() }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('../portalApi', () => ({ getOrderForm: jest.fn(), createOrder: jest.fn() }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { setLang } from '../../kernel/i18n/i18n'
import { createOrder, getOrderForm } from '../portalApi'
import { NewOrderScreen } from './NewOrderScreen'
import { buildRequest, emptyConsignee, emptyDraft, parseNumber, validateDraft } from './newOrderRules'

const form = getOrderForm as jest.Mock
const create = createOrder as jest.Mock
const place = (id: string, name: string) => ({ publicId: id, name, line1: 'Calle 1', city: 'Ponce' })

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
})

describe('reglas', () => {
  it('parseNumber acepta coma decimal, vacío es null y lo demás NaN', () => {
    expect(parseNumber('3,5')).toBe(3.5)
    expect(parseNumber(' ')).toBeNull()
    expect(parseNumber('abc')).toBeNaN()
    expect(parseNumber('-2')).toBeNaN()
  })

  it('valida consignatario, paquetes, piezas, peso, COD, fecha y notas', () => {
    expect(validateDraft(emptyDraft())).toEqual(['consignee'])
    const ok = { ...emptyDraft(), consigneeId: 'x' }
    expect(validateDraft(ok)).toEqual([])
    expect(validateDraft({ ...ok, packages: [] })).toContain('packages')
    expect(validateDraft({ ...ok, packages: [{ description: '', pieces: '0', weightKg: '' }] })).toContain('pieces')
    expect(validateDraft({ ...ok, packages: [{ description: '', pieces: '1', weightKg: 'x' }] })).toContain('weight')
    expect(validateDraft({ ...ok, codAmount: 'x' })).toContain('cod')
    expect(validateDraft({ ...ok, requestedDate: '17/10/2026' })).toContain('date')
    expect(validateDraft({ ...ok, requestedDate: '2020-01-01' }, new Date(2026, 9, 17))).toContain('date')
    expect(validateDraft({ ...ok, requestedDate: '2026-10-20' }, new Date(2026, 9, 17))).toEqual([])
    expect(validateDraft({ ...ok, notes: 'x'.repeat(501) })).toContain('notes')
    expect(validateDraft({ ...emptyDraft(), newConsignee: { ...emptyConsignee(), name: 'A', line1: 'B' } })).toContain('consignee')   // falta la ciudad
  })

  it('arma la solicitud sin cliente, con consignatario existente o nuevo', () => {
    const r = buildRequest({ ...emptyDraft(), consigneeId: 'abc', codAmount: '10,50', packages: [{ description: ' Caja ', pieces: '2', weightKg: '3,5' }], invoiceNumber: 'F-1', notes: ' hola ' }, true)
    expect(r).toMatchObject({ consigneePublicId: 'abc', packages: [{ description: 'Caja', pieces: 2, weightKg: 3.5 }], codAmount: 10.5, clientInvoiceNumber: 'F-1', notes: 'hola' })
    expect('clientPublicId' in r).toBe(false)
    const n = buildRequest({ ...emptyDraft(), newConsignee: { ...emptyConsignee(), name: ' Tienda ', line1: 'Calle 5', city: 'Ponce' } }, false)
    expect(n.consigneePublicId).toBeNull()
    expect(n.newConsignee).toMatchObject({ name: 'Tienda', city: 'Ponce', line2: null })
    expect(n.clientInvoiceNumber).toBeNull()                                    // si Teikem numera la factura, no se manda
  })
})

describe('NewOrderScreen', () => {
  it('quien solo consulta ve el aviso y no el formulario', async () => {
    form.mockResolvedValue({ canCreate: false, consignees: [], pickups: [], maxPackages: 50 })
    await render(<NewOrderScreen />)
    expect(await screen.findByText(/solo puede consultar/)).toBeTruthy()
    expect(screen.queryByTestId('neworder-submit')).toBeNull()
  })

  it('sin consignatario avisa y no manda', async () => {
    form.mockResolvedValue({ canCreate: true, consignees: [place('c1', 'Tienda 1')], pickups: [], maxPackages: 50 })
    await render(<NewOrderScreen />)
    await fireEvent.press(await screen.findByTestId('neworder-submit'))
    expect(await screen.findByText(/Escoja el consignatario/)).toBeTruthy()
    expect(create).not.toHaveBeenCalled()
  })

  it('escoge un consignatario, crea la orden y abre su ficha', async () => {
    form.mockResolvedValue({ canCreate: true, consignees: [place('c1', 'Tienda 1')], pickups: [], maxPackages: 50 })
    create.mockResolvedValue({ publicId: 'o-1', waybillNumber: 'CND-1' })
    await render(<NewOrderScreen />)
    await fireEvent.press(await screen.findByTestId('consignee-c1'))
    await fireEvent.press(screen.getByTestId('neworder-submit'))
    await waitFor(() => expect(create).toHaveBeenCalledTimes(1))
    expect(create.mock.calls[0][0]).toMatchObject({ consigneePublicId: 'c1' })
    expect(mockRouter.replace).toHaveBeenCalledWith({ pathname: '/order/[id]', params: { id: 'o-1' } })
  })

  it('escribe un consignatario nuevo y muestra el error del servidor', async () => {
    form.mockResolvedValue({ canCreate: true, consignees: [], pickups: [], maxPackages: 50 })
    create.mockRejectedValue(Object.assign(new Error('El cliente está suspendido.'), { name: 'ApiError' }))
    await render(<NewOrderScreen />)
    await fireEvent.press(await screen.findByTestId('consignee-new'))
    await fireEvent.changeText(screen.getByTestId('new-name'), 'Tienda Nueva')
    await fireEvent.changeText(screen.getByTestId('new-line1'), 'Calle 5')
    await fireEvent.changeText(screen.getByTestId('new-city'), 'Ponce')
    await fireEvent.press(screen.getByTestId('neworder-submit'))
    await waitFor(() => expect(create).toHaveBeenCalled())
    expect(create.mock.calls[0][0].newConsignee).toMatchObject({ name: 'Tienda Nueva', city: 'Ponce' })
    expect(mockRouter.replace).not.toHaveBeenCalled()
  })

  it('el campo de factura solo sale si el cliente la teclea', async () => {
    form.mockResolvedValue({ canCreate: true, consignees: [], pickups: [], maxPackages: 50, clientTypesInvoiceNumber: true })
    await render(<NewOrderScreen />)
    expect(await screen.findByTestId('order-invoice')).toBeTruthy()
  })
})
