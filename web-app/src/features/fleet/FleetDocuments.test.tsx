// Lote F-D2 — Flota: «Documentos del vehículo» de la ficha (alta, edición, baja, chip de vencimiento, reemplazado, errores del servidor) y la
// pestaña «Documentos por vencer» (filas, chip de días, filtros Documento / Entidad / días / incluir vencidos al servidor, Limpiar,
// vacío y clic que abre al vehículo o al chofer). Sobre el fetch simulado con estado de `fleetMock`.
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { ALL, callsTo, MANAGE, mock, problem, resetKit, VIEW, wrap } from './fleetTestKit'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('./fleetMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

beforeAll(() => setLang('es'))
beforeEach(() => resetKit())

const section = (name: string) => screen.getByRole('heading', { level: 2, name }).closest('section') as HTMLElement
const docsPanel = async () => within(await screen.findByRole('heading', { level: 2, name: 'Documentos del vehículo' }).then((h) => h.closest('section') as HTMLElement))
const rowOf = (scope: ReturnType<typeof within>, text: string) => scope.getByText(text).closest('tr') as HTMLElement

describe('Flota — documentos del vehículo', () => {
  it('lista los documentos con su chip de vencimiento y marca el reemplazado', async () => {
    wrap(ALL)
    const docs = await docsPanel()
    const table = within(await docs.findByRole('table'))
    expect(within(rowOf(table, 'R-100')).getByText('Vigente')).toBeInTheDocument()
    expect(within(rowOf(table, 'S-7')).getByText('Vence en 8 días')).toBeInTheDocument()
    expect(within(rowOf(table, 'I-3')).getByText('Vencida hace 41 días')).toBeInTheDocument()
    const old = rowOf(table, 'R-99')
    expect(within(old).getByText('Reemplazada')).toBeInTheDocument()
    expect(old).toHaveClass('dim')
    expect(docs.getByText('Registro, seguro, inspección y permiso. Aquí no se adjuntan archivos.')).toBeInTheDocument()
  })

  it('sin documentos: mensaje', async () => {
    wrap(ALL, '/catalog/fleet?vehicle=v-2')
    expect(await screen.findByText('Sin documentos registrados.')).toBeInTheDocument()
  })

  it('«Agregar documento»: valida (tipo obligatorio, vencimiento no anterior a la emisión) y manda el request', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const docs = await docsPanel()
    await user.click(docs.getByRole('button', { name: 'Agregar documento' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar documento' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El tipo de documento es obligatorio.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/vehicles/v-1/documents')).toHaveLength(0)
    await user.selectOptions(within(dialog).getByLabelText(/^Documento/), 'Permiso')
    await user.type(within(dialog).getByLabelText('Número'), ' P-1 ')
    await user.type(within(dialog).getByLabelText('Emisión'), '2026-05-02')
    await user.type(within(dialog).getByLabelText('Vencimiento'), '2026-05-01')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La fecha de vencimiento no puede ser anterior a la de emisión.')).toBeInTheDocument()
    await user.clear(within(dialog).getByLabelText('Vencimiento'))
    await user.type(within(dialog).getByLabelText('Vencimiento'), '2027-05-01')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/vehicles/v-1/documents')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/vehicles/v-1/documents')[0].body).toEqual({ docType: 'PERMIT', docNumber: 'P-1', issuedDate: '2026-05-02', expiryDate: '2027-05-01' })
    expect(await screen.findByText('Documento guardado.')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Agregar documento' })).toBeNull())
    // el alta refresca la ficha, la lista, los documentos por vencer y la disponibilidad
    await waitFor(() => expect(callsTo('GET', '/api/v1/fleet/expiring-documents').length).toBeGreaterThan(1))
  })

  it('un error del servidor sale bajo el campo y el diálogo sigue abierto', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const docs = await docsPanel()
    await user.click(docs.getByRole('button', { name: 'Agregar documento' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar documento' })
    await user.selectOptions(within(dialog).getByLabelText(/^Documento/), 'Seguro')
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/vehicles/v-1/documents'
        ? problem(400, 'Hay errores de validación.', 'validation', { docType: ["Tipo de documento de vehículo desconocido: 'X'."] })
        : undefined
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText("Tipo de documento de vehículo desconocido: 'X'.")).toBeInTheDocument()
  })

  it('«Editar» precarga el documento y manda solo lo cambiado; sin cambios no llama al servidor', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const docs = await docsPanel()
    const table = within(await docs.findByRole('table'))
    await user.click(within(rowOf(table, 'S-7')).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar documento' })
    expect(within(dialog).getByLabelText(/^Documento/)).toHaveValue('INSURANCE')
    expect(within(dialog).getByLabelText('Número')).toHaveValue('S-7')
    expect(within(dialog).getByLabelText('Vencimiento')).toHaveValue('2026-10-20')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar documento' })).toBeNull())
    expect(callsTo('PATCH', '/api/v1/vehicles/v-1/documents/2')).toHaveLength(0)
    await user.click(within(rowOf(table, 'S-7')).getByRole('button', { name: 'Editar' }))
    const again = await screen.findByRole('dialog', { name: 'Editar documento' })
    await user.clear(within(again).getByLabelText('Vencimiento'))
    await user.type(within(again).getByLabelText('Vencimiento'), '2027-10-20')
    await user.click(within(again).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/vehicles/v-1/documents/2')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/vehicles/v-1/documents/2')[0].body).toEqual({ expiryDate: '2027-10-20' })
  })

  it('vaciar el vencimiento manda clearExpiryDate', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const docs = await docsPanel()
    const table = within(await docs.findByRole('table'))
    await user.click(within(rowOf(table, 'S-7')).getByRole('button', { name: 'Editar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Editar documento' })
    await user.clear(within(dialog).getByLabelText('Vencimiento'))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/vehicles/v-1/documents/2')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/vehicles/v-1/documents/2')[0].body).toEqual({ clearExpiryDate: true })
  })

  it('«Quitar» confirma y da de baja el documento (deactivate); un error queda dentro del diálogo', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const docs = await docsPanel()
    const table = within(await docs.findByRole('table'))
    await user.click(within(rowOf(table, 'I-3')).getByRole('button', { name: 'Quitar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Quitar documento' })
    expect(dialog).toHaveTextContent('Se quita el documento Inspección (I-3) del vehículo. Queda en el historial.')
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname === '/api/v1/vehicles/v-1/documents/3/deactivate' ? problem(404, 'Documento no encontrado.', 'not_found') : undefined)
    await user.click(within(dialog).getByRole('button', { name: 'Quitar' }))
    expect(await within(dialog).findByText('Documento no encontrado.')).toBeInTheDocument()
    mock.override = null
    await user.click(within(dialog).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/vehicles/v-1/documents/3/deactivate').length).toBeGreaterThan(1))
    expect(await screen.findByText('Documento quitado.')).toBeInTheDocument()
  })

  it('solo con fleet.view los documentos se consultan: sin agregar, editar ni quitar', async () => {
    wrap(VIEW)
    const docs = await docsPanel()
    const table = within(await docs.findByRole('table'))
    expect(docs.queryByRole('button', { name: 'Agregar documento' })).toBeNull()
    expect(within(rowOf(table, 'S-7')).queryByRole('button', { name: 'Editar' })).toBeNull()
    expect(within(rowOf(table, 'S-7')).queryByRole('button', { name: 'Quitar' })).toBeNull()
  })
})

describe('Flota — pestaña «Documentos por vencer»', () => {
  const open = () => wrap(ALL, '/catalog/fleet?tab=expiring')
  const table = async () => within(await screen.findByRole('table'))

  it('lista los de los próximos 30 días con entidad, documento, fecha y chip de días; pide 30 días con vencidos', async () => {
    open()
    const t = await table()
    await t.findByText('Vencido')
    const rows = t.getAllByRole('row').slice(1)
    expect(rows).toHaveLength(3)
    // orden por vencimiento: el vencido primero
    expect(within(rows[0]).getByText('Vencido')).toBeInTheDocument()
    expect(within(rows[0]).getByText('Vehículo')).toBeInTheDocument()
    expect(within(rows[0]).getByText('Inspección')).toBeInTheDocument()
    expect(within(rows[1]).getByText('8 días')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Chofer')).toBeInTheDocument()
    expect(within(rows[2]).getByText('24 días')).toBeInTheDocument()
    const call = callsTo('GET', '/api/v1/fleet/expiring-documents')[0].url.searchParams
    expect(call.get('withinDays')).toBe('30')
    expect(call.get('includeExpired')).toBe('true')
    expect(within(section('Documentos por vencer')).getByText('3')).toBeInTheDocument()
  })

  it('el buscador filtra lo cargado (código, nombre, documento o número) sin acentos ni mayúsculas; «Limpiar» también lo borra', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await t.findByText('Vencido')
    await user.type(screen.getByRole('searchbox'), 'RAMÓN')
    await waitFor(() => expect(t.getAllByRole('row')).toHaveLength(2))
    expect(t.getByText('Ramón Díaz')).toBeInTheDocument()
    await user.clear(screen.getByRole('searchbox'))
    await user.type(screen.getByRole('searchbox'), 's-7')
    await waitFor(() => expect(t.getAllByRole('row')).toHaveLength(2))
    expect(t.getByText('Seguro')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Limpiar' }))
    await waitFor(() => expect(t.getAllByRole('row')).toHaveLength(4))
    expect(screen.getByRole('searchbox')).toHaveValue('')
  })

  it('las columnas se ordenan: Días de menor a mayor y al revés', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await t.findByText('Vencido')
    await user.click(t.getByRole('button', { name: /^Días/ }))
    const first = () => t.getAllByRole('row')[1]
    expect(within(first()).getByText('Vencido')).toBeInTheDocument()
    await user.click(t.getByRole('button', { name: /^Días/ }))
    expect(within(first()).getByText('24 días')).toBeInTheDocument()
  })

  it('el filtro Documento manda docType al servidor (Licencia y Seguro) y «Limpiar» lo quita', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await t.findByText('Vencido')
    await user.click(screen.getByLabelText('Documento'))
    await user.click(await screen.findByRole('option', { name: 'Licencia' }))
    await waitFor(() => expect(callsTo('GET', '/api/v1/fleet/expiring-documents').some((c) => c.url.searchParams.getAll('docType').join() === 'LICENSE')).toBe(true))
    await waitFor(() => expect(t.getAllByRole('row')).toHaveLength(2))
    expect(t.getByText('Ramón Díaz')).toBeInTheDocument()
    await user.click(screen.getByRole('option', { name: 'Seguro' }))
    await waitFor(() => expect(callsTo('GET', '/api/v1/fleet/expiring-documents').some((c) => c.url.searchParams.getAll('docType').join() === 'LICENSE,INSURANCE')).toBe(true))
    await user.keyboard('{Escape}')
    await user.click(screen.getByRole('button', { name: 'Limpiar' }))
    await waitFor(() => expect(t.getAllByRole('row')).toHaveLength(4))
    expect(callsTo('GET', '/api/v1/fleet/expiring-documents').at(-1)?.url.searchParams.getAll('docType')).toEqual([])
  })

  it('el filtro Entidad manda entity (Vehículo / Chofer)', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await t.findByText('Vencido')
    await user.click(screen.getByLabelText('Entidad'))
    await user.click(await screen.findByRole('option', { name: 'Chofer' }))
    await waitFor(() => expect(callsTo('GET', '/api/v1/fleet/expiring-documents').some((c) => c.url.searchParams.getAll('entity').join() === 'DRIVER')).toBe(true))
    await waitFor(() => expect(t.getAllByRole('row')).toHaveLength(2))
    expect(t.queryByText('Vehículo')).toBeNull()
  })

  it('«Dentro de (días)» cambia la ventana; un valor fuera de rango avisa y no cambia la consulta', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await t.findByText('Vencido')
    const days = screen.getByLabelText('Dentro de (días)')
    await user.clear(days)
    await user.type(days, '90')
    await waitFor(() => expect(callsTo('GET', '/api/v1/fleet/expiring-documents').some((c) => c.url.searchParams.get('withinDays') === '90')).toBe(true))
    await waitFor(() => expect(t.getAllByRole('row')).toHaveLength(5))
    expect(t.getByText('69 días')).toBeInTheDocument()
    const sent = callsTo('GET', '/api/v1/fleet/expiring-documents').length
    await user.clear(days)
    await user.type(days, '999')
    expect(await screen.findByText('Escriba un número entero entre 0 y 365.')).toBeInTheDocument()
    // se quedó la última ventana válida (99 es válido: "9", "99" se pidieron al teclear; "999" no)
    expect(callsTo('GET', '/api/v1/fleet/expiring-documents').slice(sent).every((c) => Number(c.url.searchParams.get('withinDays')) <= 365)).toBe(true)
  })

  it('«Incluir vencidos» apagado manda includeExpired=false y quita los vencidos', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await t.findByText('Vencido')
    await user.click(screen.getByRole('switch', { name: 'Incluir vencidos' }))
    await waitFor(() => expect(callsTo('GET', '/api/v1/fleet/expiring-documents').some((c) => c.url.searchParams.get('includeExpired') === 'false')).toBe(true))
    await waitFor(() => expect(t.queryByText('Vencido')).toBeNull())
  })

  it('sin documentos por vencer: mensaje; un error del servidor se muestra tal cual', async () => {
    mock.override = ({ method, url }) => (method === 'GET' && url.pathname === '/api/v1/fleet/expiring-documents' ? [] : undefined)
    const first = open()
    expect(await screen.findByText('Sin documentos por vencer')).toBeInTheDocument()
    first.unmount()
    mock.override = ({ method, url }) => (method === 'GET' && url.pathname === '/api/v1/fleet/expiring-documents' ? problem(400, 'withinDays debe estar entre 0 y 365.', 'validation') : undefined)
    open()
    expect((await screen.findAllByText('withinDays debe estar entre 0 y 365.')).length).toBeGreaterThan(0)
  })

  it('un clic en una fila de vehículo abre su ficha en la pestaña Vehículos', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await user.click(await t.findByText('Inspección'))
    expect(await screen.findByRole('heading', { level: 2, name: 'Ficha del vehículo' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Vehículos' })).toHaveAttribute('aria-selected', 'true')
    expect(await screen.findByDisplayValue('HGR-204')).toBeInTheDocument()
  })

  it('un clic en una fila de chofer abre la ficha del chofer', async () => {
    const user = userEvent.setup()
    open()
    const t = await table()
    await user.click(await t.findByText('Ramón Díaz'))
    expect(await screen.findByTestId('drivers-screen')).toHaveTextContent('/catalog/drivers?driver=d-1')
  })

  it('con fleet.manage o sin él se ve igual (solo lectura)', async () => {
    wrap(MANAGE, '/catalog/fleet?tab=expiring')
    expect((await (await table()).findAllByRole('row')).length).toBe(4)
  })
})
