// Pruebas de «Exportar a contabilidad» (Lote 78) sobre un fetch simulado: el selector de plantilla, la vista previa y la descarga que marca la corrida, y el editor de la plantilla.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import BillingScreen from './BillingScreen'

const mock = vi.hoisted(() => ({
  requests: [] as { method: string; path: string; search: string; body: unknown }[],
  runStatus: 'APPROVED',
  templates: [] as { publicId: string; name: string; format: string; delimiter: string; dateFormat: string; includeHeader: boolean; columns: { field: string; header?: string }[] }[],
  failSave: false,
}))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, search: url.search, body })
    const p = url.pathname
    const m = req.method
    if (m === 'GET' && p === '/api/v1/billing/runs') return json([{ id: 7, periodStart: '2026-10-01', periodEnd: '2026-10-15', status: mock.runStatus, statusLabel: mock.runStatus, statusColor: '#3B82F6', clientName: null, invoiceCount: 1, totalAmount: 100, generatedAtUtc: '2026-10-16T12:00:00Z' }])
    if (m === 'GET' && p === '/api/v1/billing/runs/7') return json(RUN())
    if (m === 'GET' && p === '/api/v1/export-templates') return json(mock.templates)
    if (m === 'GET' && p === '/api/v1/export-templates/fields') return json(FIELDS)
    if (m === 'GET' && p === '/api/v1/billing/runs/7/export-preview') return json(FILE)
    if (m === 'POST' && p === '/api/v1/billing/runs/7/export') {
      mock.runStatus = 'EXPORTED'
      return json(FILE)
    }
    if (m === 'POST' && p === '/api/v1/export-templates') {
      if (mock.failSave) return problem(409, 'Ya existe una plantilla con ese nombre.')
      const b = body as { name: string }
      mock.templates.push({ publicId: 'cccccccc-0000-0000-0000-000000000003', name: b.name, format: 'CSV', delimiter: ',', dateFormat: 'YYYY-MM-DD', includeHeader: true, columns: [{ field: 'INVOICE_NUMBER' }] })
      return json(mock.templates[mock.templates.length - 1])
    }
    if (m === 'PUT' && p === `/api/v1/export-templates/${T1}`) return json(mock.templates[0])
    if (m === 'DELETE' && p === `/api/v1/export-templates/${T2}`) return new Response(null, { status: 204 })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
function problem(status: number, title: string) {
  return new Response(JSON.stringify({ title, status, code: 'conflict' }), { status, headers: { 'Content-Type': 'application/problem+json' } })
}

const T1 = 'aaaaaaaa-0000-0000-0000-000000000001'
const T2 = 'bbbbbbbb-0000-0000-0000-000000000002'
const FIELDS = [
  { code: 'INVOICE_NUMBER', kind: 'TEXT', defaultHeader: 'InvoiceNo', label: 'Número de factura' },
  { code: 'ISSUE_DATE', kind: 'DATE', defaultHeader: 'InvoiceDate', label: 'Fecha de emisión' },
  { code: 'INVOICE_TOTAL', kind: 'MONEY', defaultHeader: 'InvoiceTotal', label: 'Total de la factura' },
]
const FILE = { fileName: 'facturacion-corrida-7.csv', contentType: 'text/csv; charset=utf-8', text: 'InvoiceNo,InvoiceTotal\r\nFAC-00001,100.00\r\n', rowCount: 1, templateName: 'Formato estándar' }
const RUN = () => ({
  id: 7, periodStart: '2026-10-01', periodEnd: '2026-10-15', status: mock.runStatus, statusLabel: mock.runStatus, statusColor: '#3B82F6', clientPublicId: null, clientName: null, note: null,
  invoiceCount: 1, totalAmount: 100, generatedAtUtc: '2026-10-16T12:00:00Z', reviewedAtUtc: null, approvedAtUtc: '2026-10-16T13:00:00Z', invoices: [],
  exportedAtUtc: mock.runStatus === 'EXPORTED' ? '2026-10-16T14:00:00Z' : null,
})

const SESSION = {
  me: { userId: 99, tenantName: 'Advance Logistics' },
  isAuthenticated: true, isLoading: false, error: null, tenantId: 7, lang: 'es', setLang: () => {}, logout: async () => {}, switchTenant: async () => {},
  permissions: new Set(['billing.view', 'billing.generate', 'billing.approve', 'billing.export']),
  modules: new Set(['BILLING']),
  reloadMe: async () => {},
} as unknown as Session

function wrap(permissions?: string[]) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const perms = permissions ? new Set(permissions) : SESSION.permissions
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={perms} modules={SESSION.modules}>
          <MemoryRouter>
            <BillingScreen />
          </MemoryRouter>
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

const sent = (method: string, path: string) => mock.requests.filter((r) => r.method === method && r.path === path)

async function openRun(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByText('#7'))
  return screen.findByRole('dialog', { name: /Corrida #7/ })
}

describe('Exportar a contabilidad', () => {
  beforeAll(() => setLang('es'))
  beforeEach(() => {
    mock.requests.length = 0
    mock.runStatus = 'APPROVED'
    mock.failSave = false
    mock.templates = [
      { publicId: T1, name: 'Formato estándar', format: 'CSV', delimiter: ',', dateFormat: 'YYYY-MM-DD', includeHeader: true, columns: [{ field: 'INVOICE_NUMBER' }, { field: 'INVOICE_TOTAL', header: 'Total' }] },
      { publicId: T2, name: 'QuickBooks', format: 'TXT', delimiter: '\t', dateFormat: 'MM/DD/YYYY', includeHeader: false, columns: [{ field: 'INVOICE_NUMBER' }] },
    ]
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: vi.fn(() => 'blob:x'), revokeObjectURL: vi.fn() }))
  })

  it('la sección sale en una corrida aprobada con permiso de exportar y con las plantillas en el selector', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openRun(user)
    expect(await within(dialog).findByText('Exportar a contabilidad')).toBeInTheDocument()
    const select = await within(dialog).findByLabelText('Plantilla de exportación')
    expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual(['Formato estándar', 'QuickBooks'])
    expect(within(dialog).getByRole('button', { name: 'Configurar…' })).toBeEnabled()
  })

  it('no sale sin permiso de exportar ni en una corrida que no está aprobada', async () => {
    const user = userEvent.setup()
    wrap(['billing.view', 'billing.generate', 'billing.approve'])
    const dialog = await openRun(user)
    await within(dialog).findByText(/Corrida/)
    expect(within(dialog).queryByText('Exportar a contabilidad')).toBeNull()
  })

  it('no sale en una corrida generada', async () => {
    const user = userEvent.setup()
    mock.runStatus = 'GENERATED'
    wrap()
    const dialog = await openRun(user)
    await within(dialog).findByText(/Corrida/)
    await waitFor(() => expect(sent('GET', '/api/v1/billing/runs/7').length).toBeGreaterThan(0))
    expect(within(dialog).queryByText('Exportar a contabilidad')).toBeNull()
  })

  it('la vista previa muestra el archivo con la plantilla elegida y descargar marca la corrida como exportada', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openRun(user)
    await user.selectOptions(await within(dialog).findByLabelText('Plantilla de exportación'), T2)
    await user.click(within(dialog).getByRole('button', { name: 'Archivo de exportación' }))
    const preview = await screen.findByRole('dialog', { name: /Archivo de la corrida #7/ })
    expect(await within(preview).findByLabelText('Contenido del archivo')).toHaveTextContent('FAC-00001,100.00')
    expect(sent('GET', '/api/v1/billing/runs/7/export-preview')[0].search).toContain(`templateId=${T2}`)
    expect(sent('POST', '/api/v1/billing/runs/7/export')).toHaveLength(0)   // ver no cambia nada
    expect(within(preview).getByText('Al descargar, la corrida pasa a «Exportada».')).toBeInTheDocument()

    await user.click(within(preview).getByRole('button', { name: 'Descargar y marcar como exportada' }))
    await waitFor(() => expect(sent('POST', '/api/v1/billing/runs/7/export')).toHaveLength(1))
    expect(sent('POST', '/api/v1/billing/runs/7/export')[0].body).toEqual({ templatePublicId: T2 })
    await waitFor(() => expect(URL.createObjectURL).toHaveBeenCalled())
    await waitFor(() => expect(screen.queryByRole('dialog', { name: /Archivo de la corrida #7/ })).toBeNull())
    expect(await within(dialog).findByText(/Exportada el/)).toBeInTheDocument()
  })

  it('una corrida ya exportada ofrece descargar de nuevo sin el aviso de marcar', async () => {
    const user = userEvent.setup()
    mock.runStatus = 'EXPORTED'
    wrap()
    const dialog = await openRun(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Archivo de exportación' }))
    const preview = await screen.findByRole('dialog', { name: /Archivo de la corrida #7/ })
    expect(await within(preview).findByRole('button', { name: 'Descargar de nuevo' })).toBeInTheDocument()
    expect(within(preview).queryByText('Al descargar, la corrida pasa a «Exportada».')).toBeNull()
  })

  it('configurar: cambia el formato, mueve y agrega columnas y guarda la plantilla con el cuerpo completo', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openRun(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Configurar…' }))
    const editor = await screen.findByRole('dialog', { name: 'Configurar la plantilla' })
    expect(await within(editor).findByLabelText('Nombre')).toHaveValue('Formato estándar')
    await user.selectOptions(within(editor).getByLabelText('Delimitador'), ';')
    await user.selectOptions(within(editor).getByLabelText('Formato de fecha'), 'DD/MM/YYYY')
    await user.click(within(editor).getByLabelText('Incluir fila de encabezado'))
    await user.click(await within(editor).findByRole('button', { name: 'Subir Total de la factura' }))   // el total pasa antes del número
    await user.selectOptions(within(editor).getByLabelText('Agregar una columna'), 'ISSUE_DATE')
    await user.type(within(editor).getByLabelText('Encabezado de Fecha de emisión'), 'Fecha')
    await user.click(within(editor).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(sent('PUT', `/api/v1/export-templates/${T1}`)).toHaveLength(1))
    expect(sent('PUT', `/api/v1/export-templates/${T1}`)[0].body).toEqual({
      name: 'Formato estándar',
      format: 'CSV',
      delimiter: ';',
      dateFormat: 'DD/MM/YYYY',
      includeHeader: false,
      columns: [{ field: 'INVOICE_TOTAL', header: 'Total' }, { field: 'INVOICE_NUMBER' }, { field: 'ISSUE_DATE', header: 'Fecha' }],
    })
  })

  it('valida antes de enviar y «Guardar como plantilla nueva» crea otra con su nombre', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openRun(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Configurar…' }))
    const editor = await screen.findByRole('dialog', { name: 'Configurar la plantilla' })
    await user.clear(await within(editor).findByLabelText('Nombre'))
    await user.click(within(editor).getByRole('button', { name: 'Guardar' }))
    expect(await within(editor).findByText('Escriba el nombre de la plantilla.')).toBeInTheDocument()
    await user.type(within(editor).getByLabelText('Nombre'), 'Contable')
    await user.click(await within(editor).findByRole('button', { name: 'Quitar Número de factura' }))
    await user.click(await within(editor).findByRole('button', { name: 'Quitar Total de la factura' }))
    await user.click(within(editor).getByRole('button', { name: 'Guardar como plantilla nueva' }))
    expect(await within(editor).findByText('Elija al menos una columna.')).toBeInTheDocument()
    expect(sent('POST', '/api/v1/export-templates')).toHaveLength(0)

    await user.selectOptions(within(editor).getByLabelText('Agregar una columna'), 'INVOICE_NUMBER')
    await user.click(within(editor).getByRole('button', { name: 'Guardar como plantilla nueva' }))
    await waitFor(() => expect(sent('POST', '/api/v1/export-templates')).toHaveLength(1))
    expect(sent('POST', '/api/v1/export-templates')[0].body).toMatchObject({ name: 'Contable', columns: [{ field: 'INVOICE_NUMBER' }] })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Configurar la plantilla' })).toBeNull())
    expect(await within(dialog).findByRole('option', { name: 'Contable' })).toBeInTheDocument()
  })

  it('muestra el mensaje del servidor si el nombre ya existe', async () => {
    const user = userEvent.setup()
    mock.failSave = true
    wrap()
    const dialog = await openRun(user)
    await user.click(await within(dialog).findByRole('button', { name: 'Configurar…' }))
    const editor = await screen.findByRole('dialog', { name: 'Configurar la plantilla' })
    await user.click(await within(editor).findByRole('button', { name: 'Guardar como plantilla nueva' }))
    expect(await within(editor).findByText('Ya existe una plantilla con ese nombre.')).toBeInTheDocument()
  })

  it('desactivar una plantilla pide confirmación y llama al servidor', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openRun(user)
    await user.selectOptions(await within(dialog).findByLabelText('Plantilla de exportación'), T2)
    await user.click(within(dialog).getByRole('button', { name: 'Configurar…' }))
    const editor = await screen.findByRole('dialog', { name: 'Configurar la plantilla' })
    await user.click(await within(editor).findByRole('button', { name: 'Desactivar la plantilla' }))
    const confirm = await screen.findByRole('dialog', { name: /¿Desactivar la plantilla «QuickBooks»\?/ })
    await user.click(within(confirm).getByRole('button', { name: 'Desactivar la plantilla' }))
    await waitFor(() => expect(sent('DELETE', `/api/v1/export-templates/${T2}`)).toHaveLength(1))
  })
})
