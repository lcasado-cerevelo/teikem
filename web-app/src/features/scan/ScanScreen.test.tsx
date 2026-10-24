// Lote F-E1 — Estación de escaneo (salida) sobre un fetch simulado: cada resultado del servidor con su mensaje exacto y su texto de «qué hacer»,
// Enter y botón «Registrar», código vacío o largo (sin llamar), 400 del servidor, ráfagas en orden, duplicados, lista de la sesión, medidor,
// «Pendientes» con sus filtros, la ventana del envío, voz y permisos (con y sin trips.view / fleet.view / clients.read / orders.view).
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { tenantToday } from '../../kernel/api/tenantZone'
import { setLang } from '../../kernel/i18n/i18n'
import { assigned, callsTo, mock, P1, P2, problem, resetMock } from './scanMock'
import ScanScreen from './ScanScreen'
import { resetScanSession } from './scanSession'
import { shownDate } from '../../kernel/ui/typeDate'

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const kit = await import('./scanMock')
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch: kit.stubFetch }) }
})

const SCAN = ['trips.scan']
const ALL = ['trips.scan', 'trips.view', 'fleet.view', 'clients.read', 'orders.view']
const MODULES = ['LTL_GROUND', 'CATALOG']

function Where() {
  return <span data-testid="where">{useLocation().pathname}</span>
}

function wrap(permissions: string[], modules = MODULES) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={['/ops/scan']}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={modules}>
          <Routes>
            <Route path="/ops/scan" element={<ScanScreen />} />
            <Route path="/orders/:publicId" element={<Where />} />
          </Routes>
          <Where />
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const input = () => screen.getByRole('textbox', { name: 'Código a escanear' })
const result = () => screen.getByRole('status', { name: 'Resultado del último escaneo' })
const posts = () => callsTo('POST', '/api/v1/scan/action')
const feed = () => screen.getByRole('list', { name: 'Escaneados en esta sesión' })

/** Escribe el código y pulsa Enter, como el lector. */
async function scanCode(user: ReturnType<typeof userEvent.setup>, code: string) {
  await user.type(input(), `${code}{Enter}`)
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  resetScanSession()
  resetMock()
  window.localStorage.clear()
})
afterEach(() => vi.unstubAllGlobals())

describe('Estación de escaneo — pantalla', () => {
  it('Lote 112: «Devolver» solo se ofrece con el permiso orders.return', () => {
    wrap(ALL)
    expect(screen.queryByRole('radio', { name: 'Devolver' })).toBeNull()
  })

  it('Lote 112: con orders.return aparece la acción «Devolver»', () => {
    wrap([...ALL, 'orders.return'])
    expect(screen.getByRole('radio', { name: 'Devolver' })).toBeInTheDocument()
  })

  it('título, las cuatro acciones (Enrutar por omisión), entrada con foco y avisos; sin modos que el servidor no tiene', async () => {
    wrap(ALL)
    expect(screen.getByRole('heading', { level: 1, name: 'Estación de escaneo' })).toBeInTheDocument()
    expect(input()).toHaveFocus()
    const actions = within(screen.getByRole('radiogroup', { name: 'Acción del escaneo' }))
    for (const name of ['Recoger', 'Llegó', 'Enrutar', 'Salir']) expect(actions.getByRole('radio', { name })).toBeInTheDocument()
    expect(actions.getByRole('radio', { name: 'Enrutar' })).toBeChecked()
    for (const fake of [/Inbound|Entrada/, /Procesamiento/, /COD/]) expect(screen.queryByRole('button', { name: fake })).toBeNull()
    // Lote F-G2: el «Informe de faltantes» ya existe (con trips.view)
    expect(screen.getByRole('button', { name: 'Informe de faltantes' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Registrar' })).toBeInTheDocument()
    expect(result()).toHaveTextContent('Listo para escanear')
    expect(screen.getByText(/se pierde al recargar la página/)).toBeInTheDocument()
    expect(screen.getByText('Todavía nada bajo este filtro — escanee el primero')).toBeInTheDocument()
  })

  it('FOUND_ASSIGNED: Enter manda {code, planDate = hoy} con Idempotency-Key, muestra el mensaje exacto, limpia, vuelve el foco y deja la fila en la sesión', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByText('OT-0003')
    mock.override = (c) => {
      if (c.method === 'POST') mock.pending = mock.pending.filter((o) => o.publicId !== 'o-1')
      return undefined
    }
    mock.scans['OT-0001'] = assigned('o-1', 'OT-0001')
    await scanCode(user, 'OT-0001')
    await waitFor(() => expect(result()).toHaveTextContent('Asignada a la ruta R-0001.'))
    expect(posts()).toHaveLength(1)
    expect(posts()[0].body).toEqual({ action: 'ROUTE', code: 'OT-0001', planDate: tenantToday() })
    expect(posts()[0].headers.get('Idempotency-Key')).toMatch(/[0-9a-f-]{36}/)
    expect(result()).toHaveTextContent('Asignada')
    expect(result()).toHaveTextContent('Encontrada por número de envío.')
    expect(result()).toHaveTextContent('Zona R-01')
    expect(input()).toHaveValue('')
    expect(input()).toHaveFocus()
    // la fila de la sesión trae el cliente y el pueblo que se sabían de «Pendientes» (el envío ya salió de la lista)
    const row = within(feed()).getByRole('button', { name: 'Ver el envío OT-0001' })
    expect(row).toHaveTextContent('Asignada')
    expect(row).toHaveTextContent('Advance Solutions')
    expect(row).toHaveTextContent('Bayamón')
    expect(row).toHaveTextContent('R-0001')
    // la lista de pendientes se refresca y el medidor dice «1 de 3»
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Ver el envío OT-0001', hidden: false })?.closest('.chipsgrid')).toBeNull())
    await waitFor(() => expect(callsTo('GET', '/api/v1/trips/unassigned-orders').length).toBeGreaterThan(1))
    await waitFor(() => expect(screen.getByText('de 3')).toBeInTheDocument())
    expect(screen.getByRole('progressbar', { name: 'Avance de la sesión' })).toHaveAttribute('aria-valuenow', '33')
  })

  it('la acción escogida va en cada escaneo (Llegó → ARRIVE) y se queda escogida', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    await user.click(screen.getByRole('radio', { name: 'Llegó' }))
    expect(screen.getByRole('radio', { name: 'Llegó' })).toBeChecked()
    expect(screen.getByTestId('scan-action-help')).toHaveTextContent('Llegó al almacén')
    mock.scans['L1'] = assigned('o-1', 'OT-0001')
    await scanCode(user, 'L1')
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body?.action).toBe('ARRIVE')
    await user.click(screen.getByRole('radio', { name: 'Salir' }))
    await scanCode(user, 'L1')
    await waitFor(() => expect(posts()).toHaveLength(2))
    expect(posts()[1].body?.action).toBe('DEPART')
  })

  it('Lote 110: muestra «N de M piezas», los faltantes y el botón «Cerrar conteo» que llama al servidor', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    mock.scans['PZ-1'] = { ...assigned('o-1', 'OT-0001'), outcome: 'COUNTED', scanned: 4, total: 5, missing: 1, pieceNumber: 'PZ-1' } as never
    await scanCode(user, 'PZ-1')
    await waitFor(() => expect(screen.getByTestId('scan-count')).toHaveTextContent('4 de 5 piezas'))
    expect(screen.getByTestId('scan-count')).toHaveTextContent('faltan por verificar: 1')
    await user.click(screen.getByRole('button', { name: 'Cerrar conteo' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/scan/close-count')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/scan/close-count')[0].body).toEqual({ orderPublicId: 'o-1' })
  })

  it('Lote 111b: «Cerrar conteo» muestra las piezas sin escanear y el envío nuevo de las que aparecieron, y solo se hace al confirmar', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    mock.reappeared = true
    mock.scans['PZ-1'] = { ...assigned('o-1', 'OT-0001'), outcome: 'COUNTED', scanned: 4, total: 5, missing: 1, pieceNumber: 'PZ-1' } as never
    await scanCode(user, 'PZ-1')
    await user.click(await screen.findByRole('button', { name: 'Cerrar conteo' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cerrar el conteo de OT-0001' })
    expect(within(dialog).getByText(/envío nuevo con 1 pieza\(s\) que aparecieron del envío OT-0001/)).toBeInTheDocument()
    expect(within(dialog).getByTestId('reappeared-pieces')).toHaveTextContent('PZ-9')
    expect(within(dialog).getByTestId('missing-pieces')).toHaveTextContent('PZ-5')
    expect(callsTo('POST', '/api/v1/scan/close-count')[0].body).toEqual({ orderPublicId: 'o-1' })
    await user.click(within(dialog).getByRole('button', { name: 'Confirmar y cerrar el conteo' }))
    await waitFor(() => expect(callsTo('POST', '/api/v1/scan/close-count')).toHaveLength(2))
    expect(callsTo('POST', '/api/v1/scan/close-count')[1].body).toEqual({ orderPublicId: 'o-1', confirm: true })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Cerrar el conteo de OT-0001' })).toBeNull())
  })

  it('el botón «Registrar» hace lo mismo que Enter y las lecturas llevan cada una su Idempotency-Key', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    mock.scans['A1'] = assigned('o-1', 'OT-0001')
    await user.type(input(), '  A1  ')
    await user.click(screen.getByRole('button', { name: 'Registrar' }))
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body?.code).toBe('A1') // recortado
    await waitFor(() => expect(input()).toHaveFocus())
    await scanCode(user, 'A1')
    await waitFor(() => expect(posts()).toHaveLength(2))
    expect(posts()[0].headers.get('Idempotency-Key')).not.toBe(posts()[1].headers.get('Idempotency-Key'))
  })

  it('ALREADY_ASSIGNED: «Ya estaba…», chip propio, voz «Ok» y el medidor no cuenta de nuevo', async () => {
    const user = userEvent.setup()
    const speak = installVoice()
    wrap(ALL)
    mock.scans['OT-0001'] = assigned('o-1', 'OT-0001')
    await scanCode(user, 'OT-0001')
    await waitFor(() => expect(result()).toHaveTextContent('Asignada a la ruta'))
    mock.scans['OT-0001'] = { outcome: 'ALREADY_ASSIGNED', voice: 'dup', message: 'Ya estaba en la ruta R-0001.', matchedBy: 'ORDER_NUMBER', orderPublicId: 'o-1', orderNumber: 'OT-0001', tripCode: 'R-0001', reasonCode: 'IN_TRIP' }
    await scanCode(user, 'OT-0001')
    await waitFor(() => expect(result()).toHaveTextContent('Ya estaba en la ruta R-0001.'))
    expect(result()).toHaveTextContent('No hace falta hacer nada')
    expect(within(feed()).getAllByRole('button', { name: 'Ver el envío OT-0001' })).toHaveLength(2)
    expect(speak.map((u) => u.text)).toEqual(['Sí', 'Ok'])
    expect(screen.getByRole('progressbar', { name: 'Avance de la sesión' }).closest('.scanmeter')?.querySelector('b')).toHaveTextContent('1')
  })

  it.each([
    ['NOT_FOUND', 'NO_MATCH', 'No se encontró el envío.', 'Revise el código'],
    ['NOT_FOUND', 'MULTIPLE_MATCHES', 'Hay varias envíos con ese código; escanee el empaque.', 'Escanee el número del empaque'],
    ['NOT_ELIGIBLE', 'NOT_ELIGIBLE', "El estatus actual no permite la acción 'ASSIGN_TRIP'.", 'Arregle el estatus del envío'],
    ['FOUND_UNASSIGNED', 'NO_ZONE', 'No se pudo resolver la zona de despacho por código postal ni pueblo; queda sin asignar.', 'Agregue el código postal o el pueblo a una zona'],
    ['FOUND_UNASSIGNED', 'AMBIGUOUS_ZONE', 'El código postal o pueblo pertenece a varias zonas (R-01, R-02); queda sin asignar.', 'está en más de una zona'],
    ['FOUND_UNASSIGNED', 'NO_OPEN_ROUTE', 'No hay ruta abierta para la zona R-01 en la fecha 2026-10-12; queda sin asignar.', 'cree la ruta de esa zona en Sala de despacho'],
  ])('%s / %s: el mensaje exacto del servidor y qué hacer', async (outcome, reasonCode, message, hint) => {
    const user = userEvent.setup()
    const speak = installVoice()
    wrap(SCAN)
    const voice = outcome === 'FOUND_UNASSIGNED' ? 'found' : 'notfound'
    mock.scans['X-1'] = { outcome, voice, message, reasonCode, orderPublicId: outcome === 'NOT_FOUND' ? null : 'o-9', orderNumber: outcome === 'NOT_FOUND' ? null : 'OT-9' }
    await scanCode(user, 'X-1')
    await waitFor(() => expect(result()).toHaveTextContent(message))
    expect(result()).toHaveTextContent(hint)
    expect(speak.at(-1)?.text).toBe(voice === 'found' ? 'Sí' : 'No')
    expect(within(feed()).getByText(message)).toBeInTheDocument()
    // lo no encontrado no abre ventana; lo demás sí
    expect(within(feed()).queryAllByRole('button')).toHaveLength(outcome === 'NOT_FOUND' ? 0 : 1)
  })

  it('código vacío, de solo espacios o de más de 40 caracteres: avisa con el mensaje del servidor y NO llama', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    await user.click(screen.getByRole('button', { name: 'Registrar' }))
    expect(result()).toHaveTextContent('Escanee o escriba un código.')
    await user.type(input(), '   {Enter}')
    expect(result()).toHaveTextContent('Escanee o escriba un código.')
    await user.type(input(), `${'A'.repeat(41)}{Enter}`)
    expect(result()).toHaveTextContent('El código no puede exceder 40 caracteres.')
    expect(posts()).toHaveLength(0)
    expect(input()).toHaveFocus()
    expect(screen.queryByRole('list', { name: 'Escaneados en esta sesión' })).toBeNull()
  })

  it('un 400 del servidor se muestra con su mensaje (campo code) y no entra a la lista', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    mock.scans['MALO'] = problem(400, 'Hay errores de validación.', 'validation', { code: ['El código no puede exceder 40 caracteres.'] })
    await scanCode(user, 'MALO')
    await waitFor(() => expect(result()).toHaveTextContent('No se registró'))
    expect(result()).toHaveTextContent('El código no puede exceder 40 caracteres.')
    expect(screen.getByText('Todavía nada bajo este filtro — escanee el primero')).toBeInTheDocument()
    expect(input()).toHaveFocus()
  })

  it('un error de red o 500 se muestra y la estación sigue lista para el siguiente', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    mock.scans['CAE'] = problem(500, 'Ocurrió un error. Intente de nuevo.', 'internal')
    await scanCode(user, 'CAE')
    await waitFor(() => expect(result()).toHaveTextContent('Ocurrió un error. Intente de nuevo.'))
    mock.scans['OK'] = assigned('o-1', 'OT-0001')
    await scanCode(user, 'OK')
    await waitFor(() => expect(result()).toHaveTextContent('Asignada a la ruta R-0001.'))
  })

  it('una ráfaga del lector se registra en orden: cada lectura espera a la anterior y la lista queda con la última arriba', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    mock.scans['A'] = assigned('o-1', 'OT-0001', 'R-0001')
    mock.scans['B'] = assigned('o-2', 'OT-0002', 'R-0002')
    mock.scans['C'] = assigned('o-3', 'OT-0003', 'R-0003')
    await user.type(input(), 'A{Enter}B{Enter}C{Enter}')
    await waitFor(() => expect(posts()).toHaveLength(3))
    expect(posts().map((c) => c.body?.code)).toEqual(['A', 'B', 'C'])
    await waitFor(() => expect(within(feed()).getAllByRole('button')).toHaveLength(3))
    expect(within(feed()).getAllByRole('button').map((b) => b.getAttribute('aria-label'))).toEqual(['Ver el envío OT-0003', 'Ver el envío OT-0002', 'Ver el envío OT-0001'])
  })

  it('la sesión sobrevive a abrir otra pantalla y volver (se pierde solo al recargar o al cambiar de usuario o compañía)', async () => {
    const user = userEvent.setup()
    const first = wrap(SCAN)
    mock.scans['OT-0001'] = assigned('o-1', 'OT-0001')
    await scanCode(user, 'OT-0001')
    await within(feed()).findByRole('button', { name: 'Ver el envío OT-0001' })
    first.unmount()
    wrap(SCAN)
    expect(within(feed()).getByRole('button', { name: 'Ver el envío OT-0001' })).toBeInTheDocument()
    expect(input()).toHaveFocus()
    expect(screen.getByText('1', { selector: '.scanmeter b' })).toBeInTheDocument()
    resetScanSession() // recargar la página
    first.unmount()
  })

  it('«Ruta del día»: elegir otra fecha la manda como planDate y «Hoy» vuelve a la fecha de hoy', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    const date = screen.getByLabelText('Ruta del día')
    expect(date).toHaveValue(shownDate(tenantToday()))
    fireEvent.change(date, { target: { value: '2030-01-15' } })
    expect(date).toHaveValue('01/15/2030')
    expect(screen.getByText(/para el/)).toBeInTheDocument()
    await scanCode(user, 'Z1')
    await waitFor(() => expect(posts()).toHaveLength(1))
    expect(posts()[0].body?.planDate).toBe('2030-01-15')
    await user.click(screen.getByRole('button', { name: 'Hoy' }))
    expect(date).toHaveValue(shownDate(tenantToday()))
    await scanCode(user, 'Z2')
    await waitFor(() => expect(posts()).toHaveLength(2))
    expect(posts()[1].body?.planDate).toBe(tenantToday())
  })
})

describe('Estación de escaneo — lista de la sesión y ventana del envío', () => {
  it('el buscador filtra lo escaneado por referencia, resultado o mensaje; una fila abre la ventana con la ficha y «Abrir» lleva al expediente', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByText('OT-0002')
    mock.scans['OT-0001'] = assigned('o-1', 'OT-0001')
    await scanCode(user, 'OT-0001')
    await scanCode(user, 'NOEXISTE')
    await waitFor(() => expect(within(feed()).getByText('NOEXISTE')).toBeInTheDocument())
    await user.type(screen.getByRole('searchbox', { name: 'Buscar en lo escaneado…' }), 'no encontrada')
    expect(within(feed()).queryByText('OT-0001')).toBeNull()
    expect(within(feed()).getByText('NOEXISTE')).toBeInTheDocument()
    await user.clear(screen.getByRole('searchbox', { name: 'Buscar en lo escaneado…' }))
    await user.click(within(feed()).getByRole('button', { name: 'Ver el envío OT-0001' }))
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText('Taller Díaz')).toBeInTheDocument()
    expect(within(dialog).getByText('Estándar')).toBeInTheDocument()
    expect(within(dialog).getByText('3 cajas · 3 piezas')).toBeInTheDocument()
    expect(within(dialog).getByText('$125.50')).toBeInTheDocument()
    expect(within(dialog).getByText('R-0001')).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Abrir' }))
    expect(screen.getAllByTestId('where').at(-1)).toHaveTextContent('/orders/o-1')
  })

  it('sin orders.view la ventana muestra lo que ya se sabía, no pide la ficha y no pinta «Abrir»', async () => {
    const user = userEvent.setup()
    wrap(['trips.scan', 'trips.view'])
    await screen.findByText('OT-0001')
    mock.scans['OT-0001'] = assigned('o-1', 'OT-0001')
    await scanCode(user, 'OT-0001')
    await user.click(await within(feed()).findByRole('button', { name: 'Ver el envío OT-0001' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Advance Solutions')).toBeInTheDocument()
    expect(within(dialog).getByText('Taller Díaz')).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Abrir' })).toBeNull()
    expect(callsTo('GET', '/api/v1/orders/o-1')).toHaveLength(0)
    await user.click(within(dialog).getAllByRole('button', { name: 'Cerrar' }).at(-1)!)
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    await waitFor(() => expect(input()).toHaveFocus())
  })

  it('con orders.view y el envío fuera de «Pendientes», la ficha completa cliente y pueblo de la fila', async () => {
    const user = userEvent.setup()
    wrap(['trips.scan', 'orders.view'])
    mock.scans['OT-0001'] = assigned('o-1', 'OT-0001')
    await scanCode(user, 'OT-0001')
    const row = await within(feed()).findByRole('button', { name: 'Ver el envío OT-0001' })
    await waitFor(() => expect(row).toHaveTextContent('Advance Solutions'))
    expect(row).toHaveTextContent('Taller Díaz')
  })

  it('si la ficha falla, la ventana lo dice y sigue mostrando lo conocido', async () => {
    const user = userEvent.setup()
    wrap(['trips.scan', 'trips.view', 'orders.view'])
    await screen.findByText('OT-0002')
    mock.orders = {}
    await user.click(screen.getByRole('button', { name: 'Ver el envío OT-0002' }))
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText(/No se pudo leer la ficha del envío/)).toBeInTheDocument()
    expect(within(dialog).getByText('Ferretería Sol')).toBeInTheDocument()
  })
})

describe('Estación de escaneo — pendientes y filtros', () => {
  it('muestra los pendientes con referencia · cliente · pueblo CP · zona, el contador y el buscador libre', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    const grid = await screen.findByRole('list', { name: 'Pendientes por escanear' })
    expect(within(grid).getAllByRole('button')).toHaveLength(3)
    expect(within(grid).getByRole('button', { name: 'Ver el envío OT-0001' })).toHaveTextContent('Advance Solutions · Bayamón 00959 · R-01')
    expect(within(grid).getByRole('button', { name: 'Ver el envío OT-0003' })).toHaveTextContent('Sin zona')
    const first = callsTo('GET', '/api/v1/trips/unassigned-orders')[0].url.searchParams
    expect(first.get('Take')).toBe('500')
    expect(first.get('Skip')).toBe('0')
    await user.type(screen.getByRole('searchbox', { name: 'Buscar en los pendientes…' }), 'caguas')
    expect(within(grid).getAllByRole('button')).toHaveLength(1)
    await user.clear(screen.getByRole('searchbox', { name: 'Buscar en los pendientes…' }))
    await user.type(screen.getByRole('searchbox', { name: 'Buscar en los pendientes…' }), 'zzz')
    expect(await screen.findByText('Ningún envío pendiente con esa búsqueda o esos filtros')).toBeInTheDocument()
  })

  it('un pendiente abre la ventana del envío', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await user.click(await screen.findByRole('button', { name: 'Ver el envío OT-0001' }))
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText('$125.50')).toBeInTheDocument()
  })

  it('Zona, Sin zona, Código postal, Pueblo y Cliente van al servidor; «Limpiar» los quita', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByRole('list', { name: 'Pendientes por escanear' })
    const last = () => callsTo('GET', '/api/v1/trips/unassigned-orders').at(-1)!.url.searchParams
    const grid = () => screen.getByRole('list', { name: 'Pendientes por escanear' })

    await user.selectOptions(screen.getByLabelText('Zona'), 'R-02 · Caguas · Aguas Buenas')
    await waitFor(() => expect(last().get('DispatchZoneId')).toBe('2'))
    await waitFor(() => expect(within(grid()).getAllByRole('button')).toHaveLength(1))

    await user.selectOptions(screen.getByLabelText('Zona'), 'Sin zona')
    await waitFor(() => expect(last().get('NoZone')).toBe('true'))
    expect(last().get('DispatchZoneId')).toBeNull()
    await waitFor(() => expect(within(grid()).getByRole('button')).toHaveTextContent('OT-0003'))

    await user.selectOptions(screen.getByLabelText('Zona'), 'Todos')
    await user.type(screen.getByLabelText('Código postal'), '009')
    await waitFor(() => expect(last().get('PostalCode')).toBe('009'))
    await waitFor(() => expect(within(grid()).getAllByRole('button')).toHaveLength(1))
    await user.clear(screen.getByLabelText('Código postal'))
    await user.type(screen.getByLabelText('Pueblo'), 'vieques')
    await waitFor(() => expect(last().get('City')).toBe('vieques'))
    await waitFor(() => expect(within(grid()).getByRole('button')).toHaveTextContent('OT-0003'))
    await user.clear(screen.getByLabelText('Pueblo'))

    await user.click(screen.getByRole('combobox', { name: 'Cliente' }))
    await user.click(await screen.findByRole('option', { name: /Truss Puerto Rico/ }))
    await waitFor(() => expect(last().get('ClientPublicId')).toBe('c-2'))
    await waitFor(() => expect(within(grid()).getByRole('button')).toHaveTextContent('OT-0002'))

    await user.click(screen.getByRole('button', { name: 'Limpiar' }))
    await waitFor(() => expect(last().get('ClientPublicId')).toBeNull())
    await waitFor(() => expect(within(grid()).getAllByRole('button')).toHaveLength(3))
    expect(screen.queryByRole('button', { name: 'Limpiar' })).toBeNull()
  })

  it('el filtro también acota lo escaneado y el medidor: «N de M» cuenta solo lo de ese filtro', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByText('OT-0002')
    mock.override = (c) => {
      if (c.method === 'POST') mock.pending = mock.pending.filter((o) => o.publicId !== 'o-1')
      return undefined
    }
    mock.scans['OT-0001'] = assigned('o-1', 'OT-0001')
    await scanCode(user, 'OT-0001')
    await within(feed()).findByRole('button', { name: 'Ver el envío OT-0001' })
    await user.selectOptions(screen.getByLabelText('Zona'), 'R-02 · Caguas · Aguas Buenas')
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Ver el envío OT-0001' })).toBeNull())
    const meter = screen.getByRole('progressbar', { name: 'Avance de la sesión' })
    await waitFor(() => expect(meter).toHaveAttribute('aria-valuenow', '0'))
    expect(screen.getByText('de 1')).toBeInTheDocument() // solo OT-0002 en R-02, nada asignado ahí
    await user.selectOptions(screen.getByLabelText('Zona'), 'R-01 · Toa Baja · Bayamón')
    await waitFor(() => expect(screen.getByRole('button', { name: 'Ver el envío OT-0001' })).toBeInTheDocument())
    await waitFor(() => expect(screen.getByText('de 1')).toBeInTheDocument()) // 1 asignada + 0 pendientes en R-01
    expect(meter).toHaveAttribute('aria-valuenow', '100')
  })

  it('un código que no es de ningún envío no desaparece de la sesión por un filtro', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByText('OT-0002')
    await scanCode(user, 'NOEXISTE')
    await waitFor(() => expect(within(feed()).getByText('NOEXISTE')).toBeInTheDocument())
    await user.selectOptions(screen.getByLabelText('Zona'), 'R-02 · Caguas · Aguas Buenas')
    await waitFor(() => expect(callsTo('GET', '/api/v1/trips/unassigned-orders').at(-1)!.url.searchParams.get('DispatchZoneId')).toBe('2'))
    expect(within(feed()).getByText('NOEXISTE')).toBeInTheDocument()
  })

  it('si no se pueden leer los pendientes, el panel lo dice con «Reintentar» y el escaneo sigue funcionando', async () => {
    const user = userEvent.setup()
    let fail = true
    mock.override = (c) => (fail && c.url.pathname === '/api/v1/trips/unassigned-orders' ? problem(500, 'Ocurrió un error. Intente de nuevo.', 'internal') : undefined)
    wrap(ALL)
    expect(await screen.findByText('No se pudieron cargar los envíos pendientes.')).toBeInTheDocument()
    mock.scans['OT-0001'] = assigned('o-1', 'OT-0001')
    await scanCode(user, 'OT-0001')
    await waitFor(() => expect(result()).toHaveTextContent('Asignada a la ruta R-0001.'))
    fail = false
    await user.click(screen.getByRole('button', { name: 'Reintentar' }))
    expect(await screen.findByRole('list', { name: 'Pendientes por escanear' })).toBeInTheDocument()
  })

  it('con más de 500 pendientes avisa cuántos se ven', async () => {
    mock.override = (c) =>
      c.url.pathname === '/api/v1/trips/unassigned-orders' ? { total: 731, skip: 0, take: 500, items: [P1, P2] } : undefined
    wrap(ALL)
    expect(await screen.findByText('Se muestran las primeras 2 de 731; use los filtros para ver las demás.')).toBeInTheDocument()
  })

  it('Lote F-G2: «Informe de faltantes» lee TODOS los pendientes con el filtro y abre la vista del documento', async () => {
    const user = userEvent.setup()
    wrap(ALL)
    await screen.findByText('Pendientes por escanear')
    const before = callsTo('GET', '/api/v1/trips/unassigned-orders').length
    await user.click(screen.getByRole('button', { name: 'Informe de faltantes' }))
    expect(await screen.findByRole('dialog', { name: 'Informe de faltantes' })).toHaveTextContent('El documento está listo para imprimir.')
    const read = callsTo('GET', '/api/v1/trips/unassigned-orders').slice(before)
    expect(read.at(-1)!.url.searchParams.get('Skip')).toBe('0')
    expect(read.at(-1)!.url.searchParams.get('Take')).toBe('500')
  })
})

describe('Estación de escaneo — permisos', () => {
  it('solo trips.scan: no se pide nada de pendientes, zonas ni clientes; no hay filtros ni panel; el medidor solo cuenta', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    mock.scans['OT-0001'] = assigned('o-1', 'OT-0001')
    await scanCode(user, 'OT-0001')
    await waitFor(() => expect(result()).toHaveTextContent('Asignada a la ruta R-0001.'))
    expect(screen.queryByText('Pendientes por escanear')).toBeNull()
    expect(screen.queryByRole('group', { name: 'Filtros del escaneo' })).toBeNull()
    expect(screen.queryByRole('progressbar')).toBeNull()
    expect(screen.queryByText(/^de \d/)).toBeNull()
    expect(callsTo('GET', '/api/v1/trips/unassigned-orders')).toHaveLength(0)
    expect(callsTo('GET', '/api/v1/dispatch-zones')).toHaveLength(0)
    expect(callsTo('GET', '/api/v1/clients')).toHaveLength(0)
    expect(callsTo('GET', '/api/v1/orders/o-1')).toHaveLength(0)
  })

  it('trips.view sin fleet.view ni clients.read: pendientes y filtros de CP y pueblo, pero sin Zona ni Cliente (y sin errores)', async () => {
    wrap(['trips.scan', 'trips.view'])
    await screen.findByRole('list', { name: 'Pendientes por escanear' })
    expect(screen.getByLabelText('Código postal')).toBeInTheDocument()
    expect(screen.getByLabelText('Pueblo')).toBeInTheDocument()
    expect(screen.queryByLabelText('Zona')).toBeNull()
    expect(screen.queryByRole('combobox', { name: 'Cliente' })).toBeNull()
    expect(callsTo('GET', '/api/v1/dispatch-zones')).toHaveLength(0)
    expect(screen.queryByRole('alert')).toBeNull()
  })

  it('si el servidor niega las zonas (403), el filtro Zona se oculta sin mostrar errores', async () => {
    mock.override = (c) => (c.url.pathname === '/api/v1/dispatch-zones' ? problem(403, 'Sin permiso.', 'forbidden') : undefined)
    wrap(ALL)
    await screen.findByRole('list', { name: 'Pendientes por escanear' })
    await waitFor(() => expect(callsTo('GET', '/api/v1/dispatch-zones').length).toBeGreaterThan(0))
    await waitFor(() => expect(screen.queryByLabelText('Zona')).toBeNull())
    expect(screen.getByLabelText('Pueblo')).toBeInTheDocument()
  })

  it('con el módulo CATALOG apagado no se pide zona ni cliente', async () => {
    wrap(ALL, ['LTL_GROUND'])
    await screen.findByRole('list', { name: 'Pendientes por escanear' })
    expect(callsTo('GET', '/api/v1/dispatch-zones')).toHaveLength(0)
    expect(screen.queryByLabelText('Zona')).toBeNull()
    expect(screen.queryByRole('combobox', { name: 'Cliente' })).toBeNull()
  })
})

describe('Estación de escaneo — voz', () => {
  it('activa por omisión: dice Sí / No / Ok según `voice`; el interruptor la silencia, lo recuerda y la reactiva', async () => {
    const user = userEvent.setup()
    const spoken = installVoice()
    wrap(SCAN)
    const toggle = screen.getByRole('button', { name: 'Voz activada' })
    expect(toggle).toHaveAttribute('aria-pressed', 'true')
    mock.scans['OK'] = assigned('o-1', 'OT-0001')
    await scanCode(user, 'OK')
    await scanCode(user, 'NADA')
    mock.scans['DUP'] = { outcome: 'ALREADY_ASSIGNED', voice: 'dup', message: 'Ya estaba en la ruta R-0001.', orderPublicId: 'o-1', orderNumber: 'OT-0001', tripCode: 'R-0001', reasonCode: 'IN_TRIP' }
    await scanCode(user, 'DUP')
    await waitFor(() => expect(spoken.map((u) => u.text)).toEqual(['Sí', 'No', 'Ok']))
    expect(spoken.every((u) => u.lang === 'es-PR')).toBe(true)

    await user.click(toggle)
    expect(screen.getByRole('button', { name: 'Voz silenciada' })).toHaveAttribute('aria-pressed', 'false')
    expect(window.localStorage.getItem('teikem.scan.voice')).toBe('0')
    await scanCode(user, 'OK')
    await waitFor(() => expect(posts()).toHaveLength(4))
    expect(spoken).toHaveLength(3)

    await user.click(screen.getByRole('button', { name: 'Voz silenciada' }))
    expect(window.localStorage.getItem('teikem.scan.voice')).toBe('1')
    await scanCode(user, 'OK')
    await waitFor(() => expect(spoken).toHaveLength(4))
  })

  it('una preferencia guardada («0») arranca silenciada', () => {
    installVoice()
    window.localStorage.setItem('teikem.scan.voice', '0')
    wrap(SCAN)
    expect(screen.getByRole('button', { name: 'Voz silenciada' })).toHaveAttribute('aria-pressed', 'false')
  })

  it('sin voz en el navegador el interruptor está deshabilitado y dice por qué, y el escaneo funciona', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    const toggle = screen.getByRole('button', { name: 'Voz no disponible' })
    expect(toggle).toBeDisabled()
    expect(toggle).toHaveAttribute('title', 'Este navegador no sabe hablar. Los resultados se ven en pantalla.')
    mock.scans['OK'] = assigned('o-1', 'OT-0001')
    await scanCode(user, 'OK')
    await waitFor(() => expect(result()).toHaveTextContent('Asignada a la ruta R-0001.'))
  })

  it('el error del código (largo) también dice «No»', async () => {
    const user = userEvent.setup()
    const spoken = installVoice()
    wrap(SCAN)
    await scanCode(user, 'A'.repeat(50))
    expect(spoken.map((u) => u.text)).toEqual(['No'])
  })
})

/** Voz simulada: devuelve la lista de lo que se dijo. */
function installVoice() {
  const spoken: { text: string; lang: string }[] = []
  vi.stubGlobal('speechSynthesis', { cancel: vi.fn(), speak: (u: { text: string; lang: string }) => spoken.push(u) })
  vi.stubGlobal('SpeechSynthesisUtterance', class { lang = ''; rate = 1; volume = 1; text: string; constructor(text: string) { this.text = text } })
  return spoken
}

describe('Estación de escaneo — «más adelante» y la lupa (Lote 216)', () => {
  const beyondResult = { outcome: 'BEYOND', voice: 'warn', message: 'La pieza B-1: el envío OT-0001 ya está en «En tránsito», que va después de «Sin asignar». Si sigue, el envío regresará a «Sin asignar».', orderPublicId: 'o-1', orderNumber: 'OT-0001', pieceNumber: 'B-1', scanned: 0, total: 2, reasonCode: null }

  it('si el envío ya va más adelante, el servidor para: se advierte, se dice «Atención» y no se cuenta nada hasta que la persona lo confirma', async () => {
    const user = userEvent.setup()
    const speak = installVoice()
    wrap(SCAN)
    mock.scans['B-1'] = beyondResult
    await scanCode(user, 'B-1')
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByTestId('beyond-message')).toHaveTextContent('La pieza B-1: el envío OT-0001 ya está en «En tránsito»')
    expect(speak.map((u) => u.text)).toEqual(['Atención'])
    expect(result()).toHaveTextContent('Más adelante')
    expect(posts()).toHaveLength(1)
    expect(posts()[0].body).not.toHaveProperty('confirmBeyond')

    // Regresar el estatus: el mismo escaneo, ahora con confirmBeyond; el servidor regresa el envío
    mock.scans['B-1'] = { outcome: 'APPLIED', voice: 'found', message: 'La pieza B-1 regresó el envío OT-0001 de «En tránsito» a «Sin asignar».', orderPublicId: 'o-1', orderNumber: 'OT-0001', scanned: 1, total: 2, reasonCode: null }
    await user.click(within(dialog).getByTestId('beyond-confirm'))
    await waitFor(() => expect(posts()).toHaveLength(2))
    expect(posts()[1].body).toMatchObject({ code: 'B-1', action: 'ROUTE', confirmBeyond: true })
    await waitFor(() => expect(result()).toHaveTextContent('La pieza B-1 regresó el envío OT-0001'))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(speak.map((u) => u.text)).toEqual(['Atención', 'Sí'])
  })

  it('«Cancelar» en la advertencia no hace nada más: no se manda otro escaneo', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    mock.scans['B-1'] = beyondResult
    await scanCode(user, 'B-1')
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Cancelar' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(posts()).toHaveLength(1)
  })

  it('la lupa busca una pieza en la base y muestra por qué no se encontró: cancelado, dada de baja, reclamación, ruta y escaneos', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    mock.lookups['X-2'] = {
      code: 'X-2', matchedBy: 'PIECE',
      items: [{ pieceNumber: 'X-2', pieceActive: true, missing: 'CLAIMED', claimNumber: 'RC-0001', claimStatus: 'Abierta', scans: [{ action: 'ROUTE', scannedAtUtc: '2026-10-24T12:00:00Z', byName: 'Ana' }],
        orderPublicId: 'o-9', orderNumber: 'OT-0009', waybillNumber: 'W9', orderActive: true, statusCode: 'CANCELLED', status: 'Cancelada', stageKind: 'TERMINAL', clientName: 'Advance Solutions', consigneeName: 'Taller Díaz', city: 'Bayamón',
        tripCode: 'R-0001', tripStatus: 'En curso', driverName: 'Chofer Siete', pieces: 2, flags: ['ORDER_CANCELLED', 'PIECE_CLAIMED', 'IN_ROUTE'] }],
    }
    await user.click(screen.getByRole('button', { name: 'Buscar una pieza en la base' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByRole('textbox', { name: 'Número de pieza a buscar' }), 'X-2{Enter}')
    const card = await within(dialog).findByTestId('lookup-card')
    expect(card).toHaveTextContent('Envío cancelado')
    expect(card).toHaveTextContent('En reclamación')
    expect(card).toHaveTextContent('RC-0001')
    expect(card).toHaveTextContent('Cancelada')
    expect(card).toHaveTextContent('R-0001 · En curso · Chofer Siete')
    expect(card).toHaveTextContent('Enrutar')
    expect(callsTo('GET', '/api/v1/scan/lookup')[0].url.searchParams.get('code')).toBe('X-2')
    expect(posts()).toHaveLength(0)   // buscar no es escanear: no cuenta nada
  })

  it('la lupa dice cuando no hay nada y avisa si el código no es una pieza sino un envío', async () => {
    const user = userEvent.setup()
    wrap(SCAN)
    mock.lookups['OT-5'] = { code: 'OT-5', matchedBy: 'ORDER_NUMBER', items: [{ pieceNumber: null, pieceActive: true, missing: null, scans: [], orderPublicId: 'o-5', orderNumber: 'OT-5', orderActive: false, statusCode: 'CANCELLED', status: 'Cancelada', stageKind: 'TERMINAL', pieces: 0, flags: ['ORDER_INACTIVE', 'ORDER_CANCELLED'] }] }
    await user.click(screen.getByRole('button', { name: 'Buscar una pieza en la base' }))
    const dialog = await screen.findByRole('dialog')
    const box = within(dialog).getByRole('textbox', { name: 'Número de pieza a buscar' })
    await user.type(box, 'NADA{Enter}')
    expect(await within(dialog).findByTestId('lookup-none')).toHaveTextContent('No hay nada en la base con «NADA».')
    await user.clear(box)
    await user.type(box, 'OT-5{Enter}')
    expect(await within(dialog).findByText('No es una pieza: coincide con el envío por número de envío.')).toBeInTheDocument()
    expect(within(dialog).getByTestId('lookup-card')).toHaveTextContent('Envío dado de baja')
  })
})

