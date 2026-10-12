// Lote F-C1 — Selector de consignatario: busca por nombre, número, dirección o pueblo; ofrece los del cliente y los compartidos (no los
// inactivos); muestra el elegido aunque no esté en la lista; el «+» exige locations.create. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ConsigneePicker } from './ConsigneePicker'
import { consigneeAddress, consigneeOptionLabel } from './consigneeRules'

const mock = vi.hoisted(() => ({ urls: [] as URL[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.urls.push(url)
    return new Response(JSON.stringify(route(url)), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const BASE = { locationType: 'DELIVERY', isActive: true, allowDupInvoice: false }
const LIST = [
  { ...BASE, id: 1, publicId: 'l-1', code: 'C-001', name: 'Farmacia Central', line1: 'Calle Sol 1', city: 'San Juan', isShared: false },
  { ...BASE, id: 2, publicId: 'l-2', code: null, name: 'Colmado Boricua', line1: 'Carr. 14 km 2', city: 'Ponce', isShared: true, allowDupInvoice: true },
  { ...BASE, id: 3, publicId: 'l-3', code: null, name: 'Tienda Cerrada', line1: 'Calle Luna 3', city: 'Arecibo', isShared: false, isActive: false },
]
function route(url: URL): unknown {
  if (url.pathname === '/api/v1/locations') return LIST
  if (url.pathname === '/api/v1/locations/l-9') return { ...BASE, id: 9, publicId: 'l-9', code: 'X-9', name: 'De otro cliente', line1: 'Calle 9', city: 'Caguas', isShared: false }
  return []
}

function Harness({ client = 'c-1', initial = '' }: { client?: string | null; initial?: string }) {
  const [value, setValue] = useState(initial)
  return <ConsigneePicker clientPublicId={client} value={value} onChange={setValue} aria-describedby="x" />
}
function wrap(ui: React.ReactElement, permissions: string[] = ['locations.read']) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={['CATALOG']}>
          {ui}
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.urls = []
})

describe('ConsigneePicker', () => {
  it('etiqueta y dirección', () => {
    expect(consigneeOptionLabel({ code: 'C-001', name: 'Farmacia' })).toBe('C-001 · Farmacia')
    expect(consigneeOptionLabel({ code: null, name: 'Colmado' })).toBe('Colmado')
    expect(consigneeAddress({ line1: 'Calle 1', city: 'Ponce' })).toBe('Calle 1 · Ponce')
    expect(consigneeAddress({ line1: null, city: 'Ponce' })).toBe('Ponce')
  })

  it('pide los del cliente más los compartidos, ofrece los activos y busca por nombre, dirección o pueblo', async () => {
    const user = userEvent.setup()
    wrap(<Harness />)
    const box = screen.getByRole('combobox')
    await user.click(box)
    expect(await screen.findByRole('option', { name: /Farmacia Central/ })).toHaveTextContent('Calle Sol 1 · San Juan')
    expect(screen.queryByRole('option', { name: /Tienda Cerrada/ })).toBeNull()
    const q = mock.urls.find((u) => u.pathname === '/api/v1/locations')!.searchParams
    expect(q.get('clientId')).toBe('c-1')
    expect(q.get('includeShared')).toBe('true')
    expect(q.get('includeInactive')).toBe('false')
    await user.type(box, 'ponce')
    expect(screen.queryByRole('option', { name: /Farmacia Central/ })).toBeNull()
    expect(screen.getByRole('option', { name: /Colmado Boricua/ })).toHaveTextContent('Compartido')
    await user.click(screen.getByRole('option', { name: /Colmado Boricua/ }))
    expect(screen.getByRole('combobox')).toHaveValue('Colmado Boricua')
  })

  it('un consignatario elegido que no está en la lista se muestra con su etiqueta (pide su ficha)', async () => {
    wrap(<Harness initial="l-9" />)
    await waitFor(() => expect(screen.getByRole('combobox')).toHaveValue('X-9 · De otro cliente'))
  })

  it('sin cliente espera y no consulta; el «+» solo con locations.create', async () => {
    const { unmount } = wrap(<Harness client={null} />, ['locations.read', 'locations.create'])
    const box = screen.getByRole('combobox')
    expect(box).toBeDisabled()
    expect(box).toHaveAttribute('placeholder', 'Elija primero un cliente')
    expect(screen.getByRole('button', { name: 'Nuevo consignatario' })).toBeDisabled()
    expect(mock.urls.some((u) => u.pathname === '/api/v1/locations')).toBe(false)
    unmount()
    wrap(<Harness />)
    expect(screen.queryByRole('button', { name: 'Nuevo consignatario' })).toBeNull()
  })
})
