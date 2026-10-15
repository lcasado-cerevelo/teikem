// Pruebas de «Trabajos programados» (Lote 50) sobre un fetch simulado: la lista con horario y último resultado, el interruptor, «Ejecutar
// ahora», el cambio de horario (con la validación y el mensaje del servidor) y el historial.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import JobsScreen from './JobsScreen'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[], running: false, patchStatus: 200 }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/jobs') return json(JOBS)
    if (req.method === 'GET' && p === '/api/v1/jobs/JOB_TEST/runs') return json(RUNS)
    if (req.method === 'PATCH' && p.startsWith('/api/v1/jobs/')) {
      if (mock.patchStatus !== 200) return new Response(JSON.stringify({ title: 'El intervalo debe estar entre 60 segundos y 7 días (604800).' }), { status: mock.patchStatus, headers: { 'Content-Type': 'application/problem+json' } })
      return json({ ...JOBS[1], ...body })
    }
    if (req.method === 'POST' && p.endsWith('/run')) return json({ ...JOBS[1], manualRequested: true })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const BASE = { manualRequested: false, lastStartedUtc: null, consecutiveFailures: 0, isRunning: false, dailyAt: null, intervalSeconds: null, rowVersion: 'AAA=' }
const JOBS = [
  { ...BASE, key: 'INVENTORY_SWEEP', name: 'Revisión diaria de inventario', nameEn: 'Daily inventory check', description: 'Compara el Kárdex contra los saldos.', descriptionEn: 'x', isEnabled: true, scheduleKind: 'DAILY', dailyAt: '02:00', nextRunUtc: '2026-10-15T06:00:00Z', lastStatus: 'OK', lastMessage: '12 productos revisados.', lastError: null, lastFinishedUtc: '2026-10-14T06:00:05Z' },
  { ...BASE, key: 'JOB_TEST', name: 'Trabajo de prueba', nameEn: 'Test job', description: 'No hace nada.', descriptionEn: 'x', isEnabled: false, scheduleKind: 'INTERVAL', intervalSeconds: 3600, nextRunUtc: null, lastStatus: 'FAILED', lastMessage: null, lastError: 'se cayó', lastFinishedUtc: '2026-10-14T07:00:00Z' },
]
const RUNS = [
  { id: 2, startedUtc: '2026-10-14T07:00:00Z', finishedUtc: '2026-10-14T07:00:01Z', status: 'FAILED', triggeredBy: 'MANUAL', message: null, error: 'se cayó' },
  { id: 1, startedUtc: '2026-10-13T07:00:00Z', finishedUtc: '2026-10-13T07:00:01Z', status: 'OK', triggeredBy: 'SCHEDULE', message: 'listo', error: null },
]

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
  permissions: new Set(['jobs.manage']),
  modules: new Set(['SYSTEM']),
  reloadMe: async () => {},
} as unknown as Session

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={['jobs.manage']} modules={['SYSTEM']}>
          <JobsScreen />
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const rowOf = async (name: string) => (await screen.findByText(name)).closest('tr') as HTMLElement

describe('JobsScreen', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.patchStatus = 200
  })

  it('muestra cada trabajo con su horario, próxima corrida y último resultado', async () => {
    wrap()
    expect(screen.getByRole('heading', { level: 1, name: 'Trabajos programados' })).toBeInTheDocument()
    const sweep = await rowOf('Revisión diaria de inventario')
    expect(within(sweep).getByText(/Todos los días a las/)).toBeInTheDocument()
    expect(within(sweep).getByText('Bien')).toBeInTheDocument()
    expect(within(sweep).getByText('12 productos revisados.')).toBeInTheDocument()
    const test = await rowOf('Trabajo de prueba')
    expect(within(test).getByText('Cada hora')).toBeInTheDocument()
    expect(within(test).getByText('Falló')).toBeInTheDocument()
    expect(within(test).getByText('se cayó')).toBeInTheDocument()
    expect(within(test).getByRole('switch')).not.toBeChecked()
    expect(within(sweep).getByRole('switch')).toBeChecked()
  })

  it('el interruptor manda isEnabled con el rowVersion', async () => {
    const user = userEvent.setup()
    wrap()
    const test = await rowOf('Trabajo de prueba')
    await user.click(within(test).getByRole('switch'))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PATCH')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PATCH')).toMatchObject({ path: '/api/v1/jobs/JOB_TEST', body: { isEnabled: true, rowVersion: 'AAA=' } })
  })

  it('«Ejecutar ahora» pide la corrida', async () => {
    const user = userEvent.setup()
    wrap()
    const test = await rowOf('Trabajo de prueba')
    await user.click(within(test).getByRole('button', { name: 'Ejecutar ahora' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/jobs/JOB_TEST/run')).toBe(true))
  })

  it('cambia el horario: valida el rango y manda el intervalo en segundos', async () => {
    const user = userEvent.setup()
    wrap()
    const test = await rowOf('Trabajo de prueba')
    await user.click(within(test).getByRole('button', { name: 'Cambiar horario' }))
    const dialog = await screen.findByRole('dialog')
    const value = within(dialog).getByRole('spinbutton')
    await user.clear(value)
    await user.type(value, '0')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El intervalo debe estar entre 60 segundos y 7 días (604800).')).toBeInTheDocument()
    expect(mock.requests.some((r) => r.method === 'PATCH')).toBe(false)
    await user.clear(value)
    await user.type(value, '15')
    await user.selectOptions(within(dialog).getByRole('combobox', { name: 'Unidad' }), 'm')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PATCH')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PATCH')?.body).toMatchObject({ scheduleKind: 'INTERVAL', intervalSeconds: 900, rowVersion: 'AAA=' })
  })

  it('abre el historial del trabajo', async () => {
    const user = userEvent.setup()
    wrap()
    const test = await rowOf('Trabajo de prueba')
    await user.click(within(test).getByRole('button', { name: 'Ver historial' }))
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText('listo')).toBeInTheDocument()
    expect(within(dialog).getByText('Por horario')).toBeInTheDocument()
    expect(within(dialog).getAllByText('Ejecutar ahora').length).toBeGreaterThan(0)
  })
})
