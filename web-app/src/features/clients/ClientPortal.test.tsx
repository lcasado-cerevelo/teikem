// F-A3 — «Usuarios de portal» de la ficha del cliente: permisos y módulo, lista con estatus, invitar (validación y errores exactos del
// servidor), reenviar, suspender, reactivar, dar de baja y volver a invitar. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { dismissToast, getToast } from '../../kernel/ui/toastStore'
import { ClientPortalPanel } from './ClientPortalPanel'
import type { ClientDetail } from './clientRules'
import type { PortalUser } from './portalRules'

interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | undefined
  headers: Headers
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], override: null as null | ((call: Call) => unknown) }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined, headers: req.headers }
    mock.calls.push(call)
    const body = mock.override?.(call) ?? route(call)
    if (body instanceof Response) return body
    if (body === null) return new Response(null, { status: 204 })
    return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const CLIENT = { id: 10, publicId: 'c-1', code: 'ACME', name: 'Acme Corp', status: 'ACTIVE', isActive: true } as unknown as ClientDetail
const INACTIVE = { ...CLIENT, isActive: false } as ClientDetail

const row = (id: number, email: string, status: string, statusLabel: string, extra: Partial<PortalUser> = {}): PortalUser => ({
  id,
  clientPublicId: 'c-1',
  email,
  fullName: `Persona ${id}`,
  role: 'CLIENT_OPERATOR',
  roleLabel: 'Operador',
  status,
  statusLabel,
  lastLoginUtc: null,
  isActive: true,
  hasUser: true,
  hasPassword: status !== 'INVITED',
  clientsCount: 1,
  ...extra,
})
let users: PortalUser[] = []

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/clients/c-1/portal-users' && method === 'GET') return users
  if (p === '/api/v1/clients/c-1/portal-users/invite')
    return { user: row(9, 'nuevo@acme.com', 'INVITED', 'Invitado'), inviteToken: 'SECRETO-NO-SE-MUESTRA', expiresAtUtc: '2026-10-14T19:51:00Z' }
  if (/\/portal-users\/\d+\/resend-invite$/.test(p)) return { user: row(1, 'ana@acme.com', 'INVITED', 'Invitado'), inviteToken: 'OTRO-SECRETO', expiresAtUtc: '2026-10-14T19:51:00Z' }
  if (/\/portal-users\/\d+\/(suspend|reactivate|remove)$/.test(p)) return null
  if (p === '/api/v1/catalogs/PortalRole')
    return [
      { code: 'CLIENT_ADMIN', label: 'Admin cliente', isEnabled: true, sortOrder: 1 },
      { code: 'CLIENT_OPERATOR', label: 'Operador', isEnabled: true, sortOrder: 2 },
      { code: 'CLIENT_READONLY', label: 'Solo lectura', isEnabled: true, sortOrder: 3 },
      { code: 'CONSIGNEE', label: 'Consignatario', isEnabled: true, sortOrder: 4 },   // Lote 117: lo da el cliente desde su portal, no el expediente
    ]
  if (p === '/api/v1/status/PortalUserStatus')
    return [
      { code: 'INVITED', label: 'Invitado', stageKind: 'PIPELINE', isInitial: true, isEnabled: true, sortOrder: 1 },
      { code: 'ACTIVE', label: 'Activo', stageKind: 'PIPELINE', isInitial: false, isEnabled: true, sortOrder: 2 },
      { code: 'SUSPENDED', label: 'Suspendido', stageKind: 'LATERAL', isInitial: false, isEnabled: true, sortOrder: 3 },
      { code: 'DISABLED', label: 'Inhabilitado', stageKind: 'TERMINAL', isInitial: false, isEnabled: true, sortOrder: 4 },
    ]
  return problem(404, 'No encontrado', 'not_found')
}

function wrap(permissions: string[] = ['clients.read', 'portalusers.manage'], modules: string[] = ['CATALOG', 'CLIENT_PORTAL'], client: ClientDetail = CLIENT) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={modules}>
          <ClientPortalPanel client={client} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)
const rowOf = (email: string) => screen.getByText(email).closest('tr') as HTMLElement

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.override = null
  users = [
    row(1, 'ana@acme.com', 'INVITED', 'Invitado'),
    row(2, 'beto@acme.com', 'ACTIVE', 'Activo', { lastLoginUtc: '2026-10-10T15:00:00Z' }),
    row(3, 'carla@acme.com', 'SUSPENDED', 'Suspendido'),
    row(4, 'dario@acme.com', 'DISABLED', 'Inhabilitado'),
  ]
})
afterEach(() => dismissToast())

describe('Usuarios de portal — permisos y módulo', () => {
  it('sin portalusers.manage no se pinta ni se pide nada', async () => {
    wrap(['clients.read', 'contracts.read'])
    await new Promise((r) => setTimeout(r, 30))
    expect(screen.queryByText('Usuarios de portal')).toBeNull()
    expect(mock.calls).toHaveLength(0)
  })

  it('con el módulo CLIENT_PORTAL apagado no se pinta ni se pide nada', async () => {
    wrap(['portalusers.manage'], ['CATALOG'])
    await new Promise((r) => setTimeout(r, 30))
    expect(screen.queryByText('Usuarios de portal')).toBeNull()
    expect(mock.calls).toHaveLength(0)
  })

  it('con el permiso y el módulo muestra el panel con su contador', async () => {
    wrap()
    expect(await screen.findByRole('heading', { name: 'Usuarios de portal' })).toBeInTheDocument()
    expect(await screen.findByText('ana@acme.com')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/clients/c-1/portal-users')).toHaveLength(1)
  })
})

describe('Usuarios de portal — lista y acciones por estatus', () => {
  it('muestra el estatus del servidor y solo las acciones que admite cada uno', async () => {
    wrap()
    await screen.findByText('ana@acme.com')
    const actions = (email: string) =>
      within(rowOf(email))
        .getAllByRole('button')
        .map((b) => b.textContent)
    expect(within(rowOf('ana@acme.com')).getByText('Invitado')).toBeInTheDocument()
    expect(actions('ana@acme.com')).toEqual(['Reenviar invitación', 'Dar de baja'])
    expect(actions('beto@acme.com')).toEqual(['Suspender', 'Dar de baja'])
    expect(actions('carla@acme.com')).toEqual(['Reactivar', 'Dar de baja'])
    expect(actions('dario@acme.com')).toEqual(['Volver a invitar'])
  })

  it('sin usuarios avisa y con un error de lectura muestra el título del servidor', async () => {
    users = []
    const { unmount } = wrap()
    expect(await screen.findByText('Este cliente todavía no tiene usuarios de portal.')).toBeInTheDocument()
    unmount()
    mock.override = (c) => (c.method === 'GET' && c.url.pathname.endsWith('/portal-users') ? problem(403, "El módulo 'CLIENT_PORTAL' no está habilitado para esta compañía.", 'module_disabled') : undefined)
    wrap()
    expect(await screen.findByText("El módulo 'CLIENT_PORTAL' no está habilitado para esta compañía.")).toBeInTheDocument()
  })

  it('con el cliente de baja avisa, no deja invitar y no ofrece reenviar ni volver a invitar', async () => {
    wrap(undefined, undefined, INACTIVE)
    await screen.findByText('ana@acme.com')
    expect(screen.getByText('El cliente está dado de baja; solo se consulta su historial.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Invitar usuario' })).toBeDisabled()
    expect(within(rowOf('ana@acme.com')).queryByRole('button', { name: 'Reenviar invitación' })).toBeNull()
    expect(within(rowOf('dario@acme.com')).queryByRole('button', { name: 'Volver a invitar' })).toBeNull()
    expect(within(rowOf('beto@acme.com')).getByRole('button', { name: 'Suspender' })).toBeInTheDocument()
  })
})

describe('Usuarios de portal — invitar', () => {
  it('valida en pantalla: correo, correo válido y rol (sin llamar al servidor)', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('ana@acme.com')
    await user.click(screen.getByRole('button', { name: 'Invitar usuario' }))
    const dialog = await screen.findByRole('dialog', { name: 'Invitar a una persona del cliente' })
    await user.click(within(dialog).getByRole('button', { name: 'Enviar invitación' }))
    expect(await within(dialog).findByText('El correo es obligatorio.')).toBeInTheDocument()
    expect(within(dialog).getByText('El rol de portal es obligatorio.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Correo/), 'sin-arroba')
    await user.click(within(dialog).getByRole('button', { name: 'Enviar invitación' }))
    expect(await within(dialog).findByText('Correo inválido.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/clients/c-1/portal-users/invite')).toHaveLength(0)
  })

  it('el rol Consignatario no se ofrece al invitar desde el expediente (lo invita el cliente desde su portal)', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('ana@acme.com')
    await user.click(screen.getByRole('button', { name: 'Invitar usuario' }))
    const dialog = await screen.findByRole('dialog', { name: 'Invitar a una persona del cliente' })
    const role = within(dialog).getByLabelText(/^Rol/)
    await within(dialog).findByRole('option', { name: 'Operador' })
    expect(within(role).queryByRole('option', { name: 'Consignatario' })).toBeNull()
    expect(within(role).getAllByRole('option').map((o) => o.textContent)).toEqual(expect.arrayContaining(['Admin cliente', 'Operador', 'Solo lectura']))
  })

  it('envía correo, nombre y rol; sin Idempotency-Key; avisa cuándo vence el enlace y NUNCA muestra el token', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('ana@acme.com')
    await user.click(screen.getByRole('button', { name: 'Invitar usuario' }))
    const dialog = await screen.findByRole('dialog', { name: 'Invitar a una persona del cliente' })
    await user.type(within(dialog).getByLabelText(/^Correo/), '  Nuevo@Acme.com ')
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Nora Nuevo')
    await user.selectOptions(within(dialog).getByLabelText(/^Rol/), 'CLIENT_READONLY')
    await user.click(within(dialog).getByRole('button', { name: 'Enviar invitación' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/portal-users/invite')).toHaveLength(1))
    const call = callsTo('POST', '/api/v1/clients/c-1/portal-users/invite')[0]
    expect(call.body).toEqual({ email: 'nuevo@acme.com', fullName: 'Nora Nuevo', role: 'CLIENT_READONLY' })
    expect(call.headers.get('Idempotency-Key')).toBeNull()
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(getToast()?.message).toMatch(/^Invitación enviada a nuevo@acme\.com\. El enlace vence el /)
    expect(document.body.textContent).not.toContain('SECRETO')
    // la lista se vuelve a pedir
    await waitFor(() => expect(callsTo('GET', '/api/v1/clients/c-1/portal-users').length).toBeGreaterThan(1))
  })

  it('409 de correo no disponible: el mensaje exacto del servidor queda en el diálogo y no se cierra', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('ana@acme.com')
    mock.override = (c) => (c.url.pathname.endsWith('/invite') ? problem(409, 'Ese correo no está disponible para el portal de esta compañía.', 'conflict') : undefined)
    await user.click(screen.getByRole('button', { name: 'Invitar usuario' }))
    const dialog = await screen.findByRole('dialog', { name: 'Invitar a una persona del cliente' })
    await user.type(within(dialog).getByLabelText(/^Correo/), 'ana@acme.com')
    await user.selectOptions(within(dialog).getByLabelText(/^Rol/), 'CLIENT_ADMIN')
    await user.click(within(dialog).getByRole('button', { name: 'Enviar invitación' }))
    expect(await within(dialog).findByText('Ese correo no está disponible para el portal de esta compañía.')).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('409 de cliente dado de baja (otra sesión lo dio de baja): mensaje exacto en el diálogo', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('ana@acme.com')
    mock.override = (c) => (c.url.pathname.endsWith('/invite') ? problem(409, 'El cliente está dado de baja; solo se consulta su historial.', 'conflict') : undefined)
    await user.click(screen.getByRole('button', { name: 'Invitar usuario' }))
    const dialog = await screen.findByRole('dialog', { name: 'Invitar a una persona del cliente' })
    await user.type(within(dialog).getByLabelText(/^Correo/), 'otro@acme.com')
    await user.selectOptions(within(dialog).getByLabelText(/^Rol/), 'CLIENT_ADMIN')
    await user.click(within(dialog).getByRole('button', { name: 'Enviar invitación' }))
    expect(await within(dialog).findByText('El cliente está dado de baja; solo se consulta su historial.')).toBeInTheDocument()
  })

  it('400 por campo del servidor sale bajo el campo (correo inválido y rol)', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('ana@acme.com')
    mock.override = (c) => (c.url.pathname.endsWith('/invite') ? problem(400, 'Correo inválido.', 'validation', { email: ['Correo inválido.'] }) : undefined)
    await user.click(screen.getByRole('button', { name: 'Invitar usuario' }))
    const dialog = await screen.findByRole('dialog', { name: 'Invitar a una persona del cliente' })
    await user.type(within(dialog).getByLabelText(/^Correo/), 'a@b.c')
    await user.selectOptions(within(dialog).getByLabelText(/^Rol/), 'CLIENT_ADMIN')
    await user.click(within(dialog).getByRole('button', { name: 'Enviar invitación' }))
    expect(await within(dialog).findByText('Correo inválido.')).toBeInTheDocument()
  })

  it('una cuenta que ya existía y estaba activa queda activa sin enlace: el aviso lo dice', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('ana@acme.com')
    mock.override = (c) => (c.url.pathname.endsWith('/invite') ? { user: row(9, 'viejo@acme.com', 'ACTIVE', 'Activo'), inviteToken: null, expiresAtUtc: null } : undefined)
    await user.click(screen.getByRole('button', { name: 'Invitar usuario' }))
    const dialog = await screen.findByRole('dialog', { name: 'Invitar a una persona del cliente' })
    await user.type(within(dialog).getByLabelText(/^Correo/), 'viejo@acme.com')
    await user.selectOptions(within(dialog).getByLabelText(/^Rol/), 'CLIENT_ADMIN')
    await user.click(within(dialog).getByRole('button', { name: 'Enviar invitación' }))
    await waitFor(() => expect(getToast()?.message).toBe('viejo@acme.com ya tenía una cuenta de portal activa: quedó activo en este cliente sin enviar invitación.'))
  })

  it('«Volver a invitar» a alguien dado de baja precarga correo, nombre y rol (el correo no se edita)', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('dario@acme.com')
    await user.click(within(rowOf('dario@acme.com')).getByRole('button', { name: 'Volver a invitar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Volver a invitar' })
    const mail = within(dialog).getByLabelText(/^Correo/) as HTMLInputElement
    expect(mail.value).toBe('dario@acme.com')
    expect(mail.readOnly).toBe(true)
    expect((within(dialog).getByLabelText(/^Nombre/) as HTMLInputElement).value).toBe('Persona 4')
    expect((within(dialog).getByLabelText(/^Rol/) as HTMLSelectElement).value).toBe('CLIENT_OPERATOR')
    await user.click(within(dialog).getByRole('button', { name: 'Enviar invitación' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/portal-users/invite')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/clients/c-1/portal-users/invite')[0].body).toEqual({ email: 'dario@acme.com', fullName: 'Persona 4', role: 'CLIENT_OPERATOR' })
  })
})

describe('Usuarios de portal — reenviar, suspender, reactivar y dar de baja', () => {
  it('reenviar pide confirmación, no lleva Idempotency-Key y avisa el vencimiento sin mostrar el token', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('ana@acme.com')
    await user.click(within(rowOf('ana@acme.com')).getByRole('button', { name: 'Reenviar invitación' }))
    const dialog = await screen.findByRole('dialog', { name: 'Reenviar la invitación' })
    expect(within(dialog).getByText('Se enviará un enlace nuevo a ana@acme.com. El enlace anterior deja de valer.')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Reenviar invitación' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/portal-users/1/resend-invite')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/clients/c-1/portal-users/1/resend-invite')[0].headers.get('Idempotency-Key')).toBeNull()
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(getToast()?.message).toMatch(/^Invitación enviada a ana@acme\.com\. El enlace vence el /)
    expect(document.body.textContent).not.toContain('SECRETO')
  })

  it('reenviar cuando ya no está INVITED: el 409 exacto del servidor queda en el diálogo', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('ana@acme.com')
    mock.override = (c) =>
      c.url.pathname.endsWith('/resend-invite') ? problem(409, 'Solo se puede reenviar la invitación a un usuario que todavía no la ha aceptado (estatus INVITED).', 'conflict') : undefined
    await user.click(within(rowOf('ana@acme.com')).getByRole('button', { name: 'Reenviar invitación' }))
    const dialog = await screen.findByRole('dialog', { name: 'Reenviar la invitación' })
    await user.click(within(dialog).getByRole('button', { name: 'Reenviar invitación' }))
    expect(await within(dialog).findByText('Solo se puede reenviar la invitación a un usuario que todavía no la ha aceptado (estatus INVITED).')).toBeInTheDocument()
  })

  it('suspender y reactivar llevan una Idempotency-Key distinta por intento y avisan', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('beto@acme.com')
    await user.click(within(rowOf('beto@acme.com')).getByRole('button', { name: 'Suspender' }))
    let dialog = await screen.findByRole('dialog', { name: 'Suspender el acceso' })
    await user.click(within(dialog).getByRole('button', { name: 'Suspender' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/portal-users/2/suspend')).toHaveLength(1))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(getToast()?.message).toBe('Acceso suspendido.')
    await user.click(within(rowOf('carla@acme.com')).getByRole('button', { name: 'Reactivar' }))
    dialog = await screen.findByRole('dialog', { name: 'Reactivar el acceso' })
    await user.click(within(dialog).getByRole('button', { name: 'Reactivar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/portal-users/3/reactivate')).toHaveLength(1))
    const k1 = callsTo('POST', '/api/v1/clients/c-1/portal-users/2/suspend')[0].headers.get('Idempotency-Key')
    const k2 = callsTo('POST', '/api/v1/clients/c-1/portal-users/3/reactivate')[0].headers.get('Idempotency-Key')
    expect(k1).toBeTruthy()
    expect(k2).toBeTruthy()
    expect(k1).not.toBe(k2)
  })

  it('suspender a quien ya no está ACTIVE: el 409 exacto queda en el diálogo', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('beto@acme.com')
    mock.override = (c) => (c.url.pathname.endsWith('/suspend') ? problem(409, 'Solo se puede suspender un usuario de portal en estatus ACTIVE.', 'conflict') : undefined)
    await user.click(within(rowOf('beto@acme.com')).getByRole('button', { name: 'Suspender' }))
    const dialog = await screen.findByRole('dialog', { name: 'Suspender el acceso' })
    await user.click(within(dialog).getByRole('button', { name: 'Suspender' }))
    expect(await within(dialog).findByText('Solo se puede suspender un usuario de portal en estatus ACTIVE.')).toBeInTheDocument()
  })

  it('dar de baja confirma con tono de peligro y manda POST …/remove', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('carla@acme.com')
    await user.click(within(rowOf('carla@acme.com')).getByRole('button', { name: 'Dar de baja' }))
    const dialog = await screen.findByRole('dialog', { name: 'Dar de baja al usuario' })
    expect(within(dialog).getByText(/Su historial se conserva y se le puede volver a invitar\./)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Dar de baja' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/portal-users/3/remove')).toHaveLength(1))
    await waitFor(() => expect(getToast()?.message).toBe('Usuario dado de baja.'))
  })

  it('dar de baja a uno ya inhabilitado por otra sesión: 404/409 del servidor tal cual', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('carla@acme.com')
    mock.override = (c) => (c.url.pathname.endsWith('/remove') ? problem(409, 'El usuario de portal ya está dado de baja.', 'conflict') : undefined)
    await user.click(within(rowOf('carla@acme.com')).getByRole('button', { name: 'Dar de baja' }))
    const dialog = await screen.findByRole('dialog', { name: 'Dar de baja al usuario' })
    await user.click(within(dialog).getByRole('button', { name: 'Dar de baja' }))
    expect(await within(dialog).findByText('El usuario de portal ya está dado de baja.')).toBeInTheDocument()
  })
})
