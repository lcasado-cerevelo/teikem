// Lote F-C1 — Envíos: lista (filas por tipo de paquete, paginación por envíos, filtros de la maqueta con arreglos repetidos,
// Limpiar, ordenar sobre todo lo filtrado), papelera y abrir, columna Cotizado según orders.view_price y el botón «Nuevo envío».
// Sobre un fetch simulado.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import OrderListScreen from './OrderListScreen'
import { typeDate } from '../../kernel/ui/typeDate'

interface Call {
  method: string
  url: URL
  body: Record<string, unknown> | undefined
}
const mock = vi.hoisted(() => ({ calls: [] as Call[], override: null as null | ((call: Call) => unknown) }))
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
    const call = { method: req.method, url: new URL(req.url), body }
    mock.calls.push(call)
    const out = mock.override?.(call) ?? route(call)
    return out instanceof Response ? out : new Response(JSON.stringify(out), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

const problem = (status: number, title: string, code: string) =>
  new Response(JSON.stringify({ title, code, status }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const O1 = {
  id: 1, publicId: 'o-1', packBatchNumber: 'EMP-00001', waybillNumber: 'CND-0000001', orderNumber: 'ORD-00001', clientInvoiceNumber: 'FAC-1', clientPublicId: 'c-1', clientName: 'Acme Corp',
  consigneeName: 'Farmacia Central', consigneeCity: 'San Juan', consigneePostalCode: '00901', serviceType: 'STANDARD', serviceTypeLabel: 'Estándar',
  totalPieces: 3, codAmount: 50, quotedAmount: 120, status: 'DRAFT', statusLabel: 'Entrada', isInitialStatus: true, isActive: true, createdAtUtc: '2026-10-10T15:00:00',
  packages: [
    { id: 1, packageType: 'BOX', packageTypeLabel: 'Caja', description: 'Ropa', pieces: 2 },
    { id: 2, packageType: 'ENV', packageTypeLabel: 'Sobre', description: '', pieces: 1 },
  ],
}
const O2 = {
  id: 2, publicId: 'o-2', packBatchNumber: 'EMP-00002', waybillNumber: 'CND-0000002', orderNumber: 'ORD-00002', clientInvoiceNumber: 'FAC-2', clientPublicId: 'c-2', clientName: 'Beta SA',
  consigneeName: 'Colmado Boricua', consigneeCity: 'Ponce', consigneePostalCode: '00716', serviceType: 'EXPRESS', serviceTypeLabel: 'Express',
  totalPieces: 1, codAmount: null, quotedAmount: 80, status: 'CONFIRMED', statusLabel: 'Confirmada', isInitialStatus: false, isActive: true, createdAtUtc: '2026-10-11T15:00:00',
  packages: [{ id: 3, packageType: 'BOX', packageTypeLabel: 'Caja', description: '', pieces: 1 }],
}
let orders = [O1, O2]

function route({ method, url }: Call): unknown {
  const p = url.pathname
  if (p === '/api/v1/orders' && method === 'GET') return { total: orders.length, items: orders }
  if (p === '/api/v1/orders/o-1' && method === 'DELETE') return new Response(null, { status: 204 })
  if (p === '/api/v1/clients') return [{ id: 1, publicId: 'c-1', code: 'ACME', name: 'Acme Corp', isActive: true }, { id: 2, publicId: 'c-2', code: 'BETA', name: 'Beta SA', isActive: true }]
  if (p === '/api/v1/catalogs/ServiceType') return [{ code: 'STANDARD', label: 'Estándar', isEnabled: true }, { code: 'EXPRESS', label: 'Express', isEnabled: true }]
  if (p === '/api/v1/catalogs/PackageType') return [{ code: 'BOX', label: 'Caja', isEnabled: true }, { code: 'ENV', label: 'Sobre', isEnabled: true }]
  if (p === '/api/v1/status/OrderStatus')
    return [
      { code: 'DRAFT', label: 'Entrada', stageKind: 'PIPELINE', isInitial: true, isEnabled: true, sortOrder: 1 },
      { code: 'CONFIRMED', label: 'Confirmada', stageKind: 'PIPELINE', isInitial: false, isEnabled: true, sortOrder: 2 },
    ]
  if (p === '/api/v1/locations') return [{ id: 10, publicId: 'l-1', name: 'Farmacia', city: 'San Juan', isActive: true }, { id: 11, publicId: 'l-2', name: 'Colmado', city: 'Ponce', isActive: true }]
  if (p === '/api/v1/tenant/settings') return {}
  if (p.startsWith('/api/v1/catalogs/') || p.startsWith('/api/v1/custom-fields/')) return []
  return problem(404, 'No encontrado', 'not_found')
}

function wrap(permissions: string[]) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <MemoryRouter initialEntries={['/orders']}>
      <QueryClientProvider client={queryClient}>
        <AccessProvider permissions={permissions} modules={['LTL_GROUND', 'CATALOG']}>
          <Routes>
            <Route path="/orders" element={<OrderListScreen />} />
            <Route path="/orders/:publicId" element={<p>Ficha abierta</p>} />
          </Routes>
        </AccessProvider>
      </QueryClientProvider>
    </MemoryRouter>,
  )
}

const VIEW = ['orders.view']
const callsTo = (method: string, path: string) => mock.calls.filter((c) => c.method === method && c.url.pathname === path)
const lastOrdersQuery = () => callsTo('GET', '/api/v1/orders').at(-1)!.url.searchParams
/** Filas del cuerpo de la tabla. */
const bodyRows = () => within(screen.getAllByRole('rowgroup')[1]).getAllByRole('row')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.calls = []
  mock.override = null
  orders = [O1, O2]
  window.localStorage.clear()
})

describe('Envíos — lista', () => {
  it('una fila por tipo de paquete: «↳» en las continuaciones, el resto de celdas solo en la primera, y el contador cuenta envíos', async () => {
    wrap(VIEW)
    await screen.findByText('Farmacia Central')
    const rows = bodyRows()
    expect(rows).toHaveLength(3) // O1 = 2 filas (Caja, Sobre) + O2 = 1
    const [first, cont, second] = rows
    expect(within(first).queryByText('EMP-00001')).toBeNull()   // el empaque de la orden ya no es una columna
    expect(within(first).getByText('CND-0000001')).toBeInTheDocument()   // el conduce es el número de envío (primera columna)
    expect(within(first).getByText('ORD-00001')).toBeInTheDocument()
    expect(within(first).getByText('Caja')).toBeInTheDocument()
    expect(within(first).getByText('Ropa')).toBeInTheDocument()
    expect(within(first).getByText('$50.00')).toBeInTheDocument()
    expect(within(first).queryByText('$120.00')).toBeNull()   // sin la columna Cotizado
    expect(within(first).getByText('Entrada')).toBeInTheDocument()
    expect(within(cont).getByText('↳')).toBeInTheDocument()
    expect(within(cont).getByText('Sobre')).toBeInTheDocument()
    expect(within(cont).queryByText('ORD-00001')).toBeNull()
    expect(within(cont).queryByText('Farmacia Central')).toBeNull()
    expect(within(cont).queryByText('Entrada')).toBeNull()
    expect(within(second).getByText('ORD-00002')).toBeInTheDocument()
    expect(within(second).getAllByText('—').length).toBeGreaterThan(0) // sin COD (y sin números de paquete)
    // la paginación cuenta envíos (2), no filas (3)
    expect(screen.getByText('1–2 de 2')).toBeInTheDocument()
  })

  it('muestra las columnas de la maqueta en su envío', async () => {
    wrap(VIEW)
    await screen.findByText('Farmacia Central')
    const heads = within(screen.getAllByRole('rowgroup')[0]).getAllByRole('columnheader').map((h) => h.textContent?.trim())
    // Lote F-G2: la casilla «Elegir» primero (para Imprimir label)
    expect(heads.slice(0, 14)).toEqual(['Elegir', 'Conduce', 'Fecha', 'Zona', 'Factura procesada', 'Consignatario', 'Pueblo', 'ZIP', 'Servicio', 'Paquete', 'N.º de paquete', 'Piezas', 'COD', 'Estatus'])   // Lote 128: cuándo se procesó la factura
  })

  it('pide la primera página con skip/take', async () => {
    wrap(VIEW)
    await screen.findByText('Farmacia Central')
    const q = lastOrdersQuery()
    expect(q.get('skip')).toBe('0')
    expect(q.get('take')).toBe('25')
  })

  it('los filtros se mandan como arreglos repetidos y como texto, con la página en 1', async () => {
    const user = userEvent.setup()
    wrap(VIEW)
    await screen.findByText('Farmacia Central')
    // Servicio (múltiple)
    await user.click(screen.getByLabelText('Servicio'))
    await user.click(await screen.findByLabelText('Estándar'))
    await user.click(screen.getByLabelText('Express'))
    await waitFor(() => expect(lastOrdersQuery().getAll('serviceTypes')).toEqual(['STANDARD', 'EXPRESS']))
    // Cliente (múltiple): se suma a clientIds
    await user.click(screen.getByLabelText('Cliente'))
    await user.click(await screen.findByLabelText('ACME · Acme Corp'))
    await waitFor(() => expect(lastOrdersQuery().getAll('clientIds')).toEqual(['c-1']))
    // Estatus (múltiple)
    await user.click(screen.getByLabelText('Estatus'))
    await user.click(await screen.findByLabelText('Entrada'))
    await waitFor(() => expect(lastOrdersQuery().getAll('statuses')).toEqual(['DRAFT']))
    // Texto: ZIP (prefijo), factura, empaque, N.º de envío y consignatario
    await user.type(screen.getByLabelText('ZIP'), '009')
    await user.type(screen.getByLabelText('Factura'), 'FAC-1')
    await user.type(screen.getByLabelText('Conduce'), 'CND-1')
    await user.type(screen.getByLabelText('N.º de paquete'), '0000401')
    await user.type(screen.getByLabelText('N.º de envío'), 'ORD-1')
    await user.type(screen.getByLabelText('Consignatario'), 'Farm')
    // Lote 128: factura procesada (sí/no) y fecha de procesamiento (rangos sugeridos)
    await user.selectOptions(screen.getByLabelText('Factura procesada'), 'yes')
    await waitFor(() => expect(lastOrdersQuery().get('invoiceProcessed')).toBe('true'))
    await user.selectOptions(screen.getByLabelText('Factura procesada'), 'no')
    await waitFor(() => expect(lastOrdersQuery().get('invoiceProcessed')).toBe('false'))
    await user.selectOptions(document.querySelectorAll<HTMLSelectElement>('.filters select[id$="-preset"]')[1], 'today')   // el segundo filtro de fechas es el de procesamiento
    await waitFor(() => {
      const q = lastOrdersQuery()
      expect(q.get('processedFrom')).toMatch(/^\d{4}-\d{2}-\d{2}$/)
      expect(q.get('processedTo')).toMatch(/^\d{4}-\d{2}-\d{2}$/)
    })
    await user.selectOptions(screen.getByLabelText('Factura procesada'), '')
    await waitFor(() => {
      const q = lastOrdersQuery()
      expect(q.get('postalCodePrefix')).toBe('009')
      expect(q.get('invoice')).toBe('FAC-1')
      expect(q.get('waybill')).toBe('CND-1')
      expect(q.get('packageNumber')).toBe('0000401')
      expect(q.get('packBatch')).toBeNull()
      expect(q.get('orderNumber')).toBe('ORD-1')
      expect(q.get('consignee')).toBe('Farm')
      expect(q.getAll('serviceTypes')).toEqual(['STANDARD', 'EXPRESS'])
      expect(q.get('skip')).toBe('0')
    })
  })

  it('los pueblos del filtro salen de los consignatarios y se mandan como cities repetido', async () => {
    const user = userEvent.setup()
    wrap(VIEW)
    await screen.findByText('Farmacia Central')
    await user.click(screen.getByLabelText('Pueblo'))
    await user.click(await screen.findByLabelText('Ponce'))
    await user.click(screen.getByLabelText('San Juan'))
    await waitFor(() => expect(lastOrdersQuery().getAll('cities')).toEqual(['Ponce', 'San Juan']))
  })

  it('«Hasta» incluye el día elegido (el servidor lo toma exclusivo)', async () => {
    const user = userEvent.setup()
    wrap(VIEW)
    await screen.findByText('Farmacia Central')
    // el filtro de fecha es un desplegable de rangos: con «Personalizado» aparecen los dos campos de fecha
    await user.selectOptions(document.querySelector('.filters select[id$="-preset"]') as HTMLSelectElement, 'custom')
    const dates = document.querySelectorAll<HTMLInputElement>('.filters .datefield input')
    await typeDate(user, dates[0], '2026-10-01')
    await typeDate(user, dates[1], '2026-10-31')
    await waitFor(() => {
      const q = lastOrdersQuery()
      expect(q.get('from')).toBe('2026-10-01')
      expect(q.get('to')).toBe('2026-11-01')
    })
  })

  it('Limpiar quita todos los filtros', async () => {
    const user = userEvent.setup()
    wrap(VIEW)
    await screen.findByText('Farmacia Central')
    await user.type(screen.getByLabelText('Factura'), 'FAC-1')
    await waitFor(() => expect(lastOrdersQuery().get('invoice')).toBe('FAC-1'))
    await user.click(screen.getByRole('button', { name: 'Limpiar' }))
    await waitFor(() => expect(lastOrdersQuery().get('invoice')).toBeNull())
    expect(screen.getByLabelText('Factura')).toHaveValue('')
  })

  it('ordenar lee TODO lo filtrado (exportRows, de a 200) y no solo la página en pantalla', async () => {
    const user = userEvent.setup()
    wrap(VIEW)
    await screen.findByText('Farmacia Central')
    await user.click(screen.getByRole('button', { name: /^Consignatario/ }))
    await waitFor(() => expect(mock.calls.some((c) => c.url.pathname === '/api/v1/orders' && c.url.searchParams.get('take') === '200')).toBe(true))
  })

  it('un error del servidor se muestra en lugar de la tabla', async () => {
    mock.override = (c) => (c.url.pathname === '/api/v1/orders' ? problem(400, 'Estatus desconocido: X.', 'validation') : undefined)
    wrap(VIEW)
    expect(await screen.findByText('Estatus desconocido: X.')).toBeInTheDocument()
  })
})

describe('Envíos — papelera, abrir y precio', () => {
  it('la papelera solo sale en el estatus inicial y con orders.cancel; la flecha abre el envío (una vez por envío)', async () => {
    const user = userEvent.setup()
    wrap([...VIEW, 'orders.cancel'])
    await screen.findByText('Farmacia Central')
    expect(screen.getAllByRole('button', { name: 'Eliminar el envío' })).toHaveLength(1)
    const opens = screen.getAllByRole('button', { name: 'Abrir el envío' })
    expect(opens).toHaveLength(2)
    // la primera fila del envío con dos tipos de paquete tiene los botones; la continuación no
    expect(within(bodyRows()[1]).queryByRole('button')).toBeNull()
    await user.click(opens[1])
    expect(await screen.findByText('Ficha abierta')).toBeInTheDocument()
  })

  it('sin orders.cancel no hay papelera', async () => {
    wrap(VIEW)
    await screen.findByText('Farmacia Central')
    expect(screen.queryByRole('button', { name: 'Eliminar el envío' })).toBeNull()
  })

  it('eliminar pide confirmación, llama DELETE y refresca la lista', async () => {
    const user = userEvent.setup()
    wrap([...VIEW, 'orders.cancel'])
    await screen.findByText('Farmacia Central')
    await user.click(screen.getByRole('button', { name: 'Eliminar el envío' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Eliminar el envío ORD-00001')).toBeInTheDocument()
    const before = callsTo('GET', '/api/v1/orders').length
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar' }))
    await waitFor(() => expect(callsTo('DELETE', '/api/v1/orders/o-1')).toHaveLength(1))
    await waitFor(() => expect(callsTo('GET', '/api/v1/orders').length).toBeGreaterThan(before))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })

  it('si el servidor rechaza la eliminación (422), muestra su mensaje y no cierra', async () => {
    const user = userEvent.setup()
    mock.override = (c) =>
      c.method === 'DELETE' ? problem(422, 'Solo se puede eliminar un envío en su estatus inicial; use la cancelación.', 'status_rule') : undefined
    wrap([...VIEW, 'orders.cancel'])
    await screen.findByText('Farmacia Central')
    await user.click(screen.getByRole('button', { name: 'Eliminar el envío' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar' }))
    expect(await within(dialog).findByText('Solo se puede eliminar un envío en su estatus inicial; use la cancelación.')).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('no hay columna Cotizado, ni siquiera con orders.view_price', async () => {
    wrap([...VIEW, 'orders.view_price'])
    await screen.findByText('Farmacia Central')
    expect(screen.queryByRole('columnheader', { name: /Cotizado/ })).toBeNull()
    expect(screen.queryByText('$120.00')).toBeNull()
  })
})

describe('Envíos — Nuevo envío', () => {
  it('el botón solo existe con orders.create; abre el formulario y «Ocultar formulario» lo cierra', async () => {
    const user = userEvent.setup()
    window.localStorage.setItem('teikem.orders.entryMode', 'detail') // la Rápida es la predeterminada (F-C3); aquí se prueba el desplegable
    const { unmount } = wrap(VIEW)
    await screen.findByText('Farmacia Central')
    expect(screen.queryByRole('button', { name: /Nuevo envío/ })).toBeNull()
    unmount()
    wrap([...VIEW, 'orders.create'])
    await screen.findByText('Farmacia Central')
    await user.click(screen.getByRole('button', { name: /Nuevo envío/ }))
    expect(await screen.findByText('Ticket del envío')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Nuevo envío/ })).toBeNull()
    await user.click(screen.getByRole('button', { name: 'Ocultar formulario' }))
    expect(screen.queryByText('Ticket del envío')).toBeNull()
    expect(screen.getByRole('button', { name: /Nuevo envío/ })).toBeInTheDocument()
  })
})

describe('Envíos — Rápida / Detallada e Importar (F-C3)', () => {
  it('«Nuevo envío» abre la entrada Rápida por omisión y el segmentado cambia a Detallada', async () => {
    const user = userEvent.setup()
    wrap([...VIEW, 'orders.create'])
    await screen.findByText('Farmacia Central')
    await user.click(screen.getByRole('button', { name: /Nuevo envío/ }))
    const rapida = screen.getByRole('button', { name: /Rápida/ })
    expect(rapida).toHaveAttribute('aria-pressed', 'true')
    expect(screen.getByRole('button', { name: /Guardar fila/ })).toBeInTheDocument()
    expect(screen.queryByText('Ticket del envío')).toBeNull()
    await user.click(screen.getByRole('button', { name: /Detallada/ }))
    expect(await screen.findByText('Ticket del envío')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Guardar fila/ })).toBeNull()
    expect(screen.getByRole('button', { name: /Detallada/ })).toHaveAttribute('aria-pressed', 'true')
  })

  it('recuerda la última elección en localStorage', async () => {
    const user = userEvent.setup()
    const first = wrap([...VIEW, 'orders.create'])
    await screen.findByText('Farmacia Central')
    await user.click(screen.getByRole('button', { name: /Nuevo envío/ }))
    await user.click(screen.getByRole('button', { name: /Detallada/ }))
    expect(window.localStorage.getItem('teikem.orders.entryMode')).toBe('detail')
    first.unmount()
    wrap([...VIEW, 'orders.create'])
    await screen.findByText('Farmacia Central')
    await user.click(screen.getByRole('button', { name: /Nuevo envío/ }))
    expect(await screen.findByText('Ticket del envío')).toBeInTheDocument()
  })

  it('un localStorage que falla no rompe la pantalla (queda en Rápida)', async () => {
    const user = userEvent.setup()
    const get = vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('bloqueado')
    })
    const set = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('bloqueado')
    })
    try {
      wrap([...VIEW, 'orders.create'])
      await screen.findByText('Farmacia Central')
      await user.click(screen.getByRole('button', { name: /Nuevo envío/ }))
      expect(screen.getByRole('button', { name: /Guardar fila/ })).toBeInTheDocument()
      await user.click(screen.getByRole('button', { name: /Detallada/ }))
      expect(await screen.findByText('Ticket del envío')).toBeInTheDocument()
    } finally {
      get.mockRestore()
      set.mockRestore()
    }
  })

  it('«Importar» solo existe con orders.create y abre el modal de importación', async () => {
    const user = userEvent.setup()
    const { unmount } = wrap(VIEW)
    await screen.findByText('Farmacia Central')
    expect(screen.queryByRole('button', { name: 'Importar' })).toBeNull()
    unmount()
    wrap([...VIEW, 'orders.create'])
    await screen.findByText('Farmacia Central')
    await user.click(screen.getByRole('button', { name: 'Importar' }))
    expect(await screen.findByText('Importar envíos desde CSV')).toBeInTheDocument()
  })
})

describe('Envíos — labels y documentos (Lote F-G2)', () => {
  const withNumbers = () => [
    { ...O1, packages: [{ ...O1.packages[0], packageNumber: 'PQT-00001' }, { ...O1.packages[1], packageNumber: 'PQT-00002' }] },
    { ...O2, packages: [{ ...O2.packages[0], packageNumber: 'PQT-00003' }] },
  ]
  const confirmStation = () =>
    window.localStorage.setItem('teikem.print.station.v1.none', JSON.stringify({ label: { confirm: true }, doc: { confirm: true } }))

  it('«Imprimir label» necesita marcas; con las dos marcadas abre la vista con un label por pieza', async () => {
    orders = withNumbers()
    confirmStation()
    const user = userEvent.setup()
    wrap(VIEW)
    await screen.findByText('Farmacia Central')
    const print = screen.getByRole('button', { name: 'Imprimir label' })
    expect(print).toBeDisabled()
    expect(print).toHaveAttribute('title', 'Para imprimir solo algunas, márquelas en la lista con sus casillas.')
    await user.click(screen.getByRole('checkbox', { name: 'Elegir el envío ORD-00001' }))
    await user.click(screen.getByRole('checkbox', { name: 'Elegir el envío ORD-00002' }))
    expect(screen.getByText('2 marcadas')).toBeInTheDocument()
    expect(screen.queryByText('Ficha abierta')).toBeNull() // marcar no abre el envío
    await user.click(print)
    const dialog = await screen.findByRole('dialog', { name: 'Imprimir 4 labels' })
    expect(within(dialog).getByTestId('print-count')).toHaveTextContent('Labels: 4 · envíos: 2 (uno por pieza).')
    expect(within(dialog).getByTestId('label-preview')).toHaveTextContent('PQT-00001 · Paquete 1 de 3')
    expect(within(dialog).getByRole('button', { name: 'Descargar PDF' })).toBeInTheDocument()
  })

  it('«Todos los labels» lee todo lo filtrado; avisa las piezas sin número de paquete y los envíos cancelados', async () => {
    const [a, b] = withNumbers()
    orders = [{ ...a, packages: [a.packages[0], { ...a.packages[1], packageNumber: null }] }, { ...b, status: 'CANCELLED' }]
    confirmStation()
    const user = userEvent.setup()
    wrap(VIEW)
    await screen.findByText('Farmacia Central')
    const before = callsTo('GET', '/api/v1/orders').length
    await user.click(screen.getByRole('button', { name: 'Todos los labels' }))
    const dialog = await screen.findByRole('dialog', { name: 'Imprimir 2 labels' })
    expect(within(dialog).getByTestId('print-count')).toHaveTextContent('Labels: 2 · envíos: 1')
    const all = callsTo('GET', '/api/v1/orders').slice(before)
    expect(all.at(-1)!.url.searchParams.get('take')).toBe('200')
    expect(await screen.findByTestId('toast')).toHaveTextContent(/no se imprimieron/)
  })

  it('Waybill summary y Packing list sin marcas usan todo lo filtrado (vista del documento)', async () => {
    orders = withNumbers()
    confirmStation()
    const user = userEvent.setup()
    wrap(VIEW)
    await screen.findByText('Farmacia Central')
    await user.click(screen.getByRole('button', { name: 'Waybill summary' }))
    expect(await screen.findByRole('dialog', { name: 'Waybill summary' })).toHaveTextContent('El documento está listo para imprimir.')
    await user.click(screen.getByRole('button', { name: 'Cancelar' }))
    await user.click(screen.getByRole('button', { name: 'Packing list' }))
    expect(await screen.findByRole('dialog', { name: 'Packing list' })).toBeInTheDocument()
  })
})
