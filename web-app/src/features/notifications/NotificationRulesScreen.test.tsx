// Pruebas de «Reglas de aviso» (Lote 52) sobre un fetch simulado: la lista, el interruptor, el alta con destinatarios (el buscador de
// personas lista nombre y correo), los canales sin configurar apagados y con su explicación, «Enviar prueba», los mensajes del servidor y el registro.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import NotificationRulesScreen from './NotificationRulesScreen'

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
    if (req.method === 'GET' && p === '/api/v1/notifications/events') return json(EVENTS)
    if (req.method === 'GET' && p === '/api/v1/notifications/channels') return json(CHANNELS)
    if (req.method === 'GET' && p === '/api/v1/notifications/rules') return json(RULES)
    if (req.method === 'GET' && p === '/api/v1/roles') return json(ROLES)
    if (req.method === 'GET' && p === '/api/v1/users') return json(USERS)
    if (req.method === 'POST' && p === '/api/v1/notifications/rules') {
      if (mock.postStatus !== 200) return new Response(JSON.stringify({ title: 'Ya existe una regla con ese nombre.' }), { status: mock.postStatus, headers: { 'Content-Type': 'application/problem+json' } })
      return json({ ...RULES[0], publicId: 'nueva', name: body.name })
    }
    if (req.method === 'PATCH' && p.startsWith('/api/v1/notifications/rules/')) return json({ ...RULES[0], ...body })
    if (req.method === 'POST' && p.endsWith('/test')) return json({ channel: body.channel, status: 'SENT', message: null })
    if (req.method === 'POST' && p === '/api/v1/notifications/test-event') return json({ queued: true })
    if (req.method === 'GET' && p === '/api/v1/notifications/log') return json({ items: LOG, total: 1, skip: 0, take: 25 })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const EVENTS = [
  {
    code: 'TEST', moduleKey: null, name: 'Aviso de prueba', nameEn: 'Test notification', isOptional: true,
    variables: [{ name: 'message', label: 'Mensaje', labelEn: 'Message', sample: 'Esto es una prueba.' }, { name: 'user', label: 'Quién lo disparó', labelEn: 'Who', sample: 'Administrador' }],
    defaultSubject: 'Aviso de prueba de Teikem', defaultSubjectEn: 'Teikem test', defaultBody: '{message} (por {user})', defaultBodyEn: '{message} (by {user})',
  },
]
const CHANNELS = [{ channel: 'EMAIL', isConfigured: false }, { channel: 'SMS', isConfigured: false }, { channel: 'APP', isConfigured: true }, { channel: 'PUSH', isConfigured: false }]
const channelRow = (channel: string, on: boolean) => ({ channel, isEnabled: on, subjectEs: 'Aviso de prueba de Teikem', subjectEn: 'Teikem test', bodyEs: '{message} (por {user})', bodyEn: '{message} (by {user})', isConfigured: channel === 'APP' })
const RULES = [
  {
    publicId: 'r1', eventCode: 'TEST', name: 'Avisar a gerencia', isActive: true, rowVersion: 'AAA=',
    channels: ['EMAIL', 'SMS', 'APP', 'PUSH'].map((c) => channelRow(c, c === 'APP')),
    recipients: [{ kind: 'ROLE', roleId: 5, userId: null, value: null, label: 'Gerencia' }, { kind: 'EMAIL', roleId: null, userId: null, value: 'jefe@x.com', label: 'jefe@x.com' }],
  },
  { publicId: 'r2', eventCode: 'TEST', name: 'Regla apagada', isActive: false, rowVersion: 'BBB=', channels: [], recipients: [] },
]
const ROLES = [{ id: 5, name: 'Gerencia', isActive: true, isTemplate: false }, { id: 6, name: 'Operadores', isActive: true, isTemplate: false }]
const USERS = [
  { id: 1, fullName: 'Ana Rivera', email: 'ana@advance.com', isActive: true },
  { id: 2, fullName: 'Luis Casado', email: 'lcasadopr@gmail.com', isActive: true },
]
const LOG = [{ id: 9, outboxId: 3, rulePublicId: 'r1', ruleName: 'Avisar a gerencia', eventCode: 'TEST', channel: 'SMS', recipient: '787-555-0101', status: 'SKIPPED', error: 'El canal SMS todavía no está configurado en Teikem.', attempts: 1, createdAtUtc: '2026-10-14T12:00:00Z', sentAtUtc: null }]

const SESSION = {
  me: { userId: 1, tenantName: 'Advance Logistics' },
  isAuthenticated: true,
  isLoading: false,
  error: null,
  tenantId: 7,
  lang: 'es',
  setLang: () => {},
  logout: async () => {},
  switchTenant: async () => {},
  permissions: new Set(['notifications.manage', 'admin.users']),
  modules: new Set(['SYSTEM']),
  reloadMe: async () => {},
} as unknown as Session

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={['notifications.manage', 'admin.users']} modules={['SYSTEM']}>
          <MemoryRouter>
            <NotificationRulesScreen />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const rowOf = async (name: string) => (await screen.findByText(name)).closest('tr') as HTMLElement

describe('NotificationRulesScreen', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.postStatus = 200
  })

  it('lista las reglas con su evento, canales encendidos y destinatarios', async () => {
    wrap()
    expect(screen.getByRole('heading', { level: 1, name: 'Reglas de aviso' })).toBeInTheDocument()
    const row = await rowOf('Avisar a gerencia')
    expect(within(row).getByText('Aviso de prueba')).toBeInTheDocument()
    expect(within(row).getByText('Campanita')).toBeInTheDocument()
    expect(within(row).getByText('2 destinatarios')).toBeInTheDocument()
    expect(within(row).getByRole('switch')).toBeChecked()
    expect(within(await rowOf('Regla apagada')).getByRole('switch')).not.toBeChecked()
  })

  it('el interruptor activa o apaga con el rowVersion', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(within(await rowOf('Regla apagada')).getByRole('switch'))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PATCH')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PATCH')).toMatchObject({ path: '/api/v1/notifications/rules/r2', body: { isActive: true, rowVersion: 'BBB=' } })
  })

  it('alta: el buscador de personas lista nombre y correo, y se manda la regla completa', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Nueva regla' }))
    const dialog = await screen.findByRole('dialog')
    // sin nombre ni evento
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Escriba el nombre de la regla.')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique el evento de la regla.')).toBeInTheDocument()
    expect(mock.requests.some((r) => r.method === 'POST')).toBe(false)

    await user.selectOptions(within(dialog).getByLabelText('Evento'), 'TEST')
    await user.type(within(dialog).getByLabelText('Nombre de la regla'), '  Mi regla nueva ')
    // destinatario usuario con buscador
    await user.selectOptions(within(dialog).getByLabelText('Tipo'), 'USER')
    await user.type(within(dialog).getByRole('combobox', { name: 'Escoja un usuario…' }), 'ana')
    const option = await within(await within(dialog).findByRole('listbox')).findByRole('option')
    expect(option).toHaveTextContent('Ana Rivera')
    expect(option).toHaveTextContent('ana@advance.com')
    await user.click(option)
    expect(within(dialog).getByText('Ana Rivera (ana@advance.com)')).toBeInTheDocument()
    // un rol
    await user.selectOptions(within(dialog).getByLabelText('Tipo'), 'ROLE')
    await user.selectOptions(within(dialog).getByLabelText('Rol'), '6')
    // correo inválido y válido
    await user.selectOptions(within(dialog).getByLabelText('Tipo'), 'EMAIL')
    await user.type(within(dialog).getByPlaceholderText('nombre@empresa.com'), 'x@')
    await user.click(within(dialog).getByRole('button', { name: 'Agregar' }))
    expect(within(dialog).getByText('Correo no válido: «x@».')).toBeInTheDocument()
    await user.clear(within(dialog).getByPlaceholderText('nombre@empresa.com'))
    await user.type(within(dialog).getByPlaceholderText('nombre@empresa.com'), 'jefe@x.com')
    await user.click(within(dialog).getByRole('button', { name: 'Agregar' }))
    // la campanita (la única configurada) se enciende
    await user.click(within(dialog).getByRole('switch', { name: 'Enviar por Campanita' }))
    await user.click(within(dialog).getByText('Avisar cuando pase el evento'))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/notifications/rules')).toBe(true))
    const body = mock.requests.find((r) => r.method === 'POST' && r.path === '/api/v1/notifications/rules')!.body as Record<string, unknown>
    expect(body).toMatchObject({
      eventCode: 'TEST',
      name: 'Mi regla nueva',
      isActive: true,
      recipients: [{ kind: 'USER', userId: 1 }, { kind: 'ROLE', roleId: 6 }, { kind: 'EMAIL', value: 'jefe@x.com' }],
    })
    expect((body.channels as { channel: string; isEnabled: boolean }[]).filter((c) => c.isEnabled).map((c) => c.channel)).toEqual(['APP'])
  })

  it('los canales sin configurar quedan apagados con su explicación', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Nueva regla' }))
    const dialog = await screen.findByRole('dialog')
    for (const name of ['Correo', 'SMS', 'Push']) {
      expect(within(dialog).getByRole('switch', { name: `Enviar por ${name}` })).toBeDisabled()
    }
    expect(within(dialog).getAllByText('Este canal todavía no está configurado en Teikem.')).toHaveLength(3)
    expect(within(dialog).getByRole('switch', { name: 'Enviar por Campanita' })).toBeEnabled()
  })

  it('editar: el evento no se cambia, se ve la plantilla con las variables y «Enviar prueba» pide la prueba', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(within(await rowOf('Avisar a gerencia')).getByRole('button', { name: 'Editar regla' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByLabelText('Evento')).toBeDisabled()
    expect(within(dialog).getByText('Gerencia')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Insertar {message}' })).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Texto (español)')).toHaveValue('{message} (por {user})')
    await user.click(within(dialog).getByRole('button', { name: 'Enviar prueba' }))
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/notifications/rules/r1/test')).toBe(true))
    expect(mock.requests.find((r) => r.path.endsWith('/test'))?.body).toEqual({ channel: 'APP' })
  })

  it('un error del servidor se muestra tal cual y la ventana sigue abierta', async () => {
    const user = userEvent.setup()
    mock.postStatus = 409
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Nueva regla' }))
    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText('Evento'), 'TEST')
    await user.type(within(dialog).getByLabelText('Nombre de la regla'), 'Repetida')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Ya existe una regla con ese nombre.')).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('el registro lista los envíos y filtra por canal y estado', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Registro' }))
    expect(await screen.findByText('El canal SMS todavía no está configurado en Teikem.')).toBeInTheDocument()
    expect(within(screen.getByRole('table')).getByText('Omitido')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Canal'), 'SMS')
    await user.selectOptions(screen.getByLabelText('Estado'), 'SKIPPED')
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/notifications/log' && r.search.includes('channel=SMS') && r.search.includes('status=SKIPPED'))).toBe(true))
  })

  it('«Disparar aviso de prueba» pone el evento de prueba en la cola', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Registro' }))
    await user.click(await screen.findByRole('button', { name: 'Disparar aviso de prueba' }))
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/notifications/test-event')).toBe(true))
  })
})
