// Lote 222 — Perfil del cliente con lo de ventas (SURI, envío compañía/ninguno/propio, «exige la firma de quien autoriza») y los botones Aprobar/Rechazar de un cliente prospecto,
// sobre un fetch simulado; más las reglas puras del perfil (`profileValuesOf`, `buildProfileRequest`, `profileSchema`).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ClientApprovalActions } from './ClientApprovalActions'
import { ClientProfilePanel } from './ClientProfilePanel'
import { buildProfileRequest, isProspect, parseAmount, profileSchema, profileValuesOf, type ClientDetail } from './clientRules'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[], rejectStatus: 200 }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    const p = url.pathname
    if (req.method === 'PATCH' && p === `/api/v1/clients/${ID}/profile`) return json({ ...CLIENT, merchantRegistration: 'SURI-9' })
    if (req.method === 'POST' && p === `/api/v1/clients/${ID}/approve`) return json({ ...CLIENT, status: 'ACTIVE', statusLabel: 'Activo' })
    if (req.method === 'POST' && p === `/api/v1/clients/${ID}/reject`) {
      if (mock.rejectStatus !== 200) return new Response(JSON.stringify({ title: 'Escriba el motivo del rechazo.', status: 400 }), { status: 400, headers: { 'Content-Type': 'application/problem+json' } })
      return json({ client: { ...CLIENT, status: 'SUSPENDED' }, cancelledOrders: ['OV-1', 'OV-2'] })
    }
    if (p.startsWith('/api/v1/catalogs/')) return json([])
    return new Response('{}', { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const ID = 'c0000000-0000-0000-0000-000000000001'
const CLIENT = { id: 1, publicId: ID, code: 'FARM', name: 'Farmacia Central', status: 'ACTIVE', isActive: true, rowVersion: 'AAA=', merchantRegistration: null, shippingMode: null, shippingCharge: null, shippingFreeFrom: null, requireOrderSignature: false } as unknown as ClientDetail

function wrap(ui: React.ReactNode, perms: string[], modules = ['SALES_ORDERS', 'CATALOG']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={perms} modules={modules}>
        <MemoryRouter>{ui}</MemoryRouter>
      </AccessProvider>
    </QueryClientProvider>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

describe('perfil del cliente — reglas de ventas (Lote 222)', () => {
  const messages = { legalNameMax: 'a', taxIdMax: 'b', creditLimitNumber: 'c', creditLimitMin: 'd', creditLimitRequired: 'e', shippingChargeRequired: 'cargo' }
  it('lee SURI, regla de envío y firma de la ficha; sin regla es «la de la compañía»', () => {
    expect(profileValuesOf(CLIENT)).toMatchObject({ merchantRegistration: '', shippingMode: '', shippingCharge: null, shippingFreeFrom: null, requireOrderSignature: false })
    expect(profileValuesOf({ ...CLIENT, merchantRegistration: 'S-1', shippingMode: 'OWN', shippingCharge: 7.5, shippingFreeFrom: 100, requireOrderSignature: true })).toMatchObject({ merchantRegistration: 'S-1', shippingMode: 'OWN', shippingCharge: 7.5, shippingFreeFrom: 100, requireOrderSignature: true })
  })

  it('el PATCH solo lleva lo de ventas que cambió', () => {
    const o = profileValuesOf(CLIENT)
    expect(buildProfileRequest(o, o, 'AAA=')).not.toHaveProperty('shippingMode')
    expect(buildProfileRequest(o, o, 'AAA=')).not.toHaveProperty('merchantRegistration')
    const own = buildProfileRequest({ ...o, merchantRegistration: ' S-2 ', shippingMode: 'OWN', shippingCharge: 8, shippingFreeFrom: null, requireOrderSignature: true }, o, 'AAA=')
    expect(own).toMatchObject({ merchantRegistration: 'S-2', shippingMode: 'OWN', shippingCharge: 8, shippingFreeFrom: 0, requireOrderSignature: true })
    const free = buildProfileRequest({ ...o, shippingMode: 'FREE' }, o, 'AAA=')
    expect(free.shippingMode).toBe('FREE')
    expect(free).not.toHaveProperty('shippingCharge')
    // volver a la regla de la compañía
    const back = profileValuesOf({ ...CLIENT, shippingMode: 'OWN', shippingCharge: 8 })
    expect(buildProfileRequest({ ...back, shippingMode: '' }, back, 'AAA=').shippingMode).toBe('COMPANY')
    // quitar el SURI
    const withSuri = profileValuesOf({ ...CLIENT, merchantRegistration: 'S-1' })
    expect(buildProfileRequest({ ...withSuri, merchantRegistration: '' }, withSuri, 'AAA=').merchantRegistration).toBe('')
  })

  it('la regla propia exige un cargo mayor que cero y el SURI no pasa de 50', () => {
    const schema = profileSchema(messages, false)
    const base = profileValuesOf(CLIENT)
    expect(schema.safeParse({ ...base, shippingMode: 'OWN', shippingCharge: null }).success).toBe(false)
    expect(schema.safeParse({ ...base, shippingMode: 'OWN', shippingCharge: 0 }).success).toBe(false)
    expect(schema.safeParse({ ...base, shippingMode: 'OWN', shippingCharge: 5 }).success).toBe(true)
    expect(schema.safeParse({ ...base, merchantRegistration: 'x'.repeat(51) }).success).toBe(false)
  })

  it('reconoce al prospecto y lee montos con coma o punto', () => {
    expect(isProspect({ status: 'PROSPECT' })).toBe(true)
    expect(isProspect({ status: 'ACTIVE' })).toBe(false)
    expect(parseAmount('1500,5')).toBe(1500.5)
    expect(parseAmount('')).toBeNull()
    expect(parseAmount('x')).toBeNaN()
  })
})

describe('ClientProfilePanel — ventas', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.rejectStatus = 200
  })

  it('guarda SURI, regla propia de envío y la firma de quien autoriza', async () => {
    const user = userEvent.setup()
    wrap(<ClientProfilePanel client={CLIENT} />, ['clients.update', 'clients.read'])
    await user.click(screen.getByRole('button', { name: 'Abrir el panel' }))
    await user.type(await screen.findByLabelText(/SURI/), 'SURI-9')
    expect(screen.queryByLabelText('Cargo de envío')).not.toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Envío de sus pedidos'), 'OWN')
    await user.type(screen.getByLabelText('Cargo de envío'), '8')
    await user.type(screen.getByLabelText('Envío gratis desde'), '150')
    await user.click(screen.getByRole('switch', { name: /Exige la firma de quien autoriza/ }))
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(sent('PATCH', `/api/v1/clients/${ID}/profile`)).toHaveLength(1))
    expect(sent('PATCH', `/api/v1/clients/${ID}/profile`)[0].body).toMatchObject({ merchantRegistration: 'SURI-9', shippingMode: 'OWN', shippingCharge: 8, shippingFreeFrom: 150, requireOrderSignature: true })
  })

  it('la regla propia sin cargo no se envía y muestra el mensaje', async () => {
    const user = userEvent.setup()
    wrap(<ClientProfilePanel client={CLIENT} />, ['clients.update'])
    await user.click(screen.getByRole('button', { name: 'Abrir el panel' }))
    await user.selectOptions(await screen.findByLabelText('Envío de sus pedidos'), 'OWN')
    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(await screen.findByText('La regla propia necesita un cargo mayor que cero.')).toBeInTheDocument()
    expect(sent('PATCH', `/api/v1/clients/${ID}/profile`)).toHaveLength(0)
  })

  it('sin el módulo de ventas no pinta la sección; sin clients.update los campos son de solo lectura', async () => {
    const user = userEvent.setup()
    wrap(<ClientProfilePanel client={CLIENT} />, ['clients.read'], ['CATALOG'])
    await user.click(screen.getByRole('button', { name: 'Abrir el panel' }))
    expect(screen.queryByLabelText(/SURI/)).not.toBeInTheDocument()
  })
})

describe('Aprobar y rechazar un cliente prospecto', () => {
  const PROSPECT = { ...CLIENT, status: 'PROSPECT', statusLabel: 'Prospecto' } as ClientDetail
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.rejectStatus = 200
  })

  it('los botones solo salen con sales.approve_client y con un cliente prospecto', () => {
    const a = wrap(<ClientApprovalActions client={PROSPECT} />, ['clients.update'])
    expect(screen.queryByRole('button', { name: 'Aprobar' })).not.toBeInTheDocument()
    a.unmount()
    const b = wrap(<ClientApprovalActions client={CLIENT} />, ['sales.approve_client'])
    expect(screen.queryByRole('button', { name: 'Aprobar' })).not.toBeInTheDocument()
    b.unmount()
    wrap(<ClientApprovalActions client={PROSPECT} />, ['sales.approve_client'])
    expect(screen.getByRole('button', { name: 'Aprobar' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Rechazar' })).toBeInTheDocument()
  })

  it('aprobar manda límite, término y comentario', async () => {
    const user = userEvent.setup()
    wrap(<ClientApprovalActions client={PROSPECT} />, ['sales.approve_client'])
    await user.click(screen.getByRole('button', { name: 'Aprobar' }))
    const dialog = await screen.findByRole('dialog', { name: /Aprobar a Farmacia Central/ })
    await user.type(within(dialog).getByLabelText('Límite de crédito'), '2500')
    await user.type(within(dialog).getByLabelText(/Comentario/), 'Referencias buenas')
    await user.click(within(dialog).getByRole('button', { name: 'Aprobar' }))
    await waitFor(() => expect(sent('POST', `/api/v1/clients/${ID}/approve`)).toHaveLength(1))
    expect(sent('POST', `/api/v1/clients/${ID}/approve`)[0].body).toEqual({ creditLimit: 2500, paymentTerm: null, comment: 'Referencias buenas' })
  })

  it('rechazar exige el comentario y lo manda', async () => {
    const user = userEvent.setup()
    wrap(<ClientApprovalActions client={PROSPECT} />, ['sales.approve_client'])
    await user.click(screen.getByRole('button', { name: 'Rechazar' }))
    const dialog = await screen.findByRole('dialog', { name: /Rechazar a Farmacia Central/ })
    await user.click(within(dialog).getByRole('button', { name: 'Rechazar' }))
    expect(await within(dialog).findByText('Escriba el motivo del rechazo.')).toBeInTheDocument()
    expect(sent('POST', `/api/v1/clients/${ID}/reject`)).toHaveLength(0)
    await user.type(within(dialog).getByLabelText(/Motivo del rechazo/), 'Sin referencias')
    await user.click(within(dialog).getByRole('button', { name: 'Rechazar' }))
    await waitFor(() => expect(sent('POST', `/api/v1/clients/${ID}/reject`)).toHaveLength(1))
    expect(sent('POST', `/api/v1/clients/${ID}/reject`)[0].body).toEqual({ comment: 'Sin referencias' })
  })

  it('un error del servidor al rechazar muestra su título', async () => {
    mock.rejectStatus = 400
    const user = userEvent.setup()
    wrap(<ClientApprovalActions client={PROSPECT} />, ['sales.approve_client'])
    await user.click(screen.getByRole('button', { name: 'Rechazar' }))
    const dialog = await screen.findByRole('dialog', { name: /Rechazar a Farmacia Central/ })
    await user.type(within(dialog).getByLabelText(/Motivo del rechazo/), 'x')
    await user.click(within(dialog).getByRole('button', { name: 'Rechazar' }))
    expect(await within(dialog).findByText('Escriba el motivo del rechazo.')).toBeInTheDocument()
  })
})
