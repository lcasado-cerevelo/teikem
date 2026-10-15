// Pruebas de «Enviar avisos» (Lote 54) sobre un fetch simulado: el buscador de personas y roles, «Seleccionar todo» (solo lo que coincide con lo
// escrito), los mensajes del servidor, el envío a todos con confirmación y el historial.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { setLang } from '../../kernel/i18n/i18n'
import AnnouncementsScreen from './AnnouncementsScreen'

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; search: string; body: unknown }[],
  postStatus: 200,
}))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, search: url.search, body })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/announcements/people') {
      const q = (url.searchParams.get('q') ?? '').toLowerCase()
      const items = PEOPLE.filter((x) => !q || x.name.toLowerCase().includes(q) || x.email.toLowerCase().includes(q))
      return json({ items, total: items.length, skip: 0, take: 500 })
    }
    if (req.method === 'GET' && p === '/api/v1/announcements/roles') return json([{ id: 5, name: 'Gerencia', memberCount: 3 }, { id: 6, name: 'Vacío', memberCount: 0 }])
    if (req.method === 'POST' && p === '/api/v1/announcements') {
      if (mock.postStatus !== 200) return new Response(JSON.stringify({ title: 'No hay personas a quienes enviar el aviso.' }), { status: mock.postStatus, headers: { 'Content-Type': 'application/problem+json' } })
      return json({ publicId: 'n1', title: body.title, body: body.text ?? null, sentByName: 'Yo', sentAtUtc: new Date().toISOString(), toAll: !!body.all, audienceText: 'x', recipientCount: 4, readCount: 0 })
    }
    if (req.method === 'GET' && p === '/api/v1/announcements') return json({ items: HISTORY, total: HISTORY.length, skip: 0, take: 10 })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const PEOPLE = [
  { id: 1, name: 'Ana Rivera', email: 'ana@advance.com' },
  { id: 2, name: 'Luis Casado', email: 'lcasadopr@gmail.com' },
  { id: 3, name: 'Pedro Pérez', email: 'pedro@advance.com' },
  { id: 4, name: 'Rosa Díaz', email: 'rosa@advance.com' },
  ...Array.from({ length: 12 }, (_, i) => ({ id: 10 + i, name: `Operador ${String(i).padStart(2, '0')}`, email: `op${i}@advance.com` })),
]
const HISTORY = [{ publicId: 'h1', title: 'Cierre mañana', body: 'Traigan sus reportes.', sentByName: 'Ana Rivera', sentAtUtc: new Date().toISOString(), toAll: false, audienceText: 'Gerencia, Luis Casado', recipientCount: 5, readCount: 2 }]

const SESSION = {
  me: { userId: 99, tenantName: 'Advance Logistics' },
  isAuthenticated: true,
  isLoading: false,
  error: null,
  tenantId: 7,
  lang: 'es',
  setLang: () => {},
  logout: async () => {},
  switchTenant: async () => {},
  permissions: new Set(['notifications.send']),
  modules: new Set(['SYSTEM']),
  reloadMe: async () => {},
} as unknown as Session

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <MemoryRouter>
          <AnnouncementsScreen />
        </MemoryRouter>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const posted = () => mock.requests.filter((r) => r.method === 'POST' && r.path === '/api/v1/announcements')

describe('AnnouncementsScreen', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.postStatus = 200
  })

  it('el buscador lista personas con su correo y roles con cuántas personas tienen (sin los roles vacíos)', async () => {
    const user = userEvent.setup()
    wrap()
    const to = await screen.findByRole('combobox', { name: 'Para' })
    await user.type(to, 'ger')
    const role = await screen.findByRole('option', { name: /Gerencia/ })
    expect(role).toHaveTextContent('Rol · 3 personas')
    await user.clear(to)
    await user.type(to, 'vac')
    expect(screen.queryByRole('option', { name: /Vacío/ })).not.toBeInTheDocument()
    await user.clear(to)
    await user.type(to, 'luis')
    expect(await screen.findByRole('option', { name: /Luis Casado/ })).toHaveTextContent('lcasadopr@gmail.com')
  })

  it('«Seleccionar todo» escoge SOLO lo que coincide con lo escrito (también lo que no cabe en la lista)', async () => {
    const user = userEvent.setup()
    wrap()
    const to = await screen.findByRole('combobox', { name: 'Para' })
    await user.type(to, 'operador')
    const all = await screen.findByRole('option', { name: 'Seleccionar todo (12)' })   // 12 operadores aunque la lista solo muestre 8
    expect(screen.getByText(/…y 4 más/)).toBeInTheDocument()
    await user.click(all)
    expect(await screen.findByText('12 personas y 0 roles escogidos.')).toBeInTheDocument()
    expect(screen.getByText('+2 personas más')).toBeInTheDocument()   // las fichas muestran 10 y resumen el resto
    // lo que no coincidía no se escogió
    await user.type(to, 'rosa')
    expect(await screen.findByRole('option', { name: 'Seleccionar todo (1)' })).toBeInTheDocument()
    // «Quitar todo»
    await user.click(screen.getByRole('button', { name: 'Quitar todo' }))
    expect(screen.queryByText(/escogidos/)).not.toBeInTheDocument()
  })

  it('personas y roles juntos: «Seleccionar todo» también marca los roles que coinciden', async () => {
    const user = userEvent.setup()
    wrap()
    await user.type(await screen.findByRole('combobox', { name: 'Para' }), 'a')   // casi todas las personas y «Gerencia»
    await user.click(await screen.findByRole('option', { name: /Seleccionar todo/ }))
    expect(await screen.findByText(/personas y 1 roles escogidos\./)).toBeInTheDocument()
    expect(screen.getByText('Gerencia')).toBeInTheDocument()
  })

  it('envía a personas y roles con el título y el texto', async () => {
    const user = userEvent.setup()
    wrap()
    const to = await screen.findByRole('combobox', { name: 'Para' })
    await user.type(to, 'ana')
    await user.click(await screen.findByRole('option', { name: /Ana Rivera/ }))
    await user.type(to, 'gerencia')
    await user.click(await screen.findByRole('option', { name: /Gerencia/ }))
    await user.type(screen.getByLabelText('Título'), '  Cierre mañana  ')
    await user.type(screen.getByLabelText('Texto (opcional)'), 'Traigan sus reportes.')
    await user.click(screen.getByRole('button', { name: 'Enviar aviso' }))
    await waitFor(() => expect(posted()).toHaveLength(1))
    expect(posted()[0].body).toMatchObject({ userIds: [1], roleIds: [5], title: '  Cierre mañana  ', text: 'Traigan sus reportes.' })
    expect(posted()[0].body).not.toHaveProperty('all')
    await waitFor(() => expect(screen.getByLabelText('Título')).toHaveValue(''))   // el formulario queda limpio
  })

  it('valida lo que falta y muestra el mensaje del servidor sin perder lo escrito', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Enviar aviso' }))
    expect(await screen.findByText('Escoja al menos una persona, un rol o a todos.')).toBeInTheDocument()
    const to = screen.getByRole('combobox', { name: 'Para' })
    await user.type(to, 'ana')
    await user.click(await screen.findByRole('option', { name: /Ana Rivera/ }))
    await user.click(screen.getByRole('button', { name: 'Enviar aviso' }))
    expect(await screen.findByText('Escriba el título del aviso.')).toBeInTheDocument()
    expect(posted()).toHaveLength(0)
    mock.postStatus = 400
    await user.type(screen.getByLabelText('Título'), 'Hola')
    await user.click(screen.getByRole('button', { name: 'Enviar aviso' }))
    expect(await screen.findByText('No hay personas a quienes enviar el aviso.')).toBeInTheDocument()
    expect(screen.getByLabelText('Título')).toHaveValue('Hola')
  })

  it('«a todas las personas» pide confirmación con la cantidad y manda all', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('switch', { name: 'Enviar a todas las personas de la compañía' }))
    expect(await screen.findByText('Se enviará a las 16 personas activas (menos a usted).')).toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Para' })).not.toBeInTheDocument()
    await user.type(screen.getByLabelText('Título'), 'Para todos')
    await user.click(screen.getByRole('button', { name: 'Enviar aviso' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/El aviso le llegará a las 16 personas activas/)).toBeInTheDocument()
    expect(posted()).toHaveLength(0)
    await user.click(within(dialog).getByRole('button', { name: 'Enviar aviso' }))
    await waitFor(() => expect(posted()).toHaveLength(1))
    expect(posted()[0].body).toMatchObject({ all: true, title: 'Para todos' })
    expect(posted()[0].body).not.toHaveProperty('userIds')
  })

  it('el historial dice a quién se envió y cuántas personas lo leyeron', async () => {
    wrap()
    const row = (await screen.findByText('Cierre mañana')).closest('tr') as HTMLElement
    expect(within(row).getByText('Gerencia, Luis Casado')).toBeInTheDocument()
    expect(within(row).getByText('2 de 5')).toBeInTheDocument()
    expect(within(row).getByText('Ana Rivera')).toBeInTheDocument()
  })
})
