// Mensajes del portal: burbujas (mías / del equipo), marcar leído al verlos, enviar, tope de 429 y unión de páginas por id.
jest.mock('../portalApi', () => ({ getMessages: jest.fn(), sendMessage: jest.fn(), markMessagesRead: jest.fn().mockResolvedValue(undefined), getMessagesUnread: jest.fn() }))

import { fireEvent, render, screen, waitFor } from '@testing-library/react-native'

import { ApiError } from '../../kernel/api/problem'
import { setLang } from '../../kernel/i18n/i18n'
import { getMessages, markMessagesRead, sendMessage } from '../portalApi'
import { MessagesScreen, mergeMessages } from './MessagesScreen'
import { getUnread, setUnread } from './unread'

const get = getMessages as jest.Mock
const send = sendMessage as jest.Mock
const read = markMessagesRead as jest.Mock

const msg = (id: number, mine: boolean, body: string) => ({ id, mine, body, senderName: mine ? 'Ana' : 'Rosa', senderKind: mine ? 'CLIENT' : 'STAFF', createdAtUtc: '2026-10-17T12:00:00Z' })

beforeEach(() => {
  jest.clearAllMocks()
  setLang('es')
  setUnread(0)
})

describe('mergeMessages', () => {
  it('une por id, sin repetir y en orden', () => {
    const r = mergeMessages([msg(2, true, 'b'), msg(3, false, 'c')], [msg(1, true, 'a'), msg(3, false, 'c')])
    expect(r.map((m) => m.id)).toEqual([1, 2, 3])
  })
})

describe('MessagesScreen', () => {
  it('muestra las burbujas y marca leído lo del equipo', async () => {
    setUnread(2)
    get.mockResolvedValue({ messages: [msg(1, true, 'Hola'), msg(2, false, 'Ya la revisamos')], hasMore: false, unread: 1 })
    await render(<MessagesScreen />)
    expect(await screen.findByText('Ya la revisamos')).toBeTruthy()
    expect(screen.getAllByTestId('bubble-mine')).toHaveLength(1)
    expect(screen.getAllByTestId('bubble-team')).toHaveLength(1)
    expect(screen.getByText(/Rosa · Servicio al cliente/)).toBeTruthy()
    await waitFor(() => expect(read).toHaveBeenCalled())
    expect(getUnread()).toBe(0)
  })

  it('sin mensajes dice cómo empezar', async () => {
    get.mockResolvedValue({ messages: [], hasMore: false, unread: 0 })
    await render(<MessagesScreen />)
    expect(await screen.findByText(/Todavía no hay mensajes/)).toBeTruthy()
    expect(read).not.toHaveBeenCalled()
  })

  it('envía y deja el mensaje en la conversación', async () => {
    get.mockResolvedValue({ messages: [], hasMore: false, unread: 0 })
    send.mockResolvedValue(msg(5, true, 'Necesito ayuda'))
    await render(<MessagesScreen />)
    await screen.findByText(/Todavía no hay mensajes/)
    await fireEvent.changeText(screen.getByTestId('messages-input'), '  Necesito ayuda ')
    await fireEvent.press(screen.getByTestId('messages-send'))
    expect(await screen.findByText('Necesito ayuda')).toBeTruthy()
    expect(send).toHaveBeenCalledWith('Necesito ayuda')
  })

  it('con 429 avisa que espere y conserva lo escrito', async () => {
    get.mockResolvedValue({ messages: [], hasMore: false, unread: 0 })
    send.mockRejectedValue(new ApiError(429, { title: 'x' }))
    await render(<MessagesScreen />)
    await screen.findByText(/Todavía no hay mensajes/)
    await fireEvent.changeText(screen.getByTestId('messages-input'), 'otro')
    await fireEvent.press(screen.getByTestId('messages-send'))
    expect(await screen.findByText(/Demasiados mensajes seguidos/)).toBeTruthy()
    expect(screen.getByTestId('messages-input').props.value).toBe('otro')
  })

  it('carga los anteriores', async () => {
    get.mockResolvedValueOnce({ messages: [msg(5, true, 'cinco')], hasMore: true, unread: 0 }).mockResolvedValueOnce({ messages: [msg(4, false, 'cuatro')], hasMore: false, unread: 0 })
    await render(<MessagesScreen />)
    await fireEvent.press(await screen.findByTestId('messages-older'))
    expect(await screen.findByText('cuatro')).toBeTruthy()
    expect(get).toHaveBeenLastCalledWith(5, 30)
  })
})
