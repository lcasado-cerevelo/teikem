// Pruebas de "Ajustes de la compañía" (/system/settings, lote F9) sobre un fetch simulado: pestañas, lectura de los ajustes,
// guardar (y que toda la app tome los formatos nuevos), error 400 junto al campo, solo lectura sin admin.tenant, vista previa,
// calendario, módulos, operación y marca.
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccessProvider } from '../../kernel/access'
import { FormatProvider, getFormatSettings, resetFormatSettings } from '../../kernel/format'
import { setLang } from '../../kernel/i18n/i18n'
import { TenantBrand } from '../../kernel/ui'
import TenantSettingsPage from './TenantSettingsPage'

type Req = { method: string; url: URL; body: unknown }
const mock = vi.hoisted(() => ({
  requests: [] as Req[],
  settings: {} as Record<string, unknown>,
  modules: [] as Record<string, unknown>[],
  // logos de la marca (GET/PUT/DELETE /tenant/brand/logos) y el error que el servidor daría a la próxima subida o al próximo PUT de ajustes
  logos: [] as { slot: string; contentType: string; sizeBytes: number; eTag: string; updatedAtUtc: string }[],
  logoFail: null as null | { status: number; title: string },
  uploads: [] as { field: string; fileName: string; type: string }[],
  settingsFail: null as null | { status: number; title: string; field: string },
}))

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
      if (mock.settingsFail) {
        const f = mock.settingsFail
        return problem(f.status, { title: f.title, code: 'validation', errors: { [f.field]: [f.title] } })
      }
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
    if (p === '/api/v1/tenant/brand/logos' && req.method === 'GET') return json(mock.logos)
    if (p.startsWith('/api/v1/tenant/brand/logos/')) {
      const slot = p.split('/').pop() as string
      if (req.method === 'GET') return new Response(new Uint8Array([137, 80, 78, 71]), { status: 200, headers: { 'Content-Type': 'image/png' } })
      if (req.method === 'DELETE') {
        mock.logos = mock.logos.filter((l) => l.slot !== slot)
        return new Response(null, { status: 204 })
      }
      if (mock.logoFail) return problem(mock.logoFail.status, { title: mock.logoFail.title, code: 'validation', errors: mock.logoFail.status === 400 ? { file: [mock.logoFail.title] } : undefined })
      const dto = { slot, contentType: 'image/png', sizeBytes: 2048, eTag: `etag-${slot}-${mock.logos.length}`, updatedAtUtc: '2026-10-03T12:00:00' }
      mock.logos = [...mock.logos.filter((l) => l.slot !== slot), dto]
      return json(dto)
    }
    if (p === '/api/v1/tenant/format-options') return json(OPTIONS)
    if (p === '/api/v1/tenant/holidays' && req.method === 'GET') return json(HOLIDAYS)
    if (p === '/api/v1/tenant/holidays' && req.method === 'POST') {
      // como el servidor: una fecha ya registrada es 409 con 'Ya hay un feriado en esa fecha.'
      const dup = HOLIDAYS.some((h) => h.date === (body as { date: string }).date)
      if (dup) return problem(409, { title: 'Ya hay un feriado en esa fecha.', code: 'conflict', errors: { date: ['Ya hay un feriado en esa fecha.'] } })
      return json({ id: 99, ...(body as object) })
    }
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

// jsdom + el Request de Node no se entienden con un FormData con archivos (vitest copia el binario con un símbolo interno que jsdom 30
// cambió): aquí el FormData es un registro del campo y el archivo. La codificación multipart real la prueban el humo y Playwright.
class RecordingFormData {
  append(field: string, file: File) {
    mock.uploads.push({ field, fileName: file.name, type: file.type })
  }
}

beforeAll(() => {
  vi.stubGlobal('FormData', RecordingFormData)
  setLang('es')
  // jsdom no trae URL de objeto: una distinta por archivo bajado
  let n = 0
  URL.createObjectURL = vi.fn(() => `blob:logo-${++n}`)
  URL.revokeObjectURL = vi.fn()
})
afterAll(() => {
  vi.unstubAllGlobals()
})
beforeEach(() => {
  mock.uploads = []
  mock.logos = []
  mock.logoFail = null
  mock.settingsFail = null
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

    // feriados: "cada año" con día y mes; el duplicado lo rechaza el servidor (409) y la pantalla muestra su mensaje
    expect(await screen.findByText('Año Nuevo')).toBeInTheDocument()
    expect(screen.getByText('01/01')).toBeInTheDocument()
    expect(screen.getByText('10/12/2026')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Agregar feriado' }))
    expect(screen.getByText('Escribe la fecha y el nombre del feriado')).toBeInTheDocument()
    await user.type(screen.getByLabelText('Fecha'), '2026-10-12')
    await user.type(screen.getByLabelText('Nombre del feriado'), 'Otro')
    await user.click(screen.getByRole('button', { name: 'Agregar feriado' }))
    expect(await screen.findByText('Ya hay un feriado en esa fecha.')).toBeInTheDocument()
    expect(mock.requests.filter((r) => r.method === 'POST' && r.url.pathname === '/api/v1/tenant/holidays')).toHaveLength(1)
    await user.clear(screen.getByLabelText('Fecha'))
    await user.type(screen.getByLabelText('Fecha'), '2026-11-19')
    await user.click(screen.getByRole('switch', { name: 'Cada año' }))
    await user.click(screen.getByRole('button', { name: 'Agregar feriado' }))
    await waitFor(() => expect(mock.requests.filter((r) => r.method === 'POST' && r.url.pathname === '/api/v1/tenant/holidays')).toHaveLength(2))
    expect(mock.requests.filter((r) => r.method === 'POST').at(-1)?.body).toEqual({ date: '2026-11-19', name: 'Otro', isRecurring: true })
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

  it('Operación → conteo cíclico: quién ve lo esperado, margen de reconteo y número; se guarda parcial y valida el margen', async () => {
    const user = userEvent.setup()
    wrap('/system/settings?tab=ops')
    const panel = (await screen.findByRole('heading', { name: 'Conteo cíclico: lo esperado al contar' })).closest('.panel') as HTMLElement
    // sin valores guardados: «Solo los marcados», margen 0 y número visible
    expect(within(panel).getByLabelText('¿Quién ve lo esperado al contar?')).toHaveValue('MARKED')
    expect(within(panel).getByLabelText('Margen para no pedir reconteo (%)')).toHaveValue(0)
    expect(within(panel).getByRole('switch', { name: /Mostrar el número/ })).toBeChecked()
    expect(within(panel).getByText(/Solo los contadores marcados con «Sí» en Usuarios/)).toBeInTheDocument()

    await user.selectOptions(within(panel).getByLabelText('¿Quién ve lo esperado al contar?'), 'ALL')
    expect(within(panel).getByText(/Todos los contadores lo ven después de capturar cada línea/)).toBeInTheDocument()
    const pct = within(panel).getByLabelText('Margen para no pedir reconteo (%)')
    await user.clear(pct)
    await user.type(pct, '150')
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }))
    expect(await within(panel).findByText('El margen debe estar entre 0 y 100 %.')).toBeInTheDocument()
    expect(put()).toHaveLength(0)

    await user.clear(pct)
    await user.type(pct, '5')
    await user.click(within(panel).getByRole('switch', { name: /Mostrar el número/ }))
    await user.click(within(panel).getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(put()).toHaveLength(1))
    expect(put()[0].body).toEqual({ countExpectedReveal: 'ALL', countRecountTolerancePct: 5, countRevealShowsNumber: false, countAutoCloseMatching: false })
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

  it('Marca → logos: sin logos explica el respaldo de Teikem y ofrece subir en las cuatro ranuras', async () => {
    wrap('/system/settings?tab=brand')
    expect(await screen.findByText('Logo')).toBeInTheDocument()
    for (const name of ['Lockup (fondo claro)', 'Lockup (fondo oscuro)', 'Marca cuadrada (fondo claro)', 'Marca cuadrada (fondo oscuro)']) {
      expect(screen.getByText(name)).toBeInTheDocument()
      expect(screen.getByLabelText(`Archivo de ${name}`)).toHaveAttribute('accept', 'image/svg+xml,image/png,image/jpeg,image/webp')
    }
    expect(screen.getAllByText('Sin logo: se usa el de Teikem')).toHaveLength(4)
    expect(screen.getAllByRole('button', { name: 'Subir' })).toHaveLength(4)
    expect(screen.queryByRole('button', { name: /Quitar/ })).toBeNull()
    // ya no se explica que falte el almacenamiento
    expect(screen.queryByText(/todavía no se pueden cargar/)).toBeNull()
  })

  it('Marca → logos: subir (multipart por el endpoint nuevo), ver el logo y quitarlo', async () => {
    const user = userEvent.setup()
    wrap('/system/settings?tab=brand')
    const input = await screen.findByLabelText('Archivo de Lockup (fondo claro)')
    await user.upload(input, new File(['png'], 'logo.png', { type: 'image/png' }))
    const card = screen.getByTestId('logo-lockup')
    await waitFor(() => expect(within(card).getByRole('img', { name: 'Vista previa de Lockup (fondo claro)' })).toBeInTheDocument())
    const sent = mock.requests.filter((r) => r.method === 'PUT' && r.url.pathname === '/api/v1/tenant/brand/logos/lockup')
    expect(sent).toHaveLength(1)
    expect(mock.uploads).toEqual([{ field: 'file', fileName: 'logo.png', type: 'image/png' }])
    expect(within(card).getByText('PNG · 2 KB')).toBeInTheDocument()
    expect(within(card).getByRole('button', { name: 'Reemplazar' })).toBeInTheDocument()
    // las demás ranuras siguen sin logo
    expect(screen.getAllByText('Sin logo: se usa el de Teikem')).toHaveLength(3)

    await user.click(within(card).getByRole('button', { name: 'Quitar Lockup (fondo claro)' }))
    await waitFor(() => expect(within(card).queryByRole('img')).toBeNull())
    expect(mock.requests.some((r) => r.method === 'DELETE' && r.url.pathname === '/api/v1/tenant/brand/logos/lockup')).toBe(true)
    expect(screen.getAllByText('Sin logo: se usa el de Teikem')).toHaveLength(4)
  })

  it('Marca → logos: los 400, 413 y 415 del servidor salen junto a la ranura, con su mensaje exacto', async () => {
    const user = userEvent.setup()
    wrap('/system/settings?tab=brand')
    const input = await screen.findByLabelText('Archivo de Marca cuadrada (fondo claro)')
    const card = screen.getByTestId('logo-mark')
    for (const [status, title] of [
      [415, 'Formato no admitido: el logo debe ser SVG, PNG, JPG o WebP.'],
      [413, 'El logo supera el tamaño máximo de 512 KB.'],
      [400, 'El SVG no se acepta: contiene el elemento <script>, que puede ejecutar código o cargar contenido externo.'],
    ] as const) {
      mock.logoFail = { status, title }
      await user.upload(input, new File(['x'], 'logo.svg', { type: 'image/svg+xml' }))
      expect(await within(card).findByRole('alert')).toHaveTextContent(title)
    }
    // el error es de esa ranura: las otras no lo muestran, y no quedó ningún logo
    expect(within(screen.getByTestId('logo-lockup')).queryByRole('alert')).toBeNull()
    expect(screen.getAllByText('Sin logo: se usa el de Teikem')).toHaveLength(4)
    // una subida buena limpia el aviso
    mock.logoFail = null
    await user.upload(input, new File(['png'], 'logo.png', { type: 'image/png' }))
    await waitFor(() => expect(within(card).queryByRole('alert')).toBeNull())
  })

  it('Marca → logos: un archivo de más de 512 KB se avisa sin mandarlo', async () => {
    const user = userEvent.setup()
    wrap('/system/settings?tab=brand')
    const input = await screen.findByLabelText('Archivo de Lockup (fondo oscuro)')
    await user.upload(input, new File([new Uint8Array(512 * 1024 + 1)], 'grande.png', { type: 'image/png' }))
    expect(await within(screen.getByTestId('logo-lockup-inverted')).findByRole('alert')).toHaveTextContent('El logo supera el tamaño máximo de 512 KB.')
    expect(mock.requests.some((r) => r.method === 'PUT' && r.url.pathname.startsWith('/api/v1/tenant/brand/logos/'))).toBe(false)
  })

  it('Marca → logos: sin admin.tenant se ven los logos pero no hay cómo subir ni quitar', async () => {
    mock.logos = [{ slot: 'mark', contentType: 'image/svg+xml', sizeBytes: 1536, eTag: 'e1', updatedAtUtc: '2026-10-03T12:00:00' }]
    wrap('/system/settings?tab=brand', ['inventory.view'])
    const card = await screen.findByTestId('logo-mark')
    await waitFor(() => expect(within(card).getByRole('img', { name: 'Vista previa de Marca cuadrada (fondo claro)' })).toBeInTheDocument())
    expect(within(card).getByText('SVG · 2 KB')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Subir' })).toBeNull()
    expect(screen.queryByRole('button', { name: /Quitar|Reemplazar/ })).toBeNull()
    expect(screen.queryByLabelText(/Archivo de/)).toBeNull()
    expect(screen.getByRole('button', { name: /Bosque/ })).toBeDisabled()
  })

  it('Marca: el 400 del servidor al guardar sale junto a los colores con su mensaje', async () => {
    const user = userEvent.setup()
    wrap('/system/settings?tab=brand')
    await screen.findByText('La combinación pasa las validaciones')
    mock.settingsFail = { status: 400, field: 'brandingJson', title: 'El tema predefinido \'bosque\' no existe.' }
    await user.click(screen.getByRole('button', { name: /Bosque/ }))
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    expect(await screen.findByTestId('brand-save-error')).toHaveTextContent("El tema predefinido 'bosque' no existe.")
    // al guardar bien, el aviso se va
    mock.settingsFail = null
    await user.click(screen.getByRole('button', { name: 'Guardar cambios' }))
    await waitFor(() => expect(screen.queryByTestId('brand-save-error')).toBeNull())
  })
})
