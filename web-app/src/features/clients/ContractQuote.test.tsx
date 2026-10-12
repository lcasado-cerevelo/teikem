// F-A3 — Cotizador de la sección de contratos (pestaña «Cotizador»): permisos, líneas, COD y fecha, desglose del resultado, qué contrato
// se usó, líneas sin tarifa y errores exactos del servidor. Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ClientContractsSection } from './ClientContractsSection'
import type { ClientDetail } from './clientRules'

interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | undefined
  headers: Headers
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], override: null as null | ((call: Call) => unknown), quote: null as unknown }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    const call = { method: req.method, url: new URL(req.url), body: text ? JSON.parse(text) : undefined, headers: req.headers }
    mock.calls.push(call)
    const body = mock.override?.(call) ?? route(call)
    return body instanceof Response ? body : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const SUMMARY = { id: 1, publicId: 'k-1', contractNumber: 'ACME-C1', title: 'Contrato marco', startDate: '2026-01-15', endDate: null, status: 'ACTIVE', statusLabel: 'Vigente', isActive: true, isCurrent: true }
const CLIENT = { id: 10, publicId: 'c-1', code: 'ACME', name: 'Acme Corp', status: 'ACTIVE', isActive: true, contracts: [SUMMARY], currentContract: SUMMARY, billingSummary: 'Por servicio, COD' } as unknown as ClientDetail

const QUOTE_OK = {
  contractPublicId: 'k-1',
  contractStatus: 'ACTIVE',
  asOf: '2026-10-12',
  lines: [
    { serviceType: 'STANDARD', packageType: 'BOX', pieces: 8, baseRate: 7, baseSource: 'CONTRACT', extraPieces: 6.25, extraSource: 'CONTRACT', lineTotal: 21.25 },
    { serviceType: 'STANDARD', packageType: 'ENVELOPE', pieces: 1, baseRate: 5, baseSource: 'GENERIC', extraPieces: 0, extraSource: 'NONE', lineTotal: 5 },
  ],
  dispatchFee: 3,
  codFee: 5,
  total: 26.25,
  rateComponentIds: [11, 21],
}

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/billing/contract-rate-quote' && method === 'POST') return mock.quote
  if (p === '/api/v1/contracts/k-1') return { id: 1, publicId: 'k-1', contractNumber: 'ACME-C1', title: 'Contrato marco', status: 'ACTIVE', statusLabel: 'Vigente', currency: 'USD', canEdit: true, billingModel: {}, serviceLevels: [], rowVersion: 'AAA=' }
  if (p === '/api/v1/catalogs/ServiceType') return [{ code: 'STANDARD', label: 'Estándar', isEnabled: true, sortOrder: 1 }, { code: 'EXPRESS', label: 'Exprés', isEnabled: true, sortOrder: 2 }]
  if (p === '/api/v1/catalogs/PackageType') return [{ code: 'BOX', label: 'Caja', isEnabled: true, sortOrder: 1 }, { code: 'ENVELOPE', label: 'Sobre', isEnabled: true, sortOrder: 2 }]
  if (p.startsWith('/api/v1/status/') || p.startsWith('/api/v1/catalogs/')) return []
  return problem(404, 'No encontrado', 'not_found')
}

function wrap(permissions: string[] = ['clients.read', 'contracts.read'], client: ClientDetail = CLIENT) {
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

const quoteCalls = () => mock.calls.filter((c) => c.method === 'POST' && c.url.pathname === '/api/v1/billing/contract-rate-quote')

async function openQuote(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByRole('tablist')
  await user.click(screen.getByRole('tab', { name: 'Cotizador' }))
  return await screen.findByRole('group', { name: 'Línea 1' })
}

async function fillLine(user: ReturnType<typeof userEvent.setup>, line: HTMLElement, service: string, pack: string, pieces: string) {
  await user.selectOptions(within(line).getByLabelText(/^Servicio/), service)
  await user.selectOptions(within(line).getByLabelText(/^Paquete/), pack)
  const input = within(line).getByLabelText(/^Piezas/)
  await user.clear(input)
  await user.type(input, pieces)
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.override = null
  mock.quote = QUOTE_OK
})

describe('Cotizador — acceso', () => {
  it('con contracts.read aparece la pestaña «Cotizador» al final y no pide nada hasta cotizar', async () => {
    const user = userEvent.setup()
    wrap()
    const line = await openQuote(user)
    expect(line).toBeInTheDocument()
    expect((await screen.findAllByRole('tab')).map((x) => x.textContent)).toEqual(['Contrato', 'Tarifas', 'SLA', 'Servicios especiales', 'Cotizador'])
    expect(quoteCalls()).toHaveLength(0)
  })

  it('sin contracts.read no hay pestañas ni cotizador', async () => {
    wrap(['clients.read'])
    expect(await screen.findByText(/contracts\.read/)).toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Cotizador' })).toBeNull()
  })
})

describe('Cotizador — cotizar', () => {
  it('manda las líneas, el COD y la fecha; sin Idempotency-Key compartida; muestra el desglose y el contrato usado', async () => {
    const user = userEvent.setup()
    wrap()
    const line = await openQuote(user)
    await fillLine(user, line, 'STANDARD', 'BOX', '8')
    await user.click(screen.getByRole('button', { name: 'Agregar línea' }))
    await fillLine(user, await screen.findByRole('group', { name: 'Línea 2' }), 'STANDARD', 'ENVELOPE', '1')
    await user.type(screen.getByLabelText('Monto COD'), '200')
    await user.type(screen.getByLabelText('Cotizar a la fecha'), '2026-10-12')
    await user.click(screen.getByRole('button', { name: 'Cotizar' }))

    await waitFor(() => expect(quoteCalls()).toHaveLength(1))
    expect(quoteCalls()[0].body).toEqual({
      clientPublicId: 'c-1',
      lines: [
        { serviceType: 'STANDARD', packageType: 'BOX', pieces: 8 },
        { serviceType: 'STANDARD', packageType: 'ENVELOPE', pieces: 1 },
      ],
      codAmount: 200,
      asOf: '2026-10-12',
    })
    expect(quoteCalls()[0].headers.get('Idempotency-Key')).toBeTruthy()

    const res = await screen.findByRole('region', { name: 'Resultado de la cotización' })
    // contrato usado (número, título, estatus del catálogo y fecha)
    expect(within(res).getByTestId('quote-contract').textContent).toMatch(/Se usó el contrato ACME-C1 · Contrato marco \(Vigente\), vigente al /)
    // desglose por línea: el subtotal NO incluye despacho ni COD (13.25 = 7 + 6.25), y el origen de cada monto
    const rows = within(res).getAllByRole('row')
    expect(within(rows[1]).getByText('Estándar')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Caja')).toBeInTheDocument()
    expect(within(rows[1]).getAllByText('Contrato')).toHaveLength(2)
    expect(within(rows[1]).getByText(/13[.,]25/)).toBeInTheDocument()
    expect(within(rows[2]).getByText('Genérica')).toBeInTheDocument()
    expect(within(rows[2]).getByText('Sin tarifa')).toBeInTheDocument()
    // despacho, COD y total del servidor
    const dispatch = within(res).getByText('Cargo por despacho').nextElementSibling
    const cod = within(res).getByText('Cargo por COD').nextElementSibling
    const total = within(res).getByText('Total').nextElementSibling
    expect(dispatch?.textContent).toMatch(/3[.,]00/)
    expect(cod?.textContent).toMatch(/5[.,]00/)
    expect(total?.textContent).toMatch(/26[.,]25/)
    expect(within(res).queryByText(/No hay tarifa vigente/)).toBeNull()
  })

  it('sin COD ni fecha manda null en ambos (el servidor usa 0 y hoy)', async () => {
    const user = userEvent.setup()
    wrap()
    const line = await openQuote(user)
    await fillLine(user, line, 'EXPRESS', 'BOX', '2')
    await user.click(screen.getByRole('button', { name: 'Cotizar' }))
    await waitFor(() => expect(quoteCalls()).toHaveLength(1))
    expect(quoteCalls()[0].body).toMatchObject({ codAmount: null, asOf: null })
  })

  it('una línea sin tarifa se avisa con el servicio y el paquete (el servidor la cotiza en 0, sin error)', async () => {
    mock.quote = {
      contractPublicId: 'k-1',
      contractStatus: 'ACTIVE',
      asOf: '2026-10-12',
      lines: [{ serviceType: 'EXPRESS', packageType: 'ENVELOPE', pieces: 3, baseRate: null, baseSource: 'NONE', extraPieces: 0, extraSource: 'NONE', lineTotal: 0 }],
      dispatchFee: 0,
      codFee: 0,
      total: 0,
      rateComponentIds: [],
    }
    const user = userEvent.setup()
    wrap()
    const line = await openQuote(user)
    await fillLine(user, line, 'EXPRESS', 'ENVELOPE', '3')
    await user.click(screen.getByRole('button', { name: 'Cotizar' }))
    expect(await screen.findByText('No hay tarifa vigente para Exprés / Sobre: esa línea se cotiza en 0.')).toBeInTheDocument()
  })

  it('sin contrato vigente avisa que se usaron las tarifas genéricas y que no hay despacho ni COD', async () => {
    mock.quote = { contractPublicId: null, contractStatus: null, asOf: '2026-10-12', lines: [{ serviceType: 'STANDARD', packageType: 'BOX', pieces: 1, baseRate: 4, baseSource: 'GENERIC', extraPieces: 0, extraSource: 'NONE', lineTotal: 4 }], dispatchFee: 0, codFee: 0, total: 4, rateComponentIds: [] }
    const user = userEvent.setup()
    wrap()
    const line = await openQuote(user)
    await fillLine(user, line, 'STANDARD', 'BOX', '1')
    await user.click(screen.getByRole('button', { name: 'Cotizar' }))
    const note = await screen.findByTestId('quote-contract')
    expect(note.textContent).toMatch(/^El cliente no tiene contrato vigente al .*: se cotizó con las tarifas genéricas de la compañía y sin cargo de despacho ni de COD\.$/)
  })
})

describe('Cotizador — validación y errores del servidor', () => {
  it('valida en pantalla (servicio, paquete, piezas, COD) sin llamar al servidor', async () => {
    const user = userEvent.setup()
    wrap()
    const line = await openQuote(user)
    await user.click(screen.getByRole('button', { name: 'Cotizar' }))
    expect(await within(line).findByText('El tipo de servicio es obligatorio.')).toBeInTheDocument()
    expect(within(line).getByText('El tipo de paquete es obligatorio.')).toBeInTheDocument()
    await fillLine(user, line, 'STANDARD', 'BOX', '0')
    await user.type(screen.getByLabelText('Monto COD'), '-5')
    await user.click(screen.getByRole('button', { name: 'Cotizar' }))
    expect(await within(line).findByText('La cantidad de piezas debe ser al menos 1.')).toBeInTheDocument()
    expect(screen.getByText('El monto COD no puede ser negativo.')).toBeInTheDocument()
    expect(quoteCalls()).toHaveLength(0)
  })

  it('dos líneas con el mismo servicio y paquete: el mismo texto del servidor, sin llamarlo', async () => {
    const user = userEvent.setup()
    wrap()
    const line = await openQuote(user)
    await fillLine(user, line, 'STANDARD', 'BOX', '2')
    await user.click(screen.getByRole('button', { name: 'Agregar línea' }))
    await fillLine(user, await screen.findByRole('group', { name: 'Línea 2' }), 'STANDARD', 'BOX', '3')
    await user.click(screen.getByRole('button', { name: 'Cotizar' }))
    expect(await screen.findByText('El par servicio/tipo de paquete está repetido; envíe una sola línea por (servicio, tipo de paquete) con el total de piezas.')).toBeInTheDocument()
    expect(quoteCalls()).toHaveLength(0)
  })

  it('400 del servidor por línea sale bajo el campo de esa línea', async () => {
    mock.override = (c) => (c.url.pathname.endsWith('/contract-rate-quote') ? problem(400, 'La cantidad de piezas debe ser al menos 1.', 'validation', { 'lines[0].pieces': ['La cantidad de piezas debe ser al menos 1.'] }) : undefined)
    const user = userEvent.setup()
    wrap()
    const line = await openQuote(user)
    await fillLine(user, line, 'STANDARD', 'BOX', '4')
    await user.click(screen.getByRole('button', { name: 'Cotizar' }))
    expect(await within(line).findByText('La cantidad de piezas debe ser al menos 1.')).toBeInTheDocument()
    expect(screen.queryByRole('region', { name: 'Resultado de la cotización' })).toBeNull()
  })

  it('409 de cliente dado de baja: el mensaje exacto queda arriba del formulario y no hay resultado', async () => {
    mock.override = (c) => (c.url.pathname.endsWith('/contract-rate-quote') ? problem(409, 'El cliente está dado de baja; solo se consulta su historial.', 'conflict') : undefined)
    const user = userEvent.setup()
    wrap()
    const line = await openQuote(user)
    await fillLine(user, line, 'STANDARD', 'BOX', '4')
    await user.click(screen.getByRole('button', { name: 'Cotizar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('El cliente está dado de baja; solo se consulta su historial.')
    expect(screen.queryByRole('region', { name: 'Resultado de la cotización' })).toBeNull()
  })

  it('404 de catálogo desconocido: el título del servidor tal cual', async () => {
    mock.override = (c) => (c.url.pathname.endsWith('/contract-rate-quote') ? problem(404, "Código 'ZZZ' desconocido en el catálogo PackageType.", 'not_found') : undefined)
    const user = userEvent.setup()
    wrap()
    const line = await openQuote(user)
    await fillLine(user, line, 'STANDARD', 'BOX', '4')
    await user.click(screen.getByRole('button', { name: 'Cotizar' }))
    expect(await screen.findByRole('alert')).toHaveTextContent("Código 'ZZZ' desconocido en el catálogo PackageType.")
  })
})

describe('Cotizador — líneas y limpiar', () => {
  it('agregar y quitar líneas (la última no se puede quitar) y «Limpiar» vuelve al inicio y borra el resultado', async () => {
    const user = userEvent.setup()
    wrap()
    const line = await openQuote(user)
    expect(within(line).getByRole('button', { name: 'Quitar la línea 1' })).toBeDisabled()
    await user.click(screen.getByRole('button', { name: 'Agregar línea' }))
    const second = await screen.findByRole('group', { name: 'Línea 2' })
    await user.click(within(second).getByRole('button', { name: 'Quitar la línea 2' }))
    await waitFor(() => expect(screen.queryByRole('group', { name: 'Línea 2' })).toBeNull())

    await fillLine(user, line, 'STANDARD', 'BOX', '8')
    await user.click(screen.getByRole('button', { name: 'Cotizar' }))
    await screen.findByRole('region', { name: 'Resultado de la cotización' })
    await user.click(screen.getByRole('button', { name: 'Limpiar' }))
    await waitFor(() => expect(screen.queryByRole('region', { name: 'Resultado de la cotización' })).toBeNull())
    expect((within(screen.getByRole('group', { name: 'Línea 1' })).getByLabelText(/^Servicio/) as HTMLSelectElement).value).toBe('')
  })
})
