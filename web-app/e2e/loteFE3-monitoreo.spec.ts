// Recorrido del Monitoreo de ruta (Lote F-E3) contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000; compañía demo, admin con
// todos los permisos, módulos LTL_GROUND y CATALOG encendidos).
//
// Datos propios de cada corrida (por API), en el DÍA DE HOY de la compañía y en una zona propia (el filtro de zona aísla los totales de otras corridas):
// un cliente con contrato y tarifa, dos choferes con licencia, dos vehículos y seis órdenes. La ruta A (4 órdenes, las cuatro con pin manual, optimizada
// para tener llegadas planeadas) se despacha y se INICIA (En curso); la ruta B (2 órdenes sin pin) solo se despacha (Despachada).
//
// 1) El río con los totales reales y la insignia neutra «Sin señal de choferes» (todavía no hay quien escriba pings).  2) La lista con las dos rutas,
// su estatus real y «Sin ping»; la primera queda elegida con su plano de 4 pines, las paradas pendientes y la telemetría «sin ping».  3) Elegir la
// ruta B: plano sin coordenadas.  4) Buscar por chofer: la lista se acorta y los totales NO cambian.  5) «Incluir completadas» consulta con
// includeCompleted=true.  6) «Actualizar» consulta el monitor y el detalle.  7) «Siguiendo en vivo» se apaga, se recuerda y sobrevive a recargar.
// 8) «Abrir en Sala de despacho» lleva a la ruta.  9) Una fecha sin rutas: vacío honesto.  10) Respuesta del monitor SIMULADA con un ping reciente
// (no hay endpoint que escriba pings: es de la app del chofer, P7): insignia EN VIVO, velocidad, rumbo y punto vivo en el plano.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/fe3-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']
type OrderDetailDto = components['schemas']['OrderDetailDto']
type LocationDto = components['schemas']['LocationDto']
type TripDetailDto = components['schemas']['TripDetailDto']
type DispatchZoneDto = components['schemas']['DispatchZoneDto']
type DriverDetailDto = components['schemas']['DriverDetailDto']
type VehicleDetailDto = components['schemas']['VehicleDetailDto']
type MonitorDto = components['schemas']['MonitorDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

/** Captura de documentación (solo escritorio, para no pisarla con la móvil). */
async function shot(page: Page, isMobile: boolean, name: string, height = 1000, only?: Locator, width?: number) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.mouse.move(0, 0)
  await page.evaluate(() => document.querySelectorAll('*').forEach((el) => { if (el.scrollTop > 0) el.scrollTop = 0 }))
  const size = page.viewportSize()
  if (size) await page.setViewportSize({ width: width ?? size.width, height })
  await (only ?? page).screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  if (size) await page.setViewportSize(size)
}

async function apiToken(request: APIRequestContext): Promise<string> {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  expect(res.ok()).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password, tenantId: tenant?.tenantId } })
    body = (await res.json()) as AuthResultDto
  }
  expect(body.status).toBe('ok')
  return body.tokens?.accessToken ?? ''
}

async function login(page: Page) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(ADMIN.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(ADMIN.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login')
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/')
}

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que F14–F18). */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    document.querySelectorAll('.stage *, .bar *, .scrim *').forEach((el) => {
      if (el.parentElement?.closest('.seg')) return
      if (el.parentElement?.closest('.dt-scroll')) return
      const style = getComputedStyle(el)
      if (style.visibility === 'hidden' || style.display === 'none') return
      if (el.matches('.sw input')) return
      const r = el.getBoundingClientRect()
      if (r.width > 0 && (r.right > vw + 1 || r.left < -1)) out.push(`${el.tagName.toLowerCase()}.${String(el.getAttribute('class') ?? '').trim().replace(/\s+/g, '.')} [${Math.round(r.left)}, ${Math.round(r.right)}]`)
    })
    return { scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth, offenders: out }
  })
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
  expect(offenders).toEqual([])
}

async function ok<T>(p: Promise<import('@playwright/test').APIResponse>, what: string): Promise<T> {
  const res = await p
  expect(res.ok(), `${what}: ${res.status()} ${await res.text()}`).toBeTruthy()
  return (await res.json()) as T
}

/** "Hoy" de la compañía demo (America/Puerto_Rico) y días después, 'YYYY-MM-DD'. */
function tenantDay(plus = 0): string {
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'America/Puerto_Rico', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date())
  const d = new Date(`${today}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + plus)
  return d.toISOString().slice(0, 10)
}

test.describe('Monitoreo de ruta (Lote F-E3)', () => {
  test('río de totales, lista, búsqueda, plano y paradas, telemetría sin ping, completadas, actualizar, seguimiento, enlace a la Sala de despacho, vacío y ping simulado', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const clientName = `Cliente FE3 ${suffix}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const day = tenantDay(0) // el día de hoy: así la ruta aparece en el monitor sin esperar a nadie
    const license = tenantDay(300)

    // ---- Datos: cliente con contrato y tarifa, zona con su pueblo, dos choferes con licencia, dos vehículos y seis órdenes ----
    const client = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName, contract: { startDate: tenantDay(-30), title: 'Contrato FE3' } } }), 'cliente')
    const contractId = client.currentContract?.publicId ?? ''
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType: 'BOX', rate: 100, effectiveFrom: tenantDay(-30) } }), 'tarifa')
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/status`, { headers, data: { toCode: 'ACTIVE' } }), 'contrato activo')
    await ok(request.patch(`${API_URL}/api/v1/clients/${client.publicId}/number-settings`, { headers, data: { orderNumberFormat: `F3${LETTERS}${isMobile ? 'M' : 'D'}-#####` } }), 'formato de número')

    const town = `Pueblo F3 ${suffix}`
    const zone = await ok<DispatchZoneDto>(request.post(`${API_URL}/api/v1/dispatch-zones`, { headers, data: { code: `FZ${suffix}`, name: `Zona F3 ${suffix}` } }), 'zona')
    await ok(request.post(`${API_URL}/api/v1/dispatch-zones/${zone.id}/members`, { headers, data: { matchType: 'MUNICIPALITY', matchValue: town } }), 'miembro')
    const mkDriver = async (code: string, name: string) => {
      const d = await ok<DriverDetailDto>(request.post(`${API_URL}/api/v1/drivers`, { headers, data: { code, fullName: name, dispatchZoneId: zone.id } }), `chofer ${name}`)
      await ok(request.post(`${API_URL}/api/v1/drivers/${d.publicId}/licenses`, { headers, data: { licenseClass: 'CDL_A', licenseNumber: `L-${code}`, expiryDate: license } }), `licencia ${name}`)
      return d
    }
    const driverA = await mkDriver(`DA${suffix}`, `Elisa Monitor ${suffix}`)
    const driverB = await mkDriver(`DB${suffix}`, `Bruno Ruta ${suffix}`)
    const mkVehicle = (code: string) => ok<VehicleDetailDto>(request.post(`${API_URL}/api/v1/vehicles`, { headers, data: { code, plateNumber: `P${code}`, vin: `VIN${code}` } }), `vehículo ${code}`)
    const vehicleA = await mkVehicle(`VA${suffix}`)
    const vehicleB = await mkVehicle(`VB${suffix}`)

    const place = (name: string, zip: string) =>
      ok<LocationDto>(request.post(`${API_URL}/api/v1/locations`, { headers, data: { clientPublicId: client.publicId, locationType: 'DELIVERY', name, line1: 'Calle Luna 45', city: town, postalCode: zip, country: 'PR', allowDupInvoice: true } }), `consignatario ${name}`)
    const names = ['Farmacia Sol', 'Colmado Luna', 'Taller Díaz', 'Ferretería Mar', 'Panadería Río', 'Librería Faro']
    const orders: OrderDetailDto[] = []
    for (const [i, n] of names.entries()) {
      const cons = await place(`${n} ${suffix}`, `0096${i}`)
      orders.push(
        await ok<OrderDetailDto>(
          request.post(`${API_URL}/api/v1/orders`, { headers, data: { clientPublicId: client.publicId, consigneeLocationPublicId: cons.publicId, serviceType: 'STANDARD', packages: [{ packageType: 'BOX', pieces: 2, description: 'Ropa' }], confirmNow: true } }),
          'orden',
        ),
      )
    }
    const mkTrip = async (driver: DriverDetailDto, vehicle: VehicleDetailDto, from: number, to: number) => {
      const trip = await ok<TripDetailDto>(
        request.post(`${API_URL}/api/v1/trips`, { headers, data: { planDate: day, dispatchZoneId: zone.id, driverPublicId: driver.publicId, vehiclePublicId: vehicle.publicId, plannedStartUtc: `${day}T12:00:00Z` } }),
        'ruta',
      )
      await ok(request.post(`${API_URL}/api/v1/trips/${trip.publicId}/orders`, { headers, data: { orderPublicIds: orders.slice(from, to).map((o) => o.publicId) } }), 'órdenes en la ruta')
      return trip
    }
    const detailOf = (id: string) => ok<TripDetailDto>(request.get(`${API_URL}/api/v1/trips/${id}`, { headers }), 'ficha de la ruta')
    const tripA = await mkTrip(driverA, vehicleA, 0, 4)
    const tripB = await mkTrip(driverB, vehicleB, 4, 6)
    const idA = tripA.publicId ?? ''
    const idB = tripB.publicId ?? ''

    // La ruta A: cuatro pines manuales (para ver el plano) y optimizada (para tener llegadas planeadas); luego despachada e iniciada
    const spots = [
      { lat: 18.3985, lng: -66.1557 },
      { lat: 18.2341, lng: -66.0485 },
      { lat: 18.4655, lng: -66.1057 },
      { lat: 18.3, lng: -66.25 },
    ]
    for (const [i, p] of spots.entries()) {
      const cur = await detailOf(idA)
      await ok(request.put(`${API_URL}/api/v1/trips/${idA}/stops/${cur.stops?.[i].id}/location`, { headers, data: { ...p, rowVersion: cur.rowVersion } }), `pin ${i + 1}`)
    }
    const beforeOpt = await detailOf(idA)
    await ok(request.post(`${API_URL}/api/v1/trips/${idA}/optimize`, { headers, data: { rowVersion: beforeOpt.rowVersion } }), 'optimizar')
    await ok(request.post(`${API_URL}/api/v1/trips/dispatch`, { headers, data: { tripPublicIds: [idA, idB] } }), 'despachar')
    const beforeStart = await detailOf(idA)
    await ok(request.post(`${API_URL}/api/v1/trips/${idA}/start`, { headers, data: { rowVersion: beforeStart.rowVersion } }), 'iniciar la ruta A')
    const stopsA = (await detailOf(idA)).stops ?? []
    expect(stopsA).toHaveLength(4)

    // Lo que dice el servidor (las pruebas comparan la pantalla con esto)
    const serverMonitor = await ok<MonitorDto>(request.get(`${API_URL}/api/v1/trips/monitor?date=${day}&dispatchZoneId=${zone.id}&includeCompleted=false`, { headers }), 'monitor')
    expect(serverMonitor.totals).toMatchObject({ trips: 2, totalStops: 6, completedStops: 0, failedStops: 0, pendingStops: 6, overStopLimitTrips: 0, tripsWithoutPing: 2 })
    const rowA = serverMonitor.trips?.find((x) => x.publicId === idA)
    const rowB = serverMonitor.trips?.find((x) => x.publicId === idB)
    expect(rowA?.statusCode).toBe('IN_PROGRESS')
    expect(rowB?.statusCode).toBe('DISPATCHED')

    await login(page)
    const river = page.getByRole('group', { name: 'Totales de las rutas' })
    const nodeOf = (label: string) => river.locator('.node', { has: page.getByText(label, { exact: true }) })
    const list = page.getByRole('list', { name: 'Rutas activas' })
    const cardOf = (code: string) => list.getByRole('button', { name: new RegExp(code) })
    const planeOf = (code: string) => page.getByRole('group', { name: `Plano de la ruta ${code}` })
    const zoneLabel = `${zone.code} · ${zone.name}`

    // ---- 1. Entrar por el menú y aislar la zona propia ----
    await page.goto('/ops/monitor')
    await expect(page.getByRole('heading', { level: 1, name: 'Monitoreo de ruta' })).toBeVisible()
    await expect(page.getByRole('combobox', { name: 'Zona' })).toBeVisible()
    await expect(page.getByRole('combobox', { name: 'Zona' }).locator('option', { hasText: zoneLabel })).toHaveCount(1)
    await page.getByRole('combobox', { name: 'Zona' }).selectOption({ label: zoneLabel })

    // ---- 2. El río: los totales reales del servidor ----
    await expect(nodeOf('Rutas activas')).toContainText('2')
    await expect(nodeOf('Paradas restantes')).toContainText('6')
    await expect(nodeOf('Progreso promedio')).toContainText('0%')
    await expect(nodeOf('Progreso promedio')).toContainText('0 de 6 paradas atendidas')
    await expect(nodeOf('Sin ping')).toContainText('2')
    await expect(nodeOf('Sobre el máximo de paradas')).toContainText('0')
    await expect(nodeOf('Próxima en completar')).not.toContainText('—') // la ruta A está optimizada: tiene llegadas planeadas
    await expect(page.getByText('Sin señal de choferes')).toBeVisible()
    await expect(page.getByText('EN VIVO', { exact: true })).toHaveCount(0)
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- 3. La lista: las dos rutas con su estatus real, avance y «Sin ping» ----
    await expect(list.getByRole('listitem')).toHaveCount(2)
    await expect(cardOf(rowA?.code ?? '')).toContainText('En curso')
    await expect(cardOf(rowA?.code ?? '')).toContainText('0%')
    await expect(cardOf(rowA?.code ?? '')).toContainText(`Elisa Monitor ${suffix} · Sin ping`)
    await expect(cardOf(rowB?.code ?? '')).toContainText('Despachado')
    await expect(cardOf(rowB?.code ?? '')).toContainText(`Bruno Ruta ${suffix} · Sin ping`)

    // ---- 4. La primera ruta queda elegida: plano con sus 4 pines, paradas pendientes y telemetría sin ping ----
    await expect(cardOf(rowA?.code ?? '')).toHaveAttribute('aria-current', 'true')
    const planeA = planeOf(rowA?.code ?? '')
    await expect(planeA).toBeVisible()
    await expect(planeA.getByRole('button', { name: /^Parada \d/ })).toHaveCount(4)
    await expect(planeA.locator('g.rmap-pin.manual')).toHaveCount(4)
    const box = (await planeA.boundingBox())!
    for (const pin of await planeA.getByRole('button', { name: /^Parada \d/ }).all()) {
      const b = (await pin.boundingBox())!
      expect(b.x).toBeGreaterThanOrEqual(box.x - 1)
      expect(b.x + b.width).toBeLessThanOrEqual(box.x + box.width + 1)
    }
    const stopsList = page.getByRole('list', { name: 'Paradas de la ruta' })
    await expect(stopsList.getByRole('listitem')).toHaveCount(4)
    for (const row of await stopsList.getByRole('listitem').all()) {
      await expect(row).toHaveAttribute('data-state', 'pending')
      await expect(row).toContainText('Pendiente')
    }
    await expect(stopsList.getByRole('listitem').first()).toContainText('Llegada estimada')
    await expect(page.locator('.telemcell', { hasText: 'Velocidad' })).toContainText('—')
    await expect(page.locator('.telemcell', { hasText: 'Rumbo' })).toContainText('—')
    await expect(page.locator('.mon-pingrow .ping')).toHaveText('Sin ping')
    await expect(page.getByText(/Todavía no hay ubicación del chofer para esta ruta/)).toBeVisible()
    await expect(page.getByText('Señal GPS')).toHaveCount(0)
    await expect(page.getByText(/Batería/)).toHaveCount(0)
    // elegir una parada la marca en el plano
    await stopsList.getByRole('button', { name: /^Parada 2:/ }).click()
    await expect(planeA.getByRole('button', { name: /^Parada 2 · / })).toHaveAttribute('aria-pressed', 'true')
    if (isMobile) await page.screenshot({ path: testInfo.outputPath('movil-ruta-con-pines.png'), fullPage: true }) // para revisar a ojo
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe3-vista-general', 1300, undefined, 1600)

    // ---- 5. Elegir la ruta B: sin coordenadas ----
    await cardOf(rowB?.code ?? '').click()
    await expect(page).toHaveURL(new RegExp(`trip=${idB}`))
    await expect(cardOf(rowB?.code ?? '')).toHaveAttribute('aria-current', 'true')
    await expect(planeOf(rowB?.code ?? '')).toBeVisible()
    await expect(page.getByText('Ninguna parada tiene coordenadas todavía')).toBeVisible()
    await expect(page.getByText('Sin ubicación: 2')).toBeVisible()
    await expect(page.getByText('Sin pin exacto: 2')).toBeVisible()
    await expect(stopsList.getByRole('listitem')).toHaveCount(2)
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe3-ruta-sin-pines', 1100, undefined, 1600)

    // ---- 6. Buscar por chofer: la lista se acorta y los totales NO cambian ----
    const searchRequest = page.waitForRequest((r) => r.url().includes('/trips/monitor') && r.url().includes('search=Bruno'))
    await page.getByRole('searchbox', { name: 'Buscar ruta, chofer o zona…' }).fill('Bruno')
    await searchRequest
    await expect(list.getByRole('listitem')).toHaveCount(1)
    await expect(cardOf(rowB?.code ?? '')).toBeVisible()
    await expect(nodeOf('Rutas activas')).toContainText('2')
    await expect(nodeOf('Paradas restantes')).toContainText('6')
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe3-busqueda', 1000, undefined, 1600)
    await page.getByRole('searchbox', { name: 'Buscar ruta, chofer o zona…' }).fill('zzz-no-existe')
    await expect(page.getByText('Ninguna ruta coincide con la búsqueda.')).toBeVisible()
    await page.getByRole('searchbox', { name: 'Buscar ruta, chofer o zona…' }).fill('')
    await expect(list.getByRole('listitem')).toHaveCount(2)

    // ---- 7. Incluir completadas: consulta con includeCompleted=true (hoy no hay ninguna completada) ----
    const incl = page.waitForRequest((r) => r.url().includes('/trips/monitor') && r.url().includes('includeCompleted=true'))
    await page.getByRole('switch', { name: 'Incluir completadas' }).check()
    await incl
    await expect(nodeOf('Rutas (con completadas)')).toContainText('2')
    await expect(list.getByRole('listitem')).toHaveCount(2)
    const back = page.waitForRequest((r) => r.url().includes('/trips/monitor') && r.url().includes('includeCompleted=false'))
    await page.getByRole('switch', { name: 'Incluir completadas' }).uncheck()
    await back
    await expect(nodeOf('Rutas activas')).toBeVisible()

    // ---- 8. Actualizar: consulta el monitor y el detalle de la ruta elegida ----
    await expect(page.getByText(/^Actualizado hace \d+ s$/)).toBeVisible()
    const monitorReq = page.waitForRequest((r) => r.url().includes('/trips/monitor'))
    const detailReq = page.waitForRequest((r) => r.url().includes(`/trips/${idB}`) && !r.url().includes('monitor'))
    await page.getByRole('button', { name: 'Actualizar' }).click()
    await monitorReq
    await detailReq
    await expect(page.getByText(/^Actualizado hace [0-2] s$/)).toBeVisible()

    // ---- 9. «Siguiendo en vivo»: se apaga, se recuerda y sobrevive a recargar ----
    const follow = page.getByRole('switch', { name: 'Siguiendo en vivo' })
    await expect(follow).toBeChecked()
    await follow.uncheck()
    expect(await page.evaluate(() => window.localStorage.getItem('teikem.monitor.follow'))).toBe('off')
    await page.reload()
    await expect(page.getByRole('switch', { name: 'Siguiendo en vivo' })).not.toBeChecked()
    await page.getByRole('switch', { name: 'Siguiendo en vivo' }).check()
    expect(await page.evaluate(() => window.localStorage.getItem('teikem.monitor.follow'))).toBe('on')

    // ---- 10. Abrir en Sala de despacho ----
    await page.getByRole('combobox', { name: 'Zona' }).selectOption({ label: zoneLabel })
    await expect(cardOf(rowB?.code ?? '')).toHaveAttribute('aria-current', 'true') // ?trip= sobrevive a recargar
    const open = page.getByRole('link', { name: 'Abrir en Sala de despacho' })
    await expect(open).toHaveAttribute('href', `/ops/dispatch?date=${day}&trip=${idB}`)
    await open.click()
    await expect(page).toHaveURL(new RegExp(`/ops/dispatch\\?date=${day}&trip=${idB}`))
    await expect(page.getByRole('heading', { name: /Ruta activa/ })).toContainText(rowB?.code ?? '')
    await page.goBack()
    await expect(page.getByRole('heading', { level: 1, name: 'Monitoreo de ruta' })).toBeVisible()

    // ---- 11. Una fecha sin rutas: vacío honesto con enlace a la Sala de despacho ----
    const empty = tenantDay(31) // un día que ningún otro recorrido usa (F-E2a: 2..29, F-E2b: 32..58)
    await page.goto(`/ops/monitor?date=${empty}`)
    await expect(page.getByText('No hay rutas para esta fecha')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Abrir la Sala de despacho' })).toHaveAttribute('href', `/ops/dispatch?date=${empty}`)
    await expect(page.getByText('Seleccione una ruta de la lista')).toBeVisible()
    await expect(nodeOf('Rutas activas')).toContainText('0')
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe3-sin-rutas', 820, undefined, 1600)

    // ---- 12. Ping reciente SIMULADO (no existe un endpoint que escriba pings): insignia EN VIVO, velocidad, rumbo y punto vivo ----
    await page.route('**/api/v1/trips/monitor*', async (route) => {
      const res = await route.fetch()
      const body = (await res.json()) as MonitorDto
      const at = new Date(Date.now() - 12_000).toISOString().replace('Z', '')
      for (const t of body.trips ?? []) if (t.publicId === idA) t.lastPing = { point: { lat: 18.35, lng: -66.1 }, speedKmh: 42.4, headingDeg: 135, capturedAtUtc: at, receivedAtUtc: at, linkedToTrip: true }
      if (body.totals) body.totals.tripsWithoutPing = 1
      await route.fulfill({ response: res, json: body })
    })
    await page.goto(`/ops/monitor?date=${day}&trip=${idA}`)
    await page.getByRole('combobox', { name: 'Zona' }).selectOption({ label: zoneLabel })
    await expect(page.getByText('EN VIVO', { exact: true })).toBeVisible()
    await expect(page.locator('.telemcell', { hasText: 'Velocidad' })).toContainText('42 km/h')
    await expect(page.locator('.telemcell', { hasText: 'Rumbo' })).toContainText('135° SE')
    await expect(page.locator('.mon-pingrow .ping')).toContainText(/Último ping hace \d+ s/)
    await expect(cardOf(rowA?.code ?? '')).toContainText(/Último ping hace \d+ s/)
    await expect(nodeOf('Sin ping')).toContainText('1')
    await expect(planeOf(rowA?.code ?? '').getByRole('img', { name: /^Posición del chofer/ })).toBeVisible()
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe3-con-ping-ejemplo', 1300, undefined, 1600)
    await page.unroute('**/api/v1/trips/monitor*')
  })
})
