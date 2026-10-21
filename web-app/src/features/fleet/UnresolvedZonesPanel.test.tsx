// Pruebas de «Envíos sin zona» sobre un fetch simulado: la tabla con su motivo, «Agregar a una zona» (agrega el código postal o el pueblo y asigna), y que sin envíos sin zona el panel no aparece.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { municipalityOf, UnresolvedZonesPanel } from './UnresolvedZonesPanel'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; search: string; body: unknown }[], total: 5 }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    mock.requests.push({ method: req.method, path: url.pathname, search: url.search, body: text ? JSON.parse(text) : undefined })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/dispatch-zones/unresolved-orders') {
      return json(
        mock.total === 0
          ? { totalOrders: 0, groups: 0, rows: [] }
          : {
              totalOrders: mock.total,
              groups: 4,
              rows: [
                { postalCode: '00999', city: 'Utuado', orders: 3, reason: 'NO_MATCH', candidates: [] },
                { postalCode: '00907', city: 'San Juan', orders: 1, reason: 'RESOLVABLE', candidates: [] },
                { postalCode: null, city: null, orders: 1, reason: 'NO_DATA', candidates: [] },
                { postalCode: '00731', city: 'Ponce', orders: 2, reason: 'AMBIGUOUS', candidates: ['Z-PO', 'Z-S'] },
              ],
            },
      )
    }
    if (req.method === 'GET' && p === '/api/v1/dispatch-zones') return json([{ id: 7, code: 'Z-UT', name: 'Utuado', driverCount: 0, isActive: true }])
    if (req.method === 'POST' && p === '/api/v1/dispatch-zones/7/members') return json({})
    if (req.method === 'POST' && p === '/api/v1/dispatch-zones/assign-orders') return json({ examined: 5, assigned: 3, unresolved: 2 })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const PERMS = ['fleet.view', 'fleet.manage']
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
  modules: new Set(['LTL_GROUND']),
  reloadMe: async () => {},
} as unknown as Session

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={PERMS} modules={['LTL_GROUND']}>
          <UnresolvedZonesPanel />
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

describe('UnresolvedZonesPanel', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.total = 5
  })

  it('lista cada combinación con su motivo y solo ofrece agregar a una zona cuando ninguna la cubre', async () => {
    wrap()
    const utuado = (await screen.findByText('Utuado')).closest('tr') as HTMLElement
    expect(within(utuado).getByText('Ninguna zona lo cubre')).toBeInTheDocument()
    expect(within(utuado).getByRole('button', { name: 'Agregar a una zona' })).toBeInTheDocument()
    const sanJuan = screen.getByText('San Juan').closest('tr') as HTMLElement
    expect(within(sanJuan).getByText('Ya hay zona: falta asignarla')).toBeInTheDocument()
    expect(within(sanJuan).queryByRole('button', { name: 'Agregar a una zona' })).toBeNull()
    expect(screen.getByText('La dirección no trae código postal ni pueblo')).toBeInTheDocument()
    expect(screen.getByText(/Zonas que empatan: Z-PO, Z-S/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Asignar zona a los envíos' })).toBeInTheDocument()   // hay un grupo que ya se puede asignar
  })

  it('«Agregar y asignar» agrega el código postal a la zona elegida y luego asigna la zona a los envíos', async () => {
    const user = userEvent.setup()
    wrap()
    const utuado = (await screen.findByText('Utuado')).closest('tr') as HTMLElement
    await user.click(within(utuado).getByRole('button', { name: 'Agregar a una zona' }))
    await user.selectOptions(await screen.findByLabelText('Zona'), '7')
    await user.click(screen.getByRole('button', { name: 'Agregar y asignar' }))

    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/dispatch-zones/assign-orders')).toBe(true))
    const add = mock.requests.find((r) => r.method === 'POST' && r.path === '/api/v1/dispatch-zones/7/members')
    expect(add?.body).toEqual({ matchType: 'POSTAL_CODE', matchValue: '00999' })
    expect(await screen.findByText(/Agregado «00999». Con zona: 3; todavía sin zona: 2./)).toBeInTheDocument()
  })

  it('con el pueblo elegido agrega el municipio', async () => {
    const user = userEvent.setup()
    wrap()
    const utuado = (await screen.findByText('Utuado')).closest('tr') as HTMLElement
    await user.click(within(utuado).getByRole('button', { name: 'Agregar a una zona' }))
    await user.selectOptions(await screen.findByLabelText('Zona'), '7')
    await user.click(screen.getByLabelText(/Pueblo Utuado/))
    await user.click(screen.getByRole('button', { name: 'Agregar y asignar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/dispatch-zones/7/members')).toBe(true))
    expect(mock.requests.find((r) => r.path === '/api/v1/dispatch-zones/7/members')?.body).toEqual({ matchType: 'MUNICIPALITY', matchValue: 'Utuado' })
  })

  it('el pueblo de «BARRIO, PUEBLO» es el último tramo (lo que se agrega a la zona)', () => {
    expect(municipalityOf('LA PLATA, AIBONITO')).toBe('AIBONITO')
    expect(municipalityOf('  Carolina ')).toBe('Carolina')
    expect(municipalityOf('A, B, C')).toBe('C')
    expect(municipalityOf(null)).toBe('')
  })

  it('sin envíos sin zona el panel no aparece', async () => {
    mock.total = 0
    const { container } = wrap()
    await waitFor(() => expect(mock.requests.some((r) => r.path === '/api/v1/dispatch-zones/unresolved-orders')).toBe(true))
    await waitFor(() => expect(container.querySelector('.spin, [role="progressbar"]')).toBeNull())
    expect(screen.queryByText('Envíos sin zona')).toBeNull()
  })
})
