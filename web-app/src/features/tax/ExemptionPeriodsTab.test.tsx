// Lote 248 — exención por temporada: lista con estado, alta que valida y manda el cuerpo, período ya empezado (solo cambia la fecha final) y baja/reactivación.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ExemptionPeriodsTab } from './ExemptionPeriodsTab'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: Record<string, unknown> | undefined }[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    mock.requests.push({ method: req.method, path: url.pathname, body: text ? JSON.parse(text) : undefined })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/tax/exemption-periods') return json(PERIODS)
    if (req.method === 'POST' && p === '/api/v1/tax/exemption-periods') return json(PERIODS[0])
    if (req.method === 'PATCH') return json(PERIODS[1])
    if (req.method === 'POST' && p.endsWith('/deactivate')) return json({ ...PERIODS[1], isActive: false, state: 'INACTIVE' })
    if (p === '/api/v1/product-categories') return json([{ id: 5, name: 'Agua', path: 'Alimentos › Agua', isActive: true, productCount: 3 }])
    return new Response('{}', { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})
function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
const PERIODS = [
  { publicId: 'p1', name: 'Huracán 2027', reason: 'Orden administrativa 27-01', validFrom: '2027-09-01', validTo: '2027-09-30', scope: 'SELECTED', isActive: true, state: 'SCHEDULED', products: [], categories: [{ categoryId: 5, name: 'Agua' }], rowVersion: 'rv1' },
  { publicId: 'p2', name: 'Emergencia', reason: 'Decreto', validFrom: '2026-01-01', validTo: '2099-12-31', scope: 'ALL', isActive: true, state: 'ACTIVE', products: [], categories: [], rowVersion: 'rv2' },
]

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={new Set(['tax.manage'])} modules={new Set(['TAX'])}>
        <MemoryRouter>
          <ExemptionPeriodsTab />
        </MemoryRouter>
      </AccessProvider>
    </QueryClientProvider>,
  )
}

describe('Exención por temporada', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
  })

  it('lista los períodos con su estado y qué cubren', async () => {
    wrap()
    expect(await screen.findByText('Huracán 2027')).toBeInTheDocument()
    expect(screen.getByText('Programado')).toBeInTheDocument()
    expect(screen.getByText('Vigente')).toBeInTheDocument()
    expect(screen.getByText('1 categoría(s) · 0 producto(s)')).toBeInTheDocument()
    expect(screen.getByText('Todos los productos')).toBeInTheDocument()
  })

  it('un período nuevo valida nombre, motivo, fechas y selección, y manda el cuerpo', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('Huracán 2027')
    await user.click(screen.getByRole('button', { name: 'Nuevo período' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo período' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar período' }))
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Escriba el nombre del período de exención.')
    await user.type(within(dialog).getByLabelText('Nombre'), 'Sequía')
    await user.type(within(dialog).getByLabelText('Motivo'), 'Orden 5')
    await user.type(within(dialog).getByLabelText('Vale desde'), '11/01/2027')
    await user.type(within(dialog).getByLabelText('Vale hasta (obligatorio)'), '11/30/2027')
    await user.click(within(dialog).getByRole('radio', { name: 'Categorías o productos escogidos' }))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar período' }))
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Escoja al menos una categoría o un producto.')
    await user.click(within(dialog).getByRole('radio', { name: 'Todos los productos' }))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar período' }))
    await waitFor(() => expect(mock.requests.filter((r) => r.method === 'POST')).toHaveLength(1))
    expect(mock.requests.find((r) => r.method === 'POST')?.body).toMatchObject({ name: 'Sequía', reason: 'Orden 5', validFrom: '2027-11-01', validTo: '2027-11-30', scope: 'ALL' })
  })

  it('un período ya empezado solo deja cambiar la fecha final', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('Emergencia')
    const row = screen.getByText('Emergencia').closest('tr') as HTMLElement
    await user.click(within(row).getByRole('button', { name: 'Editar período' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar período' })
    expect(within(dialog).getByRole('status')).toHaveTextContent('El período ya empezó: solo se le puede cambiar la fecha final o darlo de baja.')
    expect(within(dialog).getByLabelText('Nombre')).toBeDisabled()
    expect(within(dialog).getByLabelText('Vale hasta (obligatorio)')).toBeEnabled()
    await user.click(within(dialog).getByRole('button', { name: 'Guardar período' }))
    await waitFor(() => expect(mock.requests.filter((r) => r.method === 'PATCH')).toHaveLength(1))
    expect(mock.requests.find((r) => r.method === 'PATCH')?.body).toEqual({ validTo: '2099-12-31', rowVersion: 'rv2' })
  })

  it('dar de baja manda deactivate', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('Emergencia')
    const row = screen.getByText('Emergencia').closest('tr') as HTMLElement
    await user.click(within(row).getByRole('button', { name: 'Dar de baja' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/tax/exemption-periods/p2/deactivate')).toBe(true))
  })
})
