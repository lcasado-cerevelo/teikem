// Lote 121 — pestañas Clientes y Consignatarios de «Roles y usuarios»: todas las personas de portal de la compañía con su tipo, la búsqueda, las acciones (suspender, reactivar, dar de baja, reenviar) con el cliente de cada fila y
// las pestañas solo con el permiso portalusers.manage y el módulo CLIENT_PORTAL. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { dismissToast, getToast } from '../../kernel/ui/toastStore'
import { PortalUsersTab } from './PortalUsersTab'
import UsersPage from './UsersPage'

interface Call {
  method: string
  url: URL
}
const mock = vi.hoisted(() => ({ calls: [] as Call[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const call = { method: req.method, url: new URL(req.url) }
    mock.calls.push(call)
    const body = route(call)
    if (body === null) return new Response(null, { status: 204 })
    return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const P = (id: number, email: string, status: string, statusLabel: string, over: Record<string, unknown> = {}) => ({
  id, clientPublicId: 'c-1', clientName: 'Acme Corp', clientCode: 'ACME', kind: 'CLIENT', consigneeName: null, email, fullName: `Persona ${id}`, role: 'CLIENT_OPERATOR', roleLabel: 'Operador', status, statusLabel, lastLoginUtc: null, isActive: true, ...over,
})
const CLIENTS = [P(1, 'ana@acme.co', 'ACTIVE', 'Activo'), P(2, 'inv@acme.co', 'INVITED', 'Invitado'), P(3, 'sam@beta.co', 'SUSPENDED', 'Suspendido', { clientPublicId: 'c-2', clientName: 'Beta SRL', clientCode: 'BETA' })]
const CONSIGNEES = [P(7, 'tienda@acme.co', 'ACTIVE', 'Activo', { kind: 'CONSIGNEE', consigneeName: 'Colmado Sur', role: 'CONSIGNEE', roleLabel: 'Consignatario' })]

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/portal-users' && method === 'GET') return url.searchParams.get('kind') === 'CONSIGNEE' ? CONSIGNEES : CLIENTS
  if (/\/portal-users\/\d+\/(suspend|reactivate|remove|resend-invite)$/.test(p)) return method === 'POST' && p.endsWith('resend-invite') ? { user: {}, inviteToken: null, expiresAtUtc: null } : null
  return []
}
const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
})
afterEach(() => {
  const t = getToast()
  if (t) dismissToast()
})

function wrap(ui: React.ReactElement, permissions = ['portalusers.manage', 'admin.users', 'admin.roles'], modules = ['CLIENT_PORTAL', 'SYSTEM']) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={modules}>
          {ui}
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

describe('PortalUsersTab — Clientes', () => {
  it('lista las personas de TODOS los clientes con su tipo y estatus, y busca por cliente', async () => {
    wrap(<PortalUsersTab kind="CLIENT" />)
    expect(await screen.findByText('Persona 1')).toBeInTheDocument()
    expect(screen.getByText('Persona 3')).toBeInTheDocument()                          // de otro cliente de la misma compañía
    expect(screen.getByText('Beta SRL · BETA')).toBeInTheDocument()
    expect(screen.getAllByText('Cliente').length).toBeGreaterThan(1)
    expect(callsTo('GET', '/api/v1/portal-users')[0].url.searchParams.get('kind')).toBe('CLIENT')
    await userEvent.type(screen.getByRole('searchbox'), 'beta')
    expect(screen.queryByText('Persona 1')).toBeNull()
    expect(screen.getByText('Persona 3')).toBeInTheDocument()
  })

  it('cada acción usa el cliente de SU fila: suspender pide confirmar, reactivar y reenviar van directo', async () => {
    wrap(<PortalUsersTab kind="CLIENT" />)
    await screen.findByText('Persona 1')
    const rowOf = (name: string) => within(screen.getByText(name).closest('tr') as HTMLElement)
    await userEvent.click(rowOf('Persona 1').getByRole('button', { name: 'Suspender' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/no podrá entrar al portal/)).toBeInTheDocument()
    await userEvent.click(within(dialog).getByRole('button', { name: 'Suspender' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/portal-users/1/suspend')).toHaveLength(1))

    await userEvent.click(rowOf('Persona 3').getByRole('button', { name: 'Reactivar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-2/portal-users/3/reactivate')).toHaveLength(1))   // del cliente de esa fila

    await userEvent.click(rowOf('Persona 2').getByRole('button', { name: 'Reenviar invitación' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/portal-users/2/resend-invite')).toHaveLength(1))
  })
})

describe('PortalUsersTab — Consignatarios', () => {
  it('lista los accesos de consignatarios con su consignatario y su tipo', async () => {
    wrap(<PortalUsersTab kind="CONSIGNEE" />)
    expect(await screen.findByText('Persona 7')).toBeInTheDocument()
    expect(screen.getByText('Colmado Sur')).toBeInTheDocument()
    expect(screen.getAllByText('Consignatario').length).toBeGreaterThan(1)
    expect(callsTo('GET', '/api/v1/portal-users')[0].url.searchParams.get('kind')).toBe('CONSIGNEE')
  })
})

describe('UsersPage — pestañas', () => {
  it('con portalusers.manage y el módulo CLIENT_PORTAL aparecen Clientes y Consignatarios junto a Roles y Usuarios', async () => {
    wrap(<UsersPage />)
    const tabs = await screen.findAllByRole('tab')
    expect(tabs.map((t) => t.textContent?.trim())).toEqual(['Roles', 'Usuarios', 'Clientes', 'Consignatarios'])
    await userEvent.click(screen.getByRole('tab', { name: 'Consignatarios' }))
    expect(await screen.findByText('Persona 7')).toBeInTheDocument()
  })

  it('sin el permiso o sin el módulo esas pestañas no se ofrecen', async () => {
    const first = wrap(<UsersPage />, ['admin.users', 'admin.roles'])
    expect((await screen.findAllByRole('tab')).map((t) => t.textContent?.trim())).toEqual(['Roles', 'Usuarios'])
    first.unmount()
    wrap(<UsersPage />, ['portalusers.manage', 'admin.users', 'admin.roles'], ['SYSTEM'])
    expect((await screen.findAllByRole('tab')).map((t) => t.textContent?.trim())).toEqual(['Roles', 'Usuarios'])
  })
})
