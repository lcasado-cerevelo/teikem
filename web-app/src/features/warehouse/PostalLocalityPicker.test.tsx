import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { PostalLocalityPicker } from './PostalLocalityPicker'

const mock = vi.hoisted(() => ({ requests: [] as URL[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  // catálogo USPS: ciudad postal en mayúsculas sin acentos; municipio (solo PR) con acentos
  const ROWS = [
    { id: 1, city: 'MAYAGUEZ', postalCode: '00680', state: 'PR', countryCode: 'PR', country: 'Puerto Rico', municipality: 'Mayagüez' },
    { id: 2, city: 'MAYAGUEZ', postalCode: '00681', state: 'PR', countryCode: 'PR', country: 'Puerto Rico', municipality: 'Mayagüez' },
    { id: 3, city: 'SABANA SECA', postalCode: '00952', state: 'PR', countryCode: 'PR', country: 'Puerto Rico', municipality: 'Toa Baja' },
    { id: 4, city: 'NEW YORK', postalCode: '10001', state: 'NY', countryCode: 'US', country: 'Estados Unidos', municipality: null },
  ]
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    mock.requests.push(url)
    const q = (url.searchParams.get('search') ?? '').normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()
    const fold = (x: string | null) => (x ?? '').normalize('NFD').replace(/\p{Diacritic}/gu, '').toLowerCase()
    const rows = ROWS.filter((r) => r.postalCode.startsWith(q) || fold(r.city).includes(q) || fold(r.municipality).includes(q))
    return new Response(JSON.stringify(rows), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>)
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
})

describe('PostalLocalityPicker', () => {
  it('busca por ciudad sin acentos (250 ms entre teclas) y muestra "ZIP · CIUDAD, Estado"; al elegir devuelve la localidad', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<PostalLocalityPicker city="" postalCode="" onChange={onChange} aria-label="Ciudad" />)
    await user.type(screen.getByRole('combobox', { name: 'Ciudad' }), 'mayag')
    await waitFor(() => expect(mock.requests.some((u) => u.searchParams.get('search') === 'mayag')).toBe(true))
    await user.click(await screen.findByRole('option', { name: '00681 · MAYAGUEZ, PR' }))
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ postalCode: '00681', countryCode: 'PR' }))
  })

  it('Enter con un código postal completo lo elige aunque otra opción esté resaltada', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<PostalLocalityPicker city="" postalCode="" onChange={onChange} aria-label="Ciudad" />)
    await user.type(screen.getByRole('combobox', { name: 'Ciudad' }), '00681')
    await screen.findByRole('option', { name: /00681/ })
    await user.keyboard('{Enter}')
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ postalCode: '00681' }))
  })

  it('municipio distinto de la ciudad postal entre paréntesis; fuera de PR, el país', async () => {
    const user = userEvent.setup()
    wrap(<PostalLocalityPicker city="" postalCode="" onChange={vi.fn()} aria-label="Ciudad" />)
    await user.type(screen.getByRole('combobox', { name: 'Ciudad' }), '0095')
    expect(await screen.findByRole('option', { name: '00952 · SABANA SECA (Toa Baja), PR' })).toBeInTheDocument()
    await user.clear(screen.getByRole('combobox', { name: 'Ciudad' }))
    await user.type(screen.getByRole('combobox', { name: 'Ciudad' }), 'new york')
    expect(await screen.findByRole('option', { name: '10001 · NEW YORK, NY · Estados Unidos' })).toBeInTheDocument()
  })

  it('muestra el valor actual (aunque no venga del catálogo) y ✕ lo quita con null', async () => {
    const user = userEvent.setup()
    const onChange = vi.fn()
    wrap(<PostalLocalityPicker city="San Juan" postalCode="00901" onChange={onChange} aria-label="Ciudad" />)
    expect(screen.getByRole('combobox', { name: 'Ciudad' })).toHaveValue('San Juan · 00901')
    await user.click(screen.getByRole('button', { name: 'Quitar ciudad y código postal' }))
    expect(onChange).toHaveBeenCalledWith(null)
  })
})
