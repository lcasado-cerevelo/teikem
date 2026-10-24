// La rejilla de captura rápida: las columnas del personal, escribir el consignatario y elegirlo, Enter/Guardar fila crea el envío, la fila queda guardada y sale una nueva con la misma
// fecha, servicio y tipo de paquete; los errores no pierden lo escrito.
jest.mock('../portalApi', () => ({ createOrder: jest.fn(), createConsignee: jest.fn() }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { createConsignee, createOrder } from '../portalApi'
import { NewOrderQuick } from './NewOrderQuick'
import { todayText } from './newOrderRules'

const createMock = createOrder as jest.Mock
const createConsigneeMock = createConsignee as jest.Mock
const NEW_PLACE = { publicId: 'new1', name: 'Tienda Nueva', line1: 'Calle 5', city: 'Caguas', code: 'C-9' }

const FORM = {
  canCreate: true,
  clientTypesInvoiceNumber: false,
  clientAssignsOrderNumber: false,
  consignees: [
    { publicId: 'a', name: 'Farmacia Norte', line1: 'Calle 1', city: 'Bayamón' },
    { publicId: 'b', name: 'Colmado Sur', line1: 'Av. Principal 22', city: 'Ponce' },
  ],
  pickups: [],
  maxPackages: 50,
  serviceTypes: [{ code: 'STD', label: 'Entrega estándar' }, { code: 'EXP', label: 'Expreso' }],
  packageTypes: [{ code: 'BOX', label: 'Caja' }, { code: 'ENV', label: 'Sobre' }],
  defaultServiceType: 'STD',
  defaultPackageType: 'BOX',
}

const future = () => {
  const d = new Date()
  d.setDate(d.getDate() + 1)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  createMock.mockResolvedValue({ publicId: 'o1', waybillNumber: 'W-1001', orderNumber: 'ORD-1' })
  createConsigneeMock.mockResolvedValue(NEW_PLACE)
})

describe('NewOrderQuick', () => {
  it('tiene las columnas del personal; N.º de orden y factura solo si el cliente los asigna', async () => {
    await render(<NewOrderQuick form={FORM} />)
    expect(screen.getByTestId('quick-date')).toBeTruthy()
    expect(screen.getByTestId('quick-consignee')).toBeTruthy()
    expect(screen.getByTestId('quick-packageType')).toBeTruthy()
    expect(screen.getByTestId('quick-serviceType')).toBeTruthy()
    expect(screen.getByTestId('quick-cod')).toBeTruthy()
    expect(screen.getByTestId('quick-notes')).toBeTruthy()
    // como en el personal: «N.º de envío» y «N.º de factura» SIEMPRE están; apagados con «(automático)» si la compañía los asigna
    expect(screen.getByTestId('quick-orderNumber').props.editable).toBe(false)
    expect(screen.getByTestId('quick-orderNumber').props.placeholder).toBe('(automático)')
    expect(screen.getByTestId('quick-orderNumber').props.focusable).toBe(false)   // Tab los salta, como los deshabilitados del personal
    expect(screen.getByTestId('quick-invoice').props.editable).toBe(false)
    expect(screen.getByTestId('quick-invoice').props.placeholder).toBe('(automático)')
    expect(screen.getByText('N.º de envío')).toBeTruthy()
    expect(screen.getByText('N.º de factura')).toBeTruthy()
    expect(screen.queryByTestId('quick-description')).toBeNull()
  })

  it('con un cliente que asigna sus numeros salen las columnas de orden y factura', async () => {
    await render(<NewOrderQuick form={{ ...FORM, clientTypesInvoiceNumber: true, clientAssignsOrderNumber: true }} />)
    expect(screen.getByTestId('quick-orderNumber').props.editable).toBe(true)
    expect(screen.getByTestId('quick-invoice').props.editable).toBe(true)
    expect(screen.getByTestId('quick-invoice').props.placeholder).toBe('N.º de factura')
  })

  it('guardar una fila sin consignatario muestra el error y no llama al servidor', async () => {
    await render(<NewOrderQuick form={FORM} />)
    await fireEvent.press(screen.getByTestId('quick-save'))
    expect(await screen.findByText(/Escoja el consignatario/)).toBeTruthy()
    expect(createMock).not.toHaveBeenCalled()
  })

  it('escribe, elige el consignatario de la lista, guarda con Enter y deja la fila siguiente con la misma fecha, servicio y paquete', async () => {
    await render(<NewOrderQuick form={FORM} />)
    await fireEvent.changeText(screen.getByTestId('quick-date'), future())
    await fireEvent.changeText(screen.getByTestId('quick-consignee'), 'farm')
    await fireEvent.press(await screen.findByTestId('quick-consignee-suggest-0'))
    expect(screen.getByTestId('quick-address')).toHaveTextContent('Calle 1, Bayamón')
    await fireEvent.changeText(screen.getByTestId('quick-cod'), '15')
    await fireEvent(screen.getByTestId('quick-cod'), 'submitEditing')   // Enter guarda desde cualquier campo

    await waitFor(() => expect(createMock).toHaveBeenCalledTimes(1))
    expect(createMock.mock.calls[0][0]).toMatchObject({ consigneePublicId: 'a', codAmount: 15, serviceType: 'STD', packages: [{ packageType: 'BOX', pieces: 1 }], requestedDate: `${future()}T00:00:00Z`, promisedDate: `${future()}T00:00:00Z` })
    // el rango de lo capturado se ensancha a la fecha del envío: el desde se queda en hoy y el hasta sigue a esa fecha
    expect(await screen.findByText(/W-1001/)).toBeTruthy()   // la fila guardada con su conduce
    expect(screen.getByText('1 guardados')).toBeTruthy()
    expect(screen.getByText('COD acumulado: $15.00')).toBeTruthy()
    // la fila nueva: misma fecha, sin consignatario, mismo servicio y paquete
    expect(screen.getByTestId('quick-date').props.value).toBe(future())
    expect(screen.getByTestId('quick-consignee').props.value).toBe('')
    expect(screen.getByTestId('quick-serviceType').props.accessibilityValue).toEqual({ text: 'Entrega estándar' })
    expect(screen.getByTestId('quick-packageType').props.accessibilityValue).toEqual({ text: 'Caja' })
  })

  it('«+ Nuevo consignatario»: Enter graba el consignatario al momento, lo deja escogido y el foco sigue en la misma fila; Enter en otro campo guarda el envío', async () => {
    const onCreated = jest.fn()
    await render(<NewOrderQuick form={FORM} onConsigneeCreated={onCreated} />)
    await fireEvent.changeText(screen.getByTestId('quick-date'), future())
    await fireEvent.changeText(screen.getByTestId('quick-consignee'), 'Tienda Nueva')
    await fireEvent(screen.getByTestId('quick-consignee'), 'submitEditing')   // la lista no tiene coincidencias: Enter = «+ Nuevo»
    expect(screen.getByTestId('quick-new-block')).toBeTruthy()
    expect(screen.getByTestId('quick-new-name').props.value).toBe('Tienda Nueva')
    // Enter con faltantes no graba: avisa y no llama al servidor
    await fireEvent(screen.getByTestId('quick-new-line1'), 'submitEditing')
    expect(createConsigneeMock).not.toHaveBeenCalled()
    expect(await screen.findByText('Escriba el nombre, la dirección y el pueblo del consignatario.')).toBeTruthy()
    await fireEvent.changeText(screen.getByTestId('quick-new-line1'), 'Calle 5')
    await fireEvent.changeText(screen.getByTestId('quick-new-city'), 'Caguas')
    await fireEvent.changeText(screen.getByTestId('quick-new-phone'), '787-555-0100')
    await fireEvent(screen.getByTestId('quick-new-city'), 'submitEditing')   // Enter: graba el consignatario
    await waitFor(() => expect(createConsigneeMock).toHaveBeenCalledTimes(1))
    expect(createConsigneeMock.mock.calls[0][0]).toMatchObject({ name: 'Tienda Nueva', line1: 'Calle 5', city: 'Caguas', phone: '787-555-0100', line2: null, state: null })
    expect(onCreated).toHaveBeenCalledWith(NEW_PLACE)
    await waitFor(() => expect(screen.queryByTestId('quick-new-block')).toBeNull())          // el bloque se cierra
    expect(screen.getByTestId('quick-consignee').props.value).toBe('Tienda Nueva')          // y queda escogido
    expect(createMock).not.toHaveBeenCalled()                                                // el envío todavía no se guardó
    await fireEvent.changeText(screen.getByTestId('quick-cod'), '12')
    await fireEvent(screen.getByTestId('quick-cod'), 'submitEditing')                        // Enter en otro campo: guarda el envío
    await waitFor(() => expect(createMock).toHaveBeenCalledTimes(1))
    expect(createMock.mock.calls[0][0]).toMatchObject({ consigneePublicId: 'new1', codAmount: 12 })
    expect(createMock.mock.calls[0][0].newConsignee).toBeUndefined()                         // el consignatario ya existe: no viaja embebido
    expect(await screen.findByText(/W-1001 · Tienda Nueva/)).toBeTruthy()
  })

  it('con el bloque abierto y completo, Enter en otro campo graba el consignatario y el envío de una vez', async () => {
    await render(<NewOrderQuick form={FORM} />)
    await fireEvent.changeText(screen.getByTestId('quick-date'), future())
    await fireEvent.changeText(screen.getByTestId('quick-consignee'), 'Tienda Nueva')
    await fireEvent(screen.getByTestId('quick-consignee'), 'submitEditing')
    await fireEvent.changeText(screen.getByTestId('quick-new-line1'), 'Calle 5')
    await fireEvent.changeText(screen.getByTestId('quick-new-city'), 'Caguas')
    await fireEvent(screen.getByTestId('quick-notes'), 'submitEditing')
    await waitFor(() => expect(createMock).toHaveBeenCalledTimes(1))
    expect(createConsigneeMock).toHaveBeenCalledTimes(1)
    expect(createMock.mock.calls[0][0]).toMatchObject({ consigneePublicId: 'new1' })
  })

  it('Escape cancela el consignatario nuevo; si el servidor lo rechaza el error sale en el bloque y lo escrito se conserva', async () => {
    await render(<NewOrderQuick form={FORM} />)
    await fireEvent.changeText(screen.getByTestId('quick-consignee'), 'Tienda Nueva')
    await fireEvent(screen.getByTestId('quick-consignee'), 'submitEditing')
    await fireEvent(screen.getByTestId('quick-new-name'), 'keyPress', { nativeEvent: { key: 'Escape' } })
    await waitFor(() => expect(screen.queryByTestId('quick-new-block')).toBeNull())
    createConsigneeMock.mockRejectedValue(new ApiError(403, { title: 'Su usuario solo puede consultar; pídale a un administrador de su empresa que le dé permiso para crear envíos.' }))
    await fireEvent(screen.getByTestId('quick-consignee'), 'submitEditing')
    await fireEvent.changeText(screen.getByTestId('quick-new-line1'), 'Calle 5')
    await fireEvent.changeText(screen.getByTestId('quick-new-city'), 'Caguas')
    await fireEvent(screen.getByTestId('quick-new-city'), 'submitEditing')
    expect(await screen.findByText(/solo puede consultar/)).toBeTruthy()
    expect(screen.getByTestId('quick-new-line1').props.value).toBe('Calle 5')
  })

  it('Enter sobre «+ Nuevo» (la última de la lista) abre el consignatario nuevo y no escoge el último que coincidía', async () => {
    await render(<NewOrderQuick form={FORM} />)
    await fireEvent.changeText(screen.getByTestId('quick-consignee'), 'farm')
    await fireEvent(screen.getByTestId('quick-consignee'), 'keyPress', { nativeEvent: { key: 'ArrowDown' } })   // la única coincidencia es Farmacia Norte (0); 1 = «+ Nuevo»
    await fireEvent(screen.getByTestId('quick-consignee'), 'submitEditing')
    expect(screen.getByTestId('quick-new-block')).toBeTruthy()
    expect(screen.getByTestId('quick-new-name').props.value).toBe('farm')
  })

  it('si el servidor rechaza la fila muestra su mensaje, marca el campo y conserva lo escrito', async () => {
    createMock.mockRejectedValue(new ApiError(400, { title: 'Revise los datos.', errors: { serviceType: ['Tipo de servicio desconocido: STD.'] } }))
    await render(<NewOrderQuick form={FORM} />)
    await fireEvent.changeText(screen.getByTestId('quick-date'), future())
    await fireEvent.changeText(screen.getByTestId('quick-consignee'), 'colmado')
    await fireEvent.press(await screen.findByTestId('quick-consignee-suggest-0'))
    await fireEvent.press(screen.getByTestId('quick-save'))
    expect(await screen.findByText('Tipo de servicio desconocido: STD.')).toBeTruthy()
    expect(screen.getByTestId('quick-consignee').props.value).toBe('Colmado Sur')
    expect(screen.queryByText('1 guardados')).toBeNull()
  })
})

describe('NewOrderQuick — tabla de lo capturado: filtros y resumen', () => {
  const save = async (consignee: string, extra: { cod?: string; date?: string; service?: string } = {}) => {
    await fireEvent.changeText(screen.getByTestId('quick-date'), extra.date ?? todayText())
    await fireEvent.changeText(screen.getByTestId('quick-consignee'), consignee)
    await fireEvent.press(await screen.findByTestId('quick-consignee-suggest-0'))
    if (extra.cod) await fireEvent.changeText(screen.getByTestId('quick-cod'), extra.cod)
    await fireEvent(screen.getByTestId('quick-cod'), 'submitEditing')
    await waitFor(() => expect(screen.getByTestId('quick-date')).toBeTruthy())
  }

  it('hoy por omisión, búsqueda libre y resumen por paquete y servicio con el COD total en el pie', async () => {
    createMock.mockResolvedValueOnce({ publicId: 'o1', waybillNumber: 'W-1001', orderNumber: 'ORD-1', clientInvoiceNumber: 'F-1, F-2' }).mockResolvedValueOnce({ publicId: 'o2', waybillNumber: 'W-1002', orderNumber: 'ORD-2', clientInvoiceNumber: 'f-2, F-3' })
    await render(<NewOrderQuick form={FORM} />)
    await save('farm', { cod: '10.5' })
    await waitFor(() => expect(createMock).toHaveBeenCalledTimes(1))
    await save('colmado', { cod: '20' })
    await waitFor(() => expect(createMock).toHaveBeenCalledTimes(2))

    expect(await screen.findByText('2 de 2 envíos guardados')).toBeTruthy()
    expect(screen.getByTestId('quick-saved-from').props.value).toBe(todayText())
    expect(screen.getByTestId('quick-saved-to').props.value).toBe(todayText())   // por omisión desde = hasta = hoy
    expect(screen.getByText('2 × Caja · Entrega estándar')).toBeTruthy()
    expect(screen.getByTestId('quick-summary-cod')).toHaveTextContent('COD total: $30.50')
    expect(screen.getByTestId('quick-summary-packages')).toHaveTextContent('Total de paquetes: 2')
    expect(screen.getByTestId('quick-summary-invoices')).toHaveTextContent('Facturas distintas: 3')   // F-1, F-2 y F-3: cada coma es otra factura y f-2 es la misma que F-2

    await fireEvent.changeText(screen.getByTestId('quick-saved-search'), 'COLMADO')
    expect(screen.getByText('1 de 2 envíos guardados')).toBeTruthy()
    expect(screen.queryByText(/W-1001/)).toBeNull()
    expect(screen.getByText(/W-1002/)).toBeTruthy()
    expect(screen.getByText('1 × Caja · Entrega estándar')).toBeTruthy()
    expect(screen.getByTestId('quick-summary-cod')).toHaveTextContent('COD total: $20.00')

    await fireEvent.changeText(screen.getByTestId('quick-saved-search'), 'colmado zzz')
    expect(screen.getByText('Ningún envío guardado coincide con las fechas y la búsqueda.')).toBeTruthy()
  })

  it('un envío con una fecha más adelantada ensancha el rango: desde se queda en hoy y hasta sigue a esa fecha; acortarlo a mano lo esconde', async () => {
    await render(<NewOrderQuick form={FORM} />)
    await fireEvent.changeText(screen.getByTestId('quick-date'), future())
    await fireEvent.changeText(screen.getByTestId('quick-consignee'), 'farm')
    await fireEvent.press(await screen.findByTestId('quick-consignee-suggest-0'))
    await fireEvent(screen.getByTestId('quick-cod'), 'submitEditing')
    await waitFor(() => expect(createMock).toHaveBeenCalledTimes(1))
    expect(await screen.findByText(/W-1001/)).toBeTruthy()
    expect(screen.getByTestId('quick-saved-from').props.value).toBe(todayText())
    expect(screen.getByTestId('quick-saved-to').props.value).toBe(future())
    expect(screen.getByText('1 de 1 envíos guardados')).toBeTruthy()
    await fireEvent.changeText(screen.getByTestId('quick-saved-to'), todayText())
    expect(screen.queryByText(/W-1001/)).toBeNull()
    expect(screen.getByText('0 de 1 envíos guardados')).toBeTruthy()
  })
})
