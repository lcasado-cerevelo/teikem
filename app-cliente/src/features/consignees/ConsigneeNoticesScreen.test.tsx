// Avisos al consignatario (portal): el cliente prende el aviso, escoge el idioma y marca a cada consignatario con la advertencia; quien solo consulta no cambia nada.
jest.mock('../portalApi', () => ({
  getConsigneeNotices: jest.fn(),
  setConsigneeNotices: jest.fn(),
  listNoticeConsignees: jest.fn(),
  markConsignee: jest.fn(),
  markConsigneesBulk: jest.fn(),
}))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { getConsigneeNotices, listNoticeConsignees, markConsignee, markConsigneesBulk, setConsigneeNotices } from '../portalApi'
import { ConsigneeNoticesScreen } from './ConsigneeNoticesScreen'

const getS = getConsigneeNotices as jest.Mock
const setS = setConsigneeNotices as jest.Mock
const list = listNoticeConsignees as jest.Mock
const mark = markConsignee as jest.Mock
const bulk = markConsigneesBulk as jest.Mock

const SETTINGS = { enabled: true, lang: 'es', emailConfigured: true, canEdit: true }
const PAGE = (over: object = {}) => ({
  items: [
    { publicId: 'l1', name: 'Tienda Norte', city: 'Ponce', email: 'tienda@norte.co', phone: '787-555-0100', acceptsEmail: false, acceptsSms: false, ...over },
    { publicId: 'l2', name: 'Colmado Sur', city: 'Yauco', email: null, phone: null, acceptsEmail: false, acceptsSms: false },
  ],
  total: 2,
  skip: 0,
  take: 20,
})

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  getS.mockResolvedValue(SETTINGS)
  list.mockResolvedValue(PAGE())
  setS.mockResolvedValue(SETTINGS)
  mark.mockResolvedValue({})
  bulk.mockResolvedValue({ done: 2, failed: 0, items: [] })
})

describe('ConsigneeNoticesScreen', () => {
  it('lista los consignatarios con su correo y deshabilita el medio que no tienen registrado', async () => {
    await render(<ConsigneeNoticesScreen />)
    expect(await screen.findByText('Tienda Norte')).toBeTruthy()
    expect(screen.getByText('Avisar por correo: tienda@norte.co')).toBeTruthy()
    expect(screen.getByText('Avisar por correo: sin registrar')).toBeTruthy()
    expect(screen.getByTestId('notices-email-l2')).toBeDisabled()
    expect(screen.getByTestId('notices-email-l1')).not.toBeDisabled()
  })

  it('marcar pide aceptar la advertencia y manda warningAccepted; cancelar no manda nada', async () => {
    await render(<ConsigneeNoticesScreen />)
    await fireEvent.press(await screen.findByTestId('notices-email-l1'))
    expect(await screen.findByText('Antes de continuar')).toBeTruthy()
    expect(screen.getByText(/aceptó recibir avisos sobre sus envíos/)).toBeTruthy()
    await fireEvent.press(screen.getByTestId('notices-warning-cancel'))
    expect(mark).not.toHaveBeenCalled()

    await fireEvent.press(screen.getByTestId('notices-email-l1'))
    await fireEvent.press(await screen.findByTestId('notices-warning-continue'))
    await waitFor(() => expect(mark).toHaveBeenCalledWith('l1', 'EMAIL', true, true))
  })

  it('desmarcar no pide la advertencia', async () => {
    list.mockResolvedValue(PAGE({ acceptsEmail: true }))
    await render(<ConsigneeNoticesScreen />)
    await fireEvent.press(await screen.findByTestId('notices-email-l1'))
    await waitFor(() => expect(mark).toHaveBeenCalledWith('l1', 'EMAIL', false, false))
    expect(screen.queryByText('Antes de continuar')).toBeNull()
  })

  it('prender y apagar el aviso y cambiar el idioma llaman al servidor', async () => {
    await render(<ConsigneeNoticesScreen />)
    await fireEvent.press(await screen.findByTestId('notices-enable'))
    await waitFor(() => expect(setS).toHaveBeenCalledWith({ enabled: false }))
    await fireEvent.press(screen.getByTestId('notices-lang-en'))
    await waitFor(() => expect(setS).toHaveBeenCalledWith({ lang: 'en' }))
  })

  it('con el aviso apagado no se puede marcar a nadie', async () => {
    getS.mockResolvedValue({ ...SETTINGS, enabled: false })
    await render(<ConsigneeNoticesScreen />)
    expect(await screen.findByTestId('notices-email-l1')).toBeDisabled()
  })

  it('quien solo consulta lo ve sin poder cambiarlo', async () => {
    getS.mockResolvedValue({ ...SETTINGS, canEdit: false })
    await render(<ConsigneeNoticesScreen />)
    expect(await screen.findByText('Su rol es de solo consulta: puede ver los avisos, no cambiarlos.')).toBeTruthy()
    expect(screen.getByTestId('notices-enable')).toBeDisabled()
    expect(screen.getByTestId('notices-email-l1')).toBeDisabled()
  })

  it('marcar varios: seleccionar los que se ven, una sola advertencia y una llamada con todos; los que no se pudieron salen con su motivo', async () => {
    await render(<ConsigneeNoticesScreen />)
    await screen.findByText('Tienda Norte')
    expect(screen.queryByTestId('notices-bulk-email-on')).toBeNull()   // sin selección no hay acciones
    await fireEvent.press(screen.getByTestId('notices-select-visible'))
    expect(screen.getByText('2 seleccionados')).toBeTruthy()
    await fireEvent.press(screen.getByTestId('notices-bulk-email-on'))
    expect(await screen.findByText('Antes de continuar')).toBeTruthy()
    expect(screen.getByText(/aplica a 2 consignatarios/)).toBeTruthy()
    bulk.mockResolvedValue({ done: 1, failed: 1, items: [{ publicId: 'l1', name: 'Tienda Norte', ok: true }, { publicId: 'l2', name: 'Colmado Sur', ok: false, message: 'El consignatario no tiene correo registrado.' }] })
    await fireEvent.press(screen.getByTestId('notices-warning-continue'))
    await waitFor(() => expect(bulk).toHaveBeenCalledWith(['l1', 'l2'], 'EMAIL', true, true))
    expect(await screen.findByText(/1 marcados, 1 no se pudieron.*Colmado Sur: El consignatario no tiene correo registrado\./)).toBeTruthy()
    expect(mark).not.toHaveBeenCalled()
  })

  it('quitar a varios no pide la advertencia', async () => {
    list.mockResolvedValue(PAGE({ acceptsEmail: true }))
    await render(<ConsigneeNoticesScreen />)
    await screen.findByText('Tienda Norte')
    await fireEvent.press(screen.getByTestId('notices-select-l1'))
    await fireEvent.press(screen.getByTestId('notices-bulk-email-off'))
    await waitFor(() => expect(bulk).toHaveBeenCalledWith(['l1'], 'EMAIL', false, false))
    expect(screen.queryByText('Antes de continuar')).toBeNull()
  })

  it('un error del servidor se muestra', async () => {
    mark.mockRejectedValue(new ApiError(409, { title: 'El consignatario se dio de baja de los avisos por ese medio: no se puede volver a marcar.' }))
    await render(<ConsigneeNoticesScreen />)
    await fireEvent.press(await screen.findByTestId('notices-email-l1'))
    await fireEvent.press(await screen.findByTestId('notices-warning-continue'))
    expect(await screen.findByText('El consignatario se dio de baja de los avisos por ese medio: no se puede volver a marcar.')).toBeTruthy()
  })
})
