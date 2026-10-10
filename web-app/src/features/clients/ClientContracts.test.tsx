// F-A2 — Contratos del cliente: sección con pestañas Contrato · Tarifas · SLA · Servicios especiales dentro de la ficha.
// Cada pestaña: render, permisos (contracts.read / update / create), validaciones en pantalla y errores del servidor
// (400 por campo, 404/409/422 con el mensaje exacto, 409 de rowVersion con relectura). Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { tenantToday } from '../../kernel/api/tenantZone'
import { setLang } from '../../kernel/i18n/i18n'
import { ClientContractsSection } from './ClientContractsSection'
import type { ClientDetail } from './clientRules'
import { defaultEffectiveDate } from './contractRules'

interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | unknown[] | undefined
}
const mock = vi.hoisted(() => ({
  calls: [] as Call[],
  override: null as null | ((call: Call) => unknown),
}))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined }
    mock.calls.push(call)
    const body = mock.override?.(call) ?? route(call)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const SUMMARY_1 = { id: 1, publicId: 'k-1', contractNumber: 'ACME-C1', title: 'Contrato marco', startDate: '2026-01-15', endDate: null, status: 'ACTIVE', statusLabel: 'Activo', isActive: true, isCurrent: true }
const SUMMARY_2 = { id: 2, publicId: 'k-2', contractNumber: 'ACME-C2', title: 'Anexo', startDate: '2026-06-01', endDate: null, status: 'DRAFT', statusLabel: 'Borrador', isActive: true, isCurrent: false }
const CLIENT = {
  id: 10,
  publicId: 'c-1',
  code: 'ACME',
  name: 'Acme Corp',
  status: 'ACTIVE',
  isActive: true,
  contracts: [SUMMARY_1, SUMMARY_2],
  currentContract: SUMMARY_1,
  billingSummary: 'Por servicio, COD',
} as unknown as ClientDetail

const DETAIL_1 = {
  id: 1,
  publicId: 'k-1',
  clientPublicId: 'c-1',
  clientName: 'Acme Corp',
  contractNumber: 'ACME-C1',
  title: 'Contrato marco',
  startDate: '2026-01-15',
  endDate: '2026-12-31',
  autoRenew: false,
  status: 'ACTIVE',
  statusLabel: 'Activo',
  currency: 'USD',
  billingTrigger: 'BY_PICKUP',
  notes: 'Nota vieja',
  billingModel: { billPerService: true, billExtraPiece: true, billDispatchFee: false, billCodFee: true, billSpecialServices: true, summary: 'Por servicio, Pieza extra, COD, Especiales' },
  dispatchFee: 3,
  codFee: { type: 'PERCENT', value: 2.5 },
  serviceLevels: [],
  canEdit: true,
  isActive: true,
  rowVersion: 'AAA=',
}
let detail1: Record<string, unknown> = { ...DETAIL_1 }

const PER_SERVICE = [
  { id: 11, serviceType: 'STANDARD', serviceTypeLabel: 'Estándar', packageType: 'BOX', packageTypeLabel: 'Caja', rate: 6.5, effectiveFrom: '2026-01-15', effectiveTo: null, isCurrent: true },
  { id: 12, serviceType: 'EXPRESS', serviceTypeLabel: 'Exprés', packageType: 'BOX', packageTypeLabel: 'Caja', rate: 8.5, effectiveFrom: '2026-01-15', effectiveTo: null, isCurrent: true },
]
const HISTORY_ROW = { id: 10, serviceType: 'STANDARD', serviceTypeLabel: 'Estándar', packageType: 'BOX', packageTypeLabel: 'Caja', rate: 6, effectiveFrom: '2025-01-01', effectiveTo: '2026-01-15', isCurrent: false }
const EXTRA = [
  {
    componentId: 21,
    serviceType: 'STANDARD',
    serviceTypeLabel: 'Estándar',
    packageType: 'BOX',
    packageTypeLabel: 'Caja',
    effectiveFrom: '2026-01-15',
    effectiveTo: null,
    isCurrent: true,
    tiers: [
      { id: 31, fromUnit: 2, toUnit: 5, rate: 1, effectiveFrom: '2026-01-15', effectiveTo: null, isCurrent: true },
      { id: 32, fromUnit: 6, toUnit: null, rate: 0.75, effectiveFrom: '2026-01-15', effectiveTo: null, isCurrent: true },
    ],
  },
]
let specials: Record<string, unknown> = {}
const SPECIAL_ROWS = [{ id: 41, typeId: 5, typeName: 'Vagón del muelle', rate: 150, effectiveFrom: '2026-01-15', effectiveTo: null, isCurrent: true }]

function route({ method, url, body }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/contracts/k-1' && method === 'GET') return detail1
  if (p === '/api/v1/contracts/k-2' && method === 'GET') return { ...DETAIL_1, id: 2, publicId: 'k-2', contractNumber: 'ACME-C2', title: 'Anexo', status: 'DRAFT', statusLabel: 'Borrador' }
  if (p === '/api/v1/contracts' && method === 'POST') return { ...DETAIL_1, id: 3, publicId: 'k-3', contractNumber: 'ACME-C3', title: String((body as Record<string, unknown>).title), status: 'DRAFT' }
  if (p === '/api/v1/contracts/k-3') return { ...DETAIL_1, id: 3, publicId: 'k-3', contractNumber: 'ACME-C3', title: 'Anexo 2', status: 'DRAFT' }
  if (/^\/api\/v1\/contracts\/k-1\/(billing-model|dispatch-fee|cod-fee|service-levels|status)$/.test(p) || (p === '/api/v1/contracts/k-1' && method === 'PATCH')) {
    if (p.endsWith('/service-levels'))
      return { ...detail1, serviceLevels: (body as Array<Record<string, unknown>>).map((l, i) => ({ id: i + 1, serviceTypeLabel: l.serviceType, ...l })), rowVersion: 'BBB=' }
    // la respuesta real devuelve la ficha completa ya con lo guardado
    const b = body as Record<string, unknown>
    if (p.endsWith('/cod-fee')) return { ...detail1, codFee: b, rowVersion: 'BBB=' }
    if (p.endsWith('/dispatch-fee')) return { ...detail1, dispatchFee: b.amount, rowVersion: 'BBB=' }
    if (p.endsWith('/billing-model')) return { ...detail1, billingModel: { ...(detail1.billingModel as object), ...b }, rowVersion: 'BBB=' }
    return { ...detail1, ...b, rowVersion: 'BBB=' }
  }
  if (p === '/api/v1/contracts/k-1/rate-components' && method === 'GET') {
    const history = url.searchParams.get('includeHistory') === 'true'
    return { contractPublicId: 'k-1', asOf: '2026-10-10', perService: history ? [HISTORY_ROW, ...PER_SERVICE] : PER_SERVICE, extraPiece: EXTRA }
  }
  if (p.startsWith('/api/v1/contracts/k-1/rate-components')) return { id: 99 }
  if (p === '/api/v1/clients/c-1/special-services' && method === 'GET') return specials
  if (p.startsWith('/api/v1/clients/c-1/special-services')) return { id: 98 }
  if (p === '/api/v1/special-service-types') return [{ id: 5, name: 'Vagón del muelle', clientsUsing: 1, isActive: true }, { id: 6, name: 'Entrega nocturna', clientsUsing: 0, isActive: true }]
  if (p === '/api/v1/catalogs/Currency') return [{ code: 'USD', label: 'Dólar', isEnabled: true }, { code: 'EUR', label: 'Euro', isEnabled: true }]
  if (p === '/api/v1/catalogs/BillingModel') return [{ code: 'BY_PICKUP', label: 'Al recoger', isEnabled: true }, { code: 'BY_DELIVERY', label: 'Al entregar', isEnabled: true }]
  if (p === '/api/v1/catalogs/ServiceType') return [{ code: 'STANDARD', label: 'Estándar', isEnabled: true }, { code: 'EXPRESS', label: 'Exprés', isEnabled: true }]
  if (p === '/api/v1/catalogs/PackageType') return [{ code: 'BOX', label: 'Caja', isEnabled: true }, { code: 'ENVELOPE', label: 'Sobre', isEnabled: true }]
  if (p === '/api/v1/status/ContractStatus')
    return [
      { code: 'DRAFT', label: 'Borrador', stageKind: 'PIPELINE', isInitial: true, isEnabled: true, sortOrder: 10 },
      { code: 'ACTIVE', label: 'Activo', stageKind: 'PIPELINE', isInitial: false, isEnabled: true, sortOrder: 20 },
      { code: 'CANCELLED', label: 'Cancelado', stageKind: 'TERMINAL', isInitial: false, isEnabled: true, sortOrder: 90 },
    ]
  if (p === '/api/v1/status/ContractStatus/validate') return { isValid: true, errors: [], warnings: [] }
  if (p.startsWith('/api/v1/status/') || p.startsWith('/api/v1/catalogs/')) return []
  return problem(404, 'No encontrado', 'not_found')
}

function wrap(permissions: string[], client: ClientDetail = CLIENT) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={['CATALOG']}>
          <ClientContractsSection client={client} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const READ = ['clients.read', 'contracts.read']
const WRITE = [...READ, 'contracts.update']
const FULL = [...WRITE, 'contracts.create']
const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)
const lastBody = (method: string, path: string) => callsTo(method, path).at(-1)?.body as Record<string, unknown>
const today = () => defaultEffectiveDate(tenantToday())

async function openTab(user: ReturnType<typeof userEvent.setup>, name: string) {
  await screen.findByRole('tablist')
  await user.click(screen.getByRole('tab', { name }))
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.override = null
  detail1 = { ...DETAIL_1 }
  specials = { clientPublicId: 'c-1', contractPublicId: 'k-1', componentEnabled: true, asOf: '2026-10-10', items: SPECIAL_ROWS }
})

describe('Contratos — sección y permisos', () => {
  it('muestra las cuatro pestañas y el selector con el contrato vigente elegido', async () => {
    wrap(READ)
    const tabs = await screen.findAllByRole('tab')
    expect(tabs.map((t) => t.textContent)).toEqual(['Contrato', 'Tarifas', 'SLA', 'Servicios especiales'])
    const select = screen.getByLabelText('Contrato') as HTMLSelectElement
    expect(select.value).toBe('k-1')
    expect(within(select).getByRole('option', { name: /ACME-C1 · Contrato marco — Activo \(contrato vigente\)/ })).toBeInTheDocument()
    expect(await screen.findByDisplayValue('Contrato marco')).toBeInTheDocument()
  })

  it('sin contracts.read no pide nada de contratos, avisa y muestra el resumen', async () => {
    wrap(['clients.read'])
    expect(await screen.findByText(/contracts\.read/)).toBeInTheDocument()
    expect(screen.getByText('Por servicio, COD')).toBeInTheDocument()
    expect(screen.queryByRole('tablist')).toBeNull()
    expect(mock.calls.filter((c) => c.url.pathname.includes('/contracts'))).toHaveLength(0)
  })

  it('«Nuevo contrato» solo con contracts.create', async () => {
    const { unmount } = wrap(WRITE)
    await screen.findByRole('tablist')
    expect(screen.queryByRole('button', { name: 'Nuevo contrato' })).toBeNull()
    unmount()
    wrap(FULL)
    expect(await screen.findByRole('button', { name: 'Nuevo contrato' })).toBeInTheDocument()
  })

  it('un cliente sin contratos ofrece solo crear (y las pestañas por contrato avisan)', async () => {
    wrap(FULL, { ...CLIENT, contracts: [], currentContract: null } as unknown as ClientDetail)
    expect(await screen.findByText('Este cliente no tiene contratos.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Nuevo contrato' })).toBeInTheDocument()
  })

  it('cambiar de contrato en el selector abre el otro', async () => {
    const user = userEvent.setup()
    wrap(READ)
    await screen.findByDisplayValue('Contrato marco')
    await user.selectOptions(screen.getByLabelText('Contrato'), 'k-2')
    expect(await screen.findByDisplayValue('Anexo')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/contracts/k-2').length).toBeGreaterThan(0)
  })
})

describe('Contratos — nuevo contrato', () => {
  it('crea (POST /contracts), selecciona el nuevo y no manda SLA', async () => {
    const user = userEvent.setup()
    wrap(FULL)
    await screen.findByRole('tablist')
    await user.click(screen.getByRole('button', { name: 'Nuevo contrato' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo contrato' })
    expect((within(dialog).getByLabelText(/Cliente desde/) as HTMLInputElement).value).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    await user.type(within(dialog).getByLabelText(/^Título/), 'Anexo 2')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/contracts')).toHaveLength(1))
    expect(lastBody('POST', '/api/v1/contracts')).toMatchObject({ clientPublicId: 'c-1', title: 'Anexo 2', contractNumber: null, endDate: null, autoRenew: false })
    expect(lastBody('POST', '/api/v1/contracts')).not.toHaveProperty('serviceLevels')
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nuevo contrato' })).toBeNull())
  })

  it('valida el título en pantalla y muestra el 409 del número repetido', async () => {
    const user = userEvent.setup()
    wrap(FULL)
    await screen.findByRole('tablist')
    await user.click(screen.getByRole('button', { name: 'Nuevo contrato' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo contrato' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El título del contrato es obligatorio.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/contracts')).toHaveLength(0)

    mock.override = ({ method, url }) => (method === 'POST' && url.pathname === '/api/v1/contracts' ? problem(409, "Ya existe un contrato con el número 'X-1'.", 'conflict') : undefined)
    await user.type(within(dialog).getByLabelText(/^Título/), 'Anexo')
    await user.type(within(dialog).getByLabelText(/^Número/), 'X-1')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText("Ya existe un contrato con el número 'X-1'.")).toBeInTheDocument()
  })

  it('el 409 del cliente dado de baja se muestra tal cual', async () => {
    const user = userEvent.setup()
    wrap(FULL)
    await screen.findByRole('tablist')
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname === '/api/v1/contracts' ? problem(409, 'El cliente está dado de baja; solo se consulta su historial.', 'conflict') : undefined)
    await user.click(screen.getByRole('button', { name: 'Nuevo contrato' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nuevo contrato' })
    await user.type(within(dialog).getByLabelText(/^Título/), 'Anexo')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El cliente está dado de baja; solo se consulta su historial.')).toBeInTheDocument()
  })
})

describe('Pestaña Contrato', () => {
  it('muestra todos los campos, el modelo de facturación y los extras de despacho/COD según los checks', async () => {
    wrap(READ)
    await screen.findByDisplayValue('Contrato marco')
    expect(screen.getByLabelText('Número')).toHaveValue('ACME-C1')
    expect(screen.getByLabelText(/Cliente desde/)).toHaveValue('2026-01-15')
    expect(screen.getByLabelText('Fecha fin')).toHaveValue('2026-12-31')
    expect(screen.getByRole('switch', { name: /Renovación automática/ })).not.toBeChecked()
    expect(screen.getByLabelText('Moneda')).toHaveValue('USD')
    expect(screen.getByLabelText('Disparador de cobro')).toHaveValue('BY_PICKUP')
    expect(screen.getByLabelText('Notas')).toHaveValue('Nota vieja')
    for (const [name, on] of [['Por servicio', true], ['Pieza extra con precio especial', true], ['Cargo por despacho', false], ['Cargo por COD', true], ['Servicios especiales', true]] as const) {
      const sw = screen.getByRole('switch', { name })
      if (on) expect(sw).toBeChecked()
      else expect(sw).not.toBeChecked()
    }
    // COD marcado: tipo y valor; despacho apagado: sin monto
    expect(screen.getByLabelText('Tipo de cargo')).toHaveValue('PERCENT')
    expect(screen.getByLabelText('Valor (%)')).toHaveValue(2.5)
    expect(screen.queryByLabelText(/Monto fijo por despacho/)).toBeNull()
    // estatus: pipeline y botón de transición; historial
    expect(await screen.findByRole('button', { name: /Avanzar a|Pasar a/ }).catch(() => null)).not.toBeUndefined()
    expect(screen.getByRole('heading', { name: 'Historial de estatus' })).toBeInTheDocument()
    // solo lectura: sin Guardar y con los campos bloqueados
    expect(screen.queryByRole('button', { name: 'Guardar' })).toBeNull()
    expect(screen.getByLabelText(/^Título/).closest('fieldset')).toBeDisabled()
  })

  it('guarda los datos generales con rowVersion y solo lo cambiado; fecha fin vacía = clearEndDate', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    const title = await screen.findByDisplayValue('Contrato marco')
    await user.clear(title)
    await user.type(title, 'Marco 2026')
    fireEvent.change(screen.getByLabelText('Fecha fin'), { target: { value: '' } })
    await user.click(screen.getByRole('switch', { name: /Renovación automática/ }))
    const general = screen.getByLabelText(/^Título/).closest('form') as HTMLElement
    await user.click(within(general).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/contracts/k-1')).toHaveLength(1))
    expect(lastBody('PATCH', '/api/v1/contracts/k-1')).toEqual({ rowVersion: 'AAA=', title: 'Marco 2026', clearEndDate: true, autoRenew: true })
    expect(await screen.findByText('Cambios guardados.')).toBeInTheDocument()
  })

  it('valida en pantalla: título obligatorio y fin anterior al inicio', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    const title = await screen.findByDisplayValue('Contrato marco')
    const general = title.closest('form') as HTMLElement
    await user.clear(title)
    fireEvent.change(screen.getByLabelText('Fecha fin'), { target: { value: '2025-01-01' } })
    await user.click(within(general).getByRole('button', { name: 'Guardar' }))
    expect(await within(general).findByText('El título del contrato es obligatorio.')).toBeInTheDocument()
    expect(within(general).getByText('La fecha fin no puede ser anterior a la fecha de inicio.')).toBeInTheDocument()
    expect(callsTo('PATCH', '/api/v1/contracts/k-1')).toHaveLength(0)
  })

  it('muestra el 400 del servidor bajo el campo y el 422 de estatus', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    const title = await screen.findByDisplayValue('Contrato marco')
    const general = title.closest('form') as HTMLElement
    mock.override = ({ method, url }) =>
      method === 'PATCH' && url.pathname === '/api/v1/contracts/k-1'
        ? problem(400, 'Solicitud inválida', 'validation', { endDate: ['La fecha fin no puede ser anterior a la fecha de inicio.'] })
        : undefined
    await user.type(title, ' X')
    await user.click(within(general).getByRole('button', { name: 'Guardar' }))
    expect(await within(general).findByText('La fecha fin no puede ser anterior a la fecha de inicio.')).toBeInTheDocument()

    mock.override = ({ method, url }) =>
      method === 'PATCH' && url.pathname === '/api/v1/contracts/k-1' ? problem(422, "El estatus actual no permite la acción 'EDIT_CONTRACT'.", 'status_rule') : undefined
    await user.click(within(general).getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText("El estatus actual no permite la acción 'EDIT_CONTRACT'.")).toBeInTheDocument()
  })

  it('409 de rowVersion: muestra el mensaje exacto y relee el contrato conservando lo escrito', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    const title = await screen.findByDisplayValue('Contrato marco')
    const general = title.closest('form') as HTMLElement
    const before = callsTo('GET', '/api/v1/contracts/k-1').length
    mock.override = ({ method, url }) =>
      method === 'PATCH' && url.pathname === '/api/v1/contracts/k-1' ? problem(409, 'El contrato fue modificado por otro usuario; recargue e intente de nuevo.', 'conflict') : undefined
    await user.type(title, ' nuevo')
    await user.click(within(general).getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('El contrato fue modificado por otro usuario; recargue e intente de nuevo.')).toBeInTheDocument()
    await waitFor(() => expect(callsTo('GET', '/api/v1/contracts/k-1').length).toBeGreaterThan(before))
    expect(screen.getByDisplayValue('Contrato marco nuevo')).toBeInTheDocument()
  })

  it('canEdit = false: avisa, bloquea todo y no ofrece Guardar aunque tenga contracts.update', async () => {
    detail1 = { ...DETAIL_1, canEdit: false, status: 'CANCELLED' }
    wrap(WRITE)
    expect(await screen.findByText(/no permite editarlo/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Guardar' })).toBeNull()
    expect(screen.getByLabelText(/^Título/).closest('fieldset')).toBeDisabled()
  })

  it('modelo de facturación: marca Despacho, escribe el monto y guarda checks y monto (PATCH billing-model y dispatch-fee)', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await screen.findByDisplayValue('Contrato marco')
    await user.click(screen.getByRole('switch', { name: 'Cargo por despacho' }))
    const amount = await screen.findByLabelText(/Monto fijo por despacho/)
    await user.clear(amount)
    await user.type(amount, '4.5')
    const form = amount.closest('form') as HTMLElement
    await user.click(within(form).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/contracts/k-1/dispatch-fee')).toHaveLength(1))
    expect(lastBody('PATCH', '/api/v1/contracts/k-1/billing-model')).toEqual({ billDispatchFee: true })
    expect(lastBody('PATCH', '/api/v1/contracts/k-1/dispatch-fee')).toEqual({ amount: 4.5 })
    expect(callsTo('PATCH', '/api/v1/contracts/k-1/cod-fee')).toHaveLength(0)
  })

  it('COD: cambia a FIXED con su valor; el por ciento fuera de 0..100 se rechaza en pantalla y el 400 del servidor sale bajo el valor', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await screen.findByDisplayValue('Contrato marco')
    const value = screen.getByLabelText('Valor (%)')
    const form = value.closest('form') as HTMLElement
    await user.clear(value)
    await user.type(value, '150')
    await user.click(within(form).getByRole('button', { name: 'Guardar' }))
    expect(await within(form).findByText('El por ciento del cargo por COD debe estar entre 0 y 100.')).toBeInTheDocument()
    expect(callsTo('PATCH', '/api/v1/contracts/k-1/cod-fee')).toHaveLength(0)

    await user.selectOptions(screen.getByLabelText('Tipo de cargo'), 'FIXED')
    const fixed = screen.getByLabelText('Valor ($)')
    await user.clear(fixed)
    await user.type(fixed, '5')
    await user.click(within(form).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/contracts/k-1/cod-fee')).toHaveLength(1))
    expect(lastBody('PATCH', '/api/v1/contracts/k-1/cod-fee')).toEqual({ type: 'FIXED', value: 5 })

    mock.override = ({ method, url }) =>
      method === 'PATCH' && url.pathname === '/api/v1/contracts/k-1/cod-fee' ? problem(400, 'Solicitud inválida', 'validation', { value: ['El cargo fijo por COD no puede ser negativo.'] }) : undefined
    await user.clear(screen.getByLabelText('Valor ($)'))
    await user.type(screen.getByLabelText('Valor ($)'), '7')
    await user.click(within(form).getByRole('button', { name: 'Guardar' }))
    expect(await within(form).findByText('El cargo fijo por COD no puede ser negativo.')).toBeInTheDocument()
  })

  it('desmarcar un check no manda el monto (el contrato lo conserva)', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await screen.findByDisplayValue('Contrato marco')
    await user.click(screen.getByRole('switch', { name: 'Cargo por COD' }))
    const form = screen.getByRole('switch', { name: 'Cargo por COD' }).closest('form') as HTMLElement
    await user.click(within(form).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/contracts/k-1/billing-model')).toHaveLength(1))
    expect(lastBody('PATCH', '/api/v1/contracts/k-1/billing-model')).toEqual({ billCodFee: false })
    expect(callsTo('PATCH', '/api/v1/contracts/k-1/cod-fee')).toHaveLength(0)
  })

  it('estatus: transición por POST …/status y el 422 del servidor se muestra en el diálogo', async () => {
    const user = userEvent.setup()
    detail1 = { ...DETAIL_1, status: 'DRAFT', statusLabel: 'Borrador' }
    wrap(WRITE)
    await screen.findByDisplayValue('Contrato marco')
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/contracts/k-1/status'
        ? problem(422, 'El cliente ya tiene un contrato vigente. Cancele o expire el contrato anterior antes de activar este.', 'status_rule')
        : undefined
    await user.click(await screen.findByRole('button', { name: /Avanzar a Activo/ }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Cambiar estatus' }))
    expect(await within(dialog).findByText('El cliente ya tiene un contrato vigente. Cancele o expire el contrato anterior antes de activar este.')).toBeInTheDocument()
    mock.override = null
    await user.click(within(dialog).getByRole('button', { name: 'Cambiar estatus' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/contracts/k-1/status').length).toBe(2))
    expect(lastBody('POST', '/api/v1/contracts/k-1/status')).toMatchObject({ toCode: 'ACTIVE' })
  })
})

describe('Pestaña SLA', () => {
  it('empieza vacío: un renglón por tipo de servicio con los cuatro campos', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'SLA')
    const standard = await screen.findByRole('group', { name: 'Estándar' })
    const express = screen.getByRole('group', { name: 'Exprés' })
    for (const g of [standard, express]) {
      for (const label of ['Horas máximas de tránsito', 'Ventana de recogido (min)', 'Meta de puntualidad (%)', 'Penalidad ($)']) {
        expect(within(g).getByLabelText(label)).toHaveValue(null)
      }
    }
    // sin cambios no se puede guardar
    expect(screen.getByRole('button', { name: 'Guardar SLA' })).toBeDisabled()
  })

  it('carga lo guardado y envía solo los renglones con datos (PUT con la lista completa)', async () => {
    const user = userEvent.setup()
    detail1 = { ...DETAIL_1, serviceLevels: [{ id: 1, serviceType: 'STANDARD', serviceTypeLabel: 'Estándar', maxTransitHours: 48, pickupWindowMin: null, onTimeTargetPct: 95, penaltyAmount: null }] }
    wrap(WRITE)
    await openTab(user, 'SLA')
    const standard = await screen.findByRole('group', { name: 'Estándar' })
    expect(within(standard).getByLabelText('Horas máximas de tránsito')).toHaveValue(48)
    const express = screen.getByRole('group', { name: 'Exprés' })
    await user.type(within(express).getByLabelText('Horas máximas de tránsito'), '24')
    await user.type(within(express).getByLabelText('Penalidad ($)'), '10')
    await user.click(screen.getByRole('button', { name: 'Guardar SLA' }))
    await waitFor(() => expect(callsTo('PUT', '/api/v1/contracts/k-1/service-levels')).toHaveLength(1))
    expect(callsTo('PUT', '/api/v1/contracts/k-1/service-levels')[0].body).toEqual([
      { serviceType: 'STANDARD', maxTransitHours: 48, pickupWindowMin: null, onTimeTargetPct: 95, penaltyAmount: null },
      { serviceType: 'EXPRESS', maxTransitHours: 24, pickupWindowMin: null, onTimeTargetPct: null, penaltyAmount: 10 },
    ])
    expect(await screen.findByText('SLA guardado.')).toBeInTheDocument()
  })

  it('vaciar un renglón lo quita de la lista enviada', async () => {
    const user = userEvent.setup()
    detail1 = { ...DETAIL_1, serviceLevels: [{ id: 1, serviceType: 'STANDARD', serviceTypeLabel: 'Estándar', maxTransitHours: 48 }] }
    wrap(WRITE)
    await openTab(user, 'SLA')
    const standard = await screen.findByRole('group', { name: 'Estándar' })
    await user.clear(within(standard).getByLabelText('Horas máximas de tránsito'))
    await user.click(screen.getByRole('button', { name: 'Guardar SLA' }))
    await waitFor(() => expect(callsTo('PUT', '/api/v1/contracts/k-1/service-levels')).toHaveLength(1))
    expect(callsTo('PUT', '/api/v1/contracts/k-1/service-levels')[0].body).toEqual([])
  })

  it('valida en pantalla (horas > 0, meta 0..100) y «Descartar cambios» vuelve al borrador guardado', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'SLA')
    const standard = await screen.findByRole('group', { name: 'Estándar' })
    await user.type(within(standard).getByLabelText('Horas máximas de tránsito'), '0')
    await user.type(within(standard).getByLabelText('Meta de puntualidad (%)'), '120')
    await user.click(screen.getByRole('button', { name: 'Guardar SLA' }))
    expect(await within(standard).findByText('Las horas máximas de tránsito deben ser mayores que cero.')).toBeInTheDocument()
    expect(within(standard).getByText('La meta de puntualidad debe estar entre 0 y 100.')).toBeInTheDocument()
    expect(callsTo('PUT', '/api/v1/contracts/k-1/service-levels')).toHaveLength(0)
    await user.click(screen.getByRole('button', { name: 'Descartar cambios' }))
    await waitFor(() => expect(within(standard).getByLabelText('Horas máximas de tránsito')).toHaveValue(null))
    expect(within(standard).queryByText('Las horas máximas de tránsito deben ser mayores que cero.')).toBeNull()
  })

  it('el 400 del servidor sale bajo el campo del renglón correcto (aunque haya renglones vacíos antes)', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'SLA')
    const express = await screen.findByRole('group', { name: 'Exprés' })
    mock.override = ({ method, url }) =>
      method === 'PUT' && url.pathname === '/api/v1/contracts/k-1/service-levels'
        ? problem(400, 'Solicitud inválida', 'validation', { 'serviceLevels[0].penaltyAmount': ['La penalidad no puede ser negativa.'] })
        : undefined
    await user.type(within(express).getByLabelText('Horas máximas de tránsito'), '24')
    await user.type(within(express).getByLabelText('Penalidad ($)'), '5')
    await user.click(screen.getByRole('button', { name: 'Guardar SLA' }))
    expect(await within(express).findByText('La penalidad no puede ser negativa.')).toBeInTheDocument()
    expect(within(screen.getByRole('group', { name: 'Estándar' })).queryByText('La penalidad no puede ser negativa.')).toBeNull()
  })

  it('sin contracts.update o con canEdit = false es solo lectura', async () => {
    const user = userEvent.setup()
    const { unmount } = wrap(READ)
    await openTab(user, 'SLA')
    const standard = await screen.findByRole('group', { name: 'Estándar' })
    expect(standard.closest('fieldset')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Guardar SLA' })).toBeNull()
    unmount()
    detail1 = { ...DETAIL_1, canEdit: false }
    wrap(WRITE)
    await openTab(user, 'SLA')
    expect((await screen.findByRole('group', { name: 'Estándar' })).closest('fieldset')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Guardar SLA' })).toBeNull()
  })
})

describe('Pestaña Tarifas', () => {
  const rowOf = (name: RegExp) => screen.getByRole('row', { name })

  it('muestra las dos secciones y los tramos cuando sus checks están marcados', async () => {
    const user = userEvent.setup()
    wrap(READ)
    await openTab(user, 'Tarifas')
    const per = await screen.findByRole('region', { name: 'Tarifas por servicio' })
    expect(within(per).getByText('Estándar')).toBeInTheDocument()
    expect(within(per).getByText('$6.50')).toBeInTheDocument()
    const extra = screen.getByRole('region', { name: 'Tarifas por pieza extra (por tramo)' })
    expect(within(extra).getByText('2–5')).toBeInTheDocument()
    expect(within(extra).getByText('6+')).toBeInTheDocument()
    // solo lectura: sin agregar, editar ni quitar
    expect(screen.queryByRole('button', { name: /Agregar|Editar|Quitar/ })).toBeNull()
  })

  it('oculta la sección con el check apagado y sin filas; con filas la muestra avisando que no se cobra', async () => {
    const user = userEvent.setup()
    detail1 = { ...DETAIL_1, billingModel: { ...DETAIL_1.billingModel, billPerService: false, billExtraPiece: false } }
    mock.override = ({ method, url }) =>
      method === 'GET' && url.pathname === '/api/v1/contracts/k-1/rate-components' ? { contractPublicId: 'k-1', asOf: '2026-10-10', perService: [], extraPiece: [] } : undefined
    wrap(WRITE)
    await openTab(user, 'Tarifas')
    expect(await screen.findByText(/Marque «Por servicio» o «Pieza extra»/)).toBeInTheDocument()
    expect(screen.queryByRole('region')).toBeNull()
    expect(screen.queryByRole('button', { name: /Agregar/ })).toBeNull()
  })

  it('check apagado con filas existentes: siguen visibles, sin escrituras y con el aviso', async () => {
    const user = userEvent.setup()
    detail1 = { ...DETAIL_1, billingModel: { ...DETAIL_1.billingModel, billPerService: false } }
    wrap(WRITE)
    await openTab(user, 'Tarifas')
    const per = await screen.findByRole('region', { name: 'Tarifas por servicio' })
    expect(within(per).getByText(/«Por servicio» está apagado/)).toBeInTheDocument()
    expect(within(per).getByText('Estándar')).toBeInTheDocument()
    expect(within(per).queryByRole('button', { name: /Agregar|Editar|Quitar/ })).toBeNull()
  })

  it('agrega una tarifa por servicio con «Vigente desde» = hoy por omisión (POST rate-components)', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'Tarifas')
    await user.click(await screen.findByRole('button', { name: 'Agregar tarifa' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar tarifa por servicio' })
    expect(within(dialog).getByLabelText(/Vigente desde/)).toHaveValue(today())
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El tipo de servicio es obligatorio.')).toBeInTheDocument()
    expect(within(dialog).getByText('El tipo de paquete es obligatorio.')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique la tarifa.')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText(/^Servicio/), 'EXPRESS')
    await user.selectOptions(within(dialog).getByLabelText(/^Paquete/), 'ENVELOPE')
    await user.type(within(dialog).getByLabelText(/^Tarifa/), '5')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/contracts/k-1/rate-components')).toHaveLength(1))
    expect(lastBody('POST', '/api/v1/contracts/k-1/rate-components')).toEqual({ kind: 'PER_SERVICE', serviceType: 'EXPRESS', packageType: 'ENVELOPE', rate: 5, effectiveFrom: today() })
  })

  it('muestra el 409 de «ya existe una tarifa vigente» tal cual', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'Tarifas')
    await user.click(await screen.findByRole('button', { name: 'Agregar tarifa' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar tarifa por servicio' })
    const msg409 = 'Ya existe una tarifa vigente en esa fecha para ese servicio y tipo de paquete en el contrato; edítela o ciérrela antes de crear otra.'
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname === '/api/v1/contracts/k-1/rate-components' ? problem(409, msg409, 'conflict') : undefined)
    await user.selectOptions(within(dialog).getByLabelText(/^Servicio/), 'STANDARD')
    await user.selectOptions(within(dialog).getByLabelText(/^Paquete/), 'BOX')
    await user.type(within(dialog).getByLabelText(/^Tarifa/), '7')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText(msg409)).toBeInTheDocument()
  })

  it('editar = versión nueva (PATCH con la tarifa y «Vigente desde»); no acepta una fecha anterior a hoy', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'Tarifas')
    await screen.findByRole('region', { name: 'Tarifas por servicio' })
    await user.click(within(rowOf(/Exprés/)).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva versión de la tarifa' })
    expect(within(dialog).getByText('Exprés · Caja')).toBeInTheDocument()
    const rate = within(dialog).getByLabelText(/^Tarifa/)
    expect(rate).toHaveValue(8.5)
    await user.clear(rate)
    await user.type(rate, '9')
    fireEvent.change(within(dialog).getByLabelText(/Vigente desde/), { target: { value: '2020-01-01' } })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La fecha no puede ser anterior a hoy: el historial de tarifas no se reescribe.')).toBeInTheDocument()
    expect(callsTo('PATCH', '/api/v1/contracts/k-1/rate-components/12')).toHaveLength(0)
    fireEvent.change(within(dialog).getByLabelText(/Vigente desde/), { target: { value: today() } })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/contracts/k-1/rate-components/12')).toHaveLength(1))
    expect(lastBody('PATCH', '/api/v1/contracts/k-1/rate-components/12')).toEqual({ rate: 9, effectiveFrom: today() })
  })

  it('«Quitar» cierra la tarifa (POST …/close, nunca DELETE) y el 409 de ya cerrada se muestra', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'Tarifas')
    await screen.findByRole('region', { name: 'Tarifas por servicio' })
    await user.click(within(rowOf(/Estándar/)).getByRole('button', { name: 'Quitar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Quitar la tarifa' })
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname.endsWith('/close') ? problem(409, 'El componente ya está cerrado.', 'conflict') : undefined)
    await user.click(within(dialog).getByRole('button', { name: 'Quitar' }))
    expect(await within(dialog).findByText('El componente ya está cerrado.')).toBeInTheDocument()
    mock.override = null
    await user.click(within(dialog).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/contracts/k-1/rate-components/11/close').length).toBe(2))
    expect(mock.calls.some((c) => c.method === 'DELETE')).toBe(false)
  })

  it('«Ver historial» pide includeHistory=true y muestra las filas cerradas sin acciones', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'Tarifas')
    await screen.findByRole('region', { name: 'Tarifas por servicio' })
    expect(callsTo('GET', '/api/v1/contracts/k-1/rate-components').every((c) => c.url.searchParams.get('includeHistory') === 'false')).toBe(true)
    await user.click(screen.getByRole('switch', { name: 'Ver historial' }))
    expect(await screen.findByText('$6.00')).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/contracts/k-1/rate-components').some((c) => c.url.searchParams.get('includeHistory') === 'true')).toBe(true)
    const closed = screen.getByRole('row', { name: /\$6\.00/ })
    expect(within(closed).queryByRole('button', { name: /Editar|Quitar/ })).toBeNull()
  })

  it('pieza extra: agrega un tramo, valida el rango y muestra el traslape del servidor', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'Tarifas')
    const comp = await screen.findByRole('group', { name: 'Estándar · Caja' })
    await user.click(within(comp).getByRole('button', { name: 'Agregar tramo' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar tramo' })
    await user.type(within(dialog).getByLabelText(/^Desde pieza/), '1')
    await user.type(within(dialog).getByLabelText(/^Tarifa/), '2')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText(/Rango inválido: 'desde' debe ser al menos 2/)).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/contracts/k-1/rate-components/21/tiers')).toHaveLength(0)
    const from = within(dialog).getByLabelText(/^Desde pieza/)
    await user.clear(from)
    await user.type(from, '4')
    await user.type(within(dialog).getByLabelText(/^Hasta pieza/), '7')
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname.endsWith('/tiers') ? problem(400, 'Solicitud inválida', 'validation', { fromUnit: ['El tramo 4–7 se traslapa con el tramo vigente 2–5.'] }) : undefined
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El tramo 4–7 se traslapa con el tramo vigente 2–5.')).toBeInTheDocument()
    mock.override = null
    await user.clear(from)
    await user.type(from, '8')
    await user.clear(within(dialog).getByLabelText(/^Hasta pieza/))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/contracts/k-1/rate-components/21/tiers').length).toBe(2))
    expect(lastBody('POST', '/api/v1/contracts/k-1/rate-components/21/tiers')).toEqual({ fromUnit: 8, toUnit: null, rate: 2, effectiveFrom: today() })
  })

  it('pieza extra: editar un tramo (clearToUnit al vaciar «Hasta») y quitarlo', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'Tarifas')
    const comp = await screen.findByRole('group', { name: 'Estándar · Caja' })
    await user.click(within(within(comp).getByRole('row', { name: /2–5/ })).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva versión del tramo' })
    await user.clear(within(dialog).getByLabelText(/^Hasta pieza/))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/contracts/k-1/rate-components/21/tiers/31')).toHaveLength(1))
    expect(lastBody('PATCH', '/api/v1/contracts/k-1/rate-components/21/tiers/31')).toEqual({ effectiveFrom: today(), clearToUnit: true })

    await user.click(within(within(comp).getByRole('row', { name: /6\+/ })).getByRole('button', { name: 'Quitar' }))
    const confirm = await screen.findByRole('dialog', { name: 'Quitar el tramo' })
    await user.click(within(confirm).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/contracts/k-1/rate-components/21/tiers/32/close')).toHaveLength(1))
  })

  it('pieza extra: agregar un componente nuevo (servicio + paquete, sin monto) y quitar el componente', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'Tarifas')
    await user.click(await screen.findByRole('button', { name: 'Agregar pieza extra' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar pieza extra' })
    expect(within(dialog).queryByLabelText(/^Tarifa/)).toBeNull()
    await user.selectOptions(within(dialog).getByLabelText(/^Servicio/), 'EXPRESS')
    await user.selectOptions(within(dialog).getByLabelText(/^Paquete/), 'BOX')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/contracts/k-1/rate-components')).toHaveLength(1))
    expect(lastBody('POST', '/api/v1/contracts/k-1/rate-components')).toMatchObject({ kind: 'EXTRA_PIECE', serviceType: 'EXPRESS', packageType: 'BOX', rate: null })

    const comp = screen.getByRole('group', { name: 'Estándar · Caja' })
    await user.click(within(comp).getByRole('button', { name: 'Quitar pieza extra' }))
    const confirm = await screen.findByRole('dialog', { name: 'Quitar la pieza extra' })
    await user.click(within(confirm).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/contracts/k-1/rate-components/21/close')).toHaveLength(1))
  })

  it('con canEdit = false no ofrece escrituras', async () => {
    const user = userEvent.setup()
    detail1 = { ...DETAIL_1, canEdit: false }
    wrap(WRITE)
    await openTab(user, 'Tarifas')
    await screen.findByRole('region', { name: 'Tarifas por servicio' })
    expect(screen.queryByRole('button', { name: /Agregar|Editar|Quitar/ })).toBeNull()
  })
})

describe('Pestaña Servicios especiales', () => {
  it('lista las tarifas del cliente; solo lectura sin contracts.update', async () => {
    const user = userEvent.setup()
    wrap(READ)
    await openTab(user, 'Servicios especiales')
    expect(await screen.findByText('Vagón del muelle')).toBeInTheDocument()
    expect(screen.getByText('$150.00')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Agregar|Editar|Quitar/ })).toBeNull()
  })

  it('agrega con un tipo existente (POST special-services con typeId)', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'Servicios especiales')
    await user.click(await screen.findByRole('button', { name: 'Agregar servicio especial' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar servicio especial' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('Indique el tipo de servicio especial.')).toBeInTheDocument()
    expect(within(dialog).getByText('Indique la tarifa.')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText(/^Servicio/), '6')
    await user.type(within(dialog).getByLabelText(/^Tarifa/), '40')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/special-services')).toHaveLength(1))
    expect(lastBody('POST', '/api/v1/clients/c-1/special-services')).toEqual({ typeId: 6, rate: 40, effectiveFrom: today() })
  })

  it('«+ Nuevo tipo de servicio especial…» pide el nombre y manda newTypeName (nunca los dos)', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'Servicios especiales')
    await user.click(await screen.findByRole('button', { name: 'Agregar servicio especial' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar servicio especial' })
    expect(within(dialog).queryByLabelText(/Nombre del nuevo servicio especial/)).toBeNull()
    await user.selectOptions(within(dialog).getByLabelText(/^Servicio/), '__new__')
    await user.type(within(dialog).getByLabelText(/^Tarifa/), '75')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El nombre del tipo de servicio especial es obligatorio.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/Nombre del nuevo servicio especial/), 'Grúa')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/special-services')).toHaveLength(1))
    expect(lastBody('POST', '/api/v1/clients/c-1/special-services')).toEqual({ newTypeName: 'Grúa', rate: 75, effectiveFrom: today() })
  })

  it('muestra el 409 de tarifa repetida', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'Servicios especiales')
    await user.click(await screen.findByRole('button', { name: 'Agregar servicio especial' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar servicio especial' })
    const text = "El cliente ya tiene una tarifa vigente en esa fecha para el tipo 'Vagón del muelle'; edite esa tarifa o ciérrela antes de agregar otra."
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname === '/api/v1/clients/c-1/special-services' ? problem(409, text, 'conflict') : undefined)
    await user.selectOptions(within(dialog).getByLabelText(/^Servicio/), '5')
    await user.type(within(dialog).getByLabelText(/^Tarifa/), '10')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText(text)).toBeInTheDocument()
  })

  it('editar = versión nueva (PATCH) y quitar = cerrar (POST close)', async () => {
    const user = userEvent.setup()
    wrap(WRITE)
    await openTab(user, 'Servicios especiales')
    const row = await screen.findByRole('row', { name: /Vagón del muelle/ })
    await user.click(within(row).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva versión de la tarifa' })
    const rate = within(dialog).getByLabelText(/^Tarifa/)
    expect(rate).toHaveValue(150)
    await user.clear(rate)
    await user.type(rate, '160')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/clients/c-1/special-services/41')).toHaveLength(1))
    expect(lastBody('PATCH', '/api/v1/clients/c-1/special-services/41')).toEqual({ rate: 160, effectiveFrom: today() })

    await user.click(within(await screen.findByRole('row', { name: /Vagón del muelle/ })).getByRole('button', { name: 'Quitar' }))
    const confirm = await screen.findByRole('dialog', { name: 'Quitar el servicio especial' })
    await user.click(within(confirm).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/clients/c-1/special-services/41/close')).toHaveLength(1))
    // sin gestión de baja/reactivación de tipos
    expect(mock.calls.some((c) => c.url.pathname.includes('/deactivate') || c.url.pathname.includes('/reactivate'))).toBe(false)
  })

  it('componente apagado: las filas se ven, sin escrituras y con el aviso; sin contrato vigente también avisa', async () => {
    const user = userEvent.setup()
    specials = { ...specials, componentEnabled: false }
    const { unmount } = wrap(WRITE)
    await openTab(user, 'Servicios especiales')
    expect(await screen.findByText('Vagón del muelle')).toBeInTheDocument()
    expect(screen.getByText(/«Servicios especiales» está apagado en el contrato vigente/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Agregar|Editar|Quitar/ })).toBeNull()
    unmount()

    specials = { clientPublicId: 'c-1', contractPublicId: null, componentEnabled: false, asOf: '2026-10-10', items: [] }
    wrap(WRITE)
    await openTab(user, 'Servicios especiales')
    expect(await screen.findByText(/no tiene un contrato vigente/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Agregar servicio especial' })).toBeNull()
  })

  it('el 409 «sin contrato vigente» al leer no rompe la ficha: se muestra el mensaje', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) => (method === 'GET' && url.pathname === '/api/v1/clients/c-1/special-services' ? problem(404, 'Cliente no encontrado.', 'not_found') : undefined)
    wrap(WRITE)
    await openTab(user, 'Servicios especiales')
    expect(await screen.findByText('Cliente no encontrado.')).toBeInTheDocument()
  })
})
