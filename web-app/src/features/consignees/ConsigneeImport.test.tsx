// Lote F-B — Importación de consignatarios: plantilla primero (sin plantilla el archivo queda deshabilitado; se ofrece crear el
// «Formato estándar»), validar el archivo en el servidor (vista previa con errores y avisos por fila), confirmar con
// Idempotency-Key o descartar el lote, resultado, editor de plantillas (validaciones locales, errores del servidor,
// valores por defecto) y desactivar/reactivar. Sobre un fetch simulado; el FormData se sustituye por un registro.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterAll, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { ConsigneeImportModal } from './ConsigneeImportModal'
import ConsigneeListScreen from './ConsigneeListScreen'

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

// jsdom + el Request de Node no se entienden con un FormData con archivos: aquí es un registro de los campos
class RecordingFormData {
  append(field: string, value: unknown) {
    mock.uploads.push({ field, value })
  }
}

const problem = (status: number, title: string, code: string, errors?: Record<string, string[]>) =>
  new Response(JSON.stringify({ title, code, status, errors }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const FIELDS = [
  { field: 'code', required: false, defaultAllowed: false },
  { field: 'name', required: true, defaultAllowed: false },
  { field: 'line1', required: true, defaultAllowed: false },
  { field: 'city', required: true, defaultAllowed: true },
  { field: 'country', required: false, defaultAllowed: true },
  { field: 'defaultServiceMinutes', required: false, defaultAllowed: true },
  { field: 'contactPhone', required: false, defaultAllowed: true },
]
const T1 = {
  id: 1,
  publicId: 't-1',
  kind: 'CONSIGNEE',
  name: 'Formato estándar',
  clientPublicId: null,
  delimiter: ',',
  hasHeader: true,
  columns: [
    { position: 1, field: 'name' },
    { position: 2, field: 'code' },
    { position: 3, field: 'line1' },
    { position: 4, field: 'city' },
  ],
  defaults: {},
  isActive: true,
}
const PREVIEW = {
  batchPublicId: 'b-1',
  status: 'VALIDATED',
  templatePublicId: 't-1',
  templateName: 'Formato estándar',
  fileName: 'consignatarios.csv',
  rowCount: 3,
  validRows: 2,
  rows: [
    { row: 1, line: 2, isValid: true, values: { name: 'Farmacia Uno', code: 'F1', line1: 'Calle 1', city: 'Ponce' }, warnings: [], errors: {} },
    { row: 2, line: 3, isValid: false, values: { name: '', line1: 'Calle 2', city: 'Ponce' }, errors: { name: 'El nombre es obligatorio.' }, warnings: [] },
    { row: 3, line: 4, isValid: true, values: { name: 'Colmado Dos', line1: 'Calle 3', city: 'Arecibo' }, errors: {}, warnings: ["El nombre 'Colmado Dos' ya lo usa otro consignatario de este dueño; se creará como sucursal distinta."] },
  ],
}
const RESULT = {
  batchPublicId: 'b-1',
  status: 'CONFIRMED',
  created: 1,
  failed: 1,
  skipped: 1,
  rows: [
    { row: 1, line: 2, isValid: true, locationPublicId: 'l-9' },
    { row: 2, line: 3, isValid: false, skipped: true },
    { row: 3, line: 4, isValid: true, error: 'Ya existe un consignatario con el código \'F3\' (\'Otro\').' },
  ],
}

let templates: Record<string, unknown>[] = []

function route({ method, url, body }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/locations/import-templates' && method === 'GET') {
    const inactive = url.searchParams.get('includeInactive') === 'true'
    return templates.filter((x) => inactive || x.isActive !== false)
  }
  if (p === '/api/v1/locations/import-templates' && method === 'POST') {
    const created = { ...T1, id: 9, publicId: 't-new', name: body?.name, columns: body?.columns, defaults: body?.defaults ?? {} }
    templates = [...templates, created]
    return created
  }
  if (p.startsWith('/api/v1/locations/import-templates/') && method === 'PATCH') return { ...T1, ...body }
  if (p.endsWith('/deactivate') || p.endsWith('/reactivate')) {
    const id = p.split('/').at(-2)
    const active = p.endsWith('/reactivate')
    templates = templates.map((x) => (x.publicId === id ? { ...x, isActive: active } : x))
    return templates.find((x) => x.publicId === id)
  }
  if (p === '/api/v1/locations/import/fields') return FIELDS
  if (p === '/api/v1/locations/import/validate-file') return PREVIEW
  if (p === '/api/v1/locations/import/b-1/confirm') return RESULT
  if (p === '/api/v1/locations/import/b-1/discard') return { ...PREVIEW, status: 'DISCARDED' }
  if (p === '/api/v1/clients')
    return [{ id: 1, publicId: 'c-1', code: 'ACME', name: 'Acme Corp', isActive: true }]
  if (p === '/api/v1/locations') return []
  if (p.startsWith('/api/v1/')) return []
  return problem(404, 'No encontrado', 'not_found')
}

function renderModal(permissions = ['locations.read', 'locations.create', 'clients.read'], defaultClient = '') {
  const onClose = vi.fn()
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <MemoryRouter>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={['CATALOG']}>
          <ConsigneeImportModal defaultClient={defaultClient} onClose={onClose} />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
  return { onClose }
}

const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)
const csv = (name = 'consignatarios.csv') => new File(['name,code,line1,city\n'], name, { type: 'text/csv' })
const fileInput = () => screen.getByLabelText('Archivo CSV') as HTMLInputElement

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

describe('Importar consignatarios — plantilla y archivo', () => {
  it('sin plantilla el selector de archivo queda deshabilitado y se ofrece crear el «Formato estándar»', async () => {
    templates = []
    const user = userEvent.setup()
    renderModal()
    expect(await screen.findByText(/Crea o escoge una plantilla/)).toBeInTheDocument()
    expect(fileInput()).toBeDisabled()
    await user.click(screen.getByRole('button', { name: /Crear «Formato estándar»/ }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import-templates')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/locations/import-templates')[0].body).toEqual({
      name: 'Formato estándar',
      delimiter: ',',
      hasHeader: true,
      columns: [
        { position: 1, field: 'name' },
        { position: 2, field: 'code' },
        { position: 3, field: 'line1' },
        { position: 4, field: 'city' },
        { position: 5, field: 'contactPhone' },
      ],
    })
    await waitFor(() => expect(fileInput()).toBeEnabled())
  })

  it('con plantilla muestra el mapeo, el cliente dueño, el límite de 5.000 filas / 2 MB y la nota de UTF-8', async () => {
    renderModal()
    expect(await screen.findByText(/1\. Nombre · 2\. Número · 3\. Dirección · 4\. Pueblo/)).toBeInTheDocument()
    expect(screen.getByLabelText('Cliente dueño de los consignatarios')).toHaveValue('')
    expect(screen.getByText('Máximo 5,000 filas y 2 MB por archivo.')).toBeInTheDocument()
    expect(screen.getByText(/Guarda el CSV en UTF-8/)).toBeInTheDocument()
    expect(fileInput()).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Importar' })).toBeDisabled()
  })

  it('valida el archivo en el servidor y muestra por fila los errores y avisos con sus mensajes', async () => {
    const user = userEvent.setup()
    renderModal(undefined, 'c-1')
    await screen.findByText(/1\. Nombre/)
    await user.upload(fileInput(), csv())
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import/validate-file')).toHaveLength(1))
    expect(mock.uploads.map((u) => u.field)).toEqual(['templatePublicId', 'clientPublicId', 'file'])
    expect(mock.uploads[0].value).toBe('t-1')
    expect(mock.uploads[1].value).toBe('c-1')
    expect(await screen.findByText(/consignatarios\.csv · 3 filas detectadas/)).toBeInTheDocument()
    const table = screen.getByRole('table')
    expect(within(table).getByText('Nombre: El nombre es obligatorio.')).toBeInTheDocument()
    expect(within(table).getByText(/se creará como sucursal distinta/)).toBeInTheDocument()
    expect(within(table).getByText('Farmacia Uno')).toBeInTheDocument()
    // válidas / con errores
    expect(screen.getByText('válidas').closest('.cs-stat')).toHaveTextContent('2 válidas')
    expect(screen.getByText('con errores').closest('.cs-stat')).toHaveTextContent('1 con errores')
    // «solo con problemas» oculta la fila limpia
    await user.click(screen.getByRole('switch', { name: 'Solo filas con errores o avisos' }))
    expect(within(screen.getByRole('table')).queryByText('Farmacia Uno')).toBeNull()
    expect(screen.getByRole('button', { name: 'Importar' })).toBeEnabled()
  })

  it('cambiar el cliente dueño con un archivo elegido valida otra vez con ese cliente', async () => {
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.upload(fileInput(), csv())
    await screen.findByText(/3 filas detectadas/)
    await user.selectOptions(screen.getByLabelText('Cliente dueño de los consignatarios'), 'Acme Corp')
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import/validate-file')).toHaveLength(2))
    // el lote anterior se descartó
    expect(callsTo('POST', '/api/v1/locations/import/b-1/discard').length).toBeGreaterThan(0)
    expect(mock.uploads.filter((u) => u.field === 'clientPublicId').map((u) => u.value)).toEqual(['c-1'])
  })

  it('un archivo de más de 2 MB se rechaza antes de llamar al servidor', async () => {
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/1\. Nombre/)
    const big = csv('grande.csv')
    Object.defineProperty(big, 'size', { value: 3 * 1024 * 1024 })
    await user.upload(fileInput(), big)
    expect(await screen.findByText('El archivo supera el límite de 5.000 filas o 2 MB.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/locations/import/validate-file')).toHaveLength(0)
  })

  it('un 400 del servidor se muestra con su título y sus errores', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/locations/import/validate-file'
        ? problem(400, 'Solicitud inválida', 'validation', { content: ['El archivo no tiene filas de datos.'] })
        : undefined
    renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.upload(fileInput(), csv())
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Solicitud inválida')
    expect(alert).toHaveTextContent('El archivo no tiene filas de datos.')
    expect(screen.getByRole('button', { name: 'Importar' })).toBeDisabled()
  })
})

describe('Importar consignatarios — confirmar, descartar y resultado', () => {
  it('Importar confirma las válidas con Idempotency-Key y muestra creados, fallidos y omitidos', async () => {
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.upload(fileInput(), csv())
    await screen.findByText(/3 filas detectadas/)
    await user.click(screen.getByRole('button', { name: 'Importar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import/b-1/confirm')).toHaveLength(1))
    const call = callsTo('POST', '/api/v1/locations/import/b-1/confirm')[0]
    expect(call.headers.get('Idempotency-Key')).toMatch(/^[0-9a-f-]{36}$/)
    expect(call.body).toEqual({ rows: null })
    const result = (await screen.findByText('Resultado de la importación')).closest('.cs-result') as HTMLElement
    expect(result).toHaveTextContent('1 creados')
    expect(result).toHaveTextContent('1 fallidos')
    expect(result).toHaveTextContent('1 omitidos')
    // la fila fallida explica por qué
    expect(within(result).getByText(/Ya existe un consignatario con el código 'F3'/)).toBeInTheDocument()
    // el ✕ del modal y el botón del pie
    expect(within(screen.getByRole('dialog', { name: 'Importar consignatarios desde CSV' })).getAllByRole('button', { name: 'Cerrar' })).toHaveLength(2)
  })

  it('Cancelar después de validar descarta el lote y cierra', async () => {
    const user = userEvent.setup()
    const { onClose } = renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.upload(fileInput(), csv())
    await screen.findByText(/3 filas detectadas/)
    await user.click(screen.getByRole('button', { name: 'Cancelar' }))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(callsTo('POST', '/api/v1/locations/import/b-1/discard')).toHaveLength(1)
    expect(callsTo('POST', '/api/v1/locations/import/b-1/confirm')).toHaveLength(0)
  })

  it('Cancelar sin archivo cierra sin descartar nada', async () => {
    const user = userEvent.setup()
    const { onClose } = renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.click(screen.getByRole('button', { name: 'Cancelar' }))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(callsTo('POST', '/api/v1/locations/import/b-1/discard')).toHaveLength(0)
  })

  it('con 0 filas válidas no se puede importar', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/locations/import/validate-file' ? { ...PREVIEW, validRows: 0, rows: [PREVIEW.rows[1]], rowCount: 1 } : undefined
    renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.upload(fileInput(), csv())
    await screen.findByText(/1 filas detectadas/)
    expect(screen.getByRole('button', { name: 'Importar' })).toBeDisabled()
  })

  it('un 409 del confirm (lote ya confirmado) se muestra y no marca resultado', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/locations/import/b-1/confirm' ? problem(409, 'El lote ya fue confirmado.', 'conflict') : undefined
    renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.upload(fileInput(), csv())
    await screen.findByText(/3 filas detectadas/)
    await user.click(screen.getByRole('button', { name: 'Importar' }))
    expect(await screen.findByText('El lote ya fue confirmado.')).toBeInTheDocument()
    expect(screen.queryByText('Resultado de la importación')).toBeNull()
  })
})

describe('Importar consignatarios — editor de plantillas', () => {
  async function openEditor(user: ReturnType<typeof userEvent.setup>, edit = false) {
    await screen.findByText(/1\. Nombre/)
    await user.click(screen.getByRole('button', { name: edit ? 'Editar' : 'Nueva plantilla' }))
    return screen.findByRole('dialog', { name: edit ? 'Editar plantilla de importación' : 'Nueva plantilla de importación' })
  }

  it('Nueva plantilla: valida en pantalla que traiga nombre, name y line1, y la ciudad', async () => {
    const user = userEvent.setup()
    renderModal()
    const dialog = await openEditor(user)
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('El nombre de la plantilla es obligatorio.')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/Nombre de la plantilla/), 'Mi formato')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('Indique al menos una columna.')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), 'Número')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('La plantilla debe incluir las columnas name y line1.')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Nombre *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 3'), 'Dirección *')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('La plantilla debe indicar la ciudad (city) en una columna o en defaults.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/locations/import-templates')).toHaveLength(0)
  })

  it('la ciudad puede venir como valor por defecto (solo se ofrecen los campos que lo admiten); guarda columnas por posición', async () => {
    const user = userEvent.setup()
    renderModal()
    const dialog = await openEditor(user)
    await user.type(within(dialog).getByLabelText(/Nombre de la plantilla/), 'Con default')
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), 'Nombre *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Dirección *')
    await user.click(within(dialog).getByRole('button', { name: /Agregar valor por defecto/ }))
    const fieldSelect = within(dialog).getByLabelText('Campo del valor por defecto 1')
    // nombre y dirección identifican al consignatario: no admiten default
    expect(within(fieldSelect).queryByRole('option', { name: 'Nombre' })).toBeNull()
    await user.selectOptions(fieldSelect, 'Pueblo')
    await user.type(within(dialog).getByLabelText('Valor por defecto 1'), 'Ponce')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import-templates')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/locations/import-templates')[0].body).toEqual({
      name: 'Con default',
      delimiter: ',',
      hasHeader: true,
      columns: [
        { position: 1, field: 'name' },
        { position: 2, field: 'line1' },
      ],
      defaults: { city: 'Ponce' },
    })
    // la plantilla nueva queda elegida
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Nueva plantilla de importación' })).toBeNull())
    expect(await screen.findByText(/1\. Nombre · 2\. Dirección/)).toBeInTheDocument()
  })

  it('agrega y quita columnas; un error del servidor sale en el editor', async () => {
    const user = userEvent.setup()
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/locations/import-templates' ? problem(409, 'Ya existe una plantilla de importación con ese nombre.', 'conflict') : undefined
    renderModal()
    const dialog = await openEditor(user)
    expect(within(dialog).getAllByLabelText(/^Columna \d$/)).toHaveLength(5)
    await user.click(within(dialog).getByRole('button', { name: 'Agregar columna' }))
    expect(within(dialog).getAllByLabelText(/^Columna \d$/)).toHaveLength(6)
    await user.click(within(dialog).getByRole('button', { name: 'Quitar la columna 6' }))
    expect(within(dialog).getAllByLabelText(/^Columna \d$/)).toHaveLength(5)
    await user.type(within(dialog).getByLabelText(/Nombre de la plantilla/), 'Repetida')
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), 'Nombre *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Dirección *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 3'), 'Pueblo *')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('Ya existe una plantilla de importación con ese nombre.')).toBeInTheDocument()
  })

  it('Editar manda PATCH con las columnas completas y limpia los defaults si no quedan', async () => {
    const user = userEvent.setup()
    renderModal()
    const dialog = await openEditor(user, true)
    expect(within(dialog).getByLabelText(/Nombre de la plantilla/)).toHaveValue('Formato estándar')
    expect(within(dialog).getByLabelText('Columna 2')).toHaveValue('code')
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Teléfono')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/locations/import-templates/t-1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/locations/import-templates/t-1')[0].body).toMatchObject({
      name: 'Formato estándar',
      columns: [
        { position: 1, field: 'name' },
        { position: 2, field: 'contactPhone' },
        { position: 3, field: 'line1' },
        { position: 4, field: 'city' },
      ],
      defaults: null,
      clearDefaults: true,
    })
  })

  it('desactiva la plantilla con confirmación (no hay DELETE) y la reactiva desde «Mostrar plantillas inactivas»', async () => {
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.click(screen.getByRole('button', { name: 'Desactivar esta plantilla' }))
    const confirm = await screen.findByRole('dialog', { name: 'Desactivar la plantilla' })
    await user.click(within(confirm).getByRole('button', { name: 'Desactivar esta plantilla' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import-templates/t-1/deactivate')).toHaveLength(1))
    expect(mock.calls.some((c) => c.method === 'DELETE')).toBe(false)
    expect(await screen.findByText(/Crea o escoge una plantilla/)).toBeInTheDocument()
    expect(fileInput()).toBeDisabled()

    await user.click(screen.getByRole('switch', { name: 'Mostrar plantillas inactivas' }))
    await user.click(await screen.findByRole('button', { name: 'Reactivar esta plantilla' }))
    const again = await screen.findByRole('dialog', { name: 'Reactivar la plantilla' })
    await user.click(within(again).getByRole('button', { name: 'Reactivar esta plantilla' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import-templates/t-1/reactivate')).toHaveLength(1))
  })
})

describe('Importar consignatarios — desde la pantalla', () => {
  function wrapScreen(permissions: string[]) {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    return render(
      <MemoryRouter initialEntries={['/catalog/consignees']}>
        <QueryClientProvider client={queryClient}>
          <AccessProvider permissions={permissions} modules={['CATALOG']}>
            <ConsigneeListScreen />
          </AccessProvider>
        </QueryClientProvider>
      </MemoryRouter>,
    )
  }

  it('el botón Importar abre el modal solo con locations.create', async () => {
    const user = userEvent.setup()
    const view = wrapScreen(['locations.read', 'locations.create'])
    await user.click(await screen.findByRole('button', { name: /Importar/ }))
    expect(await screen.findByRole('dialog', { name: 'Importar consignatarios desde CSV' })).toBeInTheDocument()
    view.unmount()
    wrapScreen(['locations.read'])
    await screen.findByRole('heading', { level: 1, name: 'Consignatarios' })
    expect(screen.queryByRole('button', { name: /Importar/ })).toBeNull()
  })
})
