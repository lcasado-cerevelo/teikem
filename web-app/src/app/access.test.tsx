// Menú filtrado por módulos y permisos (visibleNav), guarda de rutas (RouteGate) y redirección por 403 de lecturas
// (createQueryClient). Rutas de prueba propias: las de F1 casi no declaran `perm` ni `module`.
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../kernel/access'
import { ApiError } from '../kernel/api/problem'
import { setLang } from '../kernel/i18n/i18n'
import { visibleNav } from './navigation'
import { createQueryClient, setAccessDeniedHandler } from './queryClient'
import type { AppRoute } from './routes'
import { RouteGate } from './RouteGate'

function Screen() {
  return <p>pantalla protegida</p>
}

const ROUTES: AppRoute[] = [
  { path: '/', element: Screen, nav: { group: 'ops', key: 'pulse', order: 0 } },
  { path: '/orders', element: Screen, module: 'LTL_GROUND', perm: 'orders.read', nav: { group: 'ops', key: 'orders', order: 1 } },
  { path: '/products', element: Screen, module: 'CATALOG', nav: { group: 'catalog', key: 'products' } },
  { path: '/users', element: Screen, perm: 'users.manage', nav: { group: 'system', key: 'users', order: 2 } },
  { path: '/roles', element: Screen, perm: 'roles.manage', nav: { group: 'system', key: 'roles', order: 1 } },
  { path: '/hidden', element: Screen, perm: 'users.manage' },
]

const labels = (groups: ReturnType<typeof visibleNav<AppRoute>>) => groups.map((g) => [g.key, g.items.map((r) => r.path)])

describe('visibleNav (menú lateral)', () => {
  it('sin permisos ni módulos: solo lo que no exige nada; los grupos vacíos no aparecen', () => {
    expect(labels(visibleNav(ROUTES, new Set(), new Set()))).toEqual([['ops', ['/']]])
  })

  it('una ruta con módulo y permiso exige los dos', () => {
    expect(labels(visibleNav(ROUTES, new Set(['orders.read']), new Set()))).toEqual([['ops', ['/']]])
    expect(labels(visibleNav(ROUTES, new Set(), new Set(['LTL_GROUND'])))).toEqual([['ops', ['/']]])
    expect(labels(visibleNav(ROUTES, new Set(['orders.read']), new Set(['LTL_GROUND'])))).toEqual([['ops', ['/', '/orders']]])
  })

  it('el módulo encendido muestra su grupo; Sistema aparece con un permiso de admin, ordenada por `order`', () => {
    expect(labels(visibleNav(ROUTES, new Set(), new Set(['CATALOG'])))).toEqual([
      ['ops', ['/']],
      ['catalog', ['/products']],
    ])
    expect(labels(visibleNav(ROUTES, new Set(['users.manage']), new Set()))).toEqual([
      ['ops', ['/']],
      ['system', ['/users']],
    ])
    expect(labels(visibleNav(ROUTES, new Set(['users.manage', 'roles.manage']), new Set()))).toEqual([
      ['ops', ['/']],
      ['system', ['/roles', '/users']],
    ])
  })
})

function renderGate(route: AppRoute, access: { permissions: string[]; modules: string[] }) {
  return render(
    <MemoryRouter>
      <AccessProvider permissions={access.permissions} modules={access.modules}>
        <RouteGate route={route} />
      </AccessProvider>
    </MemoryRouter>,
  )
}

describe('RouteGate', () => {
  beforeAll(() => setLang('es'))
  const [, orders, products, users] = ROUTES

  it('con módulo y permiso pinta la pantalla', () => {
    renderGate(orders, { permissions: ['orders.read'], modules: ['LTL_GROUND'] })
    expect(screen.getByText('pantalla protegida')).toBeInTheDocument()
  })

  it('módulo apagado: "Módulo apagado" (aunque tenga el permiso) y no pinta la pantalla', () => {
    renderGate(orders, { permissions: ['orders.read'], modules: [] })
    expect(screen.getByTestId('module-off-screen')).toHaveTextContent('Módulo apagado')
    expect(screen.getByText('El módulo LTL_GROUND no está habilitado para su compañía.')).toBeInTheDocument()
    expect(screen.queryByText('pantalla protegida')).toBeNull()
  })

  it('módulo encendido sin el permiso de la ruta: "Sin permiso"', () => {
    renderGate(orders, { permissions: [], modules: ['LTL_GROUND'] })
    expect(screen.getByTestId('forbidden-screen')).toHaveTextContent('Sin permiso')
    expect(screen.queryByText('pantalla protegida')).toBeNull()
  })

  it('ruta con solo permiso: "Sin permiso" sin él, la pantalla con él', () => {
    const { unmount } = renderGate(users, { permissions: [], modules: [] })
    expect(screen.getByTestId('forbidden-screen')).toBeInTheDocument()
    unmount()
    renderGate(users, { permissions: ['users.manage'], modules: [] })
    expect(screen.getByText('pantalla protegida')).toBeInTheDocument()
  })

  it("perm 'a|b': entra con cualquiera de los dos; sin ninguno, 'Sin permiso'", () => {
    const either: AppRoute = { path: '/system/users', element: Screen, perm: 'admin.users|admin.roles', module: 'SYSTEM' }
    const { unmount } = renderGate(either, { permissions: ['admin.roles'], modules: ['SYSTEM'] })
    expect(screen.getByText('pantalla protegida')).toBeInTheDocument()
    unmount()
    const second = renderGate(either, { permissions: ['admin.users'], modules: ['SYSTEM'] })
    expect(screen.getByText('pantalla protegida')).toBeInTheDocument()
    second.unmount()
    renderGate({ ...either, module: undefined }, { permissions: ['admin.audit'], modules: [] })
    expect(screen.getByTestId('forbidden-screen')).toBeInTheDocument()
  })

  it('ruta con solo módulo: "Módulo apagado" sin él, la pantalla con él', () => {
    const { unmount } = renderGate(products, { permissions: [], modules: [] })
    expect(screen.getByTestId('module-off-screen')).toBeInTheDocument()
    unmount()
    renderGate(products, { permissions: [], modules: ['CATALOG'] })
    expect(screen.getByText('pantalla protegida')).toBeInTheDocument()
  })
})

describe('createQueryClient (403 en lecturas)', () => {
  const handler = vi.fn()
  afterEach(() => {
    handler.mockReset()
    setAccessDeniedHandler(null)
  })

  async function failRead(error: unknown, meta?: Record<string, unknown>) {
    setAccessDeniedHandler(handler)
    const client = createQueryClient()
    await client.fetchQuery({ queryKey: ['k'], queryFn: () => Promise.reject(error), retry: false, meta }).catch(() => {})
  }

  it('403 module_disabled → el shell va a "Módulo apagado"', async () => {
    await failRead(new ApiError(403, { code: 'module_disabled', title: 'Módulo apagado.' }))
    expect(handler).toHaveBeenCalledWith('module_disabled')
  })

  it('403 forbidden → el shell va a "Sin permiso"', async () => {
    await failRead(new ApiError(403, { code: 'forbidden', title: 'Sin permiso.' }))
    expect(handler).toHaveBeenCalledWith('forbidden')
  })

  it('con meta.handleAccessDenied false la consulta no saca al usuario de la pantalla', async () => {
    await failRead(new ApiError(403, { code: 'module_disabled' }), { handleAccessDenied: false })
    expect(handler).not.toHaveBeenCalled()
  })

  it('otros errores (404, aal2_required, red) no redirigen', async () => {
    await failRead(new ApiError(404, { code: 'not_found' }))
    await failRead(new ApiError(403, { code: 'aal2_required' }))
    await failRead(new TypeError('Failed to fetch'))
    expect(handler).not.toHaveBeenCalled()
  })

  it('las mutaciones no se interceptan', async () => {
    setAccessDeniedHandler(handler)
    const client = createQueryClient()
    await client
      .getMutationCache()
      .build(client, { mutationFn: () => Promise.reject(new ApiError(403, { code: 'forbidden' })) })
      .execute(undefined)
      .catch(() => {})
    expect(handler).not.toHaveBeenCalled()
  })
})
