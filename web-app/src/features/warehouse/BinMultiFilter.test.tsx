// Lote 14 — BinMultiFilter (filtro "Posición" entre almacenes) y OwnerFilter ("Dueño"): búsqueda en el API con pausa,
// opciones "Código · Zona · Almacén", píldoras con ✕, lector de código de barras (Enter con el código exacto) y almacenes
// del filtro como acotación.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import type { BinFilterItem } from './kardexView'
import { BinMultiFilter, OwnerFilter } from './pickers'

const mock = vi.hoisted(() => ({ requests: [] as URL[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push(url)
    let body: unknown = []
    if (url.pathname === '/api/v1/warehouses/bins/search') {
      const q = (url.searchParams.get('search') ?? '').toUpperCase()
      body = [
        { id: 10, code: 'A-01', zoneCode: 'PCK', warehousePublicId: 'W1', warehouseCode: 'ALM-01', isActive: true },
        { id: 20, code: 'A-01', zoneCode: 'STG', warehousePublicId: 'W2', warehouseCode: 'ALM-02', isActive: true },
        { id: 30, code: 'B-07', zoneCode: 'PCK', warehousePublicId: 'W1', warehouseCode: 'ALM-01', isActive: false },
      ].filter((b) => !q || b.code.includes(q))
    }
    if (url.pathname === '/api/v1/inventory/owners') body = [{ name: 'Propio', isOwn: true }, { clientPublicId: 'C1', name: 'Cliente A', isOwn: false }]
    return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const onChange = vi.fn()

function BinHost({ warehouses }: { warehouses?: string[] }) {
  const [value, setValue] = useState<BinFilterItem[]>([])
  return (
    <BinMultiFilter
      label="Posición"
      value={value}
      warehousePublicIds={warehouses}
      includeInactive
      onChange={(v) => {
        onChange(v)
        setValue(v)
      }}
    />
  )
}

function OwnerHost() {
  const [value, setValue] = useState<string[]>([])
  return (
    <OwnerFilter
      label="Dueño"
      value={value}
      onChange={(v) => {
        onChange(v)
        setValue(v)
      }}
    />
  )
}

function wrap(ui: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>)
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  onChange.mockReset()
})

describe('BinMultiFilter', () => {
  it('busca entre almacenes y agrega una píldora "Código · Zona · Almacén" que se puede quitar', async () => {
    const user = userEvent.setup()
    wrap(<BinHost />)
    await user.type(screen.getByRole('combobox', { name: 'Posición' }), 'a-0')
    expect(await screen.findByRole('option', { name: /A-01 · STG · ALM-02/ })).toBeInTheDocument()
    await user.click(screen.getByRole('option', { name: /A-01 · PCK · ALM-01/ }))
    expect(onChange).toHaveBeenLastCalledWith([{ id: 10, label: 'A-01 · PCK · ALM-01' }])
    expect(screen.getByRole('button', { name: 'Quitar A-01 · PCK · ALM-01' })).toBeInTheDocument()
    // la elegida ya no se ofrece
    await user.click(screen.getByRole('combobox', { name: 'Posición' }))
    await waitFor(() => expect(screen.queryByRole('option', { name: /A-01 · PCK · ALM-01/ })).toBeNull())
    await user.click(screen.getByRole('button', { name: 'Quitar A-01 · PCK · ALM-01' }))
    expect(onChange).toHaveBeenLastCalledWith([])
  })

  it('marca las posiciones inactivas y manda los almacenes del filtro', async () => {
    const user = userEvent.setup()
    wrap(<BinHost warehouses={['W1']} />)
    await user.type(screen.getByRole('combobox', { name: 'Posición' }), 'b')
    expect(await screen.findByRole('option', { name: /B-07.*Inactiva/ })).toBeInTheDocument()
    const last = mock.requests.filter((u) => u.pathname === '/api/v1/warehouses/bins/search').at(-1)!
    expect(last.searchParams.getAll('warehousePublicIds')).toEqual(['W1'])
    expect(last.searchParams.get('includeInactive')).toBe('true')
  })

  it('lector de código de barras: el código completo + Enter elige la posición exacta', async () => {
    const user = userEvent.setup()
    wrap(<BinHost />)
    await user.type(screen.getByRole('combobox', { name: 'Posición' }), 'B-07{Enter}')
    await waitFor(() => expect(onChange).toHaveBeenLastCalledWith([{ id: 30, label: 'B-07 · PCK · ALM-01' }]))
  })
})

describe('OwnerFilter', () => {
  it('"Propio" es OWN; los clientes, su publicId', async () => {
    const user = userEvent.setup()
    wrap(<OwnerHost />)
    await user.click(screen.getByRole('button', { name: /Dueño/ }))
    await user.click(await screen.findByRole('option', { name: /Propio/ }))
    await user.click(await screen.findByRole('option', { name: /Cliente A/ }))
    expect(onChange).toHaveBeenLastCalledWith(['OWN', 'C1'])
  })
})
