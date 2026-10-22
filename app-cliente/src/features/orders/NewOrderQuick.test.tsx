// La rejilla de captura rápida: escribir el consignatario y elegirlo, Guardar fila crea el envío, la fila queda guardada y sale una nueva con la misma fecha; los errores no pierden lo escrito.
jest.mock('../portalApi', () => ({ createOrder: jest.fn() }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { createOrder } from '../portalApi'
import { NewOrderQuick } from './NewOrderQuick'

const createMock = createOrder as jest.Mock

const FORM = {
  canCreate: true,
  clientTypesInvoiceNumber: false,
  consignees: [
    { publicId: 'a', name: 'Farmacia Norte', line1: 'Calle 1', city: 'Bayamón' },
    { publicId: 'b', name: 'Colmado Sur', line1: 'Av. Principal 22', city: 'Ponce' },
  ],
  pickups: [],
  maxPackages: 50,
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
})

describe('NewOrderQuick', () => {
  it('guardar una fila sin consignatario muestra el error y no llama al servidor', async () => {
    await render(<NewOrderQuick form={FORM} />)
    await fireEvent.press(screen.getByTestId('quick-save'))
    expect(await screen.findByText(/Escoja el consignatario/)).toBeTruthy()
    expect(createMock).not.toHaveBeenCalled()
  })

  it('escribe, elige el consignatario de la lista, guarda y deja la fila siguiente con la misma fecha', async () => {
    await render(<NewOrderQuick form={FORM} />)
    await fireEvent.changeText(screen.getByTestId('quick-date'), future())
    await fireEvent.changeText(screen.getByTestId('quick-consignee'), 'farm')
    await fireEvent.press(await screen.findByTestId('quick-suggest-0'))
    await fireEvent.changeText(screen.getByTestId('quick-description'), 'Cajas')
    await fireEvent.changeText(screen.getByTestId('quick-pieces'), '2')
    await fireEvent.changeText(screen.getByTestId('quick-cod'), '15')
    await fireEvent.press(screen.getByTestId('quick-save'))

    await waitFor(() => expect(createMock).toHaveBeenCalledTimes(1))
    expect(createMock.mock.calls[0][0]).toMatchObject({ consigneePublicId: 'a', codAmount: 15, packages: [{ description: 'Cajas', pieces: 2, weightKg: null }], requestedDate: `${future()}T00:00:00Z` })
    expect(await screen.findByText('W-1001')).toBeTruthy()   // la fila guardada con su número
    expect(screen.getByText('1 guardados')).toBeTruthy()
    // la fila nueva: misma fecha, sin consignatario
    expect(screen.getByTestId('quick-date').props.value).toBe(future())
    expect(screen.getByTestId('quick-consignee').props.value).toBe('')
  })

  it('«+ Nuevo consignatario» abre el bloque para escribirlo en la misma fila', async () => {
    await render(<NewOrderQuick form={FORM} />)
    await fireEvent.changeText(screen.getByTestId('quick-date'), future())
    await fireEvent.changeText(screen.getByTestId('quick-consignee'), 'Tienda Nueva')
    await fireEvent.press(await screen.findByTestId('quick-new-consignee'))
    expect(screen.getByTestId('quick-new-block')).toBeTruthy()
    expect(screen.getByTestId('quick-new-name').props.value).toBe('Tienda Nueva')
    await fireEvent.changeText(screen.getByTestId('quick-new-line1'), 'Calle 5')
    await fireEvent.changeText(screen.getByTestId('quick-new-city'), 'Caguas')
    await fireEvent.press(screen.getByTestId('quick-save'))
    await waitFor(() => expect(createMock).toHaveBeenCalledTimes(1))
    expect(createMock.mock.calls[0][0].newConsignee).toMatchObject({ name: 'Tienda Nueva', line1: 'Calle 5', city: 'Caguas' })
    expect(createMock.mock.calls[0][0].consigneePublicId).toBeNull()
  })

  it('si el servidor rechaza la fila muestra su mensaje y conserva lo escrito', async () => {
    createMock.mockRejectedValue(new ApiError(422, { title: 'El crédito del cliente no alcanza.' }))
    await render(<NewOrderQuick form={FORM} />)
    await fireEvent.changeText(screen.getByTestId('quick-date'), future())
    await fireEvent.changeText(screen.getByTestId('quick-consignee'), 'colmado')
    await fireEvent.press(await screen.findByTestId('quick-suggest-0'))
    await fireEvent.press(screen.getByTestId('quick-save'))
    expect(await screen.findByText('El crédito del cliente no alcanza.')).toBeTruthy()
    expect(screen.getByTestId('quick-consignee').props.value).toBe('Colmado Sur')
    expect(screen.queryByText('1 guardados')).toBeNull()
  })
})
