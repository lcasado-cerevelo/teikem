// Pruebas de «Impuestos» (Lote 56) sobre un fetch simulado: tasas (crear con componentes, cerrar vigencia, mensajes del servidor), reglas,
// categoría de producto, exenciones y el simulador.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { setLang } from '../../kernel/i18n/i18n'
import TaxScreen from './TaxScreen'
import { typeDate } from '../../kernel/ui/typeDate'

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; search: string; body: unknown }[],
  failPost: false,
}))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, search: url.search, body })
    const p = url.pathname
    const problem = (title: string, status = 409) => new Response(JSON.stringify({ title }), { status, headers: { 'Content-Type': 'application/problem+json' } })
    if (req.method === 'GET' && p === '/api/v1/tax/rates') return json(RATES)
    if (req.method === 'POST' && p === '/api/v1/tax/rates') {
      if (mock.failPost) return problem('Ya hay una tasa con ese código que cubre esas fechas. Cierre la vigencia de la anterior primero.')
      return json({ ...RATES[0], id: 9, code: String(body.code).toUpperCase() })
    }
    if (req.method === 'POST' && p === '/api/v1/tax/rates/1/close') return json({ ...RATES[0], effectiveTo: body.effectiveTo, status: 'CLOSED' })
    if (req.method === 'GET' && p === '/api/v1/tax/settings') return json(SETTINGS)
    if (req.method === 'PATCH' && p === '/api/v1/tax/settings') return json(SETTINGS)
    if (req.method === 'GET' && p === '/api/v1/tax/products') return json({ items: PRODUCTS, total: PRODUCTS.length, skip: 0, take: 500 })
    if (req.method === 'PUT' && p === '/api/v1/tax/products/5/category') return json({ ...PRODUCTS[0], category: body.category })
    if (req.method === 'GET' && p === '/api/v1/tax/exemptions') return json(EXEMPTIONS)
    if (req.method === 'POST' && p === '/api/v1/tax/exemptions/3/deactivate') return json({ ...EXEMPTIONS[0], isActive: false, status: 'INACTIVE' })
    if (req.method === 'POST' && p === '/api/v1/tax/calculate') {
      return json({
        date: '2026-10-15', rateCode: 'IVU', ratePercent: 11.5, pricesIncludeTax: false, roundingMode: 'LINE',
        lines: [{ key: '1', amount: 100, net: 100, taxAmount: 11.5, total: 111.5, reason: 'TAXABLE', isTaxExempt: false, taxRateId: 1, taxRatePct: 11.5, components: [] }],
        subtotal: 100, taxAmount: 11.5, total: 111.5, components: [{ code: 'STATE', name: 'Estatal', percent: 10.5, amount: 10.5 }], warnings: ['El cliente tiene una exención vencida (certificado X).'],
      })
    }
    if (req.method === 'GET' && p === '/api/v1/clients') return json({ items: [], total: 0, skip: 0, take: 50 })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const RATES = [
  { id: 1, publicId: 'r1', code: 'IVU', name: 'IVU Puerto Rico', percent: 11.5, effectiveFrom: '2026-01-01', effectiveTo: null, status: 'ACTIVE', components: [{ code: 'STATE', name: 'Estatal', percent: 10.5 }, { code: 'CITY', name: 'Municipal', percent: 1 }] },
  { id: 2, publicId: 'r0', code: 'IVU', name: 'IVU anterior', percent: 11, effectiveFrom: '2025-01-01', effectiveTo: '2025-12-31', status: 'CLOSED', components: [{ code: 'STATE', name: 'Estatal', percent: 11 }] },
]
const SETTINGS = {
  pricesIncludeTax: false, roundingMode: 'LINE', defaultRateCode: 'IVU',
  chargeRules: [{ chargeType: 'FREIGHT', label: 'Flete', isTaxable: false }, { chargeType: 'HANDLING', label: 'Manejo', isTaxable: true }],
}
const PRODUCTS = [{ productId: 5, sku: 'SKU-5', name: 'Agua', category: 'TAXABLE' }]
const EXEMPTIONS = [{ id: 3, publicId: 'e1', clientId: 1, clientName: 'Cliente Uno', exemptionType: 'Sin fines de lucro', certificateNumber: 'CERT-1', validFrom: '2026-01-01', validTo: null, notes: null, isActive: true, status: 'VALID' }]

const SESSION = {
  me: { userId: 99, tenantName: 'Advance Logistics' },
  isAuthenticated: true,
  isLoading: false,
  error: null,
  tenantId: 7,
  lang: 'es',
  setLang: () => {},
  logout: async () => {},
  switchTenant: async () => {},
  permissions: new Set(['tax.manage', 'clients.read']),
  modules: new Set(['TAX', 'CATALOG']),
  reloadMe: async () => {},
} as unknown as Session

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <MemoryRouter>
          <TaxScreen />
        </MemoryRouter>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

describe('TaxScreen', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.failPost = false
  })

  it('lista las tasas con su porcentaje, componentes y estado; una tasa abierta se puede cerrar y una cerrada sirve de base de la nueva', async () => {
    const user = userEvent.setup()
    wrap()
    expect(await screen.findByRole('heading', { level: 1, name: 'Impuestos' })).toBeInTheDocument()
    expect(await screen.findByText('Estatal 10.5 % + Municipal 1 %')).toBeInTheDocument()
    expect(screen.getByText('Vigente')).toBeInTheDocument()
    expect(screen.getByText('Cerrada')).toBeInTheDocument()

    await user.click(screen.getAllByRole('button', { name: 'Cerrar vigencia' })[0])
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar vigencia' }))
    expect(await within(dialog).findByText('Escriba el último día en que rige la tasa.')).toBeInTheDocument()
    await typeDate(user, within(dialog).getByLabelText('Último día de vigencia'), '2026-10-14')
    await user.click(within(dialog).getByRole('button', { name: 'Cerrar vigencia' }))
    await waitFor(() => expect(sent('POST', '/api/v1/tax/rates/1/close')[0]?.body).toEqual({ effectiveTo: '2026-10-14' }))
  })

  it('crea una tasa con componentes, valida antes de enviar y muestra el mensaje del servidor', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Nueva tasa' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Crear tasa' }))
    expect(await within(dialog).findByText('Escriba el código de la tasa.')).toBeInTheDocument()
    expect(sent('POST', '/api/v1/tax/rates')).toHaveLength(0)

    await user.type(within(dialog).getByLabelText('Código'), 'ivu')
    await user.type(within(dialog).getByLabelText('Nombre'), 'IVU nuevo')
    await typeDate(user, within(dialog).getByLabelText('Rige desde'), '2026-10-15')
    await user.type(within(dialog).getByLabelText('Código del componente 1'), 'state')
    await user.type(within(dialog).getByLabelText('Nombre del componente 1'), 'Estatal')
    await user.type(within(dialog).getByLabelText('Porcentaje del componente 1'), '10,5')
    await user.click(within(dialog).getByRole('button', { name: 'Agregar componente' }))
    await user.type(within(dialog).getByLabelText('Código del componente 2'), 'city')
    await user.type(within(dialog).getByLabelText('Nombre del componente 2'), 'Municipal')
    await user.type(within(dialog).getByLabelText('Porcentaje del componente 2'), '1')
    expect(within(dialog).getByRole('status')).toHaveTextContent('Total: 11.5 %')

    mock.failPost = true
    await user.click(within(dialog).getByRole('button', { name: 'Crear tasa' }))
    expect(await within(dialog).findByText(/Ya hay una tasa con ese código/)).toBeInTheDocument()
    expect(sent('POST', '/api/v1/tax/rates')[0].body).toEqual({
      code: 'ivu', name: 'IVU nuevo', effectiveFrom: '2026-10-15',
      components: [{ code: 'state', name: 'Estatal', percent: 10.5 }, { code: 'city', name: 'Municipal', percent: 1 }],
    })

    mock.failPost = false
    await user.click(within(dialog).getByRole('button', { name: 'Crear tasa' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('reglas: muestra lo guardado y envía los cambios', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Reglas' }))
    expect(await screen.findByLabelText('Tasa por omisión')).toHaveValue('IVU')
    expect(screen.getByRole('switch', { name: 'Manejo paga impuesto' })).toBeChecked()
    expect(screen.getByRole('switch', { name: 'Flete paga impuesto' })).not.toBeChecked()
    await user.selectOptions(screen.getByLabelText('Redondeo'), 'DOCUMENT')
    await user.click(screen.getByRole('switch', { name: 'Flete paga impuesto' }))
    await user.click(screen.getByRole('button', { name: 'Guardar reglas' }))
    await waitFor(() => expect(sent('PATCH', '/api/v1/tax/settings')).toHaveLength(1))
    expect(sent('PATCH', '/api/v1/tax/settings')[0].body).toEqual({
      pricesIncludeTax: false, roundingMode: 'DOCUMENT', defaultRateCode: 'IVU',
      chargeRules: [{ chargeType: 'FREIGHT', isTaxable: true }, { chargeType: 'HANDLING', isTaxable: true }],
    })
  })

  it('productos: cambiar la categoría la guarda al escoger', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Productos' }))
    const select = await screen.findByLabelText('Categoría de impuesto de SKU-5')
    await user.selectOptions(select, 'EXEMPT')
    await waitFor(() => expect(sent('PUT', '/api/v1/tax/products/5/category')[0]?.body).toEqual({ category: 'EXEMPT' }))
  })

  it('exenciones: lista con estado y se puede dar de baja', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Exenciones' }))
    expect(await screen.findByText('CERT-1')).toBeInTheDocument()
    expect(screen.getByText('Vigente')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Dar de baja' }))
    await waitFor(() => expect(sent('POST', '/api/v1/tax/exemptions/3/deactivate')).toHaveLength(1))
  })

  it('simulador: calcula, muestra el motivo por línea, los totales y los avisos del servidor', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Simulador' }))
    await user.click(await screen.findByRole('button', { name: 'Calcular' }))
    expect(await screen.findByText('Resultado')).toBeInTheDocument()
    expect(screen.getByText(/exención vencida/)).toBeInTheDocument()
    expect(screen.getByText('Gravable', { selector: '.chip, span' })).toBeInTheDocument()
    expect(sent('POST', '/api/v1/tax/calculate')[0].body).toEqual({ clientId: null, lines: [{ key: '1', chargeType: 'PRODUCT_SALE', amount: 100 }] })
  })
})
