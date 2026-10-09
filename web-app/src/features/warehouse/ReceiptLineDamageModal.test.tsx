// Daño declarado en una línea del recibo (2026-10-08): cantidad dañada ≤ recibida, razón del catálogo (Otra pide escribirla), dejar en una posición o desechar,
// y el cuerpo del PUT de la línea. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import type { ReceiptDetailDto } from './api'
import { ReceiptLineDamageModal } from './ReceiptLineDamageModal'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[] }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined }
    mock.calls.push(call)
    const body = route(call)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const RECEIPT = {
  header: { publicId: 'rrrrrrrr-0000-0000-0000-000000000001', warehousePublicId: 'aaaaaaaa-0000-0000-0000-000000000001', receivingModeCode: 'PUTAWAY', isOpen: true },
  lines: [{ id: 5, sku: 'SKU-1', productName: 'Tornillo', trackingTypeCode: 'NONE', receivedQty: 10, damagedQty: 0 }],
  putawayTasks: [],
  rowVersion: 'x',
  canDelete: true,
} as unknown as ReceiptDetailDto

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (method === 'PUT' && p.includes('/lines/5')) return RECEIPT
  if (p === '/api/v1/catalogs/DamageCause')
    return [
      { code: 'ARRIVED_DAMAGED', label: 'Vino así' },
      { code: 'OTHER', label: 'Otra' },
    ]
  if (p === '/api/v1/catalogs/DamageFinalDestination')
    return [
      { code: 'DISCARDED_WASTE', label: 'Tirado' },
      { code: 'RETURNED_TO_SUPPLIER', label: 'Devuelto al proveedor' },
    ]
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/status/')) return []
  return new Response(JSON.stringify({ title: 'No encontrado', code: 'not_found' }), { status: 404, headers: { 'Content-Type': 'application/problem+json' } })
}

function show(line = RECEIPT.lines![0]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={['warehouse.receive']} modules={['WMS_LOTSERIAL']}>
          <ReceiptLineDamageModal receipt={RECEIPT} line={line} onClose={() => undefined} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
})

describe('Daño de la línea del recibo', () => {
  it('no deja una cantidad dañada mayor que la recibida', async () => {
    const user = userEvent.setup()
    show()
    await user.type(await screen.findByRole('spinbutton', { name: /Cantidad dañada/ }), '11')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('La cantidad dañada no puede ser mayor que la recibida (10).')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'PUT')).toBe(false)
  })

  it('pide la razón y, con «Otra», escribirla', async () => {
    const user = userEvent.setup()
    show()
    await user.type(await screen.findByRole('spinbutton', { name: /Cantidad dañada/ }), '2')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('Indique cómo ocurrió el daño.')).toBeInTheDocument()
  })

  it('dar salida de una vez manda damageDiscard y no la posición', async () => {
    const user = userEvent.setup()
    show()
    await user.type(await screen.findByRole('spinbutton', { name: /Cantidad dañada/ }), '3')
    await user.click(screen.getByRole('combobox', { name: /Razón/ }))
    await user.click(await screen.findByRole('option', { name: 'Vino así' }))
    await user.click(screen.getByRole('radio', { name: 'Dar salida de una vez' }))
    // sin destino no guarda
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('Indique a dónde va lo que sale (tirado, devuelto al proveedor, donado…).')).toBeInTheDocument()
    expect(mock.calls.some((c) => c.method === 'PUT')).toBe(false)
    await user.click(screen.getByRole('combobox', { name: /Destino final/ }))
    await user.click(await screen.findByRole('option', { name: 'Devuelto al proveedor' }))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.calls.find((c) => c.method === 'PUT')?.body).toMatchObject({ damagedQty: 3, damageCause: 'ARRIVED_DAMAGED', damageDiscard: true, damageBinId: null, damageFinalDestination: 'RETURNED_TO_SUPPLIER' }))
  })

  it('«Quitar daño» aparece solo si la línea ya tiene daño y manda clearDamage', async () => {
    const user = userEvent.setup()
    show({ ...RECEIPT.lines![0], damagedQty: 2, damageCauseCode: 'ARRIVED_DAMAGED' })
    await user.click(await screen.findByRole('button', { name: 'Quitar daño' }))
    await waitFor(() => expect(mock.calls.find((c) => c.method === 'PUT')?.body).toEqual({ clearDamage: true }))
  })
})
