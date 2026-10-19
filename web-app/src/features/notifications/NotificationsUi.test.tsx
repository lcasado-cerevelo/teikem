// Pruebas de la campanita y de Kemi (Lote 52) sobre un fetch simulado: contador, lista, marcar leído, el globo cuando llega un aviso nuevo,
// dormir y despertar a Kemi, y silenciar un evento opcional.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { setLang } from '../../kernel/i18n/i18n'
import { KemiFloating, KemiSleepButton } from './Kemi'
import { NotificationBell } from './NotificationBell'
import { NotificationsProvider } from './NotificationsProvider'

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; body: unknown }[],
  unread: 2,
  items: [] as { id: number; eventCode: string; title: string; body: string | null; linkUrl: string | null; isRead: boolean; createdAtUtc: string }[],
  muted: false,
}))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    mock.requests.push({ method: req.method, path: url.pathname, body: text ? JSON.parse(text) : undefined })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/inbox/unread-count') return json({ count: mock.unread })
    if (req.method === 'GET' && p === '/api/v1/inbox') return json({ items: mock.items, total: mock.items.length, skip: 0, take: 20 })
    if (req.method === 'POST' && p.endsWith('/read')) {
      const id = Number(p.split('/')[4])
      const n = mock.items.find((x) => x.id === id)!
      n.isRead = true
      mock.unread = mock.items.filter((x) => !x.isRead).length
      return json(n)
    }
    if (req.method === 'POST' && p === '/api/v1/inbox/read-all') {
      mock.items.forEach((x) => (x.isRead = true))
      mock.unread = 0
      return json({ marked: 2 })
    }
    if (req.method === 'GET' && p === '/api/v1/inbox/prefs') return json([{ eventCode: 'TEST', name: 'Aviso de prueba', nameEn: 'Test', isMuted: mock.muted }])
    if (req.method === 'PUT' && p === '/api/v1/inbox/prefs/TEST') {
      mock.muted = JSON.parse(text).muted
      return json({ eventCode: 'TEST', name: 'Aviso de prueba', nameEn: 'Test', isMuted: mock.muted })
    }
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const SESSION = {
  me: { userId: 11, email: 'ana@x.com', fullName: 'Ana', tenantName: 'Advance' },
  isAuthenticated: true,
  isLoading: false,
  error: null,
  tenantId: 7,
  lang: 'es',
  setLang: () => {},
  logout: async () => {},
  switchTenant: async () => {},
  permissions: new Set<string>(),
  modules: new Set(['SYSTEM']),
  reloadMe: async () => {},
} as unknown as Session

let client: QueryClient
function Where() {
  return <span data-testid="where">{useLocation().pathname}</span>
}

function wrap() {
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <MemoryRouter initialEntries={['/']}>
          <NotificationsProvider>
            <header>
              <KemiSleepButton />
              <NotificationBell />
            </header>
            <KemiFloating />
            <Where />
          </NotificationsProvider>
        </MemoryRouter>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const ITEMS = () => [
  { id: 2, eventCode: 'TEST', title: 'Recibo con diferencia', body: 'Faltan 3 piezas.', linkUrl: '/orders/9', isRead: false, createdAtUtc: '2026-10-14T12:00:00Z', fromName: 'Ana Rivera' },
  { id: 1, eventCode: 'TEST', title: 'Aviso viejo', body: null, linkUrl: null, isRead: true, createdAtUtc: '2026-10-13T12:00:00Z' },
]

describe('campanita y Kemi', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.unread = 2
    mock.muted = false
    mock.items = ITEMS()
    localStorage.clear()
    sessionStorage.clear()
  })
  afterEach(() => vi.useRealTimers())

  it('la campanita muestra el contador y su nombre accesible lo dice', async () => {
    wrap()
    const bell = await screen.findByRole('button', { name: 'Avisos: 2 sin leer' })
    expect(within(bell).getByText('2')).toBeInTheDocument()
  })

  it('al abrirla lista los avisos; un clic marca leído y abre el enlace', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Avisos: 2 sin leer' }))
    const dialog = await screen.findByRole('dialog', { name: 'Avisos' })
    expect(await within(dialog).findByText('Recibo con diferencia')).toBeInTheDocument()
    expect(within(dialog).getByText('Faltan 3 piezas.')).toBeInTheDocument()
    expect(within(dialog).getByText('De Ana Rivera')).toBeInTheDocument()   // los avisos manuales dicen quién los envió; los de las reglas no
    expect(within(dialog).queryAllByText(/^De /)).toHaveLength(1)
    await user.click(within(dialog).getByRole('button', { name: /Recibo con diferencia/ }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/inbox/2/read')).toBe(true))
    expect(screen.getByTestId('where')).toHaveTextContent('/orders/9')
    expect(screen.queryByRole('dialog', { name: 'Avisos' })).not.toBeInTheDocument()
  })

  it('«Marcar todo como leído» limpia el contador', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Avisos: 2 sin leer' }))
    await user.click(await screen.findByRole('button', { name: 'Marcar todo como leído' }))
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/inbox/read-all')).toBe(true))
    expect(await screen.findByRole('button', { name: 'Avisos' })).toBeInTheDocument()
  })

  it('Kemi saluda con un globo la primera vez de la sesión; «Ver» abre la campanita', async () => {
    const user = userEvent.setup()
    wrap()
    const bubble = await screen.findByRole('status')
    expect(bubble).toHaveTextContent('Te llegaron 2 avisos')
    await user.click(within(bubble).getByRole('button', { name: /Ver/ }))
    expect(await screen.findByRole('dialog', { name: 'Avisos' })).toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()   // abrir la campanita esconde el globo
  })

  it('no vuelve a saludar en la misma sesión, pero sí avisa cuando llega uno nuevo', async () => {
    sessionStorage.setItem('teikem.kemi.greeted', '1')
    wrap()
    await screen.findByRole('button', { name: 'Avisos: 2 sin leer' })
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    mock.unread = 3
    await act(async () => {
      await client.invalidateQueries({ queryKey: ['notifications'] })
    })
    expect(await screen.findByRole('status')).toHaveTextContent('Te llegó un aviso')
  })

  it('el globo se esconde con su ✕', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Cerrar el aviso de Kemi' }))
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('un clic en Kemi abre sus opciones; «Dormir a Kemi» la esconde en la cabecera y un clic la despierta', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Kemi, asistente de Teikem' }))
    const panel = await screen.findByRole('dialog', { name: 'Kemi' })
    expect(within(panel).getByText(/Tienes 2 avisos sin leer/)).toBeInTheDocument()
    await user.click(within(panel).getByRole('button', { name: 'Dormir a Kemi' }))
    expect(screen.queryByRole('button', { name: 'Kemi, asistente de Teikem' })).not.toBeInTheDocument()
    expect(localStorage.getItem('teikem.kemi.asleep.11')).toBe('1')
    // dormida, la campanita sigue con su contador (no la reemplaza)
    expect(screen.getByRole('button', { name: 'Avisos: 2 sin leer' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Despertar a Kemi' }))
    expect(await screen.findByRole('button', { name: 'Kemi, asistente de Teikem' })).toBeInTheDocument()
    expect(localStorage.getItem('teikem.kemi.asleep.11')).toBeNull()
  })

  it('«Pregúntame cómo usar el sistema»: Kemi busca en el manual y la FAQ, trae opciones, muestra la respuesta y pregunta si contestó', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Kemi, asistente de Teikem' }))
    const panel = await screen.findByRole('dialog', { name: 'Kemi' })
    await user.click(within(panel).getByRole('button', { name: 'Pregúntame cómo usar el sistema' }))
    expect(within(panel).getByRole('button', { name: 'Preguntar' })).toBeDisabled()          // sin texto no se puede preguntar
    await user.type(within(panel).getByRole('textbox', { name: 'Tu pregunta para Kemi' }), 'cómo cambio mi contraseña')
    await user.click(within(panel).getByRole('button', { name: 'Preguntar' }))
    expect(await within(panel).findByText('Esto encontré:')).toBeInTheDocument()
    await user.click(within(panel).getAllByRole('button', { name: /contraseña/i })[0])
    expect(within(panel).getByText('¿Te contesté?')).toBeInTheDocument()
    await user.click(within(panel).getByRole('button', { name: 'Sí' }))
    expect(within(panel).getByRole('status')).toHaveTextContent('Qué bueno que te sirvió')
    await user.click(within(panel).getByRole('button', { name: 'Hacer otra pregunta' }))
    expect(within(panel).getByRole('textbox', { name: 'Tu pregunta para Kemi' })).toHaveValue('')
  })

  it('si dice que no le contestó, Kemi busca por otro lado; si no hay nada lo dice; «Volver» regresa a las opciones', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Kemi, asistente de Teikem' }))
    const panel = await screen.findByRole('dialog', { name: 'Kemi' })
    await user.click(within(panel).getByRole('button', { name: 'Pregúntame cómo usar el sistema' }))
    await user.type(within(panel).getByRole('textbox', { name: 'Tu pregunta para Kemi' }), 'contraseña')
    await user.click(within(panel).getByRole('button', { name: 'Preguntar' }))
    await within(panel).findByText('Esto encontré:')
    await user.click(within(panel).getAllByRole('button', { name: /contraseña/i })[0])
    await user.click(within(panel).getByRole('button', { name: 'No' }))
    expect(await within(panel).findByText(/Busquemos por otro lado/)).toBeInTheDocument()
    await user.click(within(panel).getByRole('button', { name: 'Cambiar la pregunta' }))
    await user.clear(within(panel).getByRole('textbox', { name: 'Tu pregunta para Kemi' }))
    await user.type(within(panel).getByRole('textbox', { name: 'Tu pregunta para Kemi' }), 'xyzzy plugh')
    await user.click(within(panel).getByRole('button', { name: 'Preguntar' }))
    expect(await within(panel).findByText(/No encontré nada sobre eso/)).toBeInTheDocument()
    await user.click(within(panel).getByRole('button', { name: 'Volver' }))
    expect(within(panel).getByRole('button', { name: 'Dormir a Kemi' })).toBeInTheDocument()
  })

  it('dormida no muestra globos', async () => {
    localStorage.setItem('teikem.kemi.asleep.11', '1')
    wrap()
    await screen.findByRole('button', { name: 'Despertar a Kemi' })
    await screen.findByRole('button', { name: 'Avisos: 2 sin leer' })
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('«Ver mis avisos» en las opciones de Kemi abre la campanita', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Kemi, asistente de Teikem' }))
    await user.click(within(await screen.findByRole('dialog', { name: 'Kemi' })).getByRole('button', { name: /Ver mis avisos/ }))
    expect(await screen.findByRole('dialog', { name: 'Avisos' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Kemi' })).not.toBeInTheDocument()
  })

  it('silenciar un evento opcional manda muted', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Avisos: 2 sin leer' }))
    await user.click(await screen.findByText('Avisos que puedo silenciar'))
    await user.click(await screen.findByRole('switch', { name: 'Recibir «Aviso de prueba»' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PUT' && r.path === '/api/v1/inbox/prefs/TEST')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PUT')?.body).toEqual({ muted: true })
  })
})
