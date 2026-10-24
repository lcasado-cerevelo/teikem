// Nuevo envío: reglas de validación y armado, y la pantalla detallada (solo lectura, buscar/escoger o escribir el consignatario, campos del personal, errores, envío).
const mockRouter = { push: jest.fn(), replace: jest.fn(), back: jest.fn() }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('../portalApi', () => ({ getOrderForm: jest.fn(), createOrder: jest.fn(), createConsignee: jest.fn() }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { setLang } from '../../kernel/i18n/i18n'
import { createConsignee, createOrder, getOrderForm } from '../portalApi'
import { NewOrderScreen } from './NewOrderScreen'
import { buildRequest, emptyConsignee, emptyDraft, emptyPackage, packageSummary, parseNumber, serverFieldKey, validateDraft } from './newOrderRules'

const form = getOrderForm as jest.Mock
const create = createOrder as jest.Mock
const newConsignee = createConsignee as jest.Mock
const place = (id: string, name: string) => ({ publicId: id, name, line1: 'Calle 1', city: 'Ponce', code: `C-${id}`, phone: '+1 787 555 0100', postalCode: '00730' })
const CATALOG = {
  serviceTypes: [{ code: 'STD', label: 'Entrega estándar' }, { code: 'EXP', label: 'Expreso' }],
  packageTypes: [{ code: 'BOX', label: 'Caja' }, { code: 'ENV', label: 'Sobre' }],
  defaultServiceType: 'STD',
  defaultPackageType: 'BOX',
}
const RULES = { hasServiceTypes: true, hasPackageTypes: true, typesInvoice: true, assignsOrderNumber: true }

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

  it('valida servicio, consignatario, paquetes, tipo de paquete, piezas, peso, COD, fechas, números y notas', () => {
    const today = new Date(2026, 9, 17)
    const base = emptyDraft({ serviceType: 'STD', packageType: 'BOX', today: '2026-10-17' })
    expect(validateDraft(base, RULES, today)).toEqual(['consignee'])
    const ok = { ...base, consigneeId: 'x' }
    expect(validateDraft(ok, RULES, today)).toEqual([])
    expect(validateDraft({ ...ok, serviceType: '' }, RULES, today)).toContain('service')
    expect(validateDraft({ ...ok, serviceType: '' }, { ...RULES, hasServiceTypes: false }, today)).toEqual([])   // sin catálogo, el servidor pone el predeterminado
    expect(validateDraft({ ...ok, packages: [] }, RULES, today)).toContain('packages')
    expect(validateDraft({ ...ok, packages: [emptyPackage('')] }, RULES, today)).toContain('packageType')
    expect(validateDraft({ ...ok, packages: [{ ...emptyPackage('BOX'), pieces: '0' }] }, RULES, today)).toContain('pieces')
    expect(validateDraft({ ...ok, packages: [{ ...emptyPackage('BOX'), weightKg: 'x' }] }, RULES, today)).toContain('weight')
    expect(validateDraft({ ...ok, codAmount: 'x' }, RULES, today)).toContain('cod')
    expect(validateDraft({ ...ok, readyDate: '17/10/2026' }, RULES, today)).toContain('date')
    expect(validateDraft({ ...ok, readyDate: '2020-01-01' }, RULES, today)).toContain('date')
    expect(validateDraft({ ...ok, readyDate: '2026-10-20', dueDate: '2026-10-19' }, RULES, today)).toEqual(['dueDate'])
    expect(validateDraft({ ...ok, readyDate: '2026-10-20', dueDate: '2026-10-25' }, RULES, today)).toEqual([])
    expect(validateDraft({ ...ok, orderNumber: 'x'.repeat(41) }, RULES, today)).toContain('orderNumber')
    expect(validateDraft({ ...ok, invoiceNumber: 'x'.repeat(41) }, RULES, today)).toContain('invoice')
    expect(validateDraft({ ...ok, notes: 'x'.repeat(501) }, RULES, today)).toContain('notes')
    expect(validateDraft({ ...base, newConsignee: { ...emptyConsignee(), name: 'A', line1: 'B' } }, RULES, today)).toContain('consignee')   // falta la ciudad
  })

  it('arma la solicitud sin cliente, con servicio, fechas, tipo de paquete, y los números solo si el cliente los asigna', () => {
    const d = { ...emptyDraft({ serviceType: 'EXP', packageType: 'BOX', today: '2026-10-17' }), dueDate: '2026-10-19', consigneeId: 'abc', codAmount: '10,50', packages: [{ packageType: 'ENV', description: ' Caja ', pieces: '2', weightKg: '3,5' }], invoiceNumber: 'F-1', orderNumber: 'O-1', notes: ' hola ' }
    const r = buildRequest(d, RULES)
    expect(r).toMatchObject({ consigneePublicId: 'abc', serviceType: 'EXP', packages: [{ packageType: 'ENV', description: 'Caja', pieces: 2, weightKg: 3.5 }], codAmount: 10.5, clientInvoiceNumber: 'F-1', orderNumber: 'O-1', notes: 'hola', requestedDate: '2026-10-17T00:00:00Z', promisedDate: '2026-10-19T00:00:00Z' })
    expect('clientPublicId' in r).toBe(false)
    const n = buildRequest({ ...emptyDraft(), newConsignee: { ...emptyConsignee(), name: ' Tienda ', line1: 'Calle 5', city: 'Ponce' }, invoiceNumber: 'F-1', orderNumber: 'O-1' }, { typesInvoice: false, assignsOrderNumber: false })
    expect(n.consigneePublicId).toBeNull()
    expect(n.newConsignee).toMatchObject({ name: 'Tienda', city: 'Ponce', line2: null })
    expect(n.clientInvoiceNumber).toBeNull()   // si Teikem numera, no se manda
    expect(n.orderNumber).toBeNull()
  })

  it('el resumen del ticket agrupa por tipo y los errores del servidor caen en su campo', () => {
    const labels: Record<string, string> = { BOX: 'Caja', ENV: 'Sobre' }
    expect(packageSummary([{ ...emptyPackage('BOX'), pieces: '2' }, { ...emptyPackage('BOX'), pieces: '1' }, { ...emptyPackage('ENV'), pieces: '3' }], (c) => labels[c] ?? '')).toBe('3 × Caja · 3 × Sobre')
    expect(serverFieldKey('newConsignee.city')).toBe('consignee')
    expect(serverFieldKey('packages[1].packageType')).toBe('packageType')
    expect(serverFieldKey('promisedDate')).toBe('dueDate')
    expect(serverFieldKey('$.serviceType')).toBe('service')
    expect(serverFieldKey('algo')).toBeNull()
  })
})

describe('NewOrderScreen (detallada)', () => {
  it('quien solo consulta ve el aviso y no el formulario', async () => {
    form.mockResolvedValue({ canCreate: false, consignees: [], pickups: [], maxPackages: 50 })
    await render(<NewOrderScreen />)
    expect(await screen.findByText(/solo puede consultar/)).toBeTruthy()
    expect(screen.queryByTestId('neworder-submit')).toBeNull()
  })

  it('sin consignatario avisa y no manda', async () => {
    form.mockResolvedValue({ canCreate: true, consignees: [place('c1', 'Tienda 1')], pickups: [], maxPackages: 50, ...CATALOG })
    await render(<NewOrderScreen />)
    await fireEvent.press(await screen.findByTestId('neworder-submit'))
    expect(await screen.findByText(/Escoja el consignatario/)).toBeTruthy()
    expect(create).not.toHaveBeenCalled()
  })

  it('busca el consignatario, lo escoge de la lista, ve sus datos de solo lectura, crea la orden con el servicio y el paquete predeterminados y abre su ficha', async () => {
    form.mockResolvedValue({ canCreate: true, consignees: [place('c1', 'Tienda 1'), place('c2', 'Colmado 2')], pickups: [], maxPackages: 50, ...CATALOG })
    create.mockResolvedValue({ publicId: 'o-1', waybillNumber: 'CND-1' })
    await render(<NewOrderScreen />)
    await fireEvent.changeText(await screen.findByTestId('consignee'), 'colm')
    await fireEvent.press(await screen.findByTestId('consignee-suggest-0'))
    expect(screen.getByText('C-c2')).toBeTruthy()            // número del consignatario
    expect(screen.getByText('+1 787 555 0100')).toBeTruthy()  // teléfono
    await fireEvent.changeText(screen.getByTestId('order-cod'), '12')
    expect(screen.getByTestId('ticket-cod')).toHaveTextContent('$12.00')
    await fireEvent.press(screen.getByTestId('neworder-submit'))
    await waitFor(() => expect(create).toHaveBeenCalledTimes(1))
    expect(create.mock.calls[0][0]).toMatchObject({ consigneePublicId: 'c2', serviceType: 'STD', codAmount: 12, packages: [{ packageType: 'BOX', pieces: 1 }] })
    expect(mockRouter.replace).toHaveBeenCalledWith({ pathname: '/order/[id]', params: { id: 'o-1' } })
  })

  it('escribe un consignatario nuevo desde «+ Nuevo», Enter lo graba al momento, queda escogido y el envío usa su id; el error del servidor del envío se muestra', async () => {
    form.mockResolvedValue({ canCreate: true, consignees: [], pickups: [], maxPackages: 50, ...CATALOG })
    newConsignee.mockResolvedValue({ publicId: 'n1', name: 'Tienda Nueva', line1: 'Calle 5', city: 'Ponce', code: 'C-7', phone: '+1 787 555 0100' })
    create.mockRejectedValue(Object.assign(new Error('El cliente está suspendido.'), { name: 'ApiError' }))
    await render(<NewOrderScreen />)
    await fireEvent.changeText(await screen.findByTestId('consignee'), 'Tienda Nueva')
    await fireEvent.press(await screen.findByTestId('consignee-new'))
    expect(screen.getByTestId('new-name').props.value).toBe('Tienda Nueva')
    await fireEvent.changeText(screen.getByTestId('new-line1'), 'Calle 5')
    await fireEvent.changeText(screen.getByTestId('new-city'), 'Ponce')
    await fireEvent(screen.getByTestId('new-city'), 'submitEditing')            // Enter: graba el consignatario en el servidor
    await waitFor(() => expect(newConsignee).toHaveBeenCalledWith(expect.objectContaining({ name: 'Tienda Nueva', line1: 'Calle 5', city: 'Ponce' })))
    await waitFor(() => expect(screen.queryByTestId('new-consignee-block')).toBeNull())
    expect(screen.getByTestId('consignee').props.value).toBe('Tienda Nueva')    // escogido
    expect(screen.getByText('C-7')).toBeTruthy()                                // sus datos de solo lectura salen igual que los de cualquier otro
    expect(create).not.toHaveBeenCalled()
    await fireEvent.press(screen.getByTestId('neworder-submit'))
    await waitFor(() => expect(create).toHaveBeenCalled())
    expect(create.mock.calls[0][0]).toMatchObject({ consigneePublicId: 'n1' })
    expect(mockRouter.replace).not.toHaveBeenCalled()
  })

  it('Guardar envío con el consignatario nuevo completo lo graba primero y luego crea el envío', async () => {
    form.mockResolvedValue({ canCreate: true, consignees: [], pickups: [], maxPackages: 50, ...CATALOG })
    newConsignee.mockResolvedValue({ publicId: 'n2', name: 'Ferretería Sol', line1: 'Av. 1', city: 'Yauco' })
    create.mockResolvedValue({ publicId: 'o-2', waybillNumber: 'CND-2' })
    await render(<NewOrderScreen />)
    await fireEvent.changeText(await screen.findByTestId('consignee'), 'Ferretería Sol')
    await fireEvent.press(await screen.findByTestId('consignee-new'))
    await fireEvent.changeText(screen.getByTestId('new-line1'), 'Av. 1')
    await fireEvent.changeText(screen.getByTestId('new-city'), 'Yauco')
    await fireEvent.press(screen.getByTestId('neworder-submit'))
    await waitFor(() => expect(create).toHaveBeenCalledTimes(1))
    expect(newConsignee).toHaveBeenCalledTimes(1)
    expect(create.mock.calls[0][0]).toMatchObject({ consigneePublicId: 'n2' })
    expect(mockRouter.replace).toHaveBeenCalledWith({ pathname: '/order/[id]', params: { id: 'o-2' } })
  })

  it('los números de orden y factura solo se escriben si el cliente los asigna; si no, salen como automáticos', async () => {
    form.mockResolvedValue({ canCreate: true, consignees: [], pickups: [], maxPackages: 50, ...CATALOG, clientTypesInvoiceNumber: true, clientAssignsOrderNumber: false })
    await render(<NewOrderScreen />)
    expect(await screen.findByTestId('order-invoice')).toBeTruthy()
    expect(screen.queryByTestId('order-number')).toBeNull()
    expect(screen.getAllByText('Lo asigna la compañía').length).toBe(1)
  })
})
