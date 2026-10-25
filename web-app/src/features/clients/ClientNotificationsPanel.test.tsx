// Lote 98 — panel «Avisos al cliente» de la ficha del cliente sobre un fetch simulado: todo apagado por omisión, encender canales, correos extra, validar, guardar y el registro de resúmenes.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ClientNotificationsPanel } from './ClientNotificationsPanel'
import type { ClientDetail } from './clientRules'

const CLIENT = '11111111-0000-0000-0000-000000000001'
const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    const p = url.pathname
    if (req.method === 'GET' && p === `/api/v1/clients/${CLIENT}/notifications/settings`) return json(SETTINGS())
    if (req.method === 'PUT' && p === `/api/v1/clients/${CLIENT}/notifications/settings`) return json(SETTINGS((body as { settings: Record<string, unknown>[] }).settings))
    if (req.method === 'GET' && p === `/api/v1/clients/${CLIENT}/notifications/log`) return json({ items: [LOG], total: 1, skip: 0, take: 10 })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
const KINDS = ['PICKED_UP', 'OUT_FOR_DELIVERY', 'DELIVERED', 'WH_RECEIVED', 'WH_DISPATCHED', 'COD_PROCESSED', 'INVOICE_ISSUED']
const SETTINGS = (rows: Record<string, unknown>[] = []) => ({
  clientPublicId: CLIENT, clientName: 'Acme', portalRecipients: 2, emailConfigured: true, smsConfigured: false, digestHour: 18,
  settings: KINDS.map((k) => ({ kind: k, emailEnabled: false, appEnabled: false, smsEnabled: false, extraEmails: null, extraPhones: null, ...(rows.find((r) => r.kind === k) ?? {}) })),
})
const LOG = {
  publicId: 'n1', kind: 'DELIVERED', subject: 'Se entregaron sus paquetes — Acme', bodyText: 'Entregado: 8 caja(s) de 2 orden(es) en la ruta RUT-1.', inPortal: true, createdAtUtc: '2026-10-17T15:00:00Z',
  deliveries: [{ channel: 'EMAIL', recipient: 'a@b.co', status: 'SENT', message: null, attempts: 1, sentAtUtc: '2026-10-17T15:00:01Z' }, { channel: 'SMS', recipient: '787-555-0100', status: 'SKIPPED', message: 'x', attempts: 0, sentAtUtc: null }],
}

function wrap(permissions = ['clients.read', 'clients.update']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={new Set(permissions)} modules={new Set(['CATALOG'])}>
        <MemoryRouter>
          <ClientNotificationsPanel client={{ publicId: CLIENT, name: 'Acme', isActive: true } as unknown as ClientDetail} />
        </MemoryRouter>
      </AccessProvider>
    </QueryClientProvider>,
  )
}

async function open(user: ReturnType<typeof userEvent.setup>) {
  await user.click((await screen.findAllByRole('button', { expanded: false }))[0])   // el panel nace plegado
}

describe('Avisos al cliente (panel)', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
  })

  it('todo apagado por omisión, con el resumen, los destinatarios y el registro', async () => {
    const user = userEvent.setup()
    wrap()
    expect(await screen.findByText('0 aviso(s) encendido(s)')).toBeInTheDocument()
    await open(user)
    expect(await screen.findByText(/nunca al consignatario/)).toBeInTheDocument()
    expect(screen.getByText(/usuarios activos del portal de este cliente \(2\)/)).toBeInTheDocument()
    for (const title of ['Recogido', 'Salió a entrega', 'Entregado', 'Mercancía recibida', 'Mercancía despachada', 'COD procesado', 'Factura emitida']) expect(screen.getAllByText(title).length).toBeGreaterThan(0)
    expect(screen.getAllByRole('checkbox').every((c) => !(c as HTMLInputElement).checked)).toBe(true)
    expect(await screen.findByText('Entregado: 8 caja(s) de 2 orden(es) en la ruta RUT-1.')).toBeInTheDocument()
    expect(screen.getByText('En el portal')).toBeInTheDocument()
    expect(screen.getByText('1 enviado(s)')).toBeInTheDocument()
    expect(screen.getByText('1 omitido(s)')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Guardar avisos' })).not.toBeInTheDocument()   // sin cambios no hay qué guardar
  })

  it('encender canales y poner correos extra manda solo lo configurado y guarda', async () => {
    const user = userEvent.setup()
    wrap()
    await open(user)
    const delivered = (await screen.findByText('Entregado', { selector: 'legend' })).closest('fieldset') as HTMLElement
    await user.click(within(delivered).getByLabelText('Correo'))
    await user.click(within(delivered).getByLabelText('Bandeja del portal'))
    await user.click(within(delivered).getByLabelText('SMS'))
    await user.type(within(delivered).getByLabelText('Correos extra (separados por coma)'), 'ana@x.co; luis@x.co')
    expect(within(delivered).getByText(/el SMS no sale hasta que se contrate el proveedor/)).toBeInTheDocument()
    await user.click(screen.getAllByRole('button', { name: 'Guardar avisos' })[0])
    await waitFor(() => expect(mock.requests.filter((r) => r.method === 'PUT')).toHaveLength(1))
    const sent = (mock.requests.find((r) => r.method === 'PUT')!.body as { settings: Record<string, unknown>[] }).settings
    expect(sent.find((s) => s.kind === 'DELIVERED')).toMatchObject({ emailEnabled: true, appEnabled: true, smsEnabled: true, extraEmails: 'ana@x.co; luis@x.co', extraPhones: null })
    expect(sent.find((s) => s.kind === 'PICKED_UP')).toMatchObject({ emailEnabled: false, appEnabled: false, smsEnabled: false })
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Guardar avisos' })).not.toBeInTheDocument())   // guardado: ya no hay cambios
  })

  it('la hora del resumen del día es la guardada (18:00 por omisión) y cambiarla la manda al guardar', async () => {
    const user = userEvent.setup()
    wrap()
    await open(user)
    const hour = (await screen.findByLabelText('Hora del resumen del día')) as HTMLSelectElement
    expect(hour.value).toBe('18')
    expect(screen.getByText(/UN solo resumen al día/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Guardar avisos' })).not.toBeInTheDocument()
    await user.selectOptions(hour, '20')
    await user.click(screen.getAllByRole('button', { name: 'Guardar avisos' })[0])
    await waitFor(() => expect(mock.requests.filter((r) => r.method === 'PUT')).toHaveLength(1))
    expect((mock.requests.find((r) => r.method === 'PUT')!.body as { digestHour: number }).digestHour).toBe(20)
  })

  it('un correo mal escrito se avisa y no deja guardar', async () => {
    const user = userEvent.setup()
    wrap()
    await open(user)
    const picked = (await screen.findByText('Recogido', { selector: 'legend' })).closest('fieldset') as HTMLElement
    await user.click(within(picked).getByLabelText('Correo'))
    await user.type(within(picked).getByLabelText('Correos extra (separados por coma)'), 'nope')
    expect(await within(picked).findByText("El correo 'nope' no es válido.")).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Guardar avisos' })[0]).toBeDisabled()
    expect(mock.requests.filter((r) => r.method === 'PUT')).toHaveLength(0)
  })

  it('sin clients.update se ve la configuración pero no se puede cambiar', async () => {
    const user = userEvent.setup()
    wrap(['clients.read'])
    await open(user)
    const picked = (await screen.findByText('Recogido', { selector: 'legend' })).closest('fieldset') as HTMLElement
    expect(within(picked).getByLabelText('Correo')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Guardar avisos' })).not.toBeInTheDocument()
  })
})
