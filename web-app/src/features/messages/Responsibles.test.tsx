// Lote 212 — responsables de una ruta o zona: la línea con los nombres, quién puede cambiarlos y el guardado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { NotificationsProvider } from '../notifications/NotificationsProvider'
import { MessagesProvider } from './MessagesProvider'
import { ResponsiblesLine } from './Responsibles'

vi.mock('./messagesRealtime', () => ({ connectMessagesHub: () => ({ stop: async () => {} }) }))

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; search: string; body: unknown }[], responsibles: [{ userId: 2, name: 'Ana Rivera', email: 'ana@x.com' }] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, search: url.search, body })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/messages/responsibles') return json(mock.responsibles)
    if (req.method === 'PUT' && p === '/api/v1/messages/responsibles') return json((body.userIds as number[]).map((id) => ({ userId: id, name: id === 2 ? 'Ana Rivera' : 'Pedro Pérez', email: '' })))
    if (req.method === 'GET' && p === '/api/v1/messages/people') return json([{ id: 2, name: 'Ana Rivera', email: 'ana@x.com', online: true }, { id: 3, name: 'Pedro Pérez', email: 'pedro@x.com', online: false }])
    if (req.method === 'GET' && p === '/api/v1/messages/unread') return json({ count: 0, threads: 0, latest: null })
    if (req.method === 'POST' && p === '/api/v1/presence/ping') return json({ seenUtc: new Date().toISOString() })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const SESSION = {
  me: { userId: 1, email: 'yo@x.com', fullName: 'Yo', tenantName: 'Advance' },
  isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', setLang: () => {}, logout: async () => {}, switchTenant: async () => {},
  permissions: new Set(['messages.use']), modules: new Set(['SYSTEM']), reloadMe: async () => {},
} as unknown as Session

function wrap(perms: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={perms} modules={['SYSTEM']}>
          <MemoryRouter>
            <NotificationsProvider>
              <MessagesProvider>
                <ResponsiblesLine entity="TRIP" keyId="trip-1" title="2026-0001" />
              </MessagesProvider>
            </NotificationsProvider>
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

describe('Responsables de una ruta', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.responsibles = [{ userId: 2, name: 'Ana Rivera', email: 'ana@x.com' }]
  })

  it('muestra quiénes son; sin trips.plan no se puede cambiar', async () => {
    wrap(['messages.use'])
    expect(await screen.findByText('Ana Rivera')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Responsables' })).toBeNull()
    expect(mock.requests.find((r) => r.path === '/api/v1/messages/responsibles')!.search).toContain('entity=TRIP')
  })

  it('con trips.plan: quita a uno, agrega a otro y guarda exactamente esa lista', async () => {
    const user = userEvent.setup()
    wrap(['messages.use', 'trips.plan'])
    await screen.findByText('Ana Rivera')
    await user.click(screen.getByRole('button', { name: 'Responsables' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Quitar a Ana Rivera' }))
    await user.click(within(dialog).getByRole('combobox'))
    await user.click(await within(dialog).findByText('Pedro Pérez'))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PUT' && r.path === '/api/v1/messages/responsibles')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PUT')!.body).toEqual({ entity: 'TRIP', key: 'trip-1', userIds: [3] })
  })

  it('sin mensajería (sin messages.use) no se pinta nada', () => {
    const { container } = render(
      <QueryClientProvider client={new QueryClient()}>
        <MemoryRouter>
          <ResponsiblesLine entity="TRIP" keyId="trip-1" title="x" />
        </MemoryRouter>
      </QueryClientProvider>,
    )
    expect(container).toBeEmptyDOMElement()
  })
})
