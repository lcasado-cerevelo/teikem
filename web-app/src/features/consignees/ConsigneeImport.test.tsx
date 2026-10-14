// Lote F-B — Importación de consignatarios: plantilla primero (sin plantilla el archivo queda deshabilitado; se ofrece crear el
// «Formato estándar»), validar el archivo en el servidor (vista previa con errores y avisos por fila), confirmar con
// Idempotency-Key o descartar el lote, resultado, editor de plantillas (validaciones locales, errores del servidor,
// valores por defecto) y desactivar/reactivar. Sobre un fetch simulado; el FormData se sustituye por un registro.
// Lote F-B2: el archivo se revisa (`inspect-file`) antes de validar, Excel con varias hojas (selector de hoja), cliente por defecto de la
// plantilla y «Recordar este cliente en la plantilla», columna Cliente en la vista previa y el editor nuevo («(No usar)», encabezados y
// muestras del archivo, una columna a dos campos, columnas unidas con separador y orden, cliente por defecto).
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
  { field: 'postalCode', required: false, defaultAllowed: true },
  { field: 'country', required: false, defaultAllowed: true },
  { field: 'defaultServiceMinutes', required: false, defaultAllowed: true },
  { field: 'contactPhone', required: false, defaultAllowed: true },
  { field: 'contactName', required: false, defaultAllowed: true },
]
const INSPECTION = {
  format: 'CSV',
  sheets: [] as string[],
  sheet: null as string | null,
  rowCount: 3,
  columns: [
    { position: 1, header: 'Nombre', samples: ['Farmacia Uno', 'Colmado Dos'] },
    { position: 2, header: 'Código', samples: ['F1'] },
    { position: 3, header: 'Calle', samples: ['Calle 1', 'Calle 2'] },
    { position: 4, header: 'Pueblo', samples: ['Ponce'] },
    { position: 5, header: 'Notas', samples: ['x'] },
  ],
  sampleRows: [['Farmacia Uno', 'F1', 'Calle 1', 'Ponce', 'x']],
}
let inspection: typeof INSPECTION = { ...INSPECTION }
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
    { row: 1, line: 2, isValid: true, values: { name: 'Farmacia Uno', code: 'F1', line1: 'Calle 1', city: 'Ponce' }, owner: { clientPublicId: 'c-1', clientName: 'Acme Corp', isShared: false }, warnings: [], errors: {} },
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
    const created = { ...T1, id: 9, publicId: 't-new', name: body?.name, columns: body?.columns, defaults: body?.defaults ?? {}, variants: body?.variants ?? null }
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
  if (p === '/api/v1/locations/import/inspect-file') return inspection
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
const fileInput = () => screen.getByLabelText('Archivo (CSV o Excel)') as HTMLInputElement

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
  inspection = { ...INSPECTION }
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
    expect(screen.getByLabelText('Cliente por defecto')).toHaveValue('')
    expect(screen.getByText('Máximo 5,000 filas y 2 MB por archivo.')).toBeInTheDocument()
    expect(screen.getByText(/la codificación se detecta sola/)).toBeInTheDocument()
    expect(fileInput()).toHaveAttribute('accept', expect.stringContaining('.xlsx'))
    expect(fileInput()).toBeEnabled()
    expect(screen.getByRole('button', { name: 'Importar' })).toBeDisabled()
  })

  it('valida el archivo en el servidor y muestra por fila los errores y avisos con sus mensajes', async () => {
    const user = userEvent.setup()
    renderModal(undefined, 'c-1')
    await screen.findByText(/1\. Nombre/)
    await user.upload(fileInput(), csv())
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import/validate-file')).toHaveLength(1))
    // primero se revisa el archivo (hojas, encabezados) y después se valida con la plantilla y el cliente por defecto
    expect(mock.uploads.map((u) => u.field)).toEqual(['file', 'delimiter', 'hasHeader', 'templatePublicId', 'clientPublicId', 'file'])
    expect(mock.uploads.slice(1, 3).map((u) => u.value)).toEqual([',', 'true'])
    expect(mock.uploads[3].value).toBe('t-1')
    expect(mock.uploads[4].value).toBe('c-1')
    expect(await screen.findByText(/consignatarios\.csv · 3 filas detectadas/)).toBeInTheDocument()
    const table = screen.getByRole('table')
    expect(within(table).getByText('Nombre: El nombre es obligatorio.')).toBeInTheDocument()
    expect(within(table).getByText(/se creará como sucursal distinta/)).toBeInTheDocument()
    expect(within(table).getByText('Farmacia Uno')).toBeInTheDocument()
    // la columna Cliente dice de quién queda cada fila (la fila con su propio cliente manda)
    expect(within(table).getByRole('columnheader', { name: 'Cliente' })).toBeInTheDocument()
    expect(within(table).getByText('Acme Corp')).toBeInTheDocument()
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
    await user.selectOptions(screen.getByLabelText('Cliente por defecto'), 'Acme Corp')
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
    expect(within(screen.getByRole('dialog', { name: 'Importar consignatarios (CSV o Excel)' })).getAllByRole('button', { name: 'Cerrar' })).toHaveLength(2)
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
    // el aviso ya corregido desaparece al cambiar la columna (sin volver a guardar)
    await waitFor(() => expect(within(dialog).queryByText('Indique al menos una columna.')).toBeNull())
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('La plantilla debe incluir las columnas name y line1.')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Nombre *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 3'), 'Dirección *')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('La plantilla debe indicar la ciudad (city) o el código postal (postalCode), en una columna o en defaults.')).toBeInTheDocument()
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
      // las filas del editor que quedan en «(No usar)» se guardan (la plantilla recuerda todas las columnas)
      columns: [
        { position: 1, field: 'name' },
        { position: 2, field: 'line1' },
        { position: 3, field: 'ignore' },
        { position: 4, field: 'ignore' },
        { position: 5, field: 'ignore' },
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

describe('Importar consignatarios — Excel, cliente por defecto y mapeo (Lote F-B2)', () => {
  const xlsx = () => new File(['PK'], 'consignatarios.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' })
  const uploadsOf = (field: string) => mock.uploads.filter((u) => u.field === field).map((u) => u.value)

  it('un libro con varias hojas ofrece escoger la hoja; cambiarla revisa y valida otra vez con esa hoja', async () => {
    inspection = { ...INSPECTION, format: 'XLSX', sheets: ['Portada', 'Datos'], sheet: 'Portada' }
    mock.override = ({ method, url }) => {
      if (method === 'POST' && url.pathname === '/api/v1/locations/import/inspect-file') {
        const wanted = mock.uploads.filter((u) => u.field === 'sheet').at(-1)?.value as string | undefined
        return { ...inspection, sheet: wanted ?? 'Portada' }
      }
      return undefined
    }
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.upload(fileInput(), xlsx())
    expect(await screen.findByText(/consignatarios\.xlsx · Portada · 3 filas detectadas/)).toBeInTheDocument()
    const sheet = screen.getByLabelText('Hoja del libro')
    expect(sheet).toHaveValue('Portada')
    expect(screen.getByText('El libro tiene 2 hojas; se importa solo la escogida.')).toBeInTheDocument()
    expect(uploadsOf('sheet')).toEqual(['Portada']) // la validación usa la hoja que leyó la revisión
    await user.selectOptions(sheet, 'Datos')
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import/validate-file')).toHaveLength(2))
    expect(uploadsOf('sheet')).toEqual(['Portada', 'Datos', 'Datos'])
    expect(callsTo('POST', '/api/v1/locations/import/b-1/discard').length).toBeGreaterThan(0)
  })

  it('un CSV no muestra selector de hoja', async () => {
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.upload(fileInput(), csv())
    await screen.findByText(/3 filas detectadas/)
    expect(screen.queryByLabelText('Hoja del libro')).toBeNull()
  })

  it('la plantilla trae su cliente por defecto; escoger otro ofrece recordarlo y la confirmación lo pide', async () => {
    templates = [{ ...T1, defaultClientPublicId: 'c-1', defaultClientName: 'Acme Corp' }]
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/1\. Nombre/)
    await waitFor(() => expect(screen.getByLabelText('Cliente por defecto')).toHaveValue('c-1'))
    // es el de la plantilla: no hay nada que recordar
    expect(screen.queryByRole('switch', { name: 'Recordar este cliente en la plantilla' })).toBeNull()
    await user.selectOptions(screen.getByLabelText('Cliente por defecto'), 'Compartido (todos los clientes)')
    const remember = await screen.findByRole('switch', { name: 'Recordar este cliente en la plantilla' })
    expect(remember).toBeChecked()
    await user.upload(fileInput(), csv())
    await screen.findByText(/3 filas detectadas/)
    expect(uploadsOf('clientPublicId')).toEqual([]) // compartidos: sin cliente por defecto
    await user.click(screen.getByRole('button', { name: 'Importar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import/b-1/confirm')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/locations/import/b-1/confirm')[0].body).toEqual({ rows: null, rememberClient: true })
  })

  it('sin «Recordar» la confirmación no toca la plantilla', async () => {
    const user = userEvent.setup()
    renderModal(undefined, 'c-1')
    await screen.findByText(/1\. Nombre/)
    await user.click(await screen.findByRole('switch', { name: 'Recordar este cliente en la plantilla' }))
    await user.upload(fileInput(), csv())
    await screen.findByText(/3 filas detectadas/)
    await user.click(screen.getByRole('button', { name: 'Importar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import/b-1/confirm')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/locations/import/b-1/confirm')[0].body).toEqual({ rows: null })
  })

  it('el editor muestra el encabezado y las muestras del archivo, «(No usar)», una columna a dos campos y columnas unidas', async () => {
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.upload(fileInput(), csv())
    await screen.findByText(/3 filas detectadas/)
    await user.click(screen.getByRole('button', { name: 'Nueva plantilla' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva plantilla de importación' })
    // encabezado y muestras de cada columna del archivo revisado
    expect(within(dialog).getByRole('columnheader', { name: 'En el archivo' })).toBeInTheDocument()
    expect(within(dialog).getByText('Código')).toBeInTheDocument()
    expect(within(dialog).getByText('Farmacia Uno · Colmado Dos')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Columna 1')).toHaveValue('')
    expect(within(within(dialog).getByLabelText('Columna 1')).getByRole('option', { name: '(No usar)' })).toBeInTheDocument()

    await user.type(within(dialog).getByLabelText(/Nombre de la plantilla/), 'Unidas')
    // Nombre = Código + « - » + Nombre; la columna del código también va sola al Número
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Número')
    await user.click(within(dialog).getAllByRole('button', { name: /Usar también en otro campo/ })[0])
    await user.selectOptions(within(dialog).getByLabelText('Columna 2, campo 2'), 'Nombre *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), 'Nombre *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 3'), 'Dirección *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 4'), 'Pueblo *')
    // la unión aparece con la vista previa de la primera fila del archivo; se pone el código primero y « - » de separador
    const joins = within(dialog).getByRole('region', { name: 'Campos con varias columnas' })
    expect(within(joins).getByText('Farmacia Uno F1')).toBeInTheDocument()
    await user.click(within(joins).getByRole('button', { name: 'Subir Columna 2 «Código» en Nombre' }))
    const sep = within(joins).getByLabelText('Separador 1 de Nombre')
    await user.clear(sep)
    await user.type(sep, ' - ')
    expect(within(joins).getByText('F1 - Farmacia Uno')).toBeInTheDocument()
    // cliente por defecto
    await user.selectOptions(within(dialog).getByLabelText('Cliente por defecto'), 'Acme Corp')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import-templates')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/locations/import-templates')[0].body).toEqual({
      name: 'Unidas',
      delimiter: ',',
      hasHeader: true,
      columns: [
        { position: 1, field: 'name', part: 2, separator: ' - ' },
        { position: 2, field: 'code' },
        { position: 2, field: 'name', part: 1 },
        { position: 3, field: 'line1' },
        { position: 4, field: 'city' },
        { position: 5, field: 'ignore' },
      ],
      defaults: null,
      defaultClientPublicId: 'c-1',
    })
  })

  it('«(No usar)» en el primer campo quita los demás de esa columna; un campo con formato no se une', async () => {
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.click(screen.getByRole('button', { name: 'Nueva plantilla' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva plantilla de importación' })
    expect(within(dialog).queryByRole('columnheader', { name: 'En el archivo' })).toBeNull() // sin archivo no hay muestras
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), 'Número')
    await user.click(within(dialog).getAllByRole('button', { name: /Usar también en otro campo/ })[0])
    expect(within(dialog).getByLabelText('Columna 1, campo 2')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), '(No usar)')
    await waitFor(() => expect(within(dialog).queryByLabelText('Columna 1, campo 2')).toBeNull())

    await user.type(within(dialog).getByLabelText(/Nombre de la plantilla/), 'País doble')
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), 'Nombre *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Dirección *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 3'), 'Pueblo *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 4'), 'País')
    await user.selectOptions(within(dialog).getByLabelText('Columna 5'), 'País')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('El campo country está repetido.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/locations/import-templates')).toHaveLength(0)
  })

  it('editar una plantilla con columnas unidas las muestra con su separador y un PATCH sin cliente lo quita', async () => {
    templates = [
      {
        ...T1,
        defaultClientPublicId: 'c-1',
        defaultClientName: 'Acme Corp',
        columns: [
          { position: 1, field: 'code' },
          { position: 1, field: 'name', part: 1 },
          { position: 2, field: 'name', part: 2, separator: ' / ' },
          { position: 3, field: 'ignore' },
          { position: 4, field: 'line1' },
          { position: 5, field: 'city' },
        ],
      },
    ]
    const user = userEvent.setup()
    renderModal()
    expect(await screen.findByText(/1\. Número · 1\+2\. Nombre · 4\. Dirección · 5\. Pueblo · 3\. \(No usar\)/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar plantilla de importación' })
    expect(within(dialog).getByLabelText('Columna 1')).toHaveValue('code')
    expect(within(dialog).getByLabelText('Columna 1, campo 2')).toHaveValue('name')
    expect(within(dialog).getByLabelText('Columna 3')).toHaveValue('')
    expect(within(dialog).getByLabelText('Separador 1 de Nombre')).toHaveValue(' / ')
    expect(within(dialog).getByText('[Columna 1] / [Columna 2]')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Cliente por defecto')).toHaveValue('c-1')
    await user.selectOptions(within(dialog).getByLabelText('Cliente por defecto'), 'Sin cliente por defecto (compartidos)')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/locations/import-templates/t-1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/locations/import-templates/t-1')[0].body).toMatchObject({
      columns: [
        { position: 1, field: 'code' },
        { position: 1, field: 'name', part: 1 },
        { position: 2, field: 'name', part: 2, separator: ' / ' },
        { position: 3, field: 'ignore' },
        { position: 4, field: 'line1' },
        { position: 5, field: 'city' },
      ],
      clearDefaultClient: true,
    })
  })
})

// Lote F-B3: variantes de mapeo con condición por fila y lo que la vista previa dice del catálogo de códigos postales (datos sintéticos).
const MIXED = {
  ...T1,
  publicId: 't-mix',
  name: 'Mixta',
  columns: [
    { position: 1, field: 'code' },
    { position: 2, field: 'name' },
    { position: 6, field: 'line1' },
    { position: 8, field: 'city' },
    { position: 9, field: 'ignore' },
  ],
  variants: [
    {
      name: 'Formato B',
      conditions: [{ position: 9, operator: 'EQUALS', value: 'PUERTO RICO' }],
      columns: [
        { position: 1, field: 'code' },
        { position: 2, field: 'name' },
        { position: 6, field: 'line1' },
        { position: 8, field: 'city' },
        { position: 9, field: 'ignore' },
      ],
    },
    {
      name: null,
      conditions: [],
      columns: [
        { position: 1, field: 'code' },
        { position: 2, field: 'name' },
        { position: 3, field: 'line1' },
        { position: 5, field: 'postalCode' },
      ],
    },
  ],
}
const MIXED_PREVIEW = {
  ...PREVIEW,
  templatePublicId: 't-mix',
  templateName: 'Mixta',
  rowCount: 3,
  validRows: 2,
  rows: [
    {
      row: 1, line: 2, isValid: true, variant: 2, variantName: 'Variante 2', inferred: ['city', 'state'],
      values: { code: 'A-1', name: 'Colmado Uno', line1: 'Calle 1', postalCode: '00716', city: 'Ponce', state: 'PR' },
      errors: {}, warnings: ['Pueblo tomado del código postal 00716: Ponce.'],
    },
    {
      row: 2, line: 3, isValid: true, variant: 2, variantName: 'Variante 2', inferred: ['postalCode', 'city', 'state'], originalPostalCode: '00734',
      values: { code: 'A-2', name: 'Ferretería Tres', line1: 'Calle 3', postalCode: '00716', city: 'Ponce', state: 'PR' },
      errors: {}, warnings: ['Código postal ajustado: 00734 es de apartados postales (P.O. Box); se usó 00716, de entrega en Ponce.'],
    },
    {
      row: 3, line: 4, isValid: false, cells: ['X-1', 'Fila rara', '', 'USA'],
      values: {}, errors: { variant: 'Ninguna variante de la plantilla aplica a esta fila; revise las condiciones de las variantes.' }, warnings: [],
    },
  ],
}

describe('Importar consignatarios — variantes y lista de códigos postales (Lote F-B3)', () => {
  it('Nueva plantilla con dos variantes: condición, mapeo propio, la variante sin condición va al final y viaja como variants', async () => {
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.click(screen.getByRole('button', { name: 'Nueva plantilla' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva plantilla de importación' })
    expect(within(dialog).queryByRole('tablist')).toBeNull() // una sola variante: el editor de siempre
    await user.type(within(dialog).getByLabelText(/Nombre de la plantilla/), 'Mixta')
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), 'Número')
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Nombre *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 3'), 'Dirección *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 4'), 'Pueblo *')

    await user.click(within(dialog).getByRole('button', { name: /Agregar variante/ }))
    const tabs = within(dialog).getByRole('tablist', { name: 'Variantes de mapeo' })
    expect(within(tabs).getByRole('tab', { name: 'Variante 2' })).toHaveAttribute('aria-selected', 'true')
    // la variante nueva copia el mapeo de la actual y trae una condición por llenar
    expect(within(dialog).getByLabelText('Columna 4')).toHaveValue('city')
    await user.type(within(dialog).getByLabelText('Nombre de la variante 2'), 'Formato B')
    await user.selectOptions(within(dialog).getByLabelText('Columna de la condición 1'), 'Columna 5')
    expect(within(dialog).getByLabelText('Condición 1')).toHaveValue('EQUALS')
    await user.type(within(dialog).getByLabelText('Valor de la condición 1'), 'PUERTO RICO')
    await user.selectOptions(within(dialog).getByLabelText('Columna 3'), 'Pueblo *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 4'), 'Dirección *')

    // la 1 no tiene condición y no es la última: no se guarda y su pestaña lo dice
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    await waitFor(() => expect(within(tabs).getAllByRole('tab')[0]).toHaveTextContent('Variante 1 · con errores'))
    expect(callsTo('POST', '/api/v1/locations/import-templates')).toHaveLength(0)
    await user.click(within(dialog).getByRole('button', { name: 'Mover la variante 2 antes' }))
    expect(within(tabs).getByRole('tab', { name: 'Variante 1: Formato B' })).toHaveAttribute('aria-selected', 'true')
    expect(within(dialog).getByLabelText('Columna 3')).toHaveValue('city')
    await user.click(within(tabs).getByRole('tab', { name: /Variante 2/ }))
    expect(within(dialog).getByText('Sin condición: aplica a todas las filas')).toBeInTheDocument()
    expect(within(dialog).getByLabelText('Columna 3')).toHaveValue('line1')

    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import-templates')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/locations/import-templates')[0].body).toEqual({
      name: 'Mixta',
      delimiter: ',',
      hasHeader: true,
      variants: [
        {
          name: 'Formato B',
          conditions: [{ position: 5, operator: 'EQUALS', value: 'PUERTO RICO' }],
          columns: [
            { position: 1, field: 'code' },
            { position: 2, field: 'name' },
            { position: 3, field: 'city' },
            { position: 4, field: 'line1' },
            { position: 5, field: 'ignore' },
          ],
        },
        {
          name: null,
          conditions: [],
          columns: [
            { position: 1, field: 'code' },
            { position: 2, field: 'name' },
            { position: 3, field: 'line1' },
            { position: 4, field: 'city' },
            { position: 5, field: 'ignore' },
          ],
        },
      ],
      defaults: null,
    })
  })

  it('una condición «igual a» sin valor no se guarda; «no está vacía» no pide valor', async () => {
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/1\. Nombre/)
    await user.click(screen.getByRole('button', { name: 'Nueva plantilla' }))
    const dialog = await screen.findByRole('dialog', { name: 'Nueva plantilla de importación' })
    await user.type(within(dialog).getByLabelText(/Nombre de la plantilla/), 'Dos')
    await user.selectOptions(within(dialog).getByLabelText('Columna 1'), 'Nombre *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 2'), 'Dirección *')
    await user.selectOptions(within(dialog).getByLabelText('Columna 3'), 'Pueblo *')
    await user.click(within(dialog).getByRole('button', { name: /Agregar variante/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Mover la variante 2 antes' }))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    expect(await within(dialog).findByText('Escoja la columna de la condición.')).toBeInTheDocument()
    expect(within(dialog).getByText('La condición «igual a» o «contiene» necesita un valor.')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText('Columna de la condición 1'), 'Columna 4')
    await user.selectOptions(within(dialog).getByLabelText('Condición 1'), 'no está vacía')
    expect(within(dialog).queryByLabelText('Valor de la condición 1')).toBeNull()
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/locations/import-templates')).toHaveLength(1))
    const body = callsTo('POST', '/api/v1/locations/import-templates')[0].body as { variants: { conditions: unknown[] }[] }
    expect(body.variants[0].conditions).toEqual([{ position: 4, operator: 'NOT_EMPTY', value: null }])
    expect(body).not.toHaveProperty('columns')
  })

  it('editar una plantilla con variantes las muestra y quitar la segunda vuelve a una sola (PATCH con columns y variants vacío)', async () => {
    templates = [{ ...MIXED }]
    const user = userEvent.setup()
    renderModal()
    expect(await screen.findByText('Mapeo en 2 variantes (se usa la primera que se cumpla):')).toBeInTheDocument()
    expect(screen.getByText(/Columna 9 es igual a «PUERTO RICO»/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar plantilla de importación' })
    const tabs = within(dialog).getByRole('tablist', { name: 'Variantes de mapeo' })
    expect(within(tabs).getAllByRole('tab').map((x) => x.textContent)).toEqual(['Variante 1: Formato B', 'Variante 2'])
    expect(within(dialog).getByLabelText('Columna de la condición 1')).toHaveValue('9')
    expect(within(dialog).getByLabelText('Valor de la condición 1')).toHaveValue('PUERTO RICO')
    expect(within(dialog).getByLabelText('Columna 8')).toHaveValue('city')
    await user.click(within(tabs).getByRole('tab', { name: 'Variante 2' }))
    expect(within(dialog).getByLabelText('Columna 5')).toHaveValue('postalCode')
    await user.click(within(dialog).getByRole('button', { name: 'Quitar la variante 2' }))
    expect(within(dialog).queryByRole('tablist')).toBeNull()
    await user.click(within(dialog).getByRole('button', { name: 'Guardar plantilla' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/locations/import-templates/t-mix')).toHaveLength(1))
    const body = callsTo('PATCH', '/api/v1/locations/import-templates/t-mix')[0].body
    expect(body).toMatchObject({ variants: [] })
    expect((body?.columns as { field: string }[]).filter((c) => c.field !== 'ignore')).toEqual([
      { position: 1, field: 'code' },
      { position: 2, field: 'name' },
      { position: 6, field: 'line1' },
      { position: 8, field: 'city' },
    ])
  })

  it('la vista previa dice qué variante usó cada fila, marca lo deducido de la lista y el código postal ajustado; el resultado lo repite', async () => {
    templates = [{ ...MIXED }]
    mock.override = ({ method, url }) => {
      if (method === 'POST' && url.pathname === '/api/v1/locations/import/validate-file') return MIXED_PREVIEW
      if (method === 'POST' && url.pathname === '/api/v1/locations/import/b-1/confirm')
        return { ...RESULT, created: 2, failed: 0, skipped: 1, rows: [{ ...MIXED_PREVIEW.rows[0], locationPublicId: 'l-1' }, { ...MIXED_PREVIEW.rows[1], locationPublicId: 'l-2' }, { ...MIXED_PREVIEW.rows[2], skipped: true }] }
      return undefined
    }
    const user = userEvent.setup()
    renderModal()
    await screen.findByText(/Mapeo en 2 variantes/)
    await user.upload(fileInput(), csv())
    await screen.findByText(/3 filas detectadas/)
    expect(screen.getByRole('columnheader', { name: 'Variante' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Estado o región' })).toBeInTheDocument() // deducido aunque la plantilla no lo trae
    expect(screen.getAllByText('Variante 2').length).toBeGreaterThanOrEqual(2)
    expect(screen.getAllByText('de la lista').length).toBeGreaterThanOrEqual(4)
    expect(screen.getByText('00734').tagName).toBe('S')
    expect(screen.getByText('Código postal ajustado')).toBeInTheDocument()
    expect(screen.getByText('Variante: Ninguna variante de la plantilla aplica a esta fila; revise las condiciones de las variantes.')).toBeInTheDocument()
    expect(screen.getByText('Celdas de la fila: X-1 | Fila rara | ∅ | USA')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Importar' }))
    const result = (await screen.findByText('Resultado de la importación')).closest('.cs-result') as HTMLElement
    const adjusted = within(result).getByRole('table', { name: /completados o ajustados con la lista de códigos postales: 2/ })
    expect(within(adjusted).getByText('Ferretería Tres')).toBeInTheDocument()
    expect(within(adjusted).getByText(/se usó 00716, de entrega en Ponce/)).toBeInTheDocument()
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
    expect(await screen.findByRole('dialog', { name: 'Importar consignatarios (CSV o Excel)' })).toBeInTheDocument()
    view.unmount()
    wrapScreen(['locations.read'])
    await screen.findByRole('heading', { level: 1, name: 'Consignatarios' })
    expect(screen.queryByRole('button', { name: /Importar/ })).toBeNull()
  })
})
