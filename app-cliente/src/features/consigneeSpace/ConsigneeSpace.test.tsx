// Espacio del consignatario (Lote 117): la bandeja de lo que se le avisó (con su enlace de seguimiento) y su cuenta, donde apaga y vuelve a prender solo lo que le corresponde.
const mockRouter = { push: jest.fn(), replace: jest.fn() }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter }))
jest.mock('../portalApi', () => ({
  PAGE_SIZE: 20,
  listConsigneeInbox: jest.fn(),
  getConsigneeMe: jest.fn(),
  setConsigneeChannel: jest.fn(),
}))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { getConsigneeMe, listConsigneeInbox, setConsigneeChannel } from '../portalApi'
import { ConsigneeAccountCard } from './ConsigneeAccountCard'
import { ConsigneeInboxScreen } from './ConsigneeInboxScreen'

const inbox = listConsigneeInbox as jest.Mock
const meApi = getConsigneeMe as jest.Mock
const setChannel = setConsigneeChannel as jest.Mock

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
})

describe('ConsigneeInboxScreen', () => {
  const ITEMS = {
    items: [
      { id: 2, kind: 'STATUS', channel: 'EMAIL', title: 'Su paquete va en camino', waybill: 'W-1', trackToken: 'tok-1', state: 'SENT', createdAtUtc: '2026-10-18T12:00:00Z', sentAtUtc: '2026-10-18T12:01:00Z' },
      { id: 1, kind: 'CONSENT', channel: 'SMS', title: 'Cliente A le enviará avisos sobre sus envíos', waybill: null, trackToken: null, state: 'SKIPPED', createdAtUtc: '2026-10-18T10:00:00Z', sentAtUtc: null },
    ],
    total: 2, skip: 0, take: 20,
  }

  it('lista los avisos con su estado, el conduce y el seguimiento solo donde hay enlace', async () => {
    inbox.mockResolvedValue(ITEMS)
    await render(<ConsigneeInboxScreen />)
    expect(await screen.findByText('Su paquete va en camino')).toBeTruthy()
    expect(screen.getByText('Conduce W-1')).toBeTruthy()
    expect(screen.getByText('Enviado')).toBeTruthy()
    expect(screen.getByText('No enviado')).toBeTruthy()
    expect(screen.getByTestId('inbox-track-2')).toBeTruthy()
    expect(screen.queryByTestId('inbox-track-1')).toBeNull()
    await fireEvent.press(screen.getByTestId('inbox-track-2'))
    expect(mockRouter.push).toHaveBeenCalledWith('/track/tok-1')
  })

  it('sin avisos lo dice', async () => {
    inbox.mockResolvedValue({ items: [], total: 0, skip: 0, take: 20 })
    await render(<ConsigneeInboxScreen />)
    expect(await screen.findByText('Todavía no se le ha enviado ningún aviso.')).toBeTruthy()
  })

  it('un error del servidor se muestra', async () => {
    inbox.mockRejectedValue(new ApiError(403, { title: 'Este espacio es solo para consignatarios con acceso.' }))
    await render(<ConsigneeInboxScreen />)
    expect(await screen.findByText('Este espacio es solo para consignatarios con acceso.')).toBeTruthy()
  })
})

describe('ConsigneeAccountCard', () => {
  const ME = (over: object = {}) => ({
    fullName: 'Doña Tienda', email: 'tienda@norte.co', consigneeName: 'Tienda Norte', city: 'Ponce', clientName: 'Cliente A', noticesEnabled: true,
    emailChannel: { contact: 'tienda@norte.co', enabled: true, canTurnOn: false, unsubscribed: false },
    smsChannel: { contact: '787-555-0100', enabled: false, canTurnOn: false, unsubscribed: false },
    ...over,
  })

  it('muestra quién es y de qué cliente, y cómo recibe los avisos', async () => {
    meApi.mockResolvedValue(ME())
    await render(<ConsigneeAccountCard />)
    expect(await screen.findByText('Doña Tienda')).toBeTruthy()
    expect(screen.getByText('Cliente: Cliente A')).toBeTruthy()
    expect(screen.getByText('Avisos por correo: tienda@norte.co')).toBeTruthy()
    expect(screen.getByTestId('consignee-email')).not.toBeDisabled()
    // el texto no lo prendió el cliente: no se puede prender
    expect(screen.getByTestId('consignee-sms')).toBeDisabled()
    expect(screen.getByText('Solo su cliente puede prenderlo.')).toBeTruthy()
  })

  it('apagar un aviso llama al servidor y vuelve a leer', async () => {
    meApi.mockResolvedValue(ME())
    setChannel.mockResolvedValue(ME())
    await render(<ConsigneeAccountCard />)
    await fireEvent.press(await screen.findByTestId('consignee-email'))
    await waitFor(() => expect(setChannel).toHaveBeenCalledWith('EMAIL', false))
    await waitFor(() => expect(meApi).toHaveBeenCalledTimes(2))
  })

  it('lo que apagó él se puede volver a prender; lo de la baja por enlace, no', async () => {
    meApi.mockResolvedValue(ME({
      emailChannel: { contact: 'tienda@norte.co', enabled: false, canTurnOn: true, unsubscribed: false },
      smsChannel: { contact: '787-555-0100', enabled: false, canTurnOn: false, unsubscribed: true },
    }))
    setChannel.mockResolvedValue(ME())
    await render(<ConsigneeAccountCard />)
    expect(await screen.findByText('Lo apagó usted: puede volver a prenderlo.')).toBeTruthy()
    expect(screen.getByText('Se dio de baja con el enlace de un aviso.')).toBeTruthy()
    expect(screen.getByTestId('consignee-sms')).toBeDisabled()
    await fireEvent.press(screen.getByTestId('consignee-email'))
    await waitFor(() => expect(setChannel).toHaveBeenCalledWith('EMAIL', true))
  })

  it('si el cliente apagó los avisos lo avisa, y un error del servidor se muestra', async () => {
    meApi.mockResolvedValue(ME({ noticesEnabled: false }))
    setChannel.mockRejectedValue(new ApiError(409, { title: 'Solo su cliente puede prender este aviso: usted puede volver a prender únicamente el que apagó.' }))
    await render(<ConsigneeAccountCard />)
    expect(await screen.findByText('Su cliente tiene apagados los avisos a consignatarios.')).toBeTruthy()
    await fireEvent.press(screen.getByTestId('consignee-email'))
    expect(await screen.findByText('Solo su cliente puede prender este aviso: usted puede volver a prender únicamente el que apagó.')).toBeTruthy()
  })
})
