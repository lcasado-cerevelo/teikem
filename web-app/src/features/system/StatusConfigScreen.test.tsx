// Pruebas de «Estatus» sobre un fetch simulado: lista las etapas del dominio con su tipo, apaga una etapa, sube una etapa intercambiando su orden con la vecina y muestra el motivo si el servidor la rechaza.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import StatusConfigScreen from './StatusConfigScreen'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[], putStatus: 200 }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    mock.requests.push({ method: req.method, path: url.pathname, body: text ? JSON.parse(text) : undefined })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/catalogs/domains') return json([{ id: 1, domainKey: 'ClientStatus', scope: 2, label: 'Estatus de cliente' }, { id: 2, domainKey: 'OrderStatus', scope: 2, label: 'Estatus de orden' }, { id: 3, domainKey: 'ServiceType', scope: 1, label: 'Tipo de servicio' }])
    if (req.method === 'GET' && p === '/api/v1/status/ClientStatus') return json(STAGES)
    if (req.method === 'GET' && p === '/api/v1/status/ClientStatus/validate') return json({ isValid: true, errors: [], warnings: [] })
    if (req.method === 'PUT' && p.startsWith('/api/v1/status/ClientStatus/') && p.endsWith('/override')) {
      if (mock.putStatus !== 200) return new Response(JSON.stringify({ title: 'Pipeline inválido: Debe quedar habilitada al menos una etapa inicial.' }), { status: mock.putStatus, headers: { 'Content-Type': 'application/json' } })
      return json(STAGES[0])
    }
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const STAGES = [
  { id: 1, entity: 'ClientStatus', code: 'PROSPECT', label: 'Prospecto', labels: { es: 'Prospecto', en: 'Prospect' }, sortOrder: 1, stageKind: 'PIPELINE', isInitial: false, isEnabled: true, isOverridden: false },
  { id: 2, entity: 'ClientStatus', code: 'ACTIVE', label: 'Activo', labels: { es: 'Activo', en: 'Active' }, sortOrder: 2, stageKind: 'PIPELINE', isInitial: true, isEnabled: true, isOverridden: false },
  { id: 3, entity: 'ClientStatus', code: 'SUSPENDED', label: 'Suspendido', labels: { es: 'Suspendido', en: 'Suspended' }, sortOrder: 3, stageKind: 'LATERAL', isInitial: false, isEnabled: true, isOverridden: false },
]

const PERMS = ['catalogs.view', 'admin.statusconfig']
const SESSION = {
  me: { tenantName: 'Advance Logistics' },
  isAuthenticated: true,
  isLoading: false,
  error: null,
  tenantId: 7,
  lang: 'es',
  setLang: () => {},
  logout: async () => {},
  switchTenant: async () => {},
  permissions: new Set(PERMS),
  modules: new Set(['SYSTEM']),
  reloadMe: async () => {},
} as unknown as Session

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={PERMS} modules={['SYSTEM']}>
          <StatusConfigScreen />
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const rowOf = async (code: string) => (await screen.findByText(code)).closest('tr') as HTMLElement
const puts = () => mock.requests.filter((r) => r.method === 'PUT')

describe('StatusConfigScreen', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.putStatus = 200
  })

  it('lista las etapas del dominio de cliente (el de estatus, no el de listas) con su tipo y la inicial', async () => {
    wrap()
    expect(screen.getByRole('heading', { level: 1, name: 'Estatus' })).toBeInTheDocument()
    const active = await rowOf('ACTIVE')
    expect(within(active).getByText('Etapa normal')).toBeInTheDocument()
    expect(within(active).getByText('Inicial')).toBeInTheDocument()
    expect(within(await rowOf('SUSPENDED')).getByText('Desvío')).toBeInTheDocument()
    const options = within(screen.getByLabelText('Estatus de')).getAllByRole('option').map((o) => o.textContent)
    expect(options).toEqual(['Estatus de cliente', 'Estatus de orden'])   // solo dominios de estatus
  })

  it('apagar una etapa manda isEnabled=false en su override', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(within(await rowOf('PROSPECT')).getByRole('switch'))
    await waitFor(() => expect(puts()).toHaveLength(1))
    expect(puts()[0]).toMatchObject({ path: '/api/v1/status/ClientStatus/PROSPECT/override', body: { isEnabled: false } })
  })

  it('«Subir» intercambia el orden con la etapa vecina', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(within(await rowOf('ACTIVE')).getByRole('button', { name: 'Subir' }))
    await waitFor(() => expect(puts()).toHaveLength(2))
    expect(puts().map((r) => [r.path.split('/')[5], (r.body as { sortOverride: number }).sortOverride])).toEqual([['ACTIVE', 1], ['PROSPECT', 2]])
  })

  it('si el servidor rechaza el cambio muestra el motivo', async () => {
    mock.putStatus = 422
    const user = userEvent.setup()
    wrap()
    await user.click(within(await rowOf('ACTIVE')).getByRole('switch'))
    expect(await screen.findByText(/Pipeline inválido: Debe quedar habilitada al menos una etapa inicial/)).toBeInTheDocument()
  })
})
