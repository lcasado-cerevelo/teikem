// Lote 244 — Configuración: rutas con titular/apoyos, panel de personas de la ruta, representante con usuario obligatorio y cuota por ruta.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { SetupTab } from './SetupTab'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    mock.requests.push({ method: req.method, path: url.pathname, body: text ? JSON.parse(text) : undefined })
    const p = url.pathname
    if (p === '/api/v1/sales-analysis/routes')
      return json([
        { id: 1, name: 'Cadenas', sortOrder: 1, isActive: true, repCount: 1, hasPrimary: true, primaryName: 'Ana Vendedora', primaryIsActiveMember: true, supportCount: 1, clientCount: 12, currentQuota: 5000 },
        { id: 2, name: 'Norte', sortOrder: 2, isActive: true, repCount: 1, hasPrimary: false, supportCount: 0, clientCount: 3 },
      ])
    if (p === '/api/v1/sales-analysis/routes/2/people') return json({ routeId: 2, routeName: 'Norte', routeIsActive: true, supports: [], history: [] })
    if (p === '/api/v1/sales-analysis/sellers/candidates') return json([{ userId: 7, name: 'Beto Apoyo', email: 'beto@x.com' }])
    if (req.method === 'POST' && p === '/api/v1/sales-analysis/routes/2/people') return json({ routeId: 2 })
    if (p === '/api/v1/sales-analysis/reps')
      return json([
        { id: 1, name: 'Ana', routeName: 'Cadenas', userId: 5, userName: 'Ana Vendedora', isActive: true, hasUser: true, userIsActiveMember: true, isPrimaryOfRoute: true },
        { id: 2, name: 'Sin Usuario', routeName: 'Norte', isActive: true, hasUser: false },
      ])
    if (p === '/api/v1/users') return json([{ id: 5, fullName: 'Ana Vendedora', email: 'ana@x.com', isActive: true }])
    return new Response('{}', { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function wrap(url: string, perms: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={perms} modules={['SALES_ORDERS']}>
        <MemoryRouter initialEntries={[url]}>
          <SetupTab />
        </MemoryRouter>
      </AccessProvider>
    </QueryClientProvider>,
  )
}

describe('Configuración: rutas de venta y personas', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => (mock.requests.length = 0))

  it('la tabla de rutas muestra titular, «Sin titular», apoyos y clientes', async () => {
    wrap('/?tab=setup', ['sales.analysis.view'])
    expect(await screen.findByText('Ana Vendedora')).toBeInTheDocument()
    expect(screen.getByText('Sin titular')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Nueva ruta' })).toBeNull()
  })

  it('«Personas» abre el panel y agrega un apoyo con POST routes/{id}/people', async () => {
    const user = userEvent.setup()
    wrap('/?tab=setup', ['sales.analysis.view', 'sales.analysis.manage'])
    await screen.findByText('Ana Vendedora')
    const row = screen.getByText('Norte').closest('tr') as HTMLElement
    const btn = within(row).queryByRole('button', { name: 'Personas' })
    if (btn) await user.click(btn)
    else await user.click(within(row).getByRole('button', { name: /Acciones|Más/ }))
    if (!btn) await user.click(await screen.findByRole('menuitem', { name: 'Personas' }))
    const dialog = await screen.findByRole('dialog', { name: /Personas de Norte/ })
    expect(await within(dialog).findByText('Esta ruta de venta no tiene personas.')).toBeInTheDocument()
    await user.click(dialog.querySelector('#sa-route-user') as HTMLElement)
    await user.click(await screen.findByRole('option', { name: /Beto Apoyo/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Agregar persona' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/sales-analysis/routes/2/people')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'POST')?.body).toMatchObject({ userId: 7, role: 'SUPPORT' })
  })

  it('representantes: chip «Sin usuario» y el alta exige el usuario', async () => {
    const user = userEvent.setup()
    wrap('/?tab=setup&sec=reps', ['sales.analysis.view', 'sales.analysis.manage', 'users.view'])
    expect(await screen.findByText('Sin usuario')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Nuevo representante' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Nombre'), 'Nuevo')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El usuario es obligatorio.')).toBeInTheDocument()
    expect(mock.requests.some((r) => r.method === 'POST')).toBe(false)
  })
})
