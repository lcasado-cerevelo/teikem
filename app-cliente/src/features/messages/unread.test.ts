// Mensajes sin leer (el número de la pestaña): una persona del cliente suma los del equipo y las conversaciones de sus consignatarios; un consignatario, solo lo que su cliente le contestó.
const mockSession: { session: { accessToken: string } | null } = { session: null }
jest.mock('../../kernel/auth/session', () => ({ getSessionState: () => mockSession, useSessionState: () => mockSession }))
jest.mock('../portalApi', () => ({
  getMessagesUnread: jest.fn(),
  getConsigneeConversationsUnread: jest.fn(),
  getConsigneeOwnUnread: jest.fn(),
}))

import { getConsigneeConversationsUnread, getConsigneeOwnUnread, getMessagesUnread } from '../portalApi'
import { getUnread, refreshUnread, setConsigneesUnread, setUnread } from './unread'

const team = getMessagesUnread as jest.Mock
const conversations = getConsigneeConversationsUnread as jest.Mock
const own = getConsigneeOwnUnread as jest.Mock

function token(payload: object): string {
  const b64 = (o: object) => btoa(JSON.stringify(o)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
  return `${b64({ alg: 'HS256' })}.${b64(payload)}.firma`
}

beforeEach(() => {
  jest.clearAllMocks()
  setUnread(0)
  setConsigneesUnread(0)
})

describe('mensajes sin leer', () => {
  it('una persona del cliente suma lo del equipo y las conversaciones de sus consignatarios', async () => {
    mockSession.session = { accessToken: token({ kind: 'PORTAL', cid: '7' }) }
    team.mockResolvedValue({ unread: 2 })
    conversations.mockResolvedValue({ unread: 3 })
    await refreshUnread()
    expect(getUnread()).toBe(5)
    expect(own).not.toHaveBeenCalled()
  })

  it('un consignatario cuenta solo lo que su cliente le contestó y no consulta lo del equipo', async () => {
    mockSession.session = { accessToken: token({ kind: 'PORTAL', cid: '7', loc: '12' }) }
    own.mockResolvedValue({ unread: 4 })
    await refreshUnread()
    expect(getUnread()).toBe(4)
    expect(team).not.toHaveBeenCalled()
    expect(conversations).not.toHaveBeenCalled()
  })

  it('leer lo del equipo baja solo su parte', () => {
    setUnread(2)
    setConsigneesUnread(3)
    expect(getUnread()).toBe(5)
    setUnread(0)
    expect(getUnread()).toBe(3)
  })

  it('si falla la consulta se queda el número que había', async () => {
    mockSession.session = { accessToken: token({ kind: 'PORTAL', cid: '7' }) }
    setUnread(1)
    team.mockRejectedValue(new Error('sin red'))
    conversations.mockRejectedValue(new Error('sin red'))
    await refreshUnread()
    expect(getUnread()).toBe(1)
  })
})
