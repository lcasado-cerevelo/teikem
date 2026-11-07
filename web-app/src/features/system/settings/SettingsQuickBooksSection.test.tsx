// Lote 225 — Ajustes → Operación → QuickBooks: se leen las cuentas, los ítems y los términos; se guarda solo lo que cambió (vacío = por omisión, término vacío = se quita);
// los límites de QuickBooks se avisan antes de enviar; el error del servidor se muestra; sin admin.tenant es solo lectura.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../../kernel/i18n/i18n'
import { quickBooksDraftError, quickBooksDraftOf, quickBooksSettingsRequest } from '../../sales/quickBooksRules'
import { SettingsQuickBooksSection } from './SettingsQuickBooksSection'

const DEFAULTS = {
  receivableAccount: 'Accounts Receivable',
  salesAccount: 'Sales',
  costAccount: 'Cost of Goods Sold',
  inventoryAccount: 'Inventory Asset',
  taxAccount: 'Sales Tax Payable',
  taxItem: 'IVU',
  shippingItem: 'Shipping',
  terms: [
    { code: 'NET30', label: 'Neto 30', quickBooksName: 'Net 30' },
    { code: 'COD', label: 'Contra entrega', quickBooksName: null },
  ],
}

const mock = vi.hoisted(() => ({ settings: null as unknown, requests: [] as { method: string; path: string; body: unknown }[], fail: false }))

vi.mock('../../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    if (url.pathname !== '/api/v1/sales-orders/quickbooks/settings') return new Response('{}', { status: 404 })
    if (req.method === 'PATCH' && mock.fail)
      return new Response(JSON.stringify({ title: 'El término de pago «XYZ» no existe en el catálogo de términos de pago.', status: 400 }), { status: 400, headers: { 'Content-Type': 'application/problem+json' } })
    return new Response(JSON.stringify(mock.settings), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function wrap(canEdit = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SettingsQuickBooksSection canEdit={canEdit} />
    </QueryClientProvider>,
  )
}

const patches = () => mock.requests.filter((r) => r.method === 'PATCH')

describe('Ajustes → Operación → QuickBooks', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.settings = structuredClone(DEFAULTS)
    mock.requests.length = 0
    mock.fail = false
  })

  it('muestra las cuentas, los ítems y los términos y guarda solo lo que cambió', async () => {
    const user = userEvent.setup()
    wrap()
    const sales = await screen.findByLabelText('Cuenta de ventas')
    expect(sales).toHaveValue('Sales')
    expect(screen.getByLabelText('Cuenta por cobrar')).toHaveValue('Accounts Receivable')
    expect(screen.getByLabelText('Ítem del IVU')).toHaveValue('IVU')
    expect(screen.getByLabelText('Ítem del envío')).toHaveValue('Shipping')
    expect(screen.getByLabelText('Nombre en QuickBooks de Neto 30')).toHaveValue('Net 30')
    expect(screen.getByLabelText('Nombre en QuickBooks de Contra entrega')).toHaveValue('')
    expect(screen.getByRole('button', { name: 'Guardar cambios' })).toBeDisabled()

    await user.clear(sales)
    await user.type(sales, 'Sales:Monitors')
    await user.clear(screen.getByLabelText('Ítem del envío'))
    await user.type(screen.getByLabelText('Nombre en QuickBooks de Contra entrega'), 'COD')
    await user.clear(screen.getByLabelText('Nombre en QuickBooks de Neto 30'))
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(patches()).toHaveLength(1))
    expect(patches()[0].body).toEqual({ salesAccount: 'Sales:Monitors', shippingItem: '', terms: { COD: 'COD', NET30: '' } })
  })

  it('un nombre demasiado largo no se envía y dice por qué; el error del servidor se muestra', async () => {
    const user = userEvent.setup()
    wrap()
    const item = await screen.findByLabelText('Ítem del IVU')
    expect(item).toHaveAttribute('maxLength', '31')
    // el maxLength del campo ya corta lo que se escribe; un valor pegado de más se detiene antes de enviar
    fireEvent.change(item, { target: { value: 'X'.repeat(32) } })
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    expect(await screen.findByText('El nombre de un ítem de QuickBooks no puede pasar de 31 caracteres.')).toBeInTheDocument()
    expect(patches()).toHaveLength(0)

    mock.fail = true
    fireEvent.change(item, { target: { value: 'IVU 11.5' } })
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    expect(await screen.findByText('El término de pago «XYZ» no existe en el catálogo de términos de pago.')).toBeInTheDocument()
  })

  it('sin admin.tenant es solo lectura', async () => {
    wrap(false)
    expect(await screen.findByLabelText('Cuenta de ventas')).toBeDisabled()
    expect(screen.getByLabelText('Nombre en QuickBooks de Neto 30')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Guardar cambios' })).not.toBeInTheDocument()
  })

  it('reglas puras: sin cambios no hay cuerpo; límites 159 / 31 / 31', () => {
    const d = quickBooksDraftOf(DEFAULTS)
    expect(quickBooksSettingsRequest(d, d)).toBeNull()
    expect(quickBooksSettingsRequest({ ...d, fields: { ...d.fields, taxItem: '  IVU  ' } }, d)).toBeNull()
    expect(quickBooksDraftError({ ...d, fields: { ...d.fields, costAccount: 'A'.repeat(160) } })).toBe('account')
    expect(quickBooksDraftError({ ...d, fields: { ...d.fields, costAccount: 'A'.repeat(159) } })).toBeNull()
    expect(quickBooksDraftError({ ...d, terms: { NET30: 'N'.repeat(32) } })).toBe('term')
  })
})
