// Lote F-D1 — paneles de la ficha: Licencias y certificaciones (chip de vencimiento, alta, edición mínima, baja, errores del servidor),
// Dispositivos (sin token, desactivar), Contactos (entidad DRIVER), Historial y Campos personalizados, con y sin permisos.
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { setLang } from '../../kernel/i18n/i18n'
import { ALL, callsTo, FLEET, mock, problem, resetKit, wrap } from './driversTestKit'
import { typeDate } from '../../kernel/ui/typeDate'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('./driversMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

beforeAll(() => setLang('es'))
beforeEach(() => resetKit())

const licenses = () => screen.findByRole('region', { name: 'Licencias' })
const certs = () => screen.findByRole('region', { name: 'Certificaciones' })
const rowOf = (scope: HTMLElement, text: RegExp) => within(scope).getByRole('row', { name: text })

describe('Licencias y certificaciones', () => {
  it('lista cada documento con su chip de vencimiento (vigente, por vencer, vencida, reemplazada, sin vencimiento)', async () => {
    wrap(ALL)
    const lic = await licenses()
    expect(within(rowOf(lic, /L-100/)).getByText('Vigente')).toBeInTheDocument()
    expect(within(rowOf(lic, /B-7/)).getByText('Vence en 8 días')).toBeInTheDocument()
    expect(within(rowOf(lic, /R-1/)).getByText('Vencida hace 41 días')).toBeInTheDocument()
    // L-99 lo reemplaza otro más nuevo del mismo tipo: no cuenta como vencimiento
    expect(within(rowOf(lic, /L-99/)).getByText('Reemplazada')).toBeInTheDocument()
    const cer = await certs()
    expect(within(rowOf(cer, /H-1/)).getByText('Sin vencimiento')).toBeInTheDocument()
    expect(within(rowOf(cer, /H-1/)).getByText('HazMat')).toBeInTheDocument()
  })

  it('agrega una licencia: POST con clase, número y fechas; el panel se vuelve a leer', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await licenses()
    await user.click(screen.getByRole('button', { name: 'Agregar licencia' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar licencia' })
    await user.selectOptions(within(dialog).getByLabelText(/^Clase de licencia/), 'CDL-B')
    await user.type(within(dialog).getByLabelText(/^Número de licencia/), '  N-55 ')
    await typeDate(user, within(dialog).getByLabelText('Emisión'), '2026-01-01')
    await typeDate(user, within(dialog).getByLabelText('Vencimiento'), '2028-01-01')
    const before = callsTo('GET', '/api/v1/drivers/d-1').length
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/licenses')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/drivers/d-1/licenses')[0].body).toEqual({ licenseClass: 'CDL_B', licenseNumber: 'N-55', issuedDate: '2026-01-01', expiryDate: '2028-01-01' })
    expect(await screen.findByText('Documento guardado.')).toBeInTheDocument()
    await waitFor(() => expect(callsTo('GET', '/api/v1/drivers/d-1').length).toBeGreaterThan(before))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Agregar licencia' })).toBeNull())
  })

  it('validaciones del manual sin llamar al servidor: clase, número y vencimiento anterior a la emisión', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await licenses()
    await user.click(screen.getByRole('button', { name: 'Agregar licencia' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar licencia' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La clase de licencia es obligatoria.')).toBeInTheDocument()
    expect(within(dialog).getByText('El número de licencia es obligatorio.')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText(/^Clase de licencia/), 'Regular')
    await user.type(within(dialog).getByLabelText(/^Número de licencia/), 'X')
    await typeDate(user, within(dialog).getByLabelText('Emisión'), '2026-05-01')
    await typeDate(user, within(dialog).getByLabelText('Vencimiento'), '2026-04-30')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('La fecha de vencimiento no puede ser anterior a la de emisión.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/drivers/d-1/licenses')).toHaveLength(0)
  })

  it('un error del servidor sobre licenseNumber sale bajo «Número de licencia»; el modal sigue abierto', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await licenses()
    await user.click(screen.getByRole('button', { name: 'Agregar licencia' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar licencia' })
    await user.selectOptions(within(dialog).getByLabelText(/^Clase de licencia/), 'Regular')
    await user.type(within(dialog).getByLabelText(/^Número de licencia/), 'X')
    mock.override = ({ method, url }) =>
      method === 'POST' && url.pathname === '/api/v1/drivers/d-1/licenses'
        ? problem(400, 'El número de licencia admite como máximo 60 caracteres.', 'validation', { licenseNumber: ['El número de licencia admite como máximo 60 caracteres.'] })
        : undefined
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    const msg = await within(dialog).findByText('El número de licencia admite como máximo 60 caracteres.')
    expect(msg).toHaveClass('ferr')
    expect(screen.getByRole('dialog', { name: 'Agregar licencia' })).toBeInTheDocument()
  })

  it('edita: PATCH solo con lo cambiado; vaciar el vencimiento manda clearExpiryDate; sin cambios no llama', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const lic = await licenses()
    await user.click(within(rowOf(lic, /L-100/)).getByRole('button', { name: 'Editar' }))
    let dialog = await screen.findByRole('dialog', { name: 'Editar licencia' })
    expect(within(dialog).getByLabelText(/^Número de licencia/)).toHaveValue('L-100')
    await user.clear(within(dialog).getByLabelText('Vencimiento'))
    await typeDate(user, within(dialog).getByLabelText('Vencimiento'), '2029-01-01')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/drivers/d-1/licenses/1')).toHaveLength(1))
    expect(callsTo('PATCH', '/api/v1/drivers/d-1/licenses/1')[0].body).toEqual({ expiryDate: '2029-01-01' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar licencia' })).toBeNull())

    await user.click(within(rowOf(await licenses(), /L-100/)).getByRole('button', { name: 'Editar' }))
    dialog = await screen.findByRole('dialog', { name: 'Editar licencia' })
    await user.clear(within(dialog).getByLabelText('Vencimiento'))
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('PATCH', '/api/v1/drivers/d-1/licenses/1')).toHaveLength(2))
    expect(callsTo('PATCH', '/api/v1/drivers/d-1/licenses/1')[1].body).toEqual({ clearExpiryDate: true })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar licencia' })).toBeNull())

    await user.click(within(rowOf(await licenses(), /B-7/)).getByRole('button', { name: 'Editar' }))
    dialog = await screen.findByRole('dialog', { name: 'Editar licencia' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Editar licencia' })).toBeNull())
    expect(callsTo('PATCH', '/api/v1/drivers/d-1/licenses/2')).toHaveLength(0)
  })

  it('quitar pide confirmar y manda deactivate (nunca DELETE); un error se muestra en el diálogo', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const lic = await licenses()
    await user.click(within(rowOf(lic, /R-1/)).getByRole('button', { name: 'Quitar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Quitar licencia' })
    expect(within(dialog).getByText('Se quita la licencia Regular (R-1) del chofer. Queda en el historial.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/drivers/d-1/licenses/3/deactivate')).toHaveLength(0)
    mock.override = ({ method, url }) => (method === 'POST' && url.pathname.endsWith('/licenses/3/deactivate') ? problem(404, 'Licencia no encontrada.', 'not_found') : undefined)
    await user.click(within(dialog).getByRole('button', { name: 'Quitar' }))
    expect(await within(dialog).findByText('Licencia no encontrada.')).toBeInTheDocument()
    mock.override = null
    await user.click(within(dialog).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/licenses/3/deactivate').length).toBeGreaterThan(0))
    expect(callsTo('DELETE', '/api/v1/drivers/d-1/licenses/3')).toHaveLength(0)
  })

  it('certificación: el número es opcional; edita y quita por su propia ruta', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await certs()
    await user.click(screen.getByRole('button', { name: 'Agregar certificación' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar certificación' })
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    expect(await within(dialog).findByText('El tipo de certificación es obligatorio.')).toBeInTheDocument()
    await user.selectOptions(within(dialog).getByLabelText(/^Tipo de certificación/), 'Refrigerado')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/certifications')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/drivers/d-1/certifications')[0].body).toEqual({ certType: 'REEFER', certNumber: '' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Agregar certificación' })).toBeNull())

    await user.click(within(rowOf(await certs(), /H-1/)).getByRole('button', { name: 'Quitar' }))
    const confirm = await screen.findByRole('dialog', { name: 'Quitar certificación' })
    await user.click(within(confirm).getByRole('button', { name: 'Quitar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/certifications/1/deactivate')).toHaveLength(1))
  })

  it('sin fleet.manage solo se consulta; con el chofer eliminado tampoco hay botones', async () => {
    const first = wrap(['fleet.view'])
    const lic = await licenses()
    expect(within(lic).getAllByRole('row').length).toBeGreaterThan(1)
    expect(screen.queryByRole('button', { name: 'Agregar licencia' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Agregar certificación' })).toBeNull()
    expect(within(lic).queryByRole('button', { name: 'Editar' })).toBeNull()
    first.unmount()
    // el chofer eliminado (d-4) trae isTerminal: ninguna escritura
    wrap(ALL, '/catalog/drivers?driver=d-4')
    await screen.findByText('El chofer fue eliminado; solo se consulta su historial.')
    expect(screen.queryByRole('button', { name: 'Agregar licencia' })).toBeNull()
  })
})

describe('Dispositivos', () => {
  it('lista plataforma, versión, última conexión y si tiene notificaciones; nunca un token', async () => {
    wrap(ALL)
    const panel = (await screen.findByRole('heading', { level: 2, name: 'Dispositivos' })).closest('section') as HTMLElement
    const row = await within(panel).findByRole('row', { name: /Android/ })
    expect(within(row).getByText('1.2.0')).toBeInTheDocument()
    expect(within(row).getByText('Activas')).toBeInTheDocument()
    expect(panel.textContent).not.toMatch(/token/i)
  })

  it('desactivar pide confirmar y manda POST deactivate; sin fleet.manage no hay botón', async () => {
    const user = userEvent.setup()
    const first = wrap(ALL)
    const panel = (await screen.findByRole('heading', { level: 2, name: 'Dispositivos' })).closest('section') as HTMLElement
    await user.click(await within(panel).findByRole('button', { name: 'Desactivar' }))
    const dialog = await screen.findByRole('dialog', { name: 'Desactivar dispositivo' })
    expect(callsTo('POST', '/api/v1/drivers/d-1/devices/1/deactivate')).toHaveLength(0)
    await user.click(within(dialog).getByRole('button', { name: 'Desactivar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/drivers/d-1/devices/1/deactivate')).toHaveLength(1))
    first.unmount()
    wrap(['fleet.view'])
    const panel2 = (await screen.findByRole('heading', { level: 2, name: 'Dispositivos' })).closest('section') as HTMLElement
    await within(panel2).findByRole('row', { name: /Android/ })
    expect(within(panel2).queryByRole('button', { name: 'Desactivar' })).toBeNull()
  })

  it('sin dispositivos explica que los registra la app del chofer', async () => {
    wrap(ALL, '/catalog/drivers?driver=d-2')
    expect(await screen.findByText('Este chofer no tiene dispositivos. Los registra la app del chofer.')).toBeInTheDocument()
  })
})

describe('Contactos, historial y campos personalizados', () => {
  it('Contactos usa la entidad DRIVER: lee y escribe en /contacts/DRIVER/{id} (contacts.manage y fleet.manage)', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('heading', { level: 2, name: 'Teléfonos y correos' })
    await waitFor(() => expect(callsTo('GET', '/api/v1/contacts/DRIVER/1').length).toBeGreaterThan(0))
    expect(screen.getByText('Este chofer no tiene teléfonos ni correos.')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Agregar teléfono' }))
    const dialog = await screen.findByRole('dialog', { name: 'Agregar teléfono' })
    await user.type(within(dialog).getByLabelText(/^Teléfono/), '7875550142')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/contacts/DRIVER/1')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/contacts/DRIVER/1')[0].body).toMatchObject({ contactType: 'PHONE', value: '7875550142' })
  })

  it('sin contacts.manage o con el chofer eliminado el panel es de lectura', async () => {
    const first = wrap(['fleet.view', 'fleet.manage'])
    await screen.findByRole('heading', { level: 2, name: 'Teléfonos y correos' })
    expect(screen.queryByRole('button', { name: 'Agregar teléfono' })).toBeNull()
    first.unmount()
    wrap(ALL, '/catalog/drivers?driver=d-4')
    await screen.findByRole('heading', { level: 2, name: 'Teléfonos y correos' })
    expect(screen.queryByRole('button', { name: 'Agregar teléfono' })).toBeNull()
  })

  it('Historial de estatus lee el historial del chofer (entidad DRIVER)', async () => {
    wrap(ALL)
    await screen.findByRole('heading', { level: 2, name: 'Historial de estatus' })
    await waitFor(() => expect(callsTo('GET', '/api/v1/status/history/DRIVER/1').length).toBeGreaterThan(0))
    expect(await screen.findByText('Todavía no hay cambios de estatus.')).toBeInTheDocument()
  })

  it('un cambio de estatus vuelve a leer el historial', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByText('Todavía no hay cambios de estatus.')
    const before = callsTo('GET', '/api/v1/status/history/DRIVER/1').length
    await user.click(await screen.findByRole('button', { name: 'No disponible' }))
    await waitFor(() => expect(callsTo('GET', '/api/v1/status/history/DRIVER/1').length).toBeGreaterThan(before))
  })

  it('Campos personalizados solo aparece si la compañía definió alguno para DRIVER', async () => {
    mock.override = ({ method, url }) =>
      method === 'GET' && url.pathname === '/api/v1/custom-fields/definitions'
        ? [{ id: 1, entityType: 'DRIVER', fieldKey: 'placa', label: 'Placa asignada', dataType: 'TEXT', isRequired: false, isActive: true, sortOrder: 1 }]
        : undefined
    wrap(ALL)
    expect(await screen.findByRole('heading', { level: 2, name: 'Campos personalizados' })).toBeInTheDocument()
    expect(callsTo('GET', '/api/v1/custom-fields/definitions').some((c) => c.url.searchParams.get('entityType') === 'DRIVER')).toBe(true)
  })

  it('los dispositivos salen de la ficha del chofer: no se pide /devices aparte', async () => {
    wrap(FLEET)
    await screen.findByRole('heading', { level: 2, name: 'Dispositivos' })
    expect(callsTo('GET', '/api/v1/drivers/d-1/devices')).toHaveLength(0)
  })
})

describe('Avisos al chofer', () => {
  const noticePanel = async () => (await screen.findByRole('heading', { level: 2, name: 'Avisos al chofer' })).closest('section') as HTMLElement

  it('con trips.dispatch escribe un aviso: POST con el título y el texto, y el formulario se limpia', async () => {
    const user = userEvent.setup()
    wrap([...ALL, 'trips.dispatch'])
    const panel = await noticePanel()
    await user.type(within(panel).getByLabelText(/^Título/), ' Cambio de ruta ')
    await user.type(within(panel).getByLabelText(/^Texto/), 'Llame a la oficina')
    await user.click(within(panel).getByRole('button', { name: 'Enviar aviso' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/driver-notices')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/driver-notices')[0].body).toMatchObject({ driverPublicId: 'd-1', title: 'Cambio de ruta', body: 'Llame a la oficina' })
    await waitFor(() => expect(within(panel).getByLabelText(/^Título/)).toHaveValue(''))
  })

  it('sin título no se envía, y sin trips.dispatch el panel no aparece', async () => {
    const user = userEvent.setup()
    const first = wrap([...ALL, 'trips.dispatch'])
    const panel = await noticePanel()
    await user.click(within(panel).getByRole('button', { name: 'Enviar aviso' }))
    expect(await within(panel).findByText('Indique el título del aviso.')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/driver-notices')).toHaveLength(0)
    first.unmount()
    wrap(ALL)
    await screen.findByRole('heading', { level: 2, name: 'Dispositivos' })
    expect(screen.queryByRole('heading', { level: 2, name: 'Avisos al chofer' })).not.toBeInTheDocument()
  })
})
