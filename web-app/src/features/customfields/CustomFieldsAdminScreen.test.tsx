// Pruebas de «Campos personalizados» (administración, lote F-F1) sobre un fetch simulado con estado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import CustomFieldsAdminScreen from './CustomFieldsAdminScreen'

interface Req {
  method: string
  url: URL
  body: Record<string, unknown> | null
  headers: Headers
}
interface Def {
  id: number
  entityType: string
  fieldKey: string
  label: string
  labels: Record<string, string>
  description: string | null
  dataType: string
  isRequired: boolean
  isUnique: boolean
  defaultValue: string | null
  validationJson: string | null
  refEntity: string | null
  showInList: boolean
  sortOrder: number
  isActive: boolean
  options: { id: number; value: string; label: string; labels: Record<string, string>; sortOrder: number; isActive: boolean }[]
}

const mock = vi.hoisted(() => ({
  requests: [] as Req[],
  defs: [] as unknown[],
  domains: [] as unknown[],
  nextId: 100,
  /** Respuesta de error para el próximo POST/PUT de definiciones. */
  fail: null as null | { status: number; problem: Record<string, unknown> },
  failList: null as null | { status: number; problem: Record<string, unknown> },
}))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const p = url.pathname
    let body: Record<string, unknown> | null = null
    try {
      body = req.method === 'GET' || req.method === 'DELETE' ? null : ((await req.clone().json()) as Record<string, unknown>)
    } catch {
      body = null
    }
    mock.requests.push({ method: req.method, url, body, headers: req.headers })
    const defs = mock.defs as Def[]
    const problem = (status: number, p: Record<string, unknown>) =>
      new Response(JSON.stringify(p), { status, headers: { 'Content-Type': 'application/problem+json' } })

    if (req.method === 'GET' && p === '/api/v1/custom-fields/definitions') {
      const et = url.searchParams.get('entityType')
      const inc = url.searchParams.get('includeInactive') === 'true'
      return json(defs.filter((d) => (!et || d.entityType === et) && (inc || d.isActive)))
    }
    const create = /^\/api\/v1\/custom-fields\/definitions\/([A-Z_]+)$/.exec(p)
    if (req.method === 'POST' && create) {
      if (mock.fail) return problem(mock.fail.status, mock.fail.problem)
      const b = body as Record<string, unknown>
      const labels = b.labels as Record<string, string>
      const def: Def = {
        id: mock.nextId++,
        entityType: create[1],
        fieldKey: String(b.fieldKey),
        label: labels.es,
        labels,
        description: null,
        dataType: String(b.dataType),
        isRequired: b.isRequired === true,
        isUnique: b.isUnique === true,
        defaultValue: (b.defaultValue as string | null) ?? null,
        validationJson: (b.validationJson as string | null) ?? null,
        refEntity: (b.refEntity as string | null) ?? null,
        showInList: b.showInList === true,
        sortOrder: (b.sortOrder as number | null) ?? 100,
        isActive: true,
        options: [],
      }
      defs.push(def)
      return json(def)
    }
    const one = /^\/api\/v1\/custom-fields\/definitions\/(\d+)(\/restore)?$/.exec(p)
    if (one) {
      const def = defs.find((d) => d.id === Number(one[1]))
      if (!def) return problem(404, { title: `Campo personalizado '${one[1]}' no encontrado.`, status: 404 })
      if (req.method === 'PUT') {
        if (mock.fail) return problem(mock.fail.status, mock.fail.problem)
        const b = body as Record<string, unknown>
        const labels = b.labels as Record<string, string>
        Object.assign(def, {
          label: labels.es,
          labels,
          dataType: b.dataType,
          isRequired: b.isRequired,
          isUnique: b.isUnique,
          defaultValue: b.defaultValue ?? null,
          validationJson: b.validationJson ?? null,
          refEntity: b.refEntity ?? null,
          showInList: b.showInList,
          sortOrder: b.sortOrder ?? def.sortOrder,
        })
        return json(def)
      }
      if (req.method === 'DELETE') {
        def.isActive = false
        return new Response(null, { status: 204 })
      }
      if (req.method === 'POST' && one[2]) {
        def.isActive = true
        return new Response(null, { status: 204 })
      }
    }
    if (req.method === 'GET' && p === '/api/v1/catalogs/domains') return json(mock.domains)
    if (req.method === 'GET' && p === '/api/v1/catalogs/T1_PRIORIDAD') return json(PRIORIDAD_VALUES)
    if (req.method === 'GET' && p === '/api/v1/catalogs/T1_NUEVA') return json([lookup('T1_NUEVA', 'ALTO', 'Alto'), lookup('T1_NUEVA', 'BAJO', 'Bajo')])
    if (req.method === 'GET' && p === '/api/v1/catalogs/Region') return json([lookup('Region', 'NORTE', 'Norte')])
    if (req.method === 'POST' && p === '/api/v1/catalogs/lists') {
      if (mock.failList) return problem(mock.failList.status, mock.failList.problem)
      const domain = { id: 9, domainKey: 'T1_NUEVA', scope: 1, label: String(body?.name), labels: {}, description: null, isSystem: false, isActive: true, tenantId: 1 }
      mock.domains = [...mock.domains, domain]
      return json(domain)
    }
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404, headers: { 'Content-Type': 'application/problem+json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function lookup(entity: string, code: string, label: string) {
  return { id: 1, entity, code, label, labels: { es: label }, description: null, extraJson: null, sortOrder: 10, isSystem: false, isEnabled: true, isOverridden: false, tenantId: 1, isActive: true }
}

const PRIORIDAD_VALUES = [lookup('T1_PRIORIDAD', 'ALTO', 'Alto'), lookup('T1_PRIORIDAD', 'MEDIO', 'Medio'), lookup('T1_PRIORIDAD', 'BAJO', 'Bajo')]

function def(over: Partial<Def>): Def {
  return {
    id: 1,
    entityType: 'CLIENT',
    fieldKey: 'x',
    label: 'X',
    labels: { es: 'X', en: 'X' },
    description: null,
    dataType: 'TEXT',
    isRequired: false,
    isUnique: false,
    defaultValue: null,
    validationJson: null,
    refEntity: null,
    showInList: false,
    sortOrder: 100,
    isActive: true,
    options: [],
    ...over,
  }
}

function seed() {
  mock.defs = [
    def({ id: 1, fieldKey: 'cost_center', label: 'Centro de costo', labels: { es: 'Centro de costo', en: 'Cost center' }, isRequired: true, isUnique: true, showInList: true, validationJson: '{"regex":"^CC-\\\\d{3}$","maxLength":6}', defaultValue: 'CC-000', sortOrder: 10 }),
    def({ id: 2, fieldKey: 'prioridad', label: 'Prioridad', labels: { es: 'Prioridad', en: 'Priority' }, dataType: 'SELECT', refEntity: 'T1_PRIORIDAD', sortOrder: 20 }),
    def({ id: 3, fieldKey: 'viejo', label: 'Campo viejo', labels: { es: 'Campo viejo' }, isActive: false, sortOrder: 30 }),
    def({ id: 4, entityType: 'DRIVER', fieldKey: 'bono', label: 'Bono', labels: { es: 'Bono' }, dataType: 'NUMBER' }),
    def({
      id: 5,
      entityType: 'VEHICLE',
      fieldKey: 'color',
      label: 'Color',
      labels: { es: 'Color', en: 'Color' },
      dataType: 'SELECT',
      options: [
        { id: 1, value: 'ROJO', label: 'Rojo', labels: { es: 'Rojo' }, sortOrder: 10, isActive: true },
        { id: 2, value: 'AZUL', label: 'Azul', labels: { es: 'Azul' }, sortOrder: 20, isActive: true },
      ],
    }),
  ]
  mock.domains = [
    { id: 1, domainKey: 'T1_PRIORIDAD', scope: 1, label: 'Prioridad', labels: {}, description: null, isSystem: false, isActive: true, tenantId: 1 },
    { id: 2, domainKey: 'Region', scope: 1, label: 'Región', labels: {}, description: null, isSystem: true, isActive: true, tenantId: null },
    { id: 3, domainKey: 'OrderStatus', scope: 2, label: 'Estatus de orden', labels: {}, description: null, isSystem: true, isActive: true, tenantId: null },
  ]
}

function wrap(permissions: string[] = ['admin.customfields', 'admin.catalogs']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <AccessProvider permissions={permissions} modules={['CUSTOM_FIELDS', 'ANALYTICS']}>
        <MemoryRouter>
          <CustomFieldsAdminScreen />
        </MemoryRouter>
      </AccessProvider>
    </QueryClientProvider>,
  )
}

const writes = () => mock.requests.filter((r) => r.method !== 'GET')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.nextId = 100
  mock.fail = null
  mock.failList = null
  seed()
})

async function openNew(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole('button', { name: /Nuevo campo/ }))
  return screen.findByRole('dialog')
}

const labelEs = () => screen.getByLabelText(/^Etiqueta \(español\)/)

describe('Campos personalizados — lista por entidad', () => {
  it('pinta las entidades con su contador (solo activos) y la tabla de la primera', async () => {
    wrap()
    const nav = await screen.findByRole('navigation', { name: 'Entidades' })
    await waitFor(() =>
      expect(within(nav).getAllByRole('button').map((b) => b.textContent)).toEqual(['Cliente2', 'Orden0', 'Chofer1', 'Vehículo1', 'Producto0', 'Ubicación (consignatario)0']),
    )
    expect(within(nav).queryByText(/Factura|Contrato|Almacén/)).toBeNull()
    const table = await screen.findByRole('table', { name: 'Cliente' })
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(2)
    expect(rows[0]).toHaveTextContent('Centro de costo')
    expect(rows[0]).toHaveTextContent('cost_center')
    expect(rows[0]).toHaveTextContent('Texto')
    expect(within(rows[0]).getAllByRole('img', { name: /Requerido|Único|Mostrar en la búsqueda/ })).toHaveLength(3)
    // la lista del catálogo y su número de opciones
    await waitFor(() => expect(rows[1]).toHaveTextContent('Lista: Prioridad · 3 opciones'))
    expect(screen.queryByText('Campo viejo')).toBeNull()
    expect(screen.getByText(/Cada campo se define una vez por entidad/)).toBeInTheDocument()
  })

  it('cambiar de entidad cambia la tabla; una sin campos muestra el vacío; las opciones propias se cuentan', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('Centro de costo')
    await user.click(screen.getByRole('button', { name: /^Orden/ }))
    expect(await screen.findByText('Todavía no hay campos personalizados para esta entidad.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /^Vehículo/ }))
    const row = (await screen.findByText('Color')).closest('tr') as HTMLElement
    expect(row).toHaveTextContent('Selección')
    expect(row).toHaveTextContent('Opciones propias · 2')
  })

  it('«Incluir inactivos» muestra los desactivados atenuados con Reactivar y sin Editar', async () => {
    const user = userEvent.setup()
    wrap()
    await screen.findByText('Centro de costo')
    await user.click(screen.getByRole('switch', { name: 'Incluir inactivos' }))
    const row = (await screen.findByText('Campo viejo')).closest('tr') as HTMLElement
    expect(row).toHaveTextContent('Inactivo')
    expect(within(row).getByRole('button', { name: 'Reactivar' })).toBeInTheDocument()
    expect(within(row).queryByRole('button', { name: 'Editar' })).toBeNull()
    // el contador sigue contando solo los activos
    expect(screen.getByRole('button', { name: /^Cliente/ })).toHaveTextContent('2')
  })

  it('sin admin.customfields no hay acciones ni «Nuevo campo»', async () => {
    wrap(['analytics.view'])
    await screen.findByText('Centro de costo')
    expect(screen.queryByRole('button', { name: /Nuevo campo/ })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Editar' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Desactivar' })).toBeNull()
  })
})

describe('Campos personalizados — alta', () => {
  it('TEXT con formato y longitudes: arma validationJson amigable y manda una Idempotency-Key', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openNew(user)
    expect(within(dialog).getByText('Cliente')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText(/^Clave/), 'Sello_Final')
    await user.type(labelEs(), 'Sello final')
    await user.type(within(dialog).getByLabelText(/^Formato/), '^S-\\d+$')
    await user.type(within(dialog).getByLabelText('Longitud mínima'), '3')
    await user.type(within(dialog).getByLabelText('Longitud máxima'), '8')
    await user.type(within(dialog).getByLabelText('Valor por defecto'), 'S-1')
    await user.click(within(dialog).getByRole('switch', { name: 'Requerido' }))
    await user.click(within(dialog).getByRole('switch', { name: 'Mostrar en la búsqueda' }))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))

    await waitFor(() => expect(writes()).toHaveLength(1))
    const req = writes()[0]
    expect(req.method).toBe('POST')
    expect(req.url.pathname).toBe('/api/v1/custom-fields/definitions/CLIENT')
    expect(req.headers.get('Idempotency-Key')).toMatch(/[0-9a-f-]{36}/)
    expect(req.body).toMatchObject({
      fieldKey: 'sello_final',
      labels: { es: 'Sello final', en: 'Sello final' },
      dataType: 'TEXT',
      isRequired: true,
      isUnique: false,
      showInList: true,
      defaultValue: 'S-1',
      refEntity: null,
      sortOrder: 100,
      options: [],
    })
    expect(JSON.parse(req.body?.validationJson as string)).toEqual({ regex: '^S-\\d+$', minLength: 3, maxLength: 8 })
    // se cierra y la lista se actualiza sola, con el contador
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(await screen.findByText('Sello final')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^Cliente/ })).toHaveTextContent('3')
  })

  it('NUMBER: mínimo y máximo; no ofrece formato ni longitudes', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openNew(user)
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), 'NUMBER')
    expect(within(dialog).queryByLabelText(/^Formato/)).toBeNull()
    await user.type(within(dialog).getByLabelText(/^Clave/), 'peso')
    await user.type(labelEs(), 'Peso')
    await user.type(within(dialog).getByLabelText('Valor mínimo'), '0')
    await user.type(within(dialog).getByLabelText('Valor máximo'), '99.5')
    await user.type(within(dialog).getByLabelText('Valor por defecto'), '10')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0].body).toMatchObject({ dataType: 'NUMBER', defaultValue: '10' })
    expect(JSON.parse(writes()[0].body?.validationJson as string)).toEqual({ min: 0, max: 99.5 })
  })

  it('DATE: mínimo/máximo en días y fecha por defecto; BOOL: casilla y sin validación', async () => {
    const user = userEvent.setup()
    wrap()
    let dialog = await openNew(user)
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), 'DATE')
    await user.type(within(dialog).getByLabelText(/^Clave/), 'vence')
    await user.type(labelEs(), 'Vence')
    await user.type(within(dialog).getByLabelText('Mínimo (días desde hoy)'), '-30')
    fireEvent.change(within(dialog).getByLabelText('Valor por defecto'), { target: { value: '2026-12-31' } })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0].body).toMatchObject({ dataType: 'DATE', defaultValue: '2026-12-31' })
    expect(JSON.parse(writes()[0].body?.validationJson as string)).toEqual({ min: -30 })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())

    dialog = await openNew(user)
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), 'BOOL')
    expect(within(dialog.querySelector('form') as HTMLElement).queryByText('Validación')).toBeNull()
    await user.type(within(dialog).getByLabelText(/^Clave/), 'fragil')
    await user.type(labelEs(), 'Frágil')
    await user.click(within(dialog).getByRole('switch', { name: /Marcado por defecto/ }))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    await waitFor(() => expect(writes()).toHaveLength(2))
    expect(writes()[1].body).toMatchObject({ dataType: 'BOOL', defaultValue: 'true', validationJson: null })
  })

  it('DATETIME: la fecha por defecto sale como instante UTC (hora de la compañía)', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openNew(user)
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), 'DATETIME')
    await user.type(within(dialog).getByLabelText(/^Clave/), 'cita')
    await user.type(labelEs(), 'Cita')
    fireEvent.change(within(dialog).getByLabelText('Valor por defecto'), { target: { value: '2026-10-12T09:30' } })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0].body).toMatchObject({ dataType: 'DATETIME', defaultValue: '2026-10-12T13:30:00.000Z' })
  })

  it('SELECT con una lista existente y valor por defecto de la lista; no ofrece listas de estatus', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openNew(user)
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), 'SELECT')
    const combo = within(dialog).getByRole('combobox', { name: /Lista de valores/ })
    await user.click(combo)
    expect(within(screen.getByRole('listbox')).getAllByRole('option').map((o) => o.textContent)).toEqual(['PrioridadPropia', 'RegiónSistema'])
    await user.click(screen.getByRole('option', { name: /Prioridad/ }))
    await within(dialog).findByText('Opciones: Alto · Medio · Bajo')
    await user.type(within(dialog).getByLabelText(/^Clave/), 'urgencia')
    await user.type(labelEs(), 'Urgencia')
    await user.selectOptions(await within(dialog).findByLabelText('Valor por defecto'), 'MEDIO')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0].body).toMatchObject({ dataType: 'SELECT', refEntity: 'T1_PRIORIDAD', defaultValue: 'MEDIO', options: [] })
  })

  it('MULTISELECT y LOOKUP_REF también envían la lista', async () => {
    const user = userEvent.setup()
    wrap()
    for (const [type, key] of [['MULTISELECT', 'etiquetas'], ['LOOKUP_REF', 'region']] as const) {
      const dialog = await openNew(user)
      await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), type)
      await user.type(within(dialog).getByLabelText(/^Clave/), key)
      await user.type(labelEs(), key)
      await user.click(within(dialog).getByRole('combobox', { name: /Lista de valores/ }))
      await user.click(screen.getByRole('option', { name: /Región/ }))
      await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    }
    expect(writes().map((w) => w.body)).toMatchObject([
      { dataType: 'MULTISELECT', refEntity: 'Region' },
      { dataType: 'LOOKUP_REF', refEntity: 'Region' },
    ])
  })

  it('«Nueva lista…» crea la lista con sus opciones y la deja elegida', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openNew(user)
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), 'SELECT')
    await user.click(within(dialog).getByRole('button', { name: /Nueva lista/ }))
    // sin nombre u opciones avisa
    await user.click(within(dialog).getByRole('button', { name: 'Crear lista' }))
    expect(within(dialog).getByText('Ponle nombre a la lista y al menos una opción')).toBeInTheDocument()
    await user.type(within(dialog).getByLabelText('Nombre de la lista'), 'Nivel')
    await user.type(within(dialog).getByLabelText('Opciones separadas por coma'), 'Alto, Bajo')
    await user.click(within(dialog).getByRole('button', { name: 'Crear lista' }))
    await waitFor(() => expect(mock.requests.some((r) => r.url.pathname === '/api/v1/catalogs/lists')).toBe(true))
    const create = mock.requests.find((r) => r.url.pathname === '/api/v1/catalogs/lists')
    expect(create?.method).toBe('POST')
    expect(create?.body).toMatchObject({ name: 'Nivel', values: [{ code: 'ALTO', labels: { es: 'Alto' } }, { code: 'BAJO', labels: { es: 'Bajo' } }] })
    // la lista nueva queda elegida y se ven sus opciones
    expect(await within(dialog).findByRole('combobox', { name: /Lista de valores/ })).toHaveValue('Nivel')
    await within(dialog).findByText('Opciones: Alto · Bajo')
    // el campo se guarda apuntando a ella
    await user.type(within(dialog).getByLabelText(/^Clave/), 'nivel')
    await user.type(labelEs(), 'Nivel')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    await waitFor(() => expect(writes().some((w) => w.url.pathname.includes('/custom-fields/'))).toBe(true))
    expect(writes().find((w) => w.url.pathname.includes('/custom-fields/'))?.body).toMatchObject({ dataType: 'SELECT', refEntity: 'T1_NUEVA' })
  })

  it('«Nueva lista…»: Enter crea la lista sin enviar el campo, y un error del servidor se ve en el recuadro', async () => {
    const user = userEvent.setup()
    mock.failList = { status: 409, problem: { title: "Ya existe una lista con la clave 'T1_NIVEL'.", status: 409 } }
    wrap()
    const dialog = await openNew(user)
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), 'SELECT')
    await user.click(within(dialog).getByRole('button', { name: /Nueva lista/ }))
    await user.type(within(dialog).getByLabelText('Nombre de la lista'), 'Nivel')
    await user.type(within(dialog).getByLabelText('Opciones separadas por coma'), 'Alto, ###{Enter}')
    expect(within(dialog).getByText('No se puede armar un código con: ###')).toBeInTheDocument()
    await user.clear(within(dialog).getByLabelText('Opciones separadas por coma'))
    await user.type(within(dialog).getByLabelText('Opciones separadas por coma'), 'Alto{Enter}')
    expect(await within(dialog).findByText("Ya existe una lista con la clave 'T1_NIVEL'.")).toBeInTheDocument()
    expect(writes().filter((w) => w.url.pathname.includes('/custom-fields/'))).toHaveLength(0)
    await user.click(within(within(dialog).getByRole('group', { name: 'Nueva lista de valores' })).getByRole('button', { name: 'Cancelar' }))
    expect(within(dialog).getByRole('combobox', { name: /Lista de valores/ })).toBeInTheDocument()
  })

  it('sin admin.catalogs no hay «Nueva lista…» y se explica qué pedir', async () => {
    const user = userEvent.setup()
    wrap(['admin.customfields'])
    const dialog = await openNew(user)
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), 'MULTISELECT')
    expect(within(dialog).queryByRole('button', { name: /Nueva lista/ })).toBeNull()
    expect(within(dialog).getByText(/Pide a un administrador que la cree/)).toBeInTheDocument()
    // elegir una lista existente sigue funcionando
    await user.click(within(dialog).getByRole('combobox', { name: /Lista de valores/ }))
    expect(within(screen.getByRole('listbox')).getAllByRole('option')).toHaveLength(2)
  })

  it('valida en el cliente: clave, etiqueta y lista obligatoria, sin llamar al API', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openNew(user)
    await user.type(within(dialog).getByLabelText(/^Clave/), 'Mal-Nombre')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    expect(await within(dialog).findByText("Clave inválida: minúsculas, dígitos y '_' (2-60), empezando por letra.")).toBeInTheDocument()
    expect(within(dialog).getByText('La etiqueta es obligatoria.')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), 'SELECT')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    expect(await within(dialog).findByText('Escoge o crea una lista de valores')).toBeInTheDocument()
    expect(writes()).toHaveLength(0)
  })
})

describe('Campos personalizados — errores del servidor', () => {
  async function fillMinimal(user: ReturnType<typeof userEvent.setup>, key = 'cost_center') {
    const dialog = await openNew(user)
    await user.type(within(dialog).getByLabelText(/^Clave/), key)
    await user.type(labelEs(), 'Centro')
    return dialog
  }

  it('409 clave repetida: el mensaje exacto va bajo la clave y el formulario sigue abierto', async () => {
    const user = userEvent.setup()
    mock.fail = { status: 409, problem: { title: "Ya existe el campo 'cost_center' en CLIENT.", status: 409, code: 'conflict' } }
    wrap()
    const dialog = await fillMinimal(user)
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    const msg = await within(dialog).findByText("Ya existe el campo 'cost_center' en CLIENT.")
    expect(msg).toHaveClass('ferr')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    // corregir la clave y reintentar funciona (otra Idempotency-Key)
    mock.fail = null
    await user.clear(within(dialog).getByLabelText(/^Clave/))
    await user.type(within(dialog).getByLabelText(/^Clave/), 'cost_center_2')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    const posts = writes()
    expect(posts).toHaveLength(2)
    expect(posts[0].headers.get('Idempotency-Key')).not.toBe(posts[1].headers.get('Idempotency-Key'))
  })

  it('400 «La lista no existe» va bajo la lista; «etiqueta» (labels) bajo la etiqueta', async () => {
    const user = userEvent.setup()
    wrap()
    const dialog = await openNew(user)
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), 'SELECT')
    await user.type(within(dialog).getByLabelText(/^Clave/), 'zona')
    await user.type(labelEs(), 'Zona')
    await user.click(within(dialog).getByRole('combobox', { name: /Lista de valores/ }))
    await user.click(screen.getByRole('option', { name: /Región/ }))
    mock.fail = { status: 400, problem: { title: 'Error de validación', status: 400, errors: { refEntity: ["La lista 'Region' no existe."], labels: ['La etiqueta es obligatoria.'] } } }
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    const list = await within(dialog).findByText("La lista 'Region' no existe.")
    expect(list).toHaveClass('ferr')
    expect(within(dialog).getByText('La etiqueta es obligatoria.')).toHaveClass('ferr')
  })

  it('un error sin campo (500) sale como aviso arriba del formulario', async () => {
    const user = userEvent.setup()
    mock.fail = { status: 500, problem: { title: 'Ocurrió un error inesperado.', status: 500 } }
    wrap()
    const dialog = await fillMinimal(user)
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Ocurrió un error inesperado.')
  })
})

describe('Campos personalizados — edición, desactivar y reactivar', () => {
  it('editar: clave de solo lectura, datos cargados, PUT con lo cambiado', async () => {
    const user = userEvent.setup()
    wrap()
    const row = (await screen.findByText('Centro de costo')).closest('tr') as HTMLElement
    await user.click(within(row).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Editar campo personalizado')).toBeInTheDocument()
    expect(within(dialog).queryByLabelText(/^Clave/)).toBeNull()
    expect(within(dialog).getByText('cost_center')).toBeInTheDocument()
    expect(within(dialog).getByLabelText(/^Etiqueta \(español\)/)).toHaveValue('Centro de costo')
    expect(within(dialog).getByLabelText(/^Etiqueta \(inglés\)/)).toHaveValue('Cost center')
    expect(within(dialog).getByLabelText(/^Formato/)).toHaveValue('^CC-\\d{3}$')
    expect(within(dialog).getByLabelText('Longitud máxima')).toHaveValue(6)
    expect(within(dialog).getByLabelText('Valor por defecto')).toHaveValue('CC-000')
    await user.clear(labelEs())
    await user.type(labelEs(), 'Centro de costos')
    await user.click(within(dialog).getByRole('switch', { name: 'Único' }))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    await waitFor(() => expect(writes()).toHaveLength(1))
    const req = writes()[0]
    expect(req.method).toBe('PUT')
    expect(req.url.pathname).toBe('/api/v1/custom-fields/definitions/1')
    expect(req.body).toMatchObject({ fieldKey: 'cost_center', labels: { es: 'Centro de costos', en: 'Cost center' }, isUnique: false, isRequired: true, dataType: 'TEXT', sortOrder: 10 })
    expect(JSON.parse(req.body?.validationJson as string)).toEqual({ regex: '^CC-\\d{3}$', maxLength: 6 })
    expect(await screen.findByText('Centro de costos')).toBeInTheDocument()
  })

  it('editar un campo con opciones propias las conserva en el PUT', async () => {
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: /^Vehículo/ }))
    const row = (await screen.findByText('Color')).closest('tr') as HTMLElement
    await user.click(within(row).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/usa opciones propias \(2: Rojo, Azul\)/)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0].body).toMatchObject({ dataType: 'SELECT', refEntity: null, options: [{ value: 'ROJO' }, { value: 'AZUL' }] })
  })

  it('409 al cambiar el tipo de un campo con valores: el mensaje exacto va bajo el tipo', async () => {
    const user = userEvent.setup()
    mock.fail = { status: 409, problem: { title: 'No se puede cambiar el tipo de un campo que ya tiene valores guardados.', status: 409, code: 'conflict' } }
    wrap()
    const row = (await screen.findByText('Centro de costo')).closest('tr') as HTMLElement
    await user.click(within(row).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog')
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo/), 'NUMBER')
    await user.clear(within(dialog).getByLabelText('Valor por defecto'))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar campo' }))
    const msg = await within(dialog).findByText('No se puede cambiar el tipo de un campo que ya tiene valores guardados.')
    expect(msg).toHaveClass('ferr')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('desactivar pide confirmación que explica que los valores se conservan; Reactivar no pregunta', async () => {
    const user = userEvent.setup()
    wrap()
    const row = (await screen.findByText('Prioridad', { selector: 'td, td *' })).closest('tr') as HTMLElement
    await user.click(within(row).getByRole('button', { name: 'Desactivar' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(/dejará de pedirse y de mostrarse en las fichas/)).toBeInTheDocument()
    expect(within(dialog).getByText(/Los valores ya guardados se conservan/)).toBeInTheDocument()
    expect(writes()).toHaveLength(0)
    await user.click(within(dialog).getByRole('button', { name: 'Desactivar' }))
    await waitFor(() => expect(writes()).toHaveLength(1))
    expect(writes()[0].method).toBe('DELETE')
    expect(writes()[0].url.pathname).toBe('/api/v1/custom-fields/definitions/2')
    await waitFor(() => expect(screen.queryByText('Lista: Prioridad · 3 opciones')).toBeNull())
    expect(screen.getByRole('button', { name: /^Cliente/ })).toHaveTextContent('1')

    // reactivar: el campo ya inactivo (3) y el que se acaba de desactivar (2)
    await user.click(screen.getByRole('switch', { name: 'Incluir inactivos' }))
    const old = (await screen.findByText('Campo viejo')).closest('tr') as HTMLElement
    await user.click(within(old).getByRole('button', { name: 'Reactivar' }))
    await waitFor(() => expect(writes()).toHaveLength(2))
    expect(writes()[1].method).toBe('POST')
    expect(writes()[1].url.pathname).toBe('/api/v1/custom-fields/definitions/3/restore')
    await waitFor(() => expect(screen.getByRole('button', { name: /^Cliente/ })).toHaveTextContent('2'))
    expect(screen.queryByRole('dialog')).toBeNull()
  })
})
