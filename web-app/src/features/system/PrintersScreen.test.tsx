// Pruebas de «Impresoras y labels» (Lote F-G1) sobre un fetch simulado y un QZ Tray falso: estado de QZ Tray y detección de impresoras,
// paneles de la estación (guardar en este navegador), constructor del label (la vista previa cambia y «Actualizar» manda la
// construcción), alta de impresora y el ajuste de la compañía «Imprimir labels directo a la impresora».
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { SessionContext, type Session } from '../../app/session'
import { AccessProvider } from '../../kernel/access'
import { setLang } from '../../kernel/i18n/i18n'
import { BUILT_IN_LAYOUT } from '../../kernel/ui/orderLabelLayout'
import { setQzClient, type QzClient } from '../../kernel/ui/qzTray'
import PrintersScreen from './PrintersScreen'

const mock = vi.hoisted(() => ({ requests: [] as { method: string; path: string; body: unknown }[], direct: false }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const text = await req.clone().text()
    const body = text ? JSON.parse(text) : undefined
    mock.requests.push({ method: req.method, path: url.pathname, body })
    const p = url.pathname
    if (req.method === 'GET' && p === '/api/v1/printing/station-options')
      return json({ printers: PRINTERS.filter((x) => x.isActive), templates: TEMPLATES, builtInLayout: BUILT_IN_LAYOUT, printLabelsDirect: mock.direct, qzSigningConfigured: false })
    if (req.method === 'GET' && p === '/api/v1/printers') return json(PRINTERS)
    if (req.method === 'GET' && p === '/api/v1/label-templates') return json(TEMPLATES)
    if (req.method === 'GET' && p === '/api/v1/printing/settings') return json({ printLabelsDirect: mock.direct })
    if (req.method === 'PUT' && p === '/api/v1/printing/settings') {
      mock.direct = body.printLabelsDirect
      return json({ printLabelsDirect: mock.direct })
    }
    if (req.method === 'POST' && p === '/api/v1/printers') return json({ ...PRINTERS[0], publicId: 'new', name: body.name, isDefault: false })
    if (req.method === 'PATCH' && p.startsWith('/api/v1/label-templates/')) return json({ ...TEMPLATES[0], layout: body.layout })
    return new Response(JSON.stringify({ title: 'no encontrado' }), { status: 404 })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const PRINTERS = [
  { publicId: 'zd', name: 'Zebra ZD420 — Mostrador 1', model: 'ZD420', purpose: 'LABEL', connectionType: 'QZ_TRAY', mediaSize: '4X6', rotateMedia: false, notes: null, isDefault: true, isActive: true, rowVersion: 'AAA=', systemName: 'ZDesigner ZD420' },
  { publicId: 'hp', name: 'HP LaserJet', model: null, purpose: 'DOCUMENT', connectionType: 'BROWSER', mediaSize: 'LETTER', rotateMedia: false, notes: null, isDefault: true, isActive: true, rowVersion: 'AAA=', systemName: null },
]
const TEMPLATES = [{ publicId: 'cod', name: '4×6 — COD', layout: BUILT_IN_LAYOUT, isDefault: true, isActive: true, rowVersion: 'AQI=' }]

const SESSION = {
  me: { tenantName: 'Advance Logistics' },
  isAuthenticated: true,
  isLoading: false,
  error: null,
  tenantId: 7,
  lang: 'es',
  setLang: () => {},
  logout: async () => {},
  switchTenant: async () => {},
  permissions: new Set(['printers.manage']),
  modules: new Set(['SYSTEM']),
  reloadMe: async () => {},
} as unknown as Session

function fakeQz(state: 'connected' | 'notRunning'): QzClient {
  return { connect: vi.fn(async () => state), findPrinters: vi.fn(async () => ['ZDesigner ZD420', 'HP LaserJet Pro']), printPdf: vi.fn(async () => {}) }
}

function wrap() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={client}>
      <SessionContext.Provider value={SESSION}>
        <AccessProvider permissions={['printers.manage']} modules={['SYSTEM']}>
          <PrintersScreen />
        </AccessProvider>
      </SessionContext.Provider>
    </QueryClientProvider>,
  )
}

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.direct = false
  localStorage.clear()
})
afterEach(() => setQzClient(null))

describe('Impresoras y labels', () => {
  it('pinta la pantalla de la maqueta: nota por estación, dos paneles, constructor de 9 elementos e impresoras', async () => {
    setQzClient(fakeQz('notRunning'))
    wrap()
    expect(screen.getByRole('heading', { level: 1, name: 'Impresoras y labels' })).toBeInTheDocument()
    expect(screen.getByText(/cada estación de trabajo guarda su propia configuración/)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Esta estación — labels' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Esta estación — documentos' })).toBeInTheDocument()
    expect(await screen.findByText('QZ Tray no detectado')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Instalar QZ Tray' })).toHaveAttribute('href', 'https://qz.io/download/')
    const builder = screen.getByRole('heading', { name: 'Construir el label' }).closest('.panel') as HTMLElement
    expect(within(builder).getAllByRole('row')).toHaveLength(10) // encabezado + 9 elementos
    expect(await screen.findByText('Zebra ZD420 — Mostrador 1', { selector: 'b' })).toBeInTheDocument()
    // la de la compañía es QZ Tray: imprime directo por su nombre en el sistema
    expect(await screen.findByText('Imprime directo con QZ Tray en «ZDesigner ZD420».')).toBeInTheDocument()
  })

  it('detectar impresoras con QZ Tray conectado las lista y las ofrece en «Impresora en esta computadora»', async () => {
    setQzClient(fakeQz('connected'))
    const user = userEvent.setup()
    wrap()
    expect(await screen.findByText('QZ Tray conectado')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Detectar impresoras de esta computadora' }))
    expect(await screen.findByTestId('qz-detected')).toHaveTextContent('ZDesigner ZD420 · HP LaserJet Pro')
    const system = await screen.findByLabelText('Impresora en esta computadora')
    expect(within(system).getByRole('option', { name: 'HP LaserJet Pro' })).toBeInTheDocument()
  })

  it('«Guardar» de la estación deja la elección en este navegador (por compañía)', async () => {
    setQzClient(fakeQz('notRunning'))
    const user = userEvent.setup()
    wrap()
    const panel = screen.getByRole('heading', { name: 'Esta estación — labels' }).closest('.panel') as HTMLElement
    const save = within(panel).getByRole('button', { name: 'Guardar' })
    expect(save).toBeDisabled()
    await user.click(within(panel).getByRole('switch', { name: 'Confirmar antes' }))
    await user.click(save)
    expect(JSON.parse(localStorage.getItem('teikem.print.station.v1.7')!).label.confirm).toBe(true)
  })

  it('el constructor: apagar el COD y moverlo cambia la vista previa; «Actualizar» manda la construcción con rowVersion', async () => {
    setQzClient(fakeQz('notRunning'))
    const user = userEvent.setup()
    wrap()
    const builder = screen.getByRole('heading', { name: 'Construir el label' }).closest('.panel') as HTMLElement
    await waitFor(() => expect(within(builder).getByLabelText('Plantilla')).toHaveValue('cod'))
    const preview = within(builder).getByTestId('label-preview')
    expect(preview.querySelector('.lbl-cell-r [data-el="cod"]')).toHaveTextContent('COD $125.82')
    await user.selectOptions(within(builder).getByLabelText('Posición de Monto COD'), 'tl')
    expect(preview.querySelector('.lbl-row-t .lbl-cell-l [data-el="cod"]')).not.toBeNull()
    await user.click(within(builder).getByRole('checkbox', { name: 'Mostrar Información del sender' }))
    expect(preview).toHaveTextContent('De: Advance Solutions')
    await user.click(within(builder).getByRole('button', { name: 'Actualizar construcción del label' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PATCH')).toBe(true))
    const patch = mock.requests.find((r) => r.method === 'PATCH')!
    expect(patch.path).toBe('/api/v1/label-templates/cod')
    expect(patch.body).toMatchObject({ rowVersion: 'AQI=', layout: { cod: { on: true, pos: 'tl' }, sender: { on: true } } })
    // sin ningún código de barras visible no se puede guardar
    await user.click(within(builder).getByRole('checkbox', { name: 'Mostrar Código de barras' }))
    expect(within(builder).getByRole('alert')).toHaveTextContent('El label necesita al menos un código de barras para poder escanearlo.')
  })

  it('alta de impresora con QZ Tray y el ajuste «Imprimir labels directo a la impresora»', async () => {
    setQzClient(fakeQz('notRunning'))
    const user = userEvent.setup()
    wrap()
    await user.click(await screen.findByRole('button', { name: 'Nueva impresora' }))
    const dialog = screen.getByRole('dialog')
    await user.type(within(dialog).getByLabelText(/^Nombre/), 'Zebra ZT230 — Almacén')
    await user.selectOptions(within(dialog).getByLabelText('Tamaño'), '4X4')
    await user.selectOptions(within(dialog).getByLabelText('Conexión'), 'QZ_TRAY')
    await user.type(within(dialog).getByLabelText('Nombre en el sistema'), 'ZDesigner ZT230')
    await user.click(within(dialog).getByRole('button', { name: 'Guardar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST' && r.path === '/api/v1/printers')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'POST' && r.path === '/api/v1/printers')!.body).toEqual({
      name: 'Zebra ZT230 — Almacén',
      purpose: 'LABEL',
      connectionType: 'QZ_TRAY',
      systemName: 'ZDesigner ZT230',
      model: null,
      mediaSize: '4X4',
      rotateMedia: false,
      notes: null,
    })

    const direct = screen.getByRole('switch', { name: 'Imprimir labels directo a la impresora' })
    await waitFor(() => expect(direct).not.toBeDisabled())
    await user.click(direct)
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PUT')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'PUT')!.body).toEqual({ printLabelsDirect: true })
  })
})
