// Pruebas de "Ajustes de la compañía" (/system/settings, lote F9) sobre un fetch simulado: pestañas, lectura de los ajustes,
// guardar (y que toda la app tome los formatos nuevos), error 400 junto al campo, solo lectura sin admin.tenant, vista previa,
// calendario, módulos, operación y marca.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { FormatProvider, getFormatSettings, resetFormatSettings } from '../../kernel/format'
import { setLang } from '../../kernel/i18n/i18n'
import { TenantBrand } from '../../kernel/ui'
import TenantSettingsPage from './TenantSettingsPage'

type Req = { method: string; url: URL; body: unknown }
const mock = vi.hoisted(() => ({ requests: [] as Req[], settings: {} as Record<string, unknown>, modules: [] as Record<string, unknown>[] }))

vi.mock('../../kernel/api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../kernel/api/client')>()
  const fetch = async (req: Request) => {
    const url = new URL(req.url)
    const p = url.pathname
    let body: unknown = null
    try {
      body = req.method === 'GET' || req.method === 'DELETE' ? null : await req.clone().json()
    } catch {
      body = null
    }
    mock.requests.push({ method: req.method, url, body })
    if (p === '/api/v1/tenant/settings' && req.method === 'GET') return json(mock.settings)
    if (p === '/api/v1/tenant/settings' && req.method === 'PUT') {
      const b = body as Record<string, unknown>
      if (b.timeZoneId === 'America/Anchorage') {
        return problem(400, {
          title: "La zona horaria 'America/Anchorage' no la reconoce la plataforma.",
          code: 'validation',
          errors: { timeZoneId: ["La zona horaria 'America/Anchorage' no la reconoce la plataforma."] },
        })
      }
      const merged = { ...mock.settings }
      for (const [k, v] of Object.entries(b)) if (v !== null && v !== undefined) merged[k] = v
      mock.settings = merged
      return json(merged)
    }
    if (p === '/api/v1/tenant/format-options') return json(OPTIONS)
    if (p === '/api/v1/tenant/holidays' && req.method === 'GET') return json(HOLIDAYS)
    if (p === '/api/v1/tenant/holidays' && req.method === 'POST') return json({ id: 99, ...(body as object) })
    if (p.startsWith('/api/v1/tenant/holidays/') && req.method === 'DELETE') return new Response(null, { status: 204 })
    if (p === '/api/v1/modules') return json(mock.modules)
    if (p.startsWith('/api/v1/modules/') && req.method === 'PUT') {
      const key = p.split('/').pop()
      const on = (body as { isEnabled: boolean }).isEnabled
      mock.modules = mock.modules.map((m) => (m.key === key || (!on && m.dependsOn === key) ? { ...m, isEnabled: on } : m))
      return json(mock.modules)
    }
    if (p === '/api/v1/status/OrderStatus') return json(STATUSES)
    if (p === '/api/v1/status/capabilities/TRANSPORT_ORDER' && req.method === 'GET') return json([{ statusCode: 'DELIVERED', capability: 'CANCEL', isAllowed: false, isTenantRule: false }])
    if (p === '/api/v1/status/capabilities/TRANSPORT_ORDER' && req.method === 'PUT') return json(body)
    if (p.startsWith('/api/v1/catalogs/')) return json([{ code: 'STANDARD', label: 'Estándar', sortOrder: 1, isEnabled: true }])
    if (p === '/api/v1/warehouses') return json(WAREHOUSES)
    if (p === '/api/v1/receipts') {
      const wh = url.searchParams.get('warehousePublicId')
      const phase = url.searchParams.get('phase')
      return json({ total: wh === 'w1' ? (phase === 'OPEN' ? 4 : 2) : 0, skip: 0, take: 1, items: [] })
    }
    return problem(404, { title: 'no encontrado' })
  }
  return { ...actual, api: actual.createApiClient({ baseUrl: 'http://api.test', fetch }) }
})

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}
function problem(status: number, body: object) {
  return new Response(JSON.stringify({ status, ...body }), { status, headers: { 'Content-Type': 'application/problem+json' } })
}

const PR = {
  regionCode: 'PR',
  timeZoneId: 'America/Puerto_Rico',
  currencyCode: 'USD',
  currencySymbol: '$',
  currencySymbolPosition: 'B',
  currencyDecimals: 2,
  dateOrder: 'MDY',
  dateSeparator: '/',
  timeFormat: 12,
  weekStartDay: 0,
  thousandsSeparator: ',',
  decimalSeparator: '.',
  phoneCountryCode: '+1',
  phoneMask: '(###) ###-####',
}
const SETTINGS = {
  id: 1,
  name: 'Advance Logistics',
  legalName: 'Advance Logistics, LLC',
  taxId: '66-0000000',
  defaultLangCode: 'es',
  workDaysMask: 62,
  maxStopsPerRouteDefault: 25,
  defaultServiceType: 'STANDARD',
  defaultPackageType: null,
  brandingJson: null,
  isRegionCustomized: false,
  ...PR,
}
const OPTIONS = {
  regions: [PR, { ...PR, regionCode: 'US', timeZoneId: 'America/New_York' }],
  currencySymbolPositions: ['B', 'A'],
  currencyDecimals: [0, 2, 3],
  dateOrders: ['MDY', 'DMY', 'YMD'],
  dateSeparators: ['/', '-', '.'],
  timeFormats: [12, 24],
  weekStartDays: [0, 1],
  thousandsSeparators: [',', '.', ' '],
  decimalSeparators: ['.', ','],
}
const HOLIDAYS = [
  { id: 1, date: '2026-01-01', name: 'Año Nuevo', isRecurring: true },
  { id: 2, date: '2026-10-12', name: 'Descubrimiento', isRecurring: false },
]
const MODULES = [
  { key: 'SYSTEM', name: 'Sistema', isCore: true, isEnabled: true, sortOrder: 1 },
  { key: 'LTL_GROUND', name: 'Operación diaria', isEnabled: true, sortOrder: 2 },
  { key: 'COD', name: 'COD', dependsOn: 'LTL_GROUND', isEnabled: true, sortOrder: 3 },
  { key: 'WMS_LOTSERIAL', name: 'WMS', isEnabled: false, sortOrder: 4 },
  { key: 'CROSSDOCK', name: 'Cross-dock', dependsOn: 'WMS_LOTSERIAL', isEnabled: false, sortOrder: 5 },
]
const STATUSES = [
  { code: 'CAPTURED', label: 'Capturada', stageKind: 'PIPELINE', sortOrder: 1, isEnabled: true },
  { code: 'DELIVERED', label: 'Entregada', stageKind: 'TERMINAL', sortOrder: 2, isEnabled: true },
]
const WAREHOUSES = [
  { publicId: 'w1', code: 'ALM-01', name: 'Principal', isActive: true, receivingModeCode: 'PUTAWAY', defaultReceivingBinId: 7, defaultReceivingBinCode: 'REC-01' },
  { publicId: 'w2', code: 'ALM-02', name: 'Norte', isActive: true, receivingModeCode: 'PUTAWAY', defaultReceivingBinId: null },
  { publicId: 'w3', code: 'ALM-03', name: 'Sur', isActive: true, receivingModeCode: 'DIRECT', defaultReceivingBinId: null },
]

const ALL = ['admin.tenant', 'admin.statusconfig', 'inventory.view']

function Where() {
  const loc = useLocation()
  return <span data-testid="where">{loc.pathname + loc.search}</span>
}

function wrap(path = '/system/settings', permissions = ALL, modules = ['SYSTEM', 'LTL_GROUND', 'WMS_LOTSERIAL']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const ui: ReactNode = (
    <QueryClientProvider client={client}>
      <AccessProvider permissions={permissions} modules={modules}>
        <FormatProvider enabled>
          <TenantBrand enabled />
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route path="/system/settings" element={<TenantSettingsPage />} />
              <Route path="*" element={<Where />} />
            </Routes>
            <Where />
          </MemoryRouter>
        </FormatProvider>
      </AccessProvider>
    </QueryClientProvider>
  )
  return render(ui)
}

const put = () => mock.requests.filter((r) => r.method === 'PUT' && r.url.pathname === '/api/v1/tenant/settings')

beforeAll(() => setLang('es'))
beforeEach(() => {
  mock.requests = []
  mock.settings = { ...SETTINGS }
  mock.modules = MODULES.map((m) => ({ ...m }))
})
afterEach(() => {
  vi.useRealTimers()
  resetFormatSettings()
  document.documentElement.removeAttribute('style')
})

describe('Ajustes de la compañía', () => {
  it('General: pestañas de la maqueta, nombre de solo lectura y guardar la razón social', async () => {
    const user = userEvent.setup()
    wrap()
    const tabs = screen.getByRole('tablist', { name: 'Secciones de los ajustes' })
    expect(within(tabs).getAllByRole('tab').map((b) => b.textContent?.trim())).toEqual(['General', 'Región y formatos', 'Calendario', 'Módulos', 'Operación', 'Marca'])
    const name = await screen.findByLabelText('Nombre')
    expect(name).toHaveValue('Advance Logistics')
    expect(name).toBeDisabled()
    const legal = screen.getByLabelText('Razón social')
    expect(legal).toHaveValue('Advance Logistics, LLC')
    await user.clear(legal)
    await user.type(legal, 'Advance Logistics Corp.')
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(put()).toHaveLength(1))
    expect(put()[0].body).toEqual({ legalName: 'Advance Logistics Corp.', taxId: '66-0000000', defaultLangCode: 'es' })
    // enlace a Seguridad y auditoría
    await user.click(screen.getByRole('button', { name: 'Abrir Seguridad y auditoría' }))
    // abre directo la pestaña Sesiones y MFA (MFA, reautenticación y duración de las sesiones viven allá)
    expect(screen.getAllByTestId('where').at(-1)).toHaveTextContent('/system/audit?tab=sessions')
  })

  it('Región y formatos: vista previa en vivo, intercambio de separadores, "Personalizada" y guardar cambia la app', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-10-03T01:30:00Z'))
    const user = userEvent.setup()
    wrap('/system/settings?tab=region')
    const preview = await screen.findByTestId('format-preview')
    const pv = (k: string) => within(preview).getByText((_, el) => el?.getAttribute('data-pv') === k)
    // 1:30 UTC del 3 de octubre = 2 de octubre en Puerto Rico
    await waitFor(() => expect(pv('pvToday')).toHaveTextContent('10/02/2026'))
    expect(pv('pvNowUtc')).toHaveTextContent('2026-10-03 01:30 UTC')
    expect(pv('pvMoney')).toHaveTextContent('$1,234,567.50')
    expect(pv('pvPhone')).toHaveTextContent('(787) 555-0142')
    expect(pv('pvTime').textContent?.replace(/\s/g, ' ')).toBe('9:30 p. m.')
    expect(screen.queryByText('Personalizada')).toBeNull()

    await user.selectOptions(screen.getByLabelText('Orden de la fecha'), 'DMY')
    await user.selectOptions(screen.getByLabelText('Hora'), '24')
    await user.selectOptions(screen.getByLabelText('Separador de miles'), '.')
    // el decimal se cambió solo para no chocar
    expect(screen.getByLabelText('Separador decimal')).toHaveValue(',')
    expect(pv('pvToday')).toHaveTextContent('02/10/2026')
    expect(pv('pvTime')).toHaveTextContent('21:30')
    expect(pv('pvMoney')).toHaveTextContent('$1.234.567,50')
    expect(screen.getByText('Personalizada')).toBeInTheDocument()
    // la app todavía no cambia (sin guardar)
    expect(getFormatSettings().dateOrder).toBe('MDY')

    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(put()).toHaveLength(1))
    expect(put()[0].body).toMatchObject({ ...PR, dateOrder: 'DMY', timeFormat: 24, thousandsSeparator: '.', decimalSeparator: ',' })
    // toda la app toma los formatos nuevos sin recargar
    await waitFor(() => expect(getFormatSettings().dateOrder).toBe('DMY'))
    expect(getFormatSettings().decimalSeparator).toBe(',')

    // "Restaurar valores de la región" vuelve al juego de Puerto Rico
    await user.click(await screen.findByRole('button', { name: 'Restaurar valores de la región' }))
    expect(screen.getByLabelText('Orden de la fecha')).toHaveValue('MDY')
    expect(screen.getByLabelText('Separador de miles')).toHaveValue(',')
    expect(screen.queryByText('Personalizada')).toBeNull()

    // otra región: el juego de format-options
    await user.click(screen.getByRole('button', { name: 'Estados Unidos' }))
    expect(screen.getByLabelText('Zona horaria')).toHaveValue('America/New_York')
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(put()).toHaveLength(2))
    expect(put()[1].body).toMatchObject({ regionCode: 'US', timeZoneId: 'America/New_York', dateOrder: 'MDY' })
    await waitFor(() => expect(getFormatSettings().timeZoneId).toBe('America/New_York'))
  })

  it('error 400 del servidor junto al campo, sin cambiar nada', async () => {
    const user = userEvent.setup()
    wrap('/system/settings?tab=region')
    const tz = await screen.findByLabelText('Zona horaria')
    await user.selectOptions(tz, 'America/Anchorage')
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    const err = await screen.findAllByText("La zona horaria 'America/Anchorage' no la reconoce la plataforma.")
    expect(err.length).toBeGreaterThan(0)
    expect(tz).toHaveAttribute('aria-invalid', 'true')
    expect(getFormatSettings().timeZoneId).toBe('America/Puerto_Rico')
  })

  it('solo lectura sin admin.tenant: controles deshabilitados y sin botones de guardar', async () => {
    wrap('/system/settings?tab=region', [])
    expect(await screen.findByText(/Solo lectura: su usuario no tiene el permiso/)).toBeInTheDocument()
    expect(await screen.findByLabelText('Zona horaria')).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Guardar cambios' })).toBeNull()
    expect(screen.getByRole('button', { name: 'Estados Unidos' })).toBeDisabled()
  })

  it('Calendario: días laborables (al menos uno), próximo día hábil y feriados', async () => {
    vi.useFakeTimers({ toFake: ['Date'] })
    // viernes 9 de octubre (Puerto Rico): el lunes 12 es feriado → martes 13
    vi.setSystemTime(new Date('2026-10-09T15:00:00Z'))
    const user = userEvent.setup()
    wrap('/system/settings?tab=calendar')
    const next = await screen.findByTestId('next-business-day')
    await waitFor(() => expect(next).toHaveTextContent('10/13/2026'))
    expect(screen.getByRole('switch', { name: 'Lunes' })).toBeChecked()
    expect(screen.getByRole('switch', { name: 'Sábado' })).not.toBeChecked()
    await user.click(screen.getByRole('switch', { name: 'Sábado' }))
    await waitFor(() => expect(put().at(-1)?.body).toEqual({ workDaysMask: 126 }))

    // feriados: "cada año" con día y mes; duplicado avisado sin llamar al API
    expect(await screen.findByText('Año Nuevo')).toBeInTheDocument()
    expect(screen.getByText('01/01')).toBeInTheDocument()
    expect(screen.getByText('10/12/2026')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Agregar feriado' }))
    expect(screen.getByText('Escribe la fecha y el nombre del feriado')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Fecha'), '2026-10-12')
    await user.type(screen.getByLabelText('Nombre del feriado'), 'Otro')
    await user.click(screen.getByRole('button', { name: 'Agregar feriado' }))
    expect(screen.getByText('Ya hay un feriado en esa fecha')).toBeInTheDocument()
    await user.clear(screen.getByLabelText('Fecha'))
    await user.type(screen.getByLabelText('Fecha'), '2026-11-19')
    await user.click(screen.getByRole('switch', { name: 'Cada año' }))
    await user.click(screen.getByRole('button', { name: 'Agregar feriado' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'POST')).toBe(true))
    expect(mock.requests.find((r) => r.method === 'POST')?.body).toEqual({ date: '2026-11-19', name: 'Otro', isRecurring: true })
    // eliminar con confirmación
    await user.click(screen.getAllByRole('button', { name: 'Eliminar' })[0])
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Eliminar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'DELETE')).toBe(true))
  })

  it('Módulos: núcleo bloqueado, dependencia apagada y apagar en cascada con confirmación', async () => {
    const user = userEvent.setup()
    wrap('/system/settings?tab=modules')
    expect(await screen.findByText('Núcleo — siempre activo')).toBeInTheDocument()
    expect(screen.getByRole('switch', { name: 'Encender o apagar Cross-dock' })).toBeDisabled()
    expect(screen.getAllByText(/Requiere:/).length).toBeGreaterThan(0)
    await user.click(screen.getByRole('switch', { name: 'Encender o apagar Operación diaria' }))
    const dialog = await screen.findByRole('dialog', { name: '¿Apagar Operación diaria?' })
    expect(dialog).toHaveTextContent('también se apagan: COD')
    await user.click(within(dialog).getByRole('button', { name: 'Apagar' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PUT' && r.url.pathname === '/api/v1/modules/LTL_GROUND')).toBe(true))
    expect(mock.requests.find((r) => r.url.pathname === '/api/v1/modules/LTL_GROUND')?.body).toEqual({ isEnabled: false })
  })

  it('Operación: valores por defecto, matriz de acciones y recepción por almacén (aviso sin posición y Abrir almacén)', async () => {
    const user = userEvent.setup()
    wrap('/system/settings?tab=ops')
    expect(await screen.findByLabelText('Tipo de servicio por defecto')).toHaveValue('STANDARD')
    expect(screen.getByLabelText('Máximo de paradas por ruta (default)')).toHaveValue(25)
    // matriz: sin regla = permitido; la regla de DELIVERED/CANCEL lo niega
    expect(await screen.findByRole('checkbox', { name: 'Cancelar en Capturada' })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: 'Cancelar en Entregada' })).not.toBeChecked()
    await user.click(screen.getByRole('checkbox', { name: 'Re-cotizar en Capturada' }))
    await waitFor(() => expect(mock.requests.some((r) => r.method === 'PUT' && r.url.pathname.includes('/capabilities/'))).toBe(true))
    const capPut = mock.requests.find((r) => r.method === 'PUT' && r.url.pathname.includes('/capabilities/'))
    expect(capPut?.url.searchParams.get('statusDomain')).toBe('OrderStatus')
    expect(capPut?.body).toEqual([{ statusCode: 'CAPTURED', capability: 'REPRICE', isAllowed: false }])

    // recepción por almacén
    const panel = (await screen.findByRole('heading', { name: 'Recepción por almacén' })).closest('.panel') as HTMLElement
    expect(await within(panel).findByText('Sin posición de recepción')).toBeInTheDocument()
    expect(within(panel).getByText('REC-01')).toBeInTheDocument()
    expect(within(panel).getByText('Directo a posición')).toBeInTheDocument()
    await waitFor(() => expect(within(panel).getByText('4')).toBeInTheDocument())
    await user.click(within(panel).getAllByRole('button', { name: 'Abrir almacén' })[0])
    expect(screen.getAllByTestId('where').at(-1)).toHaveTextContent(/^\/warehouse\/warehouses\/w\d$/)
  })

  it('Marca: tema predefinido con validaciones y BrandingJson al guardar', async () => {
    const user = userEvent.setup()
    wrap('/system/settings?tab=brand')
    expect(await screen.findByText('La combinación pasa las validaciones')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Bosque/ }))
    expect(screen.getByRole('button', { name: /Bosque/ })).toHaveAttribute('aria-pressed', 'true')
    // vista previa en toda la app (sin guardar todavía)
    expect(document.documentElement.style.getPropertyValue('--flow')).toBe('#1E8E5A')
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(put()).toHaveLength(1))
    expect(JSON.parse(String((put()[0].body as { brandingJson: string }).brandingJson))).toMatchObject({ preset: 'bosque', useCustom: false })
    // colores propios que no pasan: no se guarda
    await user.click(await screen.findByRole('switch', { name: 'Usar colores propios' }))
    const money = screen.getByLabelText('Color de dinero (hexadecimal)')
    await user.clear(money)
    await user.type(money, '#1E8E5B')
    await user.tab()
    expect(await screen.findByText('Revisa estos puntos antes de usarla')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    expect(put()).toHaveLength(1)
  })
})
