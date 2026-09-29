// Pruebas de Roles y usuarios (P4): el segmento se filtra por permisos, los permisos del rol se agrupan por
// categoría, el PUT de un rol pide reautenticación (AAL2, modal real de `ReauthProvider`), el switch de estado
// revierte si el `PUT` falla, la columna PIN solo aparece con módulo + permiso, y el modal de PIN valida la
// coincidencia en cliente y muestra el mensaje del API.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { ReauthProvider } from '../../kernel/auth/ReauthProvider'
import { ReauthContext } from '../../kernel/auth/reauthContext'
import { setTokens, clearTokens } from '../../kernel/auth/tokens'
import { setLang } from '../../kernel/i18n/i18n'
import { PinModal } from './PinModal'
import { RolesTab } from './RolesTab'
import UsersPage from './UsersPage'
import { UsersTab } from './UsersTab'
import { SessionContext, type MeDto, type Session } from '../../app/session'
import type { PermissionDto, RoleDto, UserSummaryDto } from './api'

// Cliente de la app sobre un fetch simulado (misma política que el real): igual patrón que account/account.test.tsx.
type Handler = (method: string, url: URL, body: unknown) => Response | unknown
const mock = vi.hoisted(() => ({ handler: null as unknown, calls: [] as { method: string; path: string; body: unknown }[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.text()
    const body: unknown = text ? JSON.parse(text) : undefined
    mock.calls.push({ method: req.method, path: url.pathname, body })
    const result = (mock.handler as Handler)(req.method, url, body)
    if (result instanceof Response) return result
    if (result === undefined) return new Response(null, { status: 204 })
    return new Response(JSON.stringify(result), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, body: object) =>
  new Response(JSON.stringify({ status, ...body }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const PERMISSIONS: PermissionDto[] = [
  { id: 1, code: 'orders.view', category: 'ORDERS', label: 'Ver órdenes' },
  { id: 2, code: 'warehouse.receive', category: 'WAREHOUSE', label: 'Recibir' },
  { id: 3, code: 'inventory.view', category: 'WAREHOUSE', label: 'Ver inventario' },
]

const CATEGORY_LOOKUPS = [
  { code: 'ORDERS', label: 'Órdenes', description: null, sortOrder: 1, isEnabled: true },
  { code: 'WAREHOUSE', label: 'Almacén', description: null, sortOrder: 2, isEnabled: true },
]

const ROLE: RoleDto = {
  id: 5,
  name: 'Despachador',
  description: null,
  descriptions: { es: '', en: '' },
  isSystem: false,
  isTemplate: false,
  isActive: true,
  permissions: ['orders.view'],
  userCount: 0,
}

const ME: MeDto = {
  userId: 1,
  fullName: 'Ana Admin',
  email: 'admin@teikem.local',
  tenantId: 1,
  tenantName: 'Demo Logística',
  lang: 'es',
  memberships: [],
  permissions: [],
  enabledModules: [],
  mfaEnabled: false,
}

function session(me: MeDto): Session {
  return {
    me,
    isAuthenticated: true,
    isLoading: false,
    error: null,
    tenantId: me.tenantId ?? null,
    lang: 'es',
    setLang: vi.fn(),
    logout: vi.fn(async () => {}),
    switchTenant: vi.fn(async () => {}),
    permissions: new Set(),
    modules: new Set(),
    reloadMe: vi.fn(async () => {}),
  }
}

function wrap(
  ui: ReactNode,
  opts: { permissions?: string[]; modules?: string[]; reauth?: 'real' | (() => Promise<boolean>) } = {},
) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const body = (
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={opts.permissions ?? []} modules={opts.modules ?? []}>
          <SessionContext.Provider value={session(ME)}>{ui}</SessionContext.Provider>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>
  )
  if (opts.reauth === 'real') {
    return render(<ReauthProvider mfaEnabled={false}>{body}</ReauthProvider>)
  }
  return render(<ReauthContext.Provider value={{ reauth: opts.reauth ?? (async () => true) }}>{body}</ReauthContext.Provider>)
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.handler = (() => new Response(null, { status: 404 })) satisfies Handler
  setTokens({ accessToken: 'access', refreshToken: 'refresh', tenantId: 1 })
})
afterEach(() => clearTokens())

describe('UsersPage — el segmento se filtra por permisos', () => {
  beforeEach(() => {
    mock.handler = ((_method: string, url: URL) => {
      if (url.pathname === '/api/v1/roles') return []
      if (url.pathname === '/api/v1/permissions') return []
      if (url.pathname === '/api/v1/users') return []
      return undefined
    }) satisfies Handler
  })

  it('con ambos permisos: se ve el segmento Roles | Usuarios, abierto en Roles (como la maqueta)', async () => {
    wrap(<UsersPage />, { permissions: ['admin.roles', 'admin.users'] })
    expect(await screen.findByRole('tablist')).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Roles' })).toHaveAttribute('aria-selected', 'true')
  })

  it('una sola cabecera: título, segmento y el botón de alta de la pestaña activa en la misma fila', async () => {
    const user = userEvent.setup()
    wrap(<UsersPage />, { permissions: ['admin.roles', 'admin.users'] })
    const h1 = await screen.findByRole('heading', { level: 1, name: 'Roles y usuarios' })
    const head = h1.closest('.head') as HTMLElement
    expect(within(head).getByRole('tablist')).toBeInTheDocument()
    expect(within(head).getByRole('button', { name: 'Nuevo rol' })).toBeInTheDocument()
    expect(within(head).queryByRole('button', { name: 'Nuevo usuario' })).toBeNull()

    await user.click(within(head).getByRole('tab', { name: 'Usuarios' }))
    expect(within(head).getByRole('button', { name: 'Nuevo usuario' })).toBeInTheDocument()
    expect(within(head).queryByRole('button', { name: 'Nuevo rol' })).toBeNull()
    // el botón ya no se repite dentro del panel ni en una cabecera aparte
    expect(screen.getAllByRole('button', { name: 'Nuevo usuario' })).toHaveLength(1)
    expect(document.querySelectorAll('.head')).toHaveLength(1)
  })

  it('sin admin.roles: no hay segmento, se abre directo en Usuarios', async () => {
    wrap(<UsersPage />, { permissions: ['admin.users'] })
    await screen.findByText('Usuarios', { selector: '.ph2, h2, *' }).catch(() => {})
    expect(screen.queryByRole('tablist')).toBeNull()
    expect(await screen.findByPlaceholderText('Buscar por nombre o correo…')).toBeInTheDocument()
  })

  it('sin admin.users: no hay segmento, se ve Roles', async () => {
    wrap(<UsersPage />, { permissions: ['admin.roles'] })
    expect(screen.queryByRole('tablist')).toBeNull()
    // cabecera del panel Roles: título + contador (badge) en la misma línea
    const heading = await screen.findByRole('heading', { level: 2, name: 'Roles' })
    expect(within(heading.closest('header') as HTMLElement).getByText('0')).toBeInTheDocument()
  })
})

describe('RolesTab — permisos agrupados por categoría y reautenticación', () => {
  it('el editor agrupa los permisos del catálogo por categoría', async () => {
    const user = userEvent.setup()
    mock.handler = ((_method: string, url: URL) => {
      if (url.pathname === '/api/v1/roles') return [ROLE]
      if (url.pathname === '/api/v1/permissions') return PERMISSIONS
      if (url.pathname === '/api/v1/catalogs/PermissionCategory') return CATEGORY_LOOKUPS
      return undefined
    }) satisfies Handler
    // "Nuevo rol" vive en la cabecera de la página y abre el editor de la pestaña Roles
    wrap(<UsersPage />, { permissions: ['admin.roles'] })
    await user.click(await screen.findByRole('button', { name: 'Nuevo rol' }))
    expect(await screen.findByRole('dialog', { name: 'Nuevo rol' })).toBeInTheDocument()
    expect(await screen.findByText('Órdenes')).toBeInTheDocument()
    expect(screen.getByText('Almacén')).toBeInTheDocument()
    expect(screen.getByText('Ver órdenes')).toBeInTheDocument()
    expect(screen.getByText('Recibir')).toBeInTheDocument()
    expect(screen.getByText('Ver inventario')).toBeInTheDocument()
  })

  it('editar un rol: sin pedirlo antes, el 403 aal2_required del PUT abre el modal real de ReauthProvider y reintenta sola', async () => {
    // No hay ningún `reauth()` explícito en RolesTab: esto prueba el camino reactivo real del cliente del API
    // (client.ts) — el primer PUT responde 403 aal2_required, el modal aparece, y tras confirmar la contraseña el
    // cliente reintenta el MISMO PUT solo. Antes esta prueba no probaba nada de esto: el mock nunca daba el 403.
    const user = userEvent.setup()
    let putAttempts = 0
    mock.handler = ((method: string, url: URL, body: unknown) => {
      if (method === 'GET' && url.pathname === '/api/v1/roles') return [ROLE]
      if (method === 'GET' && url.pathname === '/api/v1/permissions') return PERMISSIONS
      if (method === 'GET' && url.pathname === '/api/v1/catalogs/PermissionCategory') return CATEGORY_LOOKUPS
      if (method === 'POST' && url.pathname === '/api/v1/auth/reauth') return { accessToken: 'aal2-token' }
      if (method === 'PUT' && url.pathname === '/api/v1/roles/5') {
        putAttempts++
        if (putAttempts === 1) return problem(403, { title: 'Requiere AAL2.', code: 'aal2_required' })
        return { ...ROLE, ...(body as object) }
      }
      return undefined
    }) satisfies Handler
    wrap(<RolesTab />, { permissions: ['admin.roles'], reauth: 'real' })

    await user.click(await screen.findByRole('button', { name: 'Editar' }))
    await user.click(await screen.findByRole('button', { name: 'Guardar' }))

    // el modal de reautenticación real aparece, disparado por el 403 (no por un `reauth()` de antemano)
    expect(await screen.findByText('Confirme su identidad')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Contraseña'), 'Secreta_2026!')
    await user.click(screen.getByRole('button', { name: 'Confirmar' }))

    await waitFor(() => expect(putAttempts).toBe(2))
    expect(mock.calls.filter((c) => c.method === 'PUT' && c.path === '/api/v1/roles/5')).toHaveLength(2)
  })

  it('nota al pie (maqueta rolesNote): nombra las plantillas de sistema por su descripción; sin ellas, la versión genérica', async () => {
    const TEMPLATES: RoleDto[] = [
      { ...ROLE, id: 1, name: 'TenantAdmin', description: 'Admin de tenant', isTemplate: true },
      { ...ROLE, id: 2, name: 'Driver', description: 'Chofer', isTemplate: true },
    ]
    let withTemplates = true
    mock.handler = ((_method: string, url: URL) => {
      if (url.pathname === '/api/v1/roles')
        return url.searchParams.get('includeTemplates') === 'true' && withTemplates ? [ROLE, ...TEMPLATES] : [ROLE]
      if (url.pathname === '/api/v1/permissions') return PERMISSIONS
      return undefined
    }) satisfies Handler

    const first = wrap(<RolesTab />, { permissions: ['admin.roles'] })
    expect(await screen.findByText(/2 roles vienen ya armados \(Admin de tenant, Chofer\)/)).toBeInTheDocument()
    // las plantillas no se cuelan en la tabla (solo el rol de la compañía)
    expect(screen.queryByText('TenantAdmin')).toBeNull()
    first.unmount()

    withTemplates = false
    wrap(<RolesTab />, { permissions: ['admin.roles'] })
    await screen.findByText('Despachador')
    expect(await screen.findByText(/Los roles se pueden editar, renombrar o crear libremente/)).toBeInTheDocument()
  })
})

describe('UsersTab — estado, columna PIN y permisos', () => {
  const USERS: UserSummaryDto[] = [
    { id: 2, fullName: 'Carlos Rivera', email: 'carlos@advance.test', userKind: 'INTERNAL', isActive: true, membershipStatus: 'ACTIVE', mfaEnabled: false, lastLoginUtc: null, roles: ['Despachador'], extraPermissions: [], isPlatformAdmin: false, hasPin: false },
  ]

  it('el switch de estado (controlado por el servidor) se queda como estaba y avisa por toast si el PUT falla', async () => {
    const user = userEvent.setup()
    mock.handler = ((method: string, url: URL) => {
      if (method === 'GET' && url.pathname === '/api/v1/users') return USERS
      if (method === 'GET' && url.pathname === '/api/v1/roles') return [ROLE]
      if (method === 'GET' && url.pathname === '/api/v1/permissions') return PERMISSIONS
      if (method === 'PUT' && url.pathname === '/api/v1/users/2/membership') return problem(403, { title: 'No puede cambiar su propia membresía.', code: 'forbidden' })
      return undefined
    }) satisfies Handler
    wrap(<UsersTab />, { permissions: ['admin.users'] })

    // el switch de Estado lleva su estado como texto (como la maqueta), que es también su nombre accesible
    const statusSwitch = await screen.findByRole('checkbox', { name: 'Activo' })
    expect((statusSwitch as HTMLInputElement).checked).toBe(true)
    await user.click(statusSwitch)
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'PUT' && c.path === '/api/v1/users/2/membership')).toBe(true))
    // el toast de error del 403 se ve (no uno de éxito) — sin esto, el intento fallido pasaría desapercibido
    expect(await screen.findByText('No puede cambiar su propia membresía.')).toBeInTheDocument()
    expect(screen.queryByText('Estado actualizado.')).toBeNull()
    // el checkbox sigue reflejando el estado del servidor (ACTIVE): no hay estado optimista que revertir
    expect((statusSwitch as HTMLInputElement).checked).toBe(true)
  })

  it('la columna PIN solo aparece con módulo WMS_LOTSERIAL y permiso devices.manage|admin.users', async () => {
    mock.handler = ((_method: string, url: URL) => {
      if (url.pathname === '/api/v1/users') return USERS
      if (url.pathname === '/api/v1/roles') return [ROLE]
      if (url.pathname === '/api/v1/permissions') return PERMISSIONS
      return undefined
    }) satisfies Handler

    const withoutModule = wrap(<UsersTab />, { permissions: ['admin.users'], modules: [] })
    await screen.findByText('Carlos Rivera')
    expect(screen.queryByText('PIN app')).toBeNull()
    withoutModule.unmount()

    const withoutPerm = wrap(<UsersTab />, { permissions: [], modules: ['WMS_LOTSERIAL'] })
    await screen.findByText('Carlos Rivera')
    expect(screen.queryByText('PIN app')).toBeNull()
    withoutPerm.unmount()

    wrap(<UsersTab />, { permissions: ['admin.users'], modules: ['WMS_LOTSERIAL'] })
    await screen.findByText('Carlos Rivera')
    expect(screen.getByText('PIN app')).toBeInTheDocument()
  })

  it('"Nuevo usuario" sin contraseña muestra la temporal una sola vez (hallazgo S2)', async () => {
    const user = userEvent.setup()
    mock.handler = ((method: string, url: URL) => {
      if (method === 'GET' && url.pathname === '/api/v1/users') return USERS
      if (method === 'GET' && url.pathname === '/api/v1/roles') return [ROLE]
      if (method === 'GET' && url.pathname === '/api/v1/permissions') return PERMISSIONS
      if (method === 'POST' && url.pathname === '/api/v1/users')
        return { user: { ...USERS[0], id: 3, fullName: 'Nueva Persona', email: 'nueva@advance.test' }, temporaryPassword: 'Tq7mPz2Rk' }
      return undefined
    }) satisfies Handler
    // "Nuevo usuario" vive en la cabecera de la página (sin admin.roles, la página abre directo en Usuarios)
    wrap(<UsersPage />, { permissions: ['admin.users'] })

    await user.click(await screen.findByRole('button', { name: 'Nuevo usuario' }))
    await user.type(await screen.findByLabelText(/^Correo electrónico/), 'nueva@advance.test')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))

    // la contraseña temporal se ve una sola vez, y el modal no se puede cerrar con Esc/clic afuera
    expect(await screen.findByText('Tq7mPz2Rk')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    expect(screen.getByText('Tq7mPz2Rk')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Listo' }))
    expect(screen.queryByText('Tq7mPz2Rk')).toBeNull()
  })
})

describe('PinModal — coincidencia en cliente y mensaje del API', () => {
  const USER: UserSummaryDto = { id: 2, fullName: 'Carlos Rivera', email: 'carlos@advance.test', userKind: 'INTERNAL', isActive: true, membershipStatus: 'ACTIVE', mfaEnabled: false, lastLoginUtc: null, roles: [], extraPermissions: [], isPlatformAdmin: false, hasPin: false }

  it('valida en cliente que los dos PIN coincidan (sin llamar al API)', async () => {
    const user = userEvent.setup()
    wrap(<PinModal open user={USER} onClose={() => {}} />)
    await user.type(screen.getByLabelText('PIN (4 a 6 dígitos)'), '1234')
    await user.type(screen.getByLabelText('Confirmar PIN'), '5678')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('Los PIN no coinciden.')).toBeInTheDocument()
    expect(mock.calls).toHaveLength(0)
  })

  it('muestra el mensaje exacto que devuelve el API cuando el PIN es una secuencia trivial', async () => {
    const user = userEvent.setup()
    mock.handler = (() => problem(400, { title: 'Datos inválidos.', code: 'validation', errors: { pin: ['El PIN no puede ser una secuencia trivial.'] } })) satisfies Handler
    wrap(<PinModal open user={USER} onClose={() => {}} />)
    await user.type(screen.getByLabelText('PIN (4 a 6 dígitos)'), '1234')
    await user.type(screen.getByLabelText('Confirmar PIN'), '1234')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('El PIN no puede ser una secuencia trivial.')).toBeInTheDocument()
  })

  it('"Quitar PIN" pide confirmación y llama a DELETE /users/{id}/pin, sin reautenticación de antemano', async () => {
    const user = userEvent.setup()
    const withPin: UserSummaryDto = { ...USER, hasPin: true }
    mock.handler = ((method: string, url: URL) =>
      method === 'DELETE' && url.pathname === '/api/v1/users/2/pin' ? undefined : new Response(null, { status: 404 })) satisfies Handler
    wrap(<PinModal open user={withPin} onClose={() => {}} />)
    await user.click(screen.getByRole('button', { name: 'Quitar PIN' }))
    await user.click(await screen.findByRole('button', { name: 'Confirmar' }))
    await waitFor(() => expect(mock.calls.some((c) => c.method === 'DELETE' && c.path === '/api/v1/users/2/pin')).toBe(true))
    // no hay ningún POST /auth/reauth: el servidor no exige AAL2 para quitar el PIN
    expect(mock.calls.some((c) => c.path === '/api/v1/auth/reauth')).toBe(false)
  })
})
