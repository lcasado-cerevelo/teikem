// Mensajes de los consignatarios (Lote 117, lado del cliente): la lista de conversaciones con lo no leído, abrir una (se marca leída), contestar y, con rol de solo consulta, solo leer.
const mockRouter = { push: jest.fn(), replace: jest.fn() }
const mockParams: { id?: string; name?: string } = { id: 'conv-1', name: 'Tienda Norte' }
jest.mock('expo-router', () => ({ useRouter: () => mockRouter, useLocalSearchParams: () => mockParams }))
jest.mock('../portalApi', () => ({
  PAGE_SIZE: 20,
  listConsigneeConversations: jest.fn(),
  getConsigneeConversation: jest.fn(),
  replyConsigneeConversation: jest.fn(),
  markConsigneeConversationRead: jest.fn().mockResolvedValue(undefined),
  getConsigneeNotices: jest.fn(),
  getConsigneeConversationsUnread: jest.fn().mockResolvedValue({ unread: 0 }),
  getMessagesUnread: jest.fn().mockResolvedValue({ unread: 0 }),
}))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { setLang } from '../../kernel/i18n/i18n'
import { getConsigneeConversation, getConsigneeNotices, listConsigneeConversations, markConsigneeConversationRead, replyConsigneeConversation } from '../portalApi'
import { ConsigneeConversationScreen } from './ConsigneeConversationScreen'
import { ConsigneeConversationsScreen } from './ConsigneeConversationsScreen'

const list = listConsigneeConversations as jest.Mock
const get = getConsigneeConversation as jest.Mock
const reply = replyConsigneeConversation as jest.Mock
const read = markConsigneeConversationRead as jest.Mock
const settings = getConsigneeNotices as jest.Mock

const msg = (id: number, mine: boolean, body: string) => ({ id, mine, body, senderName: mine ? 'Ana' : 'Doña Tienda', senderKind: mine ? 'CLIENT' : 'CONSIGNEE', createdAtUtc: '2026-10-18T12:00:00Z' })

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  settings.mockResolvedValue({ enabled: true, lang: 'es', emailConfigured: true, canEdit: true })
})

describe('ConsigneeConversationsScreen', () => {
  it('lista las conversaciones con lo no leído y abre la que se toca', async () => {
    list.mockResolvedValue({
      items: [
        { publicId: 'conv-1', locationPublicId: 'l1', consigneeName: 'Tienda Norte', personName: 'Doña Tienda', personEmail: 'tienda@norte.co', lastMessagePreview: '¿Cuándo llega?', lastMessageAtUtc: '2026-10-18T12:00:00Z', lastSenderKind: 'CONSIGNEE', unread: 2 },
        { publicId: 'conv-2', locationPublicId: 'l2', consigneeName: 'Colmado Sur', personName: 'Don Colmado', personEmail: null, lastMessagePreview: 'Gracias', lastMessageAtUtc: '2026-10-17T12:00:00Z', lastSenderKind: 'CLIENT', unread: 0 },
      ],
      total: 2, skip: 0, take: 20,
    })
    await render(<ConsigneeConversationsScreen />)
    expect(await screen.findByText('Tienda Norte')).toBeTruthy()
    expect(screen.getByText('2 sin leer')).toBeTruthy()
    expect(screen.queryByText('0 sin leer')).toBeNull()
    expect(screen.getByText('¿Cuándo llega?')).toBeTruthy()
    await fireEvent.press(screen.getByText('Tienda Norte'))
    expect(mockRouter.push).toHaveBeenCalledWith({ pathname: '/consignee-messages/[id]', params: { id: 'conv-1', name: 'Tienda Norte' } })
  })

  it('filtrar solo sin leer y buscar vuelven a pedir la lista', async () => {
    list.mockResolvedValue({ items: [], total: 0, skip: 0, take: 20 })
    await render(<ConsigneeConversationsScreen />)
    expect(await screen.findByText('Ningún consignatario le ha escrito.')).toBeTruthy()
    await fireEvent.press(screen.getByTestId('cm-unread-only'))
    await waitFor(() => expect(list).toHaveBeenLastCalledWith(true, '', 0, 20))
    await fireEvent.changeText(screen.getByTestId('cm-search'), 'Norte')
    await waitFor(() => expect(list).toHaveBeenLastCalledWith(true, 'Norte', 0, 20))
  })
})

describe('ConsigneeConversationScreen', () => {
  it('abre la conversación, marca leído lo del consignatario y contesta', async () => {
    get.mockResolvedValue({ publicId: 'conv-1', messages: [msg(1, false, '¿Cuándo llega mi paquete?')], hasMore: false })
    reply.mockResolvedValue(msg(2, true, 'Mañana por la mañana.'))
    await render(<ConsigneeConversationScreen />)
    expect(await screen.findByText('¿Cuándo llega mi paquete?')).toBeTruthy()
    expect(get).toHaveBeenCalledWith('conv-1', undefined, 30)
    expect(screen.getByText(/Doña Tienda · Consignatario/)).toBeTruthy()
    expect(screen.getByText('Su conversación con Tienda Norte. Solo ese consignatario y las personas de su cliente la ven.')).toBeTruthy()
    await waitFor(() => expect(read).toHaveBeenCalledWith('conv-1'))

    await fireEvent.changeText(screen.getByTestId('messages-input'), 'Mañana por la mañana.')
    await fireEvent.press(screen.getByTestId('messages-send'))
    await waitFor(() => expect(reply).toHaveBeenCalledWith('conv-1', 'Mañana por la mañana.'))
    expect(await screen.findByText('Mañana por la mañana.')).toBeTruthy()
    expect(screen.getAllByTestId('bubble-mine')).toHaveLength(1)
  })

  it('con rol de solo consulta se lee sin cuadro para escribir', async () => {
    settings.mockResolvedValue({ enabled: true, lang: 'es', emailConfigured: true, canEdit: false })
    get.mockResolvedValue({ publicId: 'conv-1', messages: [msg(1, false, 'Hola')], hasMore: false })
    await render(<ConsigneeConversationScreen />)
    expect(await screen.findByText('Hola')).toBeTruthy()
    expect(screen.getByText('Su rol es de solo consulta: puede leer los mensajes, no contestarlos.')).toBeTruthy()
    expect(screen.queryByTestId('messages-input')).toBeNull()
    expect(reply).not.toHaveBeenCalled()
  })
})
