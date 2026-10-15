// Pruebas de la pantalla de mensajes, «Nuevo mensaje» y Kemi (Lote 53) sobre un fetch simulado y una conexión en vivo falsa.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { setLang } from '../../kernel/i18n/i18n'
import { KemiFloating } from '../notifications/Kemi'
import { NotificationsProvider } from '../notifications/NotificationsProvider'
import MessagesScreen from './MessagesScreen'
import { MessagesProvider } from './MessagesProvider'
import { NewMessageHost } from './NewMessageModal'

const live = vi.hoisted(() => ({ onState: null as null | ((ok: boolean) => void), onEvent: null as null | ((n: string, e: unknown) => void), stops: 0 }))
vi.mock('./messagesRealtime', () => ({
  connectMessagesHub: (opts: { onState: (ok: boolean) => void; onEvent: (n: string, e: unknown) => void }) => {
    live.onState = opts.onState
    live.onEvent = opts.onEvent
    return { stop: async () => void live.stops++ }
  },
}))

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; search: string; body: unknown }[],
  unread: { count: 0, threads: 0, latest: null as null | Record<string, unknown> },
  groupCreator: true,
  threadUnread: 1,
  hasMore: false,
}))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, search: url.search, body })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/messages/unread') return json(mock.unread)
    if (req.method === 'GET' && p === '/api/v1/messages/threads') return json(THREADS())
    if (req.method === 'GET' && p === '/api/v1/messages/threads/d1') return json(url.searchParams.has('before') ? OLDER : DIRECT())
    if (req.method === 'GET' && p === '/api/v1/messages/threads/g1') return json(GROUP())
    if (req.method === 'GET' && p === '/api/v1/messages/people') return json(PEOPLE)
    if (req.method === 'GET' && p === '/api/v1/messages/roles') return json([{ id: 5, name: 'Gerencia', memberCount: 3 }, { id: 6, name: 'Vacío', memberCount: 0 }])
    if (req.method === 'POST' && p === '/api/v1/messages/threads') return json({ ...DIRECT(), thread: { ...DIRECT().thread, publicId: 'nuevo' } })
    if (req.method === 'POST' && p.endsWith('/messages')) return json({ id: 99, senderId: 1, senderName: 'Yo', body: body.text, createdAtUtc: new Date().toISOString(), mine: true })
    if (req.method === 'POST' && (p.endsWith('/read') || p.endsWith('/leave'))) return new Response(null, { status: 204 })
    if (req.method === 'POST' && p.endsWith('/members')) return json(GROUP())
    if (req.method === 'PUT' && p.endsWith('/mute')) return json({ ...DIRECT().thread, isMuted: body.muted })
    if (req.method === 'POST' && p === '/api/v1/presence/ping') return json({ seenUtc: new Date().toISOString() })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const ago = (min: number) => new Date(Date.now() - min * 60_000).toISOString()
const ana = { id: 2, name: 'Ana Rivera', email: 'ana@advance.com', online: true, lastSeenUtc: ago(0) }
const summary = (over: Record<string, unknown>) => ({ publicId: 'd1', kind: 'DIRECT', name: 'Ana Rivera', memberCount: 2, other: ana, lastMessagePreview: 'Hola Luis', lastSenderName: 'Ana Rivera', lastMessageAtUtc: ago(3), unread: 1, isMuted: false, ...over })
const THREADS = () => [
  summary({ unread: mock.threadUnread }),
  summary({ publicId: 'g1', kind: 'GROUP', name: 'Gerencia', memberCount: 3, other: null, lastMessagePreview: 'Cierre listo', lastSenderName: 'Pedro Pérez', unread: 0, lastMessageAtUtc: ago(60 * 30) }),
]
const m = (id: number, sender: number, name: string, body: string, min: number) => ({ id, senderId: sender, senderName: name, body, createdAtUtc: ago(min), mine: sender === 1 })
const DIRECT = () => ({
  thread: summary({ unread: mock.threadUnread }),
  members: [{ userId: 1, name: 'Yo', email: 'yo@x.com', online: true, lastSeenUtc: ago(0), isCreator: false }, { userId: 2, name: 'Ana Rivera', email: 'ana@advance.com', online: true, lastSeenUtc: ago(0), isCreator: true }],
  messages: [m(10, 2, 'Ana Rivera', 'Hola Luis', 3), m(11, 1, 'Yo', 'Hola Ana', 2)],
  hasMore: mock.hasMore,
  canAddMembers: false,
})
const OLDER = { ...DIRECT(), messages: [m(5, 2, 'Ana Rivera', 'Mensaje viejo', 600)], hasMore: false }
const GROUP = () => ({
  thread: summary({ publicId: 'g1', kind: 'GROUP', name: 'Gerencia', memberCount: 3, other: null, unread: 0 }),
  members: [{ userId: 1, name: 'Yo', email: 'yo@x.com', online: true, lastSeenUtc: ago(0), isCreator: mock.groupCreator }, { userId: 2, name: 'Ana Rivera', email: 'ana@advance.com', online: true, lastSeenUtc: ago(0), isCreator: false },
    { userId: 3, name: 'Pedro Pérez', email: 'pedro@advance.com', online: false, lastSeenUtc: ago(120), isCreator: false }],
  messages: [m(20, 3, 'Pedro Pérez', 'Cierre listo', 60 * 30)],
  hasMore: false,
  canAddMembers: mock.groupCreator,
})
const PEOPLE = [ana, { id: 3, name: 'Pedro Pérez', email: 'pedro@advance.com', online: false, lastSeenUtc: ago(120) }, { id: 4, name: 'Rosa Díaz', email: 'rosa@advance.com', online: false, lastSeenUtc: null }]

const SESSION = {
  me: { userId: 1, email: 'yo@x.com', fullName: 'Yo', tenantName: 'Advance' },
  isAuthenticated: true,
  isLoading: false,
  error: null,
  tenantId: 7,
  lang: 'es',
  setLang: () => {},
  logout: async () => {},
  switchTenant: async () => {},
  permissions: new Set(['messages.use']),
  modules: new Set(['SYSTEM']),
  reloadMe: async () => {},
} as unknown as Session

function Where() {
  const l = useLocation()
  return <span data-testid="where">{l.pathname + l.search}</span>
}

let client: QueryClient
function wrap(initial = '/messages', withScreen = true) {
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <MemoryRouter initialEntries={[initial]}>
          <NotificationsProvider>
            <MessagesProvider>
              {withScreen && <MessagesScreen />}
              <KemiFloating />
              <NewMessageHost />
              <Where />
            </MessagesProvider>
          </NotificationsProvider>
        </MemoryRouter>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = () => mock.requests.filter((r) => r.method === 'POST' && r.path.endsWith('/messages'))

describe('Mensajes', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.unread = { count: 0, threads: 0, latest: null }
    mock.groupCreator = true
    mock.threadUnread = 1
    mock.hasMore = false
    live.onState = null
    live.onEvent = null
    localStorage.clear()
    sessionStorage.clear()
  })

  it('lista los hilos con su último mensaje, no leídos y quién está en línea', async () => {
    wrap()
    const list = await screen.findByRole('complementary', { name: 'Conversaciones' })
    const direct = (await within(list).findByText('Ana Rivera')).closest('button') as HTMLElement
    expect(within(direct).getByText('Hola Luis')).toBeInTheDocument()
    expect(within(direct).getByLabelText('1 sin leer')).toBeInTheDocument()
    expect(direct.querySelector('.mdot.on')).not.toBeNull()   // en línea
    const group = within(list).getByText('Gerencia').closest('button') as HTMLElement
    expect(within(group).getByText('Pedro Pérez: Cierre listo')).toBeInTheDocument()
    expect(group.querySelector('.mdot')).toBeNull()           // los grupos no tienen punto
  })

  it('al abrir un hilo muestra la conversación con lo tuyo a la derecha y lo marca leído', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click((await screen.findByText('Ana Rivera')).closest('button')!)
    const pane = await screen.findByRole('region', { name: 'Ana Rivera' })
    expect(await within(pane).findByText('Hola Ana')).toBeInTheDocument()
    expect(within(pane).getByText('Hola Luis').closest('.chat-group')).not.toHaveClass('mine')
    expect(within(pane).getByText('Hola Ana').closest('.chat-group')).toHaveClass('mine')
    expect(within(pane).getByText('Hoy')).toBeInTheDocument()
    expect(within(pane).getByText('En línea')).toBeInTheDocument()
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/messages/threads/d1/read')).toBe(true))
  })

  it('Enter envía, Mayús+Enter es salto de línea y vacío no se envía', async () => {
    const user = userEvent.setup()
    wrap('/messages?thread=d1')
    const box = await screen.findByRole('textbox', { name: 'Escriba un mensaje…' })
    expect(screen.getByRole('button', { name: 'Enviar' })).toBeDisabled()
    await user.type(box, 'Primera{Shift>}{Enter}{/Shift}segunda')
    expect(sent()).toHaveLength(0)
    expect(box).toHaveValue('Primera\nsegunda')
    await user.keyboard('{Enter}')
    await waitFor(() => expect(sent()).toHaveLength(1))
    expect(sent()[0]).toMatchObject({ path: '/api/v1/messages/threads/d1/messages', body: { text: 'Primera\nsegunda' } })
    await waitFor(() => expect(box).toHaveValue(''))
  })

  it('pide los mensajes anteriores con before', async () => {
    mock.hasMore = true
    const user = userEvent.setup()
    wrap('/messages?thread=d1')
    await user.click(await screen.findByRole('button', { name: 'Cargar mensajes anteriores' }))
    expect(await screen.findByText('Mensaje viejo')).toBeInTheDocument()
    expect(mock.requests.some((r) => r.path === '/api/v1/messages/threads/d1' && r.search.includes('before=10'))).toBe(true)
    expect(screen.queryByRole('button', { name: 'Cargar mensajes anteriores' })).not.toBeInTheDocument()
  })

  it('en un grupo muestra las personas con su presencia y quien lo creó agrega a otra', async () => {
    const user = userEvent.setup()
    wrap('/messages?thread=g1')
    expect(await screen.findByText('3 personas')).toBeInTheDocument()
    expect(screen.getByText('Pedro Pérez', { selector: '.chat-sender' })).toBeInTheDocument()   // el nombre de quien escribe en el grupo
    await user.click(screen.getByRole('button', { name: 'Personas' }))
    const region = await screen.findByRole('region', { name: 'Personas' })
    expect(within(region).getByText('(creó el grupo)')).toBeInTheDocument()
    await user.type(within(region).getByRole('combobox', { name: 'Agregar personas' }), 'rosa')
    await user.click(await within(region).findByRole('option', { name: /Rosa Díaz/ }))
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/messages/threads/g1/members')).toBe(true))
    expect(mock.requests.find((r) => r.path.endsWith('/members'))?.body).toEqual({ userIds: [4] })
  })

  it('quien no creó el grupo no ve «Agregar personas»; salirse pide confirmación', async () => {
    mock.groupCreator = false
    const user = userEvent.setup()
    wrap('/messages?thread=g1')
    await user.click(await screen.findByRole('button', { name: 'Personas' }))
    expect(screen.queryByRole('combobox', { name: 'Agregar personas' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Salirme' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/Va a salirse de «Gerencia»/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Salirme' }))
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/messages/threads/g1/leave')).toBe(true))
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('/messages'))
  })

  it('silenciar manda muted', async () => {
    const user = userEvent.setup()
    wrap('/messages?thread=d1')
    await user.click(await screen.findByRole('button', { name: 'Silenciar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PUT')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PUT')?.body).toEqual({ muted: true })
  })

  it('«Nuevo mensaje»: valida, busca personas por nombre con su correo, escoge un rol y manda', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Nuevo mensaje' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Enviar' }))
    expect(await within(dialog).findByText('Escoja al menos una persona o un rol.')).toBeInTheDocument()
    await user.type(within(dialog).getByRole('combobox', { name: 'Para' }), 'pedro')
    const option = await within(dialog).findByRole('option', { name: /Pedro Pérez/ })
    expect(option).toHaveTextContent('pedro@advance.com')
    await user.click(option)
    await user.click(within(dialog).getByRole('button', { name: 'Enviar' }))
    expect(await within(dialog).findByText('Escriba el mensaje.')).toBeInTheDocument()
    // un rol: se busca en el mismo campo; solo salen los que tienen personas, con cuántas
    const to = within(dialog).getByRole('combobox', { name: 'Para' })
    await user.type(to, 'vac')
    expect(within(dialog).queryByRole('option', { name: /Vacío/ })).not.toBeInTheDocument()
    await user.clear(to)
    await user.type(to, 'gerencia')
    const roleOption = await within(dialog).findByRole('option', { name: /Gerencia/ })
    expect(roleOption).toHaveTextContent('Rol · 3 personas')
    await user.click(roleOption)
    expect(within(dialog).getByText(/Se arma un grupo con las personas que tienen ese rol hoy/)).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Nombre del grupo (opcional)'), 'Cierre')
    await user.type(within(dialog).getByLabelText('Mensaje'), 'Cierre de mes listo')
    await user.click(within(dialog).getByRole('button', { name: 'Enviar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/messages/threads')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'POST' && r.path === '/api/v1/messages/threads')?.body).toMatchObject({ userIds: [3], roleId: 5, title: 'Cierre', text: 'Cierre de mes listo' })
    await waitFor(() => expect(screen.getByTestId('where')).toHaveTextContent('/messages?thread=nuevo'))
  })

  it('el en vivo vuelve a pedir lo suyo; al conectar se pone al día', async () => {
    wrap()
    await screen.findByText('Ana Rivera')
    const before = mock.requests.filter((r) => r.path === '/api/v1/messages/threads').length
    await act(async () => {
      live.onEvent?.('MessageReceived', { tenantId: 7, threadPublicId: 'd1', muted: false })
    })
    await waitFor(() => expect(mock.requests.filter((r) => r.path === '/api/v1/messages/threads').length).toBeGreaterThan(before))
    const mid = mock.requests.filter((r) => r.path === '/api/v1/messages/unread').length
    await act(async () => {
      live.onState?.(true)
    })
    await waitFor(() => expect(mock.requests.filter((r) => r.path === '/api/v1/messages/unread').length).toBeGreaterThan(mid))
  })
})

describe('Kemi y los mensajes', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    localStorage.clear()
    sessionStorage.clear()
  })

  it('al llegar un mensaje Kemi avisa «Te llegó un mensaje de Ana → Ver» y Ver abre ese hilo', async () => {
    const user = userEvent.setup()
    sessionStorage.setItem('teikem.kemi.greeted.messages', '1')
    mock.unread = { count: 0, threads: 0, latest: null }
    wrap('/', false)
    await screen.findByRole('button', { name: 'Kemi, asistente de Teikem' })
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/messages/unread')).toBe(true))   // la primera lectura (en 0) ya pasó
    await act(async () => {})
    expect(screen.queryByText(/Te llegó un mensaje/)).not.toBeInTheDocument()
    mock.unread = { count: 1, threads: 1, latest: { threadPublicId: 'd1', fromName: 'Ana Rivera', preview: 'Hola Luis', createdAtUtc: ago(0), isMuted: false } }
    await act(async () => {
      await client.invalidateQueries({ queryKey: ['messages'] })
    })
    const bubble = (await screen.findByText('Te llegó un mensaje de Ana Rivera')).closest('.kemi-bubble') as HTMLElement
    await user.click(within(bubble).getByRole('button', { name: /Ver/ }))
    expect(screen.getByTestId('where')).toHaveTextContent('/messages?thread=d1')
    expect(screen.queryByText(/Te llegó un mensaje/)).not.toBeInTheDocument()
  })

  it('un hilo silenciado cuenta pero no hace globo', async () => {
    sessionStorage.setItem('teikem.kemi.greeted.messages', '1')
    mock.unread = { count: 0, threads: 0, latest: null }
    wrap('/', false)
    await screen.findByRole('button', { name: 'Kemi, asistente de Teikem' })
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/messages/unread')).toBe(true))   // la primera lectura (en 0) ya pasó
    await act(async () => {})
    mock.unread = { count: 1, threads: 1, latest: { threadPublicId: 'd1', fromName: 'Ana Rivera', preview: 'x', createdAtUtc: ago(0), isMuted: true } }
    await act(async () => {
      await client.invalidateQueries({ queryKey: ['messages'] })
    })
    await waitFor(() => expect(screen.getByRole('button', { name: 'Kemi, asistente de Teikem' })).toHaveTextContent('1'))
    expect(screen.queryByText(/te escribió|Te llegó/)).not.toBeInTheDocument()
  })

  it('las opciones de Kemi: «Enviar un mensaje a…» abre Nuevo mensaje y «Mis mensajes» abre la pantalla', async () => {
    const user = userEvent.setup()
    mock.unread = { count: 3, threads: 2, latest: null }
    sessionStorage.setItem('teikem.kemi.greeted.messages', '1')
    wrap('/', false)
    await user.click(await screen.findByRole('button', { name: 'Kemi, asistente de Teikem' }))
    const panel = await screen.findByRole('dialog', { name: 'Kemi' })
    expect(within(panel).getByRole('button', { name: /Mis mensajes/ })).toHaveTextContent('3')
    await user.click(within(panel).getByRole('button', { name: 'Enviar un mensaje a…' }))
    expect(await screen.findByRole('dialog', { name: 'Nuevo mensaje' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Kemi' })).not.toBeInTheDocument()
  })
})
