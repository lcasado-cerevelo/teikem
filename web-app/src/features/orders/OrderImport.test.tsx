// Lote F-C3 — Importación de envíos desde CSV: plantilla → cliente → archivo → vista previa del servidor (errores, avisos y consignatario por
// fila) → Importar con las casillas de la confirmación (Idempotency-Key) o Descartar → resultado con número de orden o error por fila;
// editor de plantillas de envíos (reglas de consignatario y paquete, valores por defecto, desactivar/reactivar) y permisos. Sobre un fetch
// simulado; el FormData se sustituye por un registro (jsdom + el Request de Node no se entienden con archivos).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { OrderImportModal } from './OrderImportModal'

interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | undefined
  headers: Headers
}
const mock = vi.hoisted(() => ({
  calls: [] as Call[],
  uploads: [] as { field: string; value: unknown }[],
  override: null as null | ((call: Call) => unknown),
}))
vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const text = req.method === 'GET' ? '' : await req.text()
    let body: Record<string, unknown> | undefined
    try {
      body = text ? (JSON.parse(text) as Record<string, unknown>) : undefined
    } catch {
      body = undefined
    }
    const call = { method: req.method, url: new URL(req.url), body, headers: req.headers }
    mock.calls.push(call)
    const out = mock.override?.(call) ?? route(call)
    return out instanceof Response ? out : new Response(JSON.stringify(out), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

class RecordingFormData {
  append(field: string, value: unknown) {
    mock.uploads.push({ field, value })
  }
}

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const FIELDS = [
  { field: 'orderNumber', required: false, defaultAllowed: false },
  { field: 'clientInvoiceNumber', required: false, defaultAllowed: false },
  { field: 'serviceType', required: false, defaultAllowed: true },
  { field: 'consigneeName', required: false, defaultAllowed: false },
  { field: 'consigneeCode', required: false, defaultAllowed: true },
  { field: 'line1', required: false, defaultAllowed: true },
  { field: 'city', required: false, defaultAllowed: true },
  { field: 'packageType', required: false, defaultAllowed: true },
  { field: 'pieces', required: false, defaultAllowed: true },
  { field: 'codAmount', required: false, defaultAllowed: true },
]
const T1 = {
  id: 1,
  publicId: 't-1',
  kind: 'ORDER',
  name: 'Formato estándar',
  clientPublicId: null,
  delimiter: ',',
  hasHeader: true,
  columns: [
    { position: 1, field: 'consigneeName' },
    { position: 2, field: 'line1' },
    { position: 3, field: 'packageType' },
    { position: 4, field: 'pieces' },
  ],
  defaults: {},
  isActive: true,
}
const PREVIEW = {
  batchPublicId: 'b-1',
  status: 'VALIDATED',
  templatePublicId: 't-1',
  templateName: 'Formato estándar',
  clientPublicId: 'c-1',
  clientName: 'Acme Corp',
  fileName: 'ordenes.csv',
  rowCount: 3,
  validRows: 2,
  rows: [
    { row: 1, line: 2, isValid: true, values: { consigneeName: 'Farmacia Central', line1: 'Calle Sol 1', packageType: 'BOX', pieces: '2' }, consignee: { action: 'EXISTING', name: 'Farmacia Central', locationPublicId: 'l-1' }, errors: {}, warnings: [] },
    { row: 2, line: 3, isValid: false, values: { consigneeName: 'Tienda Nueva', line1: 'Calle 9', packageType: 'XYZ', pieces: '1' }, consignee: { action: 'CREATE', name: 'Tienda Nueva' }, errors: { packageType: 'Tipo de paquete desconocido: XYZ.' }, warnings: [] },
    { row: 3, line: 4, isValid: true, values: { consigneeName: 'Colmado Dos', line1: 'Calle 3', packageType: 'ENV', pieces: '1' }, consignee: { action: 'CREATE', name: 'Colmado Dos' }, errors: {}, warnings: ["La factura F-9 ya la usa el envío ORD-00007 de este consignatario."] },
  ],
}
const RESULT = {
  batchPublicId: 'b-1',
  status: 'CONFIRMED',
  created: 1,
  failed: 1,
  skipped: 1,
  rows: [
    { row: 1, line: 2, isValid: true, orderPublicId: 'o-9', orderNumber: 'ORD-00009', orderStatus: 'DRAFT' },
    { row: 2, line: 3, isValid: false, skipped: true },
    { row: 3, line: 4, isValid: true, error: 'El cliente excede su límite de crédito.' },
  ],
}

let templates: Record<string, unknown>[] = []

function route({ method, url, body }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/import-templates' && method === 'GET') {
    const inactive = url.searchParams.get('includeInactive') === 'true'
    return templates.filter((x) => inactive || x.isActive !== false)
  }
  if (p === '/api/v1/import-templates' && method === 'POST') {
    const created = { ...T1, id: 9, publicId: 't-new', name: body?.name, columns: body?.columns, defaults: body?.defaults ?? {} }
    templates = [...templates, created]
    return created
  }
  if (p === '/api/v1/import-templates/fields') return FIELDS
  if (p.startsWith('/api/v1/import-templates/') && method === 'PATCH') return { ...T1, ...body }
  if (p.endsWith('/deactivate') || p.endsWith('/reactivate')) {
    const id = p.split('/').at(-2)
    const active = p.endsWith('/reactivate')
    templates = templates.map((x) => (x.publicId === id ? { ...x, isActive: active } : x))
    return templates.find((x) => x.publicId === id)
  }
  if (p === '/api/v1/orders/import/validate-file') return PREVIEW
  if (p === '/api/v1/orders/import/b-1/confirm') return RESULT
  if (p === '/api/v1/orders/import/b-1/discard') return { ...PREVIEW, status: 'DISCARDED' }
  if (p === '/api/v1/clients') return [{ id: 1, publicId: 'c-1', code: 'ACME', name: 'Acme Corp', isActive: true }, { id: 2, publicId: 'c-2', code: 'BETA', name: 'Beta SA', isActive: true }]
  if (p.startsWith('/api/v1/')) return []
  return problem(404, 'No encontrado', 'not_found')
}

const ALL = ['orders.create', 'orders.edit', 'orders.credit_override', 'clients.read']
function renderModal(permissions = ALL, defaultClient = '') {
  const onClose = vi.fn()
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={['LTL_GROUND', 'CATALOG']}>
          <OrderImportModal defaultClient={defaultClient} onClose={onClose} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
  return { onClose }
}

type User = ReturnType<typeof userEvent.setup>
const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)
const csv = (name = 'ordenes.csv') => new File(['consignee,line1,package,pieces\n'], name, { type: 'text/csv' })
const fileInput = () => screen.getByLabelText('Archivo CSV') as HTMLInputElement
const confirmCalls = () => callsTo('POST', '/api/v1/orders/import/b-1/confirm')

/** Cliente Acme elegido, archivo subido y vista previa a la vista. */
async function previewed(user: User) {
  await screen.findByText(/1\. Consignatario \(nombre\)/)
  await user.selectOptions(screen.getByLabelText('Cliente'), 'ACME · Acme Corp')
  await user.upload(fileInput(), csv())
  await screen.findByText(/3 filas detectadas/)
}

beforeAll(() => {
  vi.stubGlobal('FormData', RecordingFormData)
  setLang('es')
})
afterAll(() => vi.unstubAllGlobals())
beforeEach(() => {
  mock.calls = []
  mock.uploads = []
  mock.override = null
  templates = [{ ...T1 }]
})

describe('Importar envíos — plantilla, cliente y archivo', () => {
  it('sin plantilla el archivo queda deshabilitado y se ofrece crear el «Formato estándar» (consignatario + paquete)', async () => {
    templates = []
    const user = userEvent.setup()
    renderModal()
    expect(await screen.findByText(/Crea o escoge una plantilla/)).toBeInTheDocument()
    expect(fileInput()).toBeDisabled()
    await user.click(screen.getByRole('button', { name: /Crear «Formato estándar»/ }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/import-templates')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/import-templates')[0].body).toEqual({
      name: 'Formato estándar',
      kind: 'ORDER',
      delimiter: ',',
      hasHeader: true,
      columns: [
        { position: 1, field: 'consigneeName' },
        { position: 2, field: 'line1' },
        { position: 3, field: 'city' },
        { position: 4, field: 'serviceType' },
        { position: 5, field: 'packageType' },
        { position: 6, field: 'pieces' },
        { position: 7, field: 'codAmount' },
      ],
    })
  })

  it('pide plantillas de envíos (kind=ORDER) y no deja escoger archivo hasta elegir el cliente', async () => {
    const user = userEvent.setup()
    renderModal()
    expect(await screen.findByText(/1\. Consignatario \(nombre\) · 2\. Dirección · 3\. Tipo de paquete · 4\. Piezas/)).toBeInTheDocument()
    const q = callsTo('GET', '/api/v1/import-templates')[0].url.searchParams
    expect(q.get('kind')).toBe('ORDER')
    expect(fileInput()).toBeDisabled()
    expect(screen.getByText('Elija primero el cliente para poder escoger el archivo.')).toBeInTheDocument()
    expect(screen.getByText('Máximo 5,000 filas y 2 MB por archivo.')).toBeInTheDocument()
    expect(screen.getByText(/Guarda el CSV en UTF-8/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Importar' })).toBeDisabled()
    await user.selectOptions(screen.getByLabelText('Cliente'), 'ACME · Acme Corp')
    expect(fileInput()).toBeEnabled()
  })

  it('las plantillas del cliente se piden con su clientId', async () => {
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/1\. Consignatario/)
    await user.selectOptions(screen.getByLabelText('Cliente'), 'ACME · Acme Corp')
    await waitFor(() => expect(callsTo('GET', '/api/v1/import-templates').some((c) => c.url.searchParams.get('clientId') === 'c-1')).toBe(true))
  })

  it('valida el archivo en el servidor y muestra por fila el estado, el consignatario, los errores y los avisos', async () => {
    const user = userEvent.setup()
    renderModal(ALL, 'c-1')
    await screen.findByText(/1\. Consignatario/)
    await user.upload(fileInput(), csv())
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/import/validate-file')).toHaveLength(1))
    expect(mock.uploads.map((u) => u.field)).toEqual(['templatePublicId', 'clientPublicId', 'file'])
    expect(mock.uploads[0].value).toBe('t-1')
    expect(mock.uploads[1].value).toBe('c-1')
    expect(await screen.findByText(/ordenes\.csv · 3 filas detectadas/)).toBeInTheDocument()
    const table = screen.getByRole('table')
    expect(within(table).getByText('Tipo de paquete: Tipo de paquete desconocido: XYZ.')).toBeInTheDocument()
    expect(within(table).getByText(/La factura F-9 ya la usa el envío ORD-00007/)).toBeInTheDocument()
    expect(within(table).getByText('Existente: Farmacia Central')).toBeInTheDocument()
    expect(within(table).getByText('Se creará: Tienda Nueva')).toBeInTheDocument()
    expect(screen.getByText('válidas').closest('.cs-stat')).toHaveTextContent('2 válidas')
    expect(screen.getByText('con errores').closest('.cs-stat')).toHaveTextContent('1 con errores')
    await user.click(screen.getByRole('switch', { name: 'Solo filas con errores o avisos' }))
    expect(within(screen.getByRole('table')).queryByText('Existente: Farmacia Central')).toBeNull()
  })

  it('cambiar el cliente con un archivo elegido descarta el lote y valida otra vez con ese cliente', async () => {
    const user = userEvent.setup()
    renderModal()
    await previewed(user)
    await user.selectOptions(screen.getByLabelText('Cliente'), 'BETA · Beta SA')
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/import/validate-file')).toHaveLength(2))
    expect(callsTo('POST', '/api/v1/orders/import/b-1/discard').length).toBeGreaterThan(0)
    expect(mock.uploads.filter((u) => u.field === 'clientPublicId').map((u) => u.value)).toEqual(['c-1', 'c-2'])
  })

  it('un archivo de más de 2 MB se rechaza antes de llamar al servidor', async () => {
    const user = userEvent.setup()
    renderModal(ALL, 'c-1')
    await screen.findByText(/1\. Consignatario/)
    const big = csv('grande.csv')
    Object.defineProperty(big, 'size', { value: 3 * 1024 * 1024 })
    await user.upload(fileInput(), big)
    expect(await screen.findByText('El archivo supera el límite de 5.000 filas o 2 MB.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/orders/import/validate-file')).toHaveLength(0)
  })

  it('un 400 del servidor se muestra con su título y sus errores', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/orders/import/validate-file' ? problem(400, 'Solicitud inválida', 'validation', { content: ['El archivo no tiene filas de datos.'] }) : undefined
    renderModal(ALL, 'c-1')
    await screen.findByText(/1\. Consignatario/)
    await user.upload(fileInput(), csv())
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Solicitud inválida')
    expect(alert).toHaveTextContent('El archivo no tiene filas de datos.')
    expect(screen.getByRole('button', { name: 'Importar' })).toBeDisabled()
  })

  it('un cliente suspendido (422) al validar se muestra y no deja importar', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/orders/import/validate-file' ? problem(422, 'El cliente está suspendido; no se pueden crear ni confirmar envíos.', 'status_rule') : undefined
    renderModal(ALL, 'c-1')
    await screen.findByText(/1\. Consignatario/)
    await user.upload(fileInput(), csv())
    expect(await screen.findByText('El cliente está suspendido; no se pueden crear ni confirmar envíos.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Importar' })).toBeDisabled()
  })
})

describe('Importar envíos — confirmar, descartar y resultado', () => {
  it('Importar sin casillas confirma las válidas Sometidas, con Idempotency-Key, y muestra creadas, fallidas y omitidos con su número o error', async () => {
    const user = userEvent.setup()
    renderModal()
    await previewed(user)
    await user.click(screen.getByRole('button', { name: 'Importar' }))
    await waitFor(() => expect(confirmCalls()).toHaveLength(1))
    expect(confirmCalls()[0].headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/)
    expect(confirmCalls()[0].body).toEqual({})
    const result = (await screen.findByText('Resultado de la importación')).closest('.cs-result') as HTMLElement
    expect(result).toHaveTextContent('1 creadas')
    expect(result).toHaveTextContent('1 fallidas')
    expect(result).toHaveTextContent('1 omitidos')
    const link = within(result).getByRole('link', { name: 'ORD-00009' })
    expect(link).toHaveAttribute('href', '/orders/o-9')
    expect(within(result).getByText('El cliente excede su límite de crédito.')).toBeInTheDocument()
    expect(within(screen.getByRole('dialog', { name: 'Importar envíos desde CSV' })).getAllByRole('button', { name: 'Cerrar' })).toHaveLength(2)
  })

  it('las casillas viajan en el cuerpo: autorizar crédito y aceptar facturas repetidas; nunca confirmNow', async () => {
    const user = userEvent.setup()
    renderModal()
    await previewed(user)
    expect(screen.queryByRole('switch', { name: 'Confirmar los envíos al importar' })).toBeNull()
    const override = screen.getByRole('switch', { name: 'Autorizar crédito excedido' })
    const dup = screen.getByRole('switch', { name: 'Aceptar facturas repetidas' })
    expect(override).toBeEnabled()
    await user.click(override)
    await user.click(dup)
    await user.click(screen.getByRole('button', { name: 'Importar' }))
    await waitFor(() => expect(confirmCalls()).toHaveLength(1))
    expect(confirmCalls()[0].body).toEqual({ overrideCredit: true, confirmDuplicateInvoice: true })
  })

  it('sin orders.credit_override la casilla de autorizar crédito no existe', async () => {
    const user = userEvent.setup()
    renderModal(['orders.create', 'orders.edit', 'clients.read'])
    await previewed(user)
    expect(screen.getByRole('switch', { name: 'Aceptar facturas repetidas' })).toBeInTheDocument()
    expect(screen.queryByRole('switch', { name: 'Autorizar crédito excedido' })).toBeNull()
  })

  it('un reintento con las mismas opciones lleva la MISMA Idempotency-Key; con otras opciones, otra', async () => {
    const user = userEvent.setup()
    let fail = true
    mock.override = ({ method, url }) => {
      if (method === 'POST' && url.pathname === '/api/v1/orders/import/b-1/confirm' && fail) return problem(503, 'Servicio no disponible', 'internal')
      return undefined
    }
    renderModal()
    await previewed(user)
    await user.click(screen.getByRole('button', { name: 'Importar' }))
    await screen.findByText('Servicio no disponible')
    await user.click(screen.getByRole('button', { name: 'Importar' }))
    await waitFor(() => expect(confirmCalls()).toHaveLength(2))
    expect(confirmCalls()[0].headers.get('Idempotency-Key')).toBe(confirmCalls()[1].headers.get('Idempotency-Key'))
    await user.click(screen.getByRole('switch', { name: 'Aceptar facturas repetidas' }))
    fail = false
    await user.click(screen.getByRole('button', { name: 'Importar' }))
    await waitFor(() => expect(confirmCalls()).toHaveLength(3))
    expect(confirmCalls()[2].headers.get('Idempotency-Key')).not.toBe(confirmCalls()[0].headers.get('Idempotency-Key'))
  })

  it('Descartar archivo descarta el lote, no crea nada y deja el modal abierto para elegir otro archivo', async () => {
    const user = userEvent.setup()
    const { onClose } = renderModal()
    await previewed(user)
    await user.click(screen.getByRole('button', { name: 'Descartar archivo' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/orders/import/b-1/discard')).toHaveLength(1))
    expect(confirmCalls()).toHaveLength(0)
    expect(onClose).not.toHaveBeenCalled()
    expect(await screen.findByText('Selecciona un archivo CSV para continuar')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Descartar archivo' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Importar' })).toBeDisabled()
  })

  it('Cancelar después de validar descarta el lote y cierra; sin archivo cierra sin descartar', async () => {
    const user = userEvent.setup()
    const { onClose } = renderModal()
    await previewed(user)
    await user.click(screen.getByRole('button', { name: 'Cancelar' }))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(callsTo('POST', '/api/v1/orders/import/b-1/discard')).toHaveLength(1)
    expect(confirmCalls()).toHaveLength(0)
  })

  it('Cancelar sin archivo cierra sin descartar nada', async () => {
    const user = userEvent.setup()
    const { onClose } = renderModal()
    await screen.findByText(/1\. Consignatario/)
    await user.click(screen.getByRole('button', { name: 'Cancelar' }))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(callsTo('POST', '/api/v1/orders/import/b-1/discard')).toHaveLength(0)
  })

  it('con 0 filas válidas no se puede importar', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/orders/import/validate-file' ? { ...PREVIEW, validRows: 0, rows: [PREVIEW.rows[1]], rowCount: 1 } : undefined
    renderModal(ALL, 'c-1')
    await screen.findByText(/1\. Consignatario/)
    await user.upload(fileInput(), csv())
    await screen.findByText(/1 filas detectadas/)
    expect(screen.getByRole('button', { name: 'Importar' })).toBeDisabled()
  })

  it('un 409 del confirm (lote ya confirmado) y un 403 de crédito se muestran y no marcan resultado', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/orders/import/b-1/confirm' ? problem(409, 'El lote ya fue confirmado; valide un archivo nuevo para importar más envíos.', 'conflict') : undefined
    renderModal()
    await previewed(user)
    await user.click(screen.getByRole('button', { name: 'Importar' }))
    expect(await screen.findByText('El lote ya fue confirmado; valide un archivo nuevo para importar más envíos.')).toBeInTheDocument()
    expect(screen.queryByText('Resultado de la importación')).toBeNull()
  })

  it('sin orders.create no se puede importar (la acción queda deshabilitada)', async () => {
    const user = userEvent.setup()
    renderModal(['orders.edit', 'clients.read'])
    await screen.findByText(/1\. Consignatario/)
    await user.selectOptions(screen.getByLabelText('Cliente'), 'ACME · Acme Corp')
    await user.upload(fileInput(), csv())
    await screen.findByText(/3 filas detectadas/)
    expect(screen.getByRole('button', { name: 'Importar' })).toBeDisabled()
    expect(screen.getByRole('switch', { name: 'Aceptar facturas repetidas' })).toBeDisabled()
  })
})

describe('Importar envíos — editor de plantillas de envíos', () => {
  async function openEditor(user: User, edit = false) {
    await screen.findByText(/1\. Consignatario/)
    await user.click(screen.getByRole('button', { name: edit ? 'Editar' : 'Nueva plantilla' }))
    return screen.findByRole('dialog', { name: edit ? 'Editar plantilla de importación de envíos' : 'Nueva plantilla de importación de envíos' })
  }

  it('Nueva plantilla: valida nombre, columnas, consignatario y paquete con los mensajes del servidor', async () => {
    const user = userEvent.setup()
    renderModal()
    const dialog = await openEditor(user)
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('El nombre de la plantilla es obligatorio.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/Nombre de la plantilla/), 'Mi formato')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('Indique al menos una columna.')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), 'Piezas')
    await waitFor(() => expect(within(dialog).queryByText('Indique al menos una columna.')).toBeNull())
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('La plantilla debe incluir consigneeName o consigneeCode.')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Consignatario (nombre)')
    await user.click(within(dialog).getByLabelText('Columna 1'))
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), '— no importar —')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('La plantilla debe indicar el paquete: packageType o pieces, en una columna o en defaults.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/import-templates')).toHaveLength(0)
  })

  it('un campo repetido se señala en su columna', async () => {
    const user = userEvent.setup()
    renderModal()
    const dialog = await openEditor(user)
    await user.type(within(dialog).getByLabelText(/Nombre de la plantilla/), 'Repetida')
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), 'Piezas')
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Piezas')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('El campo pieces está repetido.')).toBeInTheDocument()
  })

  it('el paquete y el servicio pueden venir como valores por defecto; solo se ofrecen los campos que lo admiten; guarda con kind=ORDER', async () => {
    const user = userEvent.setup()
    renderModal()
    const dialog = await openEditor(user)
    await user.type(within(dialog).getByLabelText(/Nombre de la plantilla/), 'Con defaults')
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), 'Consignatario (nombre)')
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Dirección')
    await user.click(within(dialog).getByRole('button', { name: /Agregar valor por defecto/ }))
    const fieldSelect = within(dialog).getByLabelText('Campo del valor por defecto 1')
    // número de orden, factura y nombre del consignatario identifican cada fila: no admiten default
    for (const label of ['N.º de orden', 'N.º de factura', 'Consignatario (nombre)']) expect(within(fieldSelect).queryByRole('option', { name: label })).toBeNull()
    await user.selectOptions(fieldSelect, 'Tipo de paquete')
    await user.type(within(dialog).getByLabelText('Valor por defecto 1'), 'BOX')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/import-templates')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/import-templates')[0].body).toEqual({
      name: 'Con defaults',
      kind: 'ORDER',
      delimiter: ',',
      hasHeader: true,
      columns: [
        { position: 1, field: 'consigneeName' },
        { position: 2, field: 'line1' },
      ],
      defaults: { packageType: 'BOX' },
    })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nueva plantilla de importación de envíos' })).toBeNull())
    expect(await screen.findByText(/1\. Consignatario \(nombre\) · 2\. Dirección/)).toBeInTheDocument()
  })

  it('un error del servidor (nombre repetido, 409) sale en el editor', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/import-templates' ? problem(409, 'Ya existe una plantilla de importación con ese nombre.', 'conflict') : undefined
    renderModal()
    const dialog = await openEditor(user)
    await user.type(within(dialog).getByLabelText(/Nombre de la plantilla/), 'Repetida')
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), 'Consignatario (nombre)')
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Piezas')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('Ya existe una plantilla de importación con ese nombre.')).toBeInTheDocument()
  })

  it('Editar manda PATCH con las columnas completas y limpia los defaults si no quedan', async () => {
    const user = userEvent.setup()
    renderModal()
    const dialog = await openEditor(user, true)
    expect(within(dialog).getByLabelText(/Nombre de la plantilla/)).toHaveValue('Formato estándar')
    expect(within(dialog).getByLabelText('Columna 2')).toHaveValue('line1')
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Pueblo')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/import-templates/t-1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/import-templates/t-1')[0].body).toMatchObject({
      name: 'Formato estándar',
      columns: [
        { position: 1, field: 'consigneeName' },
        { position: 2, field: 'city' },
        { position: 3, field: 'packageType' },
        { position: 4, field: 'pieces' },
      ],
      defaults: null,
      clearDefaults: true,
    })
  })

  it('desactiva la plantilla con confirmación (no hay DELETE) y la reactiva desde «Mostrar plantillas inactivas»', async () => {
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/1\. Consignatario/)
    await user.click(screen.getByRole('button', { name: 'Desactivar esta plantilla' }))
    const confirm = await screen.findByRole('dialog', { name: 'Desactivar la plantilla' })
    await user.click(within(confirm).getByRole('button', { name: 'Desactivar esta plantilla' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/import-templates/t-1/deactivate')).toHaveLength(1))
    expect(mock.calls.some((c) => c.method === 'DELETE')).toBe(false)
    expect(await screen.findByText(/Crea o escoge una plantilla/)).toBeInTheDocument()
    expect(fileInput()).toBeDisabled()
    await user.click(screen.getByRole('switch', { name: 'Mostrar plantillas inactivas' }))
    await user.click(await screen.findByRole('button', { name: 'Reactivar esta plantilla' }))
    const again = await screen.findByRole('dialog', { name: 'Reactivar la plantilla' })
    await user.click(within(again).getByRole('button', { name: 'Reactivar esta plantilla' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/import-templates/t-1/reactivate')).toHaveLength(1))
  })

  it('sin orders.edit no hay plantillas que elegir ni editar: se avisa del permiso y no se piden al servidor', async () => {
    renderModal(['orders.create', 'clients.read'])
    expect(await screen.findByText('Para elegir o crear plantillas de importación necesita el permiso orders.edit.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Nueva plantilla' })).toBeNull()
    expect(fileInput()).toBeDisabled()
    expect(callsTo('GET', '/api/v1/import-templates/fields')).toHaveLength(0)
  })
})
