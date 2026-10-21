// Pruebas de «Plantillas» (Sistema) sobre un fetch simulado: la lista por entidad con inactivas, desactivar/reactivar con la ruta de cada entidad,
// el cambio de pestaña y el aviso sin permiso.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import TemplatesScreen from './TemplatesScreen'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; search: string }[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push({ method: req.method, path: url.pathname, search: url.search })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/import-templates') return json(ORDER_TEMPLATES)
    if (req.method === 'GET' && p === '/api/v1/locations/import-templates') return json(CONSIGNEE_TEMPLATES)
    if (req.method === 'GET' && (p === '/api/v1/import-templates/fields' || p === '/api/v1/locations/import/fields')) return json([])
    if (req.method === 'GET' && p.startsWith('/api/v1/clients')) return json([])
    if (req.method === 'POST') return json({})
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const ORDER_TEMPLATES = [
  { publicId: 'o-1', name: 'Estándar de envíos', kind: 'ORDER', clientName: null, columns: [{ position: 1, field: 'consigneeName' }], isActive: true },
  { publicId: 'o-2', name: 'Vieja de Acme', kind: 'ORDER', clientName: 'Acme', columns: [], isActive: false },
]
const CONSIGNEE_TEMPLATES = [{ publicId: 'c-1', name: 'Consignatarios Acme', clientName: 'Acme', columns: [{ position: 1, field: 'name' }], isActive: true }]

function session(perms: string[]) {
  return {
    me: { tenantName: 'Advance Logistics' },
    isAuthenticated: true,
    isLoading: false,
    error: null,
    tenantId: 7,
    lang: 'es',
    setLang: () => {},
    logout: async () => {},
    switchTenant: async () => {},
    permissions: new Set(perms),
    modules: new Set(['INTEGRATIONS']),
    reloadMe: async () => {},
  } as unknown as Session
}

function wrap(perms: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={session(perms)}>
        <AccessProvider permissions={perms} modules={['INTEGRATIONS']}>
          <TemplatesScreen />
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const rowOf = async (name: string) => (await screen.findByText(name)).closest('tr') as HTMLElement

describe('TemplatesScreen', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
  })

  it('lista las plantillas de envíos con las inactivas (activas primero) y pide includeInactive', async () => {
    wrap(['orders.edit', 'locations.create'])
    expect(screen.getByRole('heading', { level: 1, name: 'Plantillas' })).toBeInTheDocument()
    const active = await rowOf('Estándar de envíos')
    const inactive = await rowOf('Vieja de Acme')
    expect(within(active).getByText('Activa')).toBeInTheDocument()
    expect(within(inactive).getByText('Inactiva')).toBeInTheDocument()
    expect(active.compareDocumentPosition(inactive) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    const get = mock.requests.find((r) => r.method === 'GET' && r.path === '/api/v1/import-templates')
    expect(get?.search).toContain('includeInactive=true')
    expect(get?.search).toContain('kind=ORDER')
  })

  it('reactivar usa la ruta de envíos y, en la otra pestaña, desactivar usa la de consignatarios', async () => {
    const user = userEvent.setup()
    wrap(['orders.edit', 'locations.create'])
    const inactive = await rowOf('Vieja de Acme')
    await user.click(within(inactive).getByRole('button', { name: 'Reactivar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/import-templates/o-2/reactivate')).toBe(true))

    await user.click(screen.getByRole('tab', { name: 'Consignatarios' }))
    const row = await rowOf('Consignatarios Acme')
    await user.click(within(row).getByRole('button', { name: 'Desactivar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/locations/import-templates/c-1/deactivate')).toBe(true))
  })

  it('sin el permiso de la entidad no pide la lista y dice cuál permiso hace falta', async () => {
    wrap(['integrations.view'])
    expect(await screen.findByText(/necesitas el permiso orders\.edit/)).toBeInTheDocument()
    expect(mock.requests.some((r) => r.path === '/api/v1/import-templates')).toBe(false)
    expect(screen.queryByRole('button', { name: /Nueva plantilla/ })).toBeNull()
  })
})
