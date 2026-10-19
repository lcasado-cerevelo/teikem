// Pruebas de «COD» (Lote 85) sobre un fetch simulado: la lista y la ficha, el cobro, la reconciliación con archivos (lector de códigos simulado), la vista previa, cuadrar y las corridas.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import CodScreen from './CodScreen'

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; search: string; body: unknown }[],
  codStatus: 'PENDING',
  collected: 0,
  reconcileItems: [] as unknown[],
  lastMode: null as string | null,
  read: vi.fn(),
}))

vi.mock('./barcodeReader', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./barcodeReader')>()
  return { ...actual, readCodesFromFile: mock.read }
})

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, search: url.search, body })
    const p = url.pathname
    const m = req.method
    if (m === 'GET' && p === '/api/v1/cod/orders') return json({ items: [ITEM()], total: 1, skip: 0, take: 25 })
    if (m === 'GET' && p === `/api/v1/cod/orders/${ORD}`) return json(DTO())
    if (m === 'POST' && p === `/api/v1/cod/orders/${ORD}/collections`) {
      const b = body as { amount: number }
      if (b.amount > 100 - mock.collected) return problem(422, 'El cobro pasa de lo que falta por cobrar de ORD-00001: 40.00.')
      mock.collected += b.amount
      mock.codStatus = mock.collected >= 100 ? 'COLLECTED' : 'PARTIAL'
      return json(DTO())
    }
    if (m === 'POST' && p === '/api/v1/cod/reconcile') {
      const b = body as { dryRun?: boolean; mode?: string; codes: { code: string; source?: string }[] }
      mock.lastMode = b.mode ?? null
      // Lote 126: un código que empieza por «FAC-S» es la factura de una orden SIN COD (se procesa, Kind INVOICE)
      const inv = (code: string) => code.startsWith('FAC-S')
      const items = b.codes.map((c) => ({
        code: c.code, source: c.source ?? null, result: c.code === 'NADA' ? 'NOT_FOUND' : inv(c.code) ? 'INVOICE_CHECKED' : 'RECONCILED',
        message: c.code === 'NADA' ? 'No hay ninguna orden con ese número de factura ni de orden.' : inv(c.code) ? 'Factura procesada: la orden está entregada.' : 'Cuadrado: 1 cobro(s) por 60.00.',
        orderPublicId: c.code === 'NADA' ? null : ORD, orderNumber: c.code === 'NADA' ? null : 'ORD-00001', invoiceNumber: c.code === 'NADA' ? null : c.code, clientName: c.code === 'NADA' ? null : 'Farmacia Central',
        codAmount: inv(c.code) ? 0 : 100, reconciled: c.code === 'NADA' || inv(c.code) ? 0 : 60, collections: c.code === 'NADA' || inv(c.code) ? 0 : 1,
        kind: inv(c.code) ? 'INVOICE' : 'COD',
      }))
      mock.reconcileItems = items
      const hasInvoice = items.some((i) => i.kind === 'INVOICE' && i.orderNumber)
      const hasCod = items.some((i) => i.kind === 'COD' && i.orderNumber)
      const warnings = b.mode === 'COD' && hasInvoice ? ['NOT_COD_SHEETS'] : b.mode === 'INVOICE' && hasCod ? ['COD_SHEETS'] : b.mode === 'BOTH' && !hasInvoice ? ['NO_INVOICE_SHEETS'] : []
      return json({ runPublicId: b.dryRun ? null : RUN, dryRun: !!b.dryRun, total: items.length, reconciled: items.filter((i) => i.result === 'RECONCILED').length, checked: items.filter((i) => i.result === 'INVOICE_CHECKED').length,
        issues: items.filter((i) => i.result === 'NOT_FOUND').length, items, warnings })
    }
    if (m === 'GET' && p === '/api/v1/cod/runs') return json({ items: [{ publicId: RUN, source: 'hojas.pdf', total: 2, reconciled: 1, issues: 1, createdAtUtc: '2026-10-17T12:00:00Z', createdByName: 'Admin' }], total: 1, skip: 0, take: 25 })
    if (m === 'GET' && p === `/api/v1/cod/runs/${RUN}`) {
      return json({ publicId: RUN, source: 'hojas.pdf', total: 2, reconciled: 1, issues: 1, createdAtUtc: '2026-10-17T12:00:00Z', createdByName: 'Admin', items: [
        { code: 'FAC-00001', source: 'hojas.pdf p1', result: 'RECONCILED', message: 'Cuadrado', orderNumber: 'ORD-00001', invoiceNumber: 'FAC-00001', clientName: 'Farmacia Central', codAmount: 100, reconciled: 60, collections: 1 },
        { code: 'NADA', source: 'hojas.pdf p2', result: 'NOT_FOUND', message: 'No hay ninguna orden', codAmount: 0, reconciled: 0, collections: 0 },
      ] })
    }
    if (p === '/api/v1/clients') return json([{ id: 1, publicId: CLIENT, code: 'FARM', name: 'Farmacia Central', isActive: true }])
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
function problem(status: number, title: string) {
  return new Response(JSON.stringify({ title, status, code: 'status_rule' }), { status, headers: { 'Content-Type': 'application/problem+json' } })
}

const ORD = '66666666-0000-0000-0000-000000000006'
const RUN = '77777777-0000-0000-0000-000000000007'
const CLIENT = '88888888-0000-0000-0000-000000000008'
const LABEL: Record<string, string> = { PENDING: 'Por cobrar', PARTIAL: 'Parcial', COLLECTED: 'Cobrado', RECONCILED: 'Cuadrado' }
const ITEM = () => ({
  orderPublicId: ORD, orderNumber: 'ORD-00001', invoiceNumber: 'FAC-00001', clientPublicId: CLIENT, clientName: 'Farmacia Central', orderStatusLabel: 'Entregada', codAmount: 100, collected: mock.collected,
  pending: 100 - mock.collected, codStatus: mock.codStatus, codStatusLabel: LABEL[mock.codStatus], codStatusColor: '#B97400', createdAtUtc: '2026-10-17T10:00:00Z',
})
const DTO = () => ({
  ...ITEM(), orderStatus: 'DELIVERED',
  collections: mock.collected > 0 ? [{ id: 1, method: 'CASH', methodLabel: 'Efectivo', amount: mock.collected, checkNumber: null, reference: null, collectedAtUtc: '2026-10-17T11:00:00Z', status: 'COLLECTED', statusLabel: 'Cobrado', statusColor: '#10B981', reconciledAtUtc: null, notes: null }] : [],
})

const ALL = ['cod.view', 'cod.collect', 'cod.reconcile', 'clients.read']
const SESSION = {
  me: { userId: 99, tenantName: 'Advance Logistics' },
  isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', setLang: () => {}, logout: async () => {}, switchTenant: async () => {},
  permissions: new Set(ALL),
  modules: new Set(['COD']),
  reloadMe: async () => {},
} as unknown as Session

function wrap(permissions: string[] = ALL) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={new Set(permissions)} modules={SESSION.modules}>
          <MemoryRouter>
            <CodScreen />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

const pdf = (name = 'hojas.pdf') => new File(['%PDF'], name, { type: 'application/pdf' })

describe('COD', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.codStatus = 'PENDING'
    mock.collected = 0
    mock.read.mockReset()
  })

  it('la lista muestra lo pedido, cobrado y por cobrar y filtra por estatus; la ficha abre con sus cobros', async () => {
    const user = userEvent.setup()
    wrap()
    expect(await screen.findByText('ORD-00001')).toBeInTheDocument()
    expect(screen.getByText('FAC-00001')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Filtrar por estatus del COD'), 'PARTIAL')
    await waitFor(() => expect(sent('GET', '/api/v1/cod/orders').some((r) => r.search.includes('status=PARTIAL'))).toBe(true))
    await user.click(await screen.findByText('ORD-00001'))
    const dialog = await screen.findByRole('dialog', { name: /COD de ORD-00001/ })
    expect(await within(dialog).findByText('Todavía no hay cobros.')).toBeInTheDocument()
  })

  it('registrar un cobro valida el tope, pide el número del cheque y manda el cuerpo', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByText('ORD-00001'))
    const dialog = await screen.findByRole('dialog', { name: /COD de ORD-00001/ })
    await user.click(await within(dialog).findByRole('button', { name: 'Registrar cobro' }))
    const form = await screen.findByRole('dialog', { name: /Cobro de ORD-00001/ })
    const amount = within(form).getByLabelText('Monto')
    await user.clear(amount)
    await user.type(amount, '150')
    await user.click(within(form).getByRole('button', { name: 'Registrar' }))
    expect(await within(form).findByRole('alert')).toHaveTextContent('pasa de lo que falta por cobrar')
    await user.selectOptions(within(form).getByLabelText('Método'), 'CHECK')
    await user.clear(amount)
    await user.type(amount, '60')
    await user.click(within(form).getByRole('button', { name: 'Registrar' }))
    expect(await within(form).findByRole('alert')).toHaveTextContent('número del cheque')
    await user.type(within(form).getByLabelText('Número del cheque'), '5521')
    await user.click(within(form).getByRole('button', { name: 'Registrar' }))
    await waitFor(() => expect(sent('POST', `/api/v1/cod/orders/${ORD}/collections`)).toHaveLength(1))
    expect(sent('POST', `/api/v1/cod/orders/${ORD}/collections`)[0].body).toEqual({ method: 'CHECK', amount: 60, checkNumber: '5521' })
  })

  it('reconciliar: lee los archivos, junta lo escrito a mano, vista previa y luego cuadra', async () => {
    mock.read.mockResolvedValue({
      fileName: 'hojas.pdf', pages: 2, error: null, emptyPages: [2],
      codes: [{ code: 'FAC-00001', source: 'hojas.pdf p1', format: 'Code128' }],
    })
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Reconciliar' }))
    await user.upload(await screen.findByLabelText('Archivos de hojas escaneadas'), pdf())
    expect(await screen.findByText(/1 código\(s\) en 2 página\(s\)/)).toBeInTheDocument()
    expect(screen.getByText(/sin código en las páginas 2/)).toBeInTheDocument()
    expect(screen.getByText('Códigos por procesar (1)')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Códigos escritos a mano'), 'NADA')
    await user.click(screen.getByRole('button', { name: 'Agregar' }))
    expect(screen.getByText('Códigos por procesar (2)')).toBeInTheDocument()

    expect(screen.getByRole('button', { name: 'Cuadrar' })).toBeDisabled()   // antes de la vista previa no se cuadra
    await user.click(screen.getByRole('button', { name: 'Vista previa' }))
    expect(await screen.findByText('Esto es solo una simulación: todavía no se cuadró nada.')).toBeInTheDocument()
    const preview = sent('POST', '/api/v1/cod/reconcile')[0].body as { dryRun: boolean; source: string; codes: unknown[] }
    expect(preview.dryRun).toBe(true)
    expect(preview.source).toBe('hojas.pdf, Manual')
    expect(preview.codes).toEqual([{ code: 'FAC-00001', source: 'hojas.pdf p1' }, { code: 'NADA', source: 'Manual' }])
    expect(screen.getByText('2 códigos')).toBeInTheDocument()
    expect(screen.getAllByText('No encontrado').length).toBeGreaterThan(0)

    await user.click(screen.getByRole('button', { name: 'Cuadrar' }))
    const confirm = await screen.findByRole('dialog', { name: 'Procesar las hojas' })
    await user.click(within(confirm).getByRole('button', { name: 'Cuadrar' }))
    await waitFor(() => expect(sent('POST', '/api/v1/cod/reconcile')).toHaveLength(2))
    expect((sent('POST', '/api/v1/cod/reconcile')[1].body as { dryRun: boolean }).dryRun).toBe(false)
    expect(await screen.findByText('Corrida hecha')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Nueva corrida' }))
    expect(await screen.findByText('Códigos por procesar (0)')).toBeInTheDocument()
  })

  it('Lote 126: «¿Qué vas a subir?» viaja como modo; la factura de una orden sin COD se procesa (tipo Factura) y la advertencia del modo se muestra', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Reconciliar' }))
    expect(await screen.findByRole('radio', { name: 'Detectar' })).toBeChecked()                 // por omisión el sistema clasifica solo
    await user.type(screen.getByLabelText('Códigos escritos a mano'), 'FAC-00001 FAC-S0002')
    await user.click(screen.getByRole('button', { name: 'Agregar' }))
    await user.click(screen.getByRole('button', { name: 'Vista previa' }))
    await screen.findByText('Esto es solo una simulación: todavía no se cuadró nada.')
    expect(mock.lastMode).toBe('AUTO')
    expect(screen.queryByRole('alert')).toBeNull()                                                 // Detectar no advierte
    expect(screen.getAllByText('Factura procesada').length).toBeGreaterThan(0)
    expect(screen.getAllByText('Factura').length).toBeGreaterThan(0)                               // la columna Tipo
    expect(screen.getByText(/1 factura\(s\) procesada\(s\)/)).toBeInTheDocument()

    // dice «Hojas de COD» pero una es de una orden sin COD: se procesa igual y avisa
    await user.click(screen.getByRole('radio', { name: 'Hojas de COD' }))
    await user.click(screen.getByRole('button', { name: 'Vista previa' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Dijiste hojas de COD, pero algunas son de órdenes sin COD')
    expect(mock.lastMode).toBe('COD')
    expect(screen.getByRole('button', { name: 'Cuadrar' })).toBeEnabled()
    // hay una factura y no se eligió cliente: Cuadrar pide el cliente en vez de abrir la confirmación
    await user.click(screen.getByRole('button', { name: 'Cuadrar' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.getAllByText(/elige el cliente de las hojas/).length).toBeGreaterThan(0)

    // «Ambas» con los dos tipos no advierte; sin la factura, avisa que faltan facturas
    await user.click(screen.getByRole('radio', { name: 'Ambas' }))
    await user.click(screen.getByRole('button', { name: 'Vista previa' }))
    await screen.findByText('Esto es solo una simulación: todavía no se cuadró nada.')
    expect(screen.queryByRole('alert')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Quitar el código FAC-S0002' }))
    await user.click(screen.getByRole('button', { name: 'Vista previa' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('no subiste facturas')
  })

  it('un código se puede quitar de la lista y cambiar la lista descarta la vista previa', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Reconciliar' }))
    await user.type(await screen.findByLabelText('Códigos escritos a mano'), 'FAC-1 FAC-2')
    await user.click(screen.getByRole('button', { name: 'Agregar' }))
    await user.click(screen.getByRole('button', { name: 'Vista previa' }))
    await screen.findByText('Esto es solo una simulación: todavía no se cuadró nada.')
    await user.click(screen.getByRole('button', { name: 'Quitar el código FAC-2' }))
    expect(screen.queryByText('Esto es solo una simulación: todavía no se cuadró nada.')).toBeNull()
    expect(screen.getByText('Códigos por procesar (1)')).toBeInTheDocument()
  })

  it('un archivo que no es PDF ni imagen avisa y no se lee', async () => {
    const user = userEvent.setup({ applyAccept: false })
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Reconciliar' }))
    await user.upload(await screen.findByLabelText('Archivos de hojas escaneadas'), new File(['x'], 'nota.docx', { type: 'application/msword' }))
    expect(await screen.findByText('no es un PDF ni una imagen')).toBeInTheDocument()
    expect(mock.read).not.toHaveBeenCalled()
  })

  it('un archivo que no se pudo abrir avisa y los demás siguen', async () => {
    mock.read.mockResolvedValueOnce({ fileName: 'roto.pdf', pages: 0, codes: [], emptyPages: [], error: 'open' })
    mock.read.mockResolvedValueOnce({ fileName: 'bien.pdf', pages: 1, codes: [{ code: 'FAC-9', source: 'bien.pdf p1', format: 'Code128' }], emptyPages: [], error: null })
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('tab', { name: 'Reconciliar' }))
    await user.upload(await screen.findByLabelText('Archivos de hojas escaneadas'), [pdf('roto.pdf'), pdf('bien.pdf')])
    expect(await screen.findByText('no se pudo leer el archivo')).toBeInTheDocument()
    expect(await screen.findByText('Códigos por procesar (1)')).toBeInTheDocument()
  })

  it('sin cod.reconcile no hay pestaña Reconciliar; las corridas se listan y abren con su detalle', async () => {
    const user = userEvent.setup()
    wrap(['cod.view'])
    expect(await screen.findByText('ORD-00001')).toBeInTheDocument()
    expect(screen.queryByRole('tab', { name: 'Reconciliar' })).toBeNull()
    await user.click(screen.getByRole('tab', { name: 'Corridas' }))
    await user.click(await screen.findByText('hojas.pdf'))
    const dialog = await screen.findByRole('dialog', { name: /Corrida del/ })
    expect((await within(dialog).findAllByText('FAC-00001')).length).toBeGreaterThan(0)
    expect(within(dialog).getAllByText('No encontrado').length).toBeGreaterThan(0)
  })
})
