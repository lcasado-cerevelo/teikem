// Lote 181 (H1) — «Contabilización de compras / de despachos» sobre un fetch simulado: la lista, generar viendo antes lo que entraría, aceptar, exportar con la plantilla y los permisos de cada tipo.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import AccountingScreen from './AccountingScreen'

interface Call {
  method: string
  url: URL
  body: unknown
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], runStatus: 'GENERATED' as string }))
vi.mock('../billing/exportFile', () => ({ downloadExportFile: vi.fn() }))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    mock.calls.push({ method: req.method, url, body: text ? JSON.parse(text) : null })
    const [status, out] = route(req.method, url)
    return new Response(JSON.stringify(out), { status, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const RUN = '11111111-0000-0000-0000-000000000001'
const LINE = { id: 1, docDate: '2026-10-20', documentNumber: 'REC-00001', referenceNumber: 'PO-00001', partyName: 'Proveedor Uno', warehouseCode: 'ALM-01', sku: 'MED-1', productName: 'Medidor', quantity: 10, unitCost: 5, totalCost: 50, costSource: 'PO' }
const NOCOST = { ...LINE, id: 2, sku: 'MED-2', productName: 'Sin costo', quantity: 4, unitCost: null, totalCost: null, costSource: null }
const item = (status: string) => ({ publicId: RUN, kind: 'PURCHASES', number: 'CMP-00001', statusCode: status, status: status === 'GENERATED' ? 'Generada' : status === 'ACCEPTED' ? 'Aceptada' : 'Exportada', statusColor: '#9CA3AF', periodStart: '2026-10-01', periodEnd: '2026-10-21', lineCount: 2, totalAmount: 50, unpricedLineCount: 1, generatedAtUtc: '2026-10-21T12:00:00Z' })
const detail = (status: string) => ({ ...item(status), currency: 'USD', note: null, generatedByName: 'Ana', acceptedAtUtc: null, acceptedByName: null, exportedAtUtc: null, exportedByName: null, rowVersion: 'AA==', lines: [LINE, NOCOST] })
const TEMPLATE = { publicId: '22222222-0000-0000-0000-000000000001', scope: 'PURCHASE_ACCOUNTING', name: 'Formato estándar', format: 'CSV', delimiter: ',', dateFormat: 'YYYY-MM-DD', includeHeader: true, columns: [{ field: 'RUN_NUMBER', header: null }] }

function route(method: string, url: URL): [number, unknown] {
  const p = url.pathname
  if (method === 'GET' && p === '/api/v1/purchase-accounting') return [200, { items: [item(mock.runStatus)], total: 1, skip: 0, take: 25 }]
  if (method === 'GET' && p === `/api/v1/purchase-accounting/${RUN}`) return [200, detail(mock.runStatus)]
  if (method === 'POST' && p === '/api/v1/purchase-accounting/preview') return [200, { kind: 'PURCHASES', lineCount: 2, totalAmount: 50, unpricedLineCount: 1, lines: [LINE, NOCOST] }]
  if (method === 'POST' && p === '/api/v1/purchase-accounting') return [200, detail('GENERATED')]
  if (method === 'POST' && p === `/api/v1/purchase-accounting/${RUN}/accept`) return [200, detail('ACCEPTED')]
  if (method === 'POST' && p === `/api/v1/purchase-accounting/${RUN}/cancel`) return [200, detail('CANCELLED')]
  if (method === 'GET' && p === '/api/v1/export-templates') return [200, [TEMPLATE]]
  if (method === 'GET' && p === `/api/v1/purchase-accounting/${RUN}/export-preview`) return [200, { fileName: 'contabilizacion-compras-CMP-00001.csv', contentType: 'text/csv', text: 'RunNo\r\nCMP-00001\r\n', rowCount: 1, templateName: 'Formato estándar' }]
  if (method === 'POST' && p === `/api/v1/purchase-accounting/${RUN}/export`) return [200, { fileName: 'contabilizacion-compras-CMP-00001.csv', contentType: 'text/csv', text: 'RunNo\r\nCMP-00001\r\n', rowCount: 1, templateName: 'Formato estándar' }]
  return [200, []]
}

function wrap(permissions = ['purchasing.view', 'purchasing.manage']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter>
      <QueryClientProvider client={client}>
        <AccessProvider permissions={permissions} modules={['PURCHASING', 'BILLING', 'WMS_LOTSERIAL']}>
          <AccountingScreen kind="PURCHASES" />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}
const posted = (suffix: string) => mock.calls.filter((c) => c.method === 'POST' && c.url.pathname.endsWith(suffix))

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.runStatus = 'GENERATED'
  window.localStorage.clear()
})

describe('Contabilización de compras', () => {
  it('lista las corridas con su total, sus líneas sin costo y su estatus', async () => {
    wrap()
    expect(await screen.findByText('CMP-00001')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1, name: 'Contabilización de compras' })).toBeInTheDocument()
    expect(screen.getAllByText('Generada').length).toBeGreaterThan(1)   // la opción del filtro y el estatus de la fila
    expect(screen.getByText('$50.00')).toBeInTheDocument()
  })

  it('«Generar corrida» muestra antes lo que entraría y manda el período; al generar abre la ficha', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Generar corrida' }))
    const dialog = await screen.findByRole('dialog', { name: 'Generar corrida de compras' })
    expect(await within(dialog).findByText('Lo que entraría (2 líneas)')).toBeInTheDocument()
    expect(within(dialog).getByText('MED-1')).toBeInTheDocument()
    expect(within(dialog).getByText(/1 línea\(s\) no traen costo congelado/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Generar' }))
    await waitFor(() => expect(posted('/purchase-accounting')).toHaveLength(1))
    expect(posted('/purchase-accounting')[0].body).toMatchObject({ periodStart: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/), periodEnd: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/) })
    expect(await screen.findByRole('dialog', { name: 'Corrida de compras CMP-00001' })).toBeInTheDocument()
  })

  it('la ficha de una generada se acepta y la de una aceptada se exporta con la plantilla', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByText('CMP-00001'))
    let dialog = await screen.findByRole('dialog', { name: 'Corrida de compras CMP-00001' })
    expect((await within(dialog).findAllByText('REC-00001')).length).toBe(2)
    expect(within(dialog).getByText('sin costo')).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Exportar' })).toBeNull()   // generada: aún no se exporta
    await user.click(within(dialog).getByRole('button', { name: 'Aceptar' }))
    await waitFor(() => expect(posted('/accept')).toHaveLength(1))

    mock.runStatus = 'ACCEPTED'
    await user.click(within(dialog).getAllByRole('button', { name: 'Cerrar' }).at(-1)!)
    await user.click(await screen.findByText('CMP-00001'))
    dialog = await screen.findByRole('dialog', { name: 'Corrida de compras CMP-00001' })
    await user.click(await within(dialog).findByRole('button', { name: 'Exportar' }))
    const exp = await screen.findByRole('dialog', { name: 'Exportar la corrida CMP-00001' })
    expect(await within(exp).findByRole('option', { name: 'Formato estándar' })).toBeInTheDocument()
    await user.click(within(exp).getByRole('button', { name: 'Ver archivo' }))
    expect(await within(exp).findByText(/RunNo/)).toBeInTheDocument()
    await user.click(within(exp).getByRole('button', { name: 'Descargar y marcar exportada' }))
    await waitFor(() => expect(posted('/export')).toHaveLength(1))
    expect(posted('/export')[0].url.searchParams.get('templatePublicId')).toBe(TEMPLATE.publicId)
  })

  it('cancelar pide confirmación; sin permiso de gestionar no hay acciones', async () => {
    const user = userEvent.setup()
    const first = wrap()
    await user.click(await screen.findByText('CMP-00001'))
    const dialog = await screen.findByRole('dialog', { name: 'Corrida de compras CMP-00001' })
    await user.click(await within(dialog).findByRole('button', { name: 'Cancelar corrida' }))
    const confirm = await screen.findByRole('dialog', { name: '¿Cancelar la corrida CMP-00001?' })
    await user.click(within(confirm).getByRole('button', { name: 'Cancelar corrida' }))
    await waitFor(() => expect(posted('/cancel')).toHaveLength(1))
    first.unmount()

    wrap(['purchasing.view'])
    await user.click(await screen.findByText('CMP-00001'))
    const readonly = await screen.findByRole('dialog', { name: 'Corrida de compras CMP-00001' })
    await within(readonly).findAllByText('REC-00001')
    expect(within(readonly).queryByRole('button', { name: 'Aceptar' })).toBeNull()
    expect(within(readonly).queryByRole('button', { name: 'Cancelar corrida' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Generar corrida' })).toBeNull()
  })
})
