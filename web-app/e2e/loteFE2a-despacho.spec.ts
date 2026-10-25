// Recorrido de la Sala de despacho (Lote F-E2a) contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000;
// compañía demo, admin con todos los permisos, módulos LTL_GROUND y CATALOG encendidos).
//
// Datos propios de cada corrida (por API): un cliente con contrato y tarifa, dos zonas (A y B) con su pueblo, choferes (uno estándar de la zona A
// con licencia, otros con licencia, uno SIN licencia = no disponible), tres vehículos y uno en mantenimiento, y órdenes recogidas por pueblo.
// Todo ocurre en un día futuro (hoy + 2) para que «Rutas de hoy» solo traiga lo de la corrida; «Órdenes sin asignar» se filtra por el cliente.
//
// 1) Planificar el día (zona A): crea la ruta con el chofer estándar y le asigna las órdenes.   2) Nueva ruta (zona B, chofer, vehículo, salida).
// 3) Agregar órdenes: una no elegible con su motivo (422) y luego las buenas.   4) Reordenar con ▲ ▼ y quitar.   5) Optimizar (dos veces: comparación).
// 6) Chofer y vehículo: asignar, y un chofer sin licencia = 409 del servidor.   7) Reasignar por zona (y cero rutas).
// 8) Despachar: bloqueante (no se marca), aviso (confirma) y lote.   9) Iniciar ruta, cerrar con 422 por órdenes sin terminar.   10) Eliminar ruta.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/fe2a-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang, manualTarget } from './manualLang'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']
type OrderDetailDto = components['schemas']['OrderDetailDto']
type LocationDto = components['schemas']['LocationDto']
type TripDetailDto = components['schemas']['TripDetailDto']
type TripListItemDto = components['schemas']['TripListItemDto']
type DispatchZoneDto = components['schemas']['DispatchZoneDto']
type DriverDetailDto = components['schemas']['DriverDetailDto']
type VehicleDetailDto = components['schemas']['VehicleDetailDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(420_000)

/** Captura de documentación (solo escritorio, para no pisarla con la móvil). */
async function shot(page: Page, isMobile: boolean, name: string, height = 1000, only?: Locator) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.mouse.move(0, 0)
  await page.evaluate(() => document.querySelectorAll('*').forEach((el) => { if (el.scrollTop > 0) el.scrollTop = 0 }))
  const size = page.viewportSize()
  if (size) await page.setViewportSize({ width: size.width, height })
  const manualShot1 = await manualTarget((only ?? page))
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  await manualShot1.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  await manualLang(page, 'es')
  if (size) await page.setViewportSize(size)
}

/** Deja a la vista solo los choferes de la corrida (la base trae los de otros lotes) para las capturas. */
async function onlyMyDrivers(page: Page, suffix: string) {
  await page.evaluate((s) => {
    const list = document.querySelector('.disp-drivers')
    if (!list) return
    let n = 0
    list.querySelectorAll('li').forEach((li) => {
      const mine = (li.textContent ?? '').includes(s)
      ;(li as HTMLElement).style.display = mine ? '' : 'none'
      if (mine) n++
    })
    const badge = list.closest('.panel')?.querySelector('.ph2 .r')
    if (badge) badge.textContent = String(n)
  }, suffix)
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

test.describe('Sala de despacho (Lote F-E2a)', () => {
  test('planificar el día, nueva ruta, agregar órdenes, ▲ ▼, optimizar, chofer y vehículo, reasignar, despachar, iniciar, cerrar y eliminar', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const clientName = `Cliente FE2A ${suffix}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    // un día futuro SIN rutas (las corridas anteriores dejaron las suyas en otros días): «Rutas de hoy» solo trae lo de esta corrida
    const busy = new Set((await ok<TripListItemDto[]>(request.get(`${API_URL}/api/v1/trips?from=${tenantDay(2)}&to=${tenantDay(59)}`, { headers }), 'rutas ya existentes')).map((t) => t.planDate))
    // rango propio (2..29): el recorrido de F-E2b usa 32..58 y ambos corren en paralelo
    let offset = 2 + (Math.floor(Date.now() / 1000) % 28)
    while (busy.has(tenantDay(offset)) && offset < 29) offset++
    const day = tenantDay(offset)
    const dayUi = day.replace(/(\d{4})-(\d\d)-(\d\d)/, "$2/$3/$1") // la interfaz muestra MM/DD/YYYY
    const license = tenantDay(300)

    // ---- Datos: cliente con contrato y tarifa; zonas A y B con su pueblo; choferes y vehículos; órdenes recogidas ----
    const client = await ok<ClientDetailDto>(
      request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName, contract: { startDate: tenantDay(-30), title: 'Contrato FE2A' } } }),
      'cliente',
    )
    const contractId = client.currentContract?.publicId ?? ''
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType: 'BOX', rate: 100, effectiveFrom: tenantDay(-30) } }), 'tarifa')
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/status`, { headers, data: { toCode: 'ACTIVE' } }), 'contrato activo')
    await ok(request.patch(`${API_URL}/api/v1/clients/${client.publicId}/number-settings`, { headers, data: { orderNumberFormat: `E2${LETTERS}${isMobile ? 'M' : 'D'}-#####` } }), 'formato de número')

    const townA = `Pueblo A E2A ${suffix}`
    const townB = `Pueblo B E2A ${suffix}`
    const zoneA = await ok<DispatchZoneDto>(request.post(`${API_URL}/api/v1/dispatch-zones`, { headers, data: { code: `EA${suffix}`, name: `Zona A E2A ${suffix}` } }), 'zona A')
    const zoneB = await ok<DispatchZoneDto>(request.post(`${API_URL}/api/v1/dispatch-zones`, { headers, data: { code: `EB${suffix}`, name: `Zona B E2A ${suffix}` } }), 'zona B')
    await ok(request.post(`${API_URL}/api/v1/dispatch-zones/${zoneA.id}/members`, { headers, data: { matchType: 'MUNICIPALITY', matchValue: townA } }), 'miembro A')
    await ok(request.post(`${API_URL}/api/v1/dispatch-zones/${zoneB.id}/members`, { headers, data: { matchType: 'MUNICIPALITY', matchValue: townB } }), 'miembro B')

    const driver = async (code: string, name: string, zoneId: number | null, withLicense: boolean) => {
      const d = await ok<DriverDetailDto>(request.post(`${API_URL}/api/v1/drivers`, { headers, data: { code, fullName: name, ...(zoneId ? { dispatchZoneId: zoneId } : {}) } }), `chofer ${code}`)
      if (withLicense) await ok(request.post(`${API_URL}/api/v1/drivers/${d.publicId}/licenses`, { headers, data: { licenseClass: 'CDL_A', licenseNumber: `L-${code}`, expiryDate: license } }), `licencia ${code}`)
      return d
    }
    const driverA = await driver(`DA${suffix}`, `Ana Rivera ${suffix}`, zoneA.id, true) // chofer estándar de la zona A
    const driverB = await driver(`DB${suffix}`, `Beto Colón ${suffix}`, zoneB.id, true)
    const driverC = await driver(`DC${suffix}`, `Carla Díaz ${suffix}`, null, true)
    const driverN = await driver(`DN${suffix}`, `Nelson Soto ${suffix}`, null, false) // sin licencia: no disponible
    const vehicle = (code: string, n: number) => ok<VehicleDetailDto>(request.post(`${API_URL}/api/v1/vehicles`, { headers, data: { code, plateNumber: `P${n}${suffix}`, vin: `VIN${n}${suffix}` } }), `vehículo ${code}`)
    const v1 = await vehicle(`V1${suffix}`, 1)
    const v2 = await vehicle(`V2${suffix}`, 2)
    const v3 = await vehicle(`V3${suffix}`, 3)
    const vm = await vehicle(`VM${suffix}`, 4)
    await ok(request.post(`${API_URL}/api/v1/vehicles/${vm.publicId}/status`, { headers, data: { toCode: 'MAINTENANCE' } }), 'vehículo en mantenimiento')

    const place = (name: string, city: string, zip: string) =>
      ok<LocationDto>(
        request.post(`${API_URL}/api/v1/locations`, { headers, data: { clientPublicId: client.publicId, locationType: 'DELIVERY', name, line1: 'Calle Luna 45', city, postalCode: zip, country: 'PR', allowDupInvoice: true } }),
        `consignatario ${name}`,
      )
    const consA = await place(`Farmacia Sol ${suffix}`, townA, '00959')
    const consB = await place(`Colmado Luna ${suffix}`, townB, '00725')
    const newOrder = (cons: LocationDto) =>
      ok<OrderDetailDto>(
        request.post(`${API_URL}/api/v1/orders`, { headers, data: { clientPublicId: client.publicId, consigneeLocationPublicId: cons.publicId, serviceType: 'STANDARD', packages: [{ packageType: 'BOX', pieces: 2, description: 'Ropa' }], confirmNow: true } }),
        'orden',
      )
    const aOrders = [await newOrder(consA), await newOrder(consA), await newOrder(consA), await newOrder(consA)]
    const b1 = await newOrder(consB)
    const b2 = await newOrder(consB)
    const b3 = await newOrder(consB) // la que no es elegible mientras se niega ASSIGN_TRIP
    const b4 = await newOrder(consB)
    const b5 = await newOrder(consB)

    await login(page)
    await page.goto(`/ops/dispatch?date=${day}`)
    const heading = page.getByRole('heading', { level: 1, name: 'Sala de despacho' })
    await expect(heading).toBeVisible()
    const unassigned = page.locator('.disp-un-list label.unrow')
    const todayList = page.getByRole('list', { name: 'Rutas de hoy' })
    const activeHeading = page.getByRole('heading', { name: /Ruta activa/ })
    const stopsList = page.getByRole('list', { name: 'Envíos en esta ruta' })
    const header = page.locator('.head')
    const dialog = () => page.getByRole('dialog')
    const tripsOfDay = async () => (await ok<TripListItemDto[]>(request.get(`${API_URL}/api/v1/trips?date=${day}`, { headers }), 'rutas del día')).filter((t) => t.zoneCode?.endsWith(suffix))
    const detailOf = (publicId: string) => ok<TripDetailDto>(request.get(`${API_URL}/api/v1/trips/${publicId}`, { headers }), 'ficha de la ruta')
    const pick = async (code: string) => {
      await todayList.getByRole('button', { name: new RegExp(code) }).click()
      await expect(activeHeading).toContainText(code)
    }

    // ---- 0. La pantalla: tres columnas, filtros, sin rutas en la fecha ----
    await expect(page.getByLabel('Fecha de las rutas')).toHaveValue(dayUi)
    for (const name of ['Planificar el día', 'Nueva ruta', 'Reasignar por zona', 'Optimizar', 'Despachar ruta']) await expect(header.getByRole('button', { name })).toBeVisible()
    for (const title of ['Envíos sin asignar', 'Ruta activa', 'Choferes', 'Rutas de hoy']) await expect(page.getByRole('heading', { name: title })).toBeVisible()
    await expect(page.getByText('No hay rutas para esta fecha. Use «Planificar el día» o «Nueva ruta».')).toBeVisible()
    const filterByClient = async () => {
      await page.getByRole('group', { name: 'Filtros de los envíos sin asignar' }).getByRole('combobox', { name: 'Cliente' }).fill(clientName)
      await page.getByRole('option', { name: new RegExp(clientName) }).click()
    }
    await filterByClient()
    await expect(unassigned).toHaveCount(9) // 4 de la zona A + 5 de la zona B
    await expect(unassigned.filter({ hasText: aOrders[0].orderNumber ?? '' })).toContainText(`Farmacia Sol ${suffix}`)
    await expect(unassigned.filter({ hasText: aOrders[0].orderNumber ?? '' })).toContainText(`${townA} · 00959 · EA${suffix}`)
    if (isMobile) await expectNoHorizontalScroll(page)
    await onlyMyDrivers(page, suffix)
    await shot(page, isMobile, 'fe2a-sala-inicio', 1100)

    // ---- 1. Planificar el día (solo la zona A): crea la ruta con el chofer estándar y le asigna las 4 órdenes ----
    await header.getByRole('button', { name: 'Planificar el día' }).click()
    await expect(dialog()).toContainText('Reparte por zona los envíos confirmados que no tienen ruta')
    await expect(dialog().getByLabel(/^Fecha/)).toHaveValue(dayUi)
    await dialog().getByLabel(/^Zonas/).click()
    await dialog().getByLabel(new RegExp(`EA${suffix}`)).check()
    await dialog().locator('p.d').click() // cierra la lista de zonas
    await shot(page, isMobile, 'fe2a-planificar-dia', 800, dialog())
    await dialog().getByRole('button', { name: 'Planificar' }).click()
    const planResult = page.getByRole('dialog', { name: /Día planificado/ })
    await expect(planResult.getByRole('group', { name: 'Resumen de la planificación' })).toContainText('1Rutas creadas')
    await expect(planResult.getByRole('group', { name: 'Resumen de la planificación' })).toContainText('4Envíos asignados')
    await expect(planResult).toContainText(`EA${suffix}`)
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe2a-planificar-resultado', 800, planResult)
    await planResult.getByRole('button', { name: 'Listo' }).click()
    await expect(todayList.getByRole('button')).toHaveCount(1)
    const routeA = (await tripsOfDay())[0]
    const codeA = routeA.code ?? ''
    await expect(activeHeading).toContainText(codeA)
    await expect(activeHeading).toContainText('Borrador')
    await expect(stopsList.getByRole('listitem')).toHaveCount(4)
    await expect(page.getByLabel('Chofer', { exact: true })).toHaveValue(driverA.publicId ?? '') // el chofer estándar de la zona
    await expect(unassigned).toHaveCount(5) // quedan las de la zona B
    // sin vehículo la ruta trae su bloqueante
    await expect(page.getByRole('list', { name: 'Avisos y bloqueantes' })).toContainText('La ruta no tiene vehículo asignado.')
    if (isMobile) await expectNoHorizontalScroll(page)
    await onlyMyDrivers(page, suffix)
    await shot(page, isMobile, 'fe2a-ruta-activa', 1500)

    // ---- 2. Nueva ruta (zona B, chofer, vehículo y salida) ----
    await header.getByRole('button', { name: 'Nueva ruta' }).click()
    const create = page.getByRole('dialog', { name: 'Nueva ruta' })
    await create.getByLabel(/^Fecha de la ruta/).fill('2020-01-01')
    await create.getByRole('button', { name: 'Crear ruta' }).click()
    await expect(create).toContainText('La fecha de la ruta no puede ser anterior a ayer ni posterior a 60 días.')
    await create.getByLabel(/^Fecha de la ruta/).fill(dayUi)
    await create.getByLabel('Zona de despacho').selectOption({ label: `EB${suffix} · Zona B E2A ${suffix}` })
    await expect(create.getByLabel('Chofer', { exact: true }).locator('option', { hasText: `DB${suffix}` })).toHaveCount(1)
    await create.getByLabel('Chofer', { exact: true }).selectOption({ value: driverB.publicId ?? '' })
    await create.getByLabel('Vehículo', { exact: true }).selectOption({ value: v1.publicId ?? '' })
    await create.getByLabel('Hora de salida').fill('08:00')
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe2a-nueva-ruta', 900, create)
    await create.getByRole('button', { name: 'Crear ruta' }).click()
    await expect(create).toHaveCount(0)
    await expect(todayList.getByRole('button')).toHaveCount(2)
    const routeB = (await tripsOfDay()).find((t) => t.zoneCode === `EB${suffix}`)!
    const codeB = routeB.code ?? ''
    await expect(activeHeading).toContainText(codeB)
    await expect(page.getByText('Todavía no tiene envíos asignados.')).toBeVisible()
    await expect(page.getByLabel('Chofer', { exact: true })).toHaveValue(driverB.publicId ?? '')
    await expect(page.getByLabel('Vehículo', { exact: true })).toHaveValue(v1.publicId ?? '')

    // ---- 3. Agregar órdenes a la ruta B: una no elegible con su motivo (422) y luego las buenas ----
    const row = (o: OrderDetailDto) => unassigned.filter({ hasText: o.orderNumber ?? '' })
    const setAssignTrip = (allowed: boolean) =>
      ok(request.put(`${API_URL}/api/v1/status/capabilities/TRANSPORT_ORDER?statusDomain=OrderStatus`, { headers, data: [{ statusCode: 'PICKUP', capability: 'ASSIGN_TRIP', isAllowed: allowed }] }), 'capacidad ASSIGN_TRIP')
    await setAssignTrip(false)
    try {
      await row(b3).getByRole('checkbox').check()
      await page.getByRole('button', { name: /^Añadir a la ruta/ }).click()
      const problems = page.locator('.disp-problems')
      await expect(problems).toContainText('Hay envíos que no se pueden asignar a la ruta.')
      await expect(problems).toContainText(`${b3.orderNumber}: El estatus actual no permite la acción 'ASSIGN_TRIP'.`)
      await expect(page.getByRole('button', { name: /^Añadir a la ruta \(1\)/ })).toBeVisible() // la selección se conserva
      if (isMobile) await expectNoHorizontalScroll(page)
      await shot(page, isMobile, 'fe2a-agregar-error', 1100, page.locator('.disp-cols').first())
    } finally {
      await setAssignTrip(true)
    }
    await page.getByRole('button', { name: 'Entendido' }).click()
    await row(b3).getByRole('checkbox').uncheck()
    await row(b1).getByRole('checkbox').check()
    await row(b2).getByRole('checkbox').check()
    await expect(page.getByRole('button', { name: /^Añadir a la ruta/ })).toContainText('Añadir a la ruta (2)')
    await page.getByRole('button', { name: /^Añadir a la ruta/ }).click()
    await expect(stopsList.getByRole('listitem')).toHaveCount(2)
    await expect(unassigned).toHaveCount(3)
    await expect(row(b1)).toHaveCount(0)

    // ---- 4. Reordenar con ▲ ▼ y quitar ----
    const first = () => stopsList.getByRole('listitem').first()
    await expect(first()).toContainText(b1.orderNumber ?? '')
    await expect(page.getByRole('button', { name: `Subir el envío ${b1.orderNumber}` })).toBeDisabled()
    await page.getByRole('button', { name: `Subir el envío ${b2.orderNumber}` }).click()
    await expect(first()).toContainText(b2.orderNumber ?? '')
    // el orden se ve en pantalla al instante (optimista, F-E2b) y el servidor termina de guardar un momento después: se espera, no se lee de inmediato
    await expect.poll(async () => (await detailOf(routeB.publicId ?? '')).stops?.map((s) => s.orderNumber)).toEqual([b2.orderNumber, b1.orderNumber])
    await page.getByRole('button', { name: `Quitar el envío ${b1.orderNumber} de la ruta` }).click()
    await expect(stopsList.getByRole('listitem')).toHaveCount(1)
    await expect(row(b1)).toHaveCount(1) // vuelve a «sin asignar»
    await row(b1).getByRole('checkbox').check()
    await page.getByRole('button', { name: /^Añadir a la ruta/ }).click()
    await expect(stopsList.getByRole('listitem')).toHaveCount(2)
    // una orden que otra persona asignó entretanto: el 409 del servidor sale tal cual
    await row(b3).getByRole('checkbox').check()
    const other = await ok<TripDetailDto>(request.post(`${API_URL}/api/v1/trips`, { headers, data: { planDate: day, dispatchZoneId: zoneB.id, vehiclePublicId: v3.publicId } }), 'ruta auxiliar')
    await ok(request.post(`${API_URL}/api/v1/trips/${other.publicId}/orders`, { headers, data: { orderPublicIds: [b3.publicId] } }), 'b3 en otra ruta')
    await page.getByRole('button', { name: /^Añadir a la ruta/ }).click()
    await expect(page.locator('.disp-problems')).toContainText(`El envío ya está asignado a la ruta ${other.code}.`)
    await page.getByRole('button', { name: 'Entendido' }).click()
    expect((await request.delete(`${API_URL}/api/v1/trips/${other.publicId}`, { headers, data: {} })).ok(), 'eliminar la ruta auxiliar por API').toBeTruthy()
    // la ruta activa y la fecha viven en la dirección: al recargar sigue la misma ruta (el filtro y la selección empiezan de cero)
    await page.reload()
    await expect(activeHeading).toContainText(codeB)
    await filterByClient()
    await expect(unassigned).toHaveCount(3) // b3, b4 y b5 (b4 y b5 pasan a la ruta D más adelante)

    // ---- 5. Optimizar (dos veces: la segunda ya tiene con qué compararse) ----
    await header.getByRole('button', { name: 'Optimizar' }).click()
    const optimizeNote = page.getByText(/Ruta optimizada: 2 paradas ordenadas y 0 que no cupieron\./)
    await expect(optimizeNote).toBeVisible()
    await expect(optimizeNote).toContainText('Es la primera optimización de esta ruta')
    await expect(activeHeading).toContainText('Planificado')
    await header.getByRole('button', { name: 'Optimizar' }).click()
    await expect(optimizeNote).toContainText('Duración 0 min.')
    if (isMobile) await expectNoHorizontalScroll(page)
    await onlyMyDrivers(page, suffix)
    await shot(page, isMobile, 'fe2a-optimizada', 1500)

    // ---- 6. Chofer y vehículo de la ruta A: asignar vehículo, y un chofer sin licencia = 409 ----
    await pick(codeA)
    await page.getByLabel('Vehículo', { exact: true }).selectOption({ value: v2.publicId ?? '' })
    await expect.poll(async () => (await detailOf(routeA.publicId ?? '')).vehiclePublicId).toBe(v2.publicId)
    await expect(page.getByRole('list', { name: 'Avisos y bloqueantes' }).getByText('La ruta no tiene vehículo asignado.')).toHaveCount(0)
    await expect(page.getByLabel('Chofer', { exact: true }).locator('option', { hasText: 'no disponible' })).not.toHaveCount(0)
    await page.getByLabel('Chofer', { exact: true }).selectOption({ value: driverN.publicId ?? '' })
    await expect(page.getByRole('alert').filter({ hasText: 'El chofer no está disponible para despacho: Sin licencia vigente.' })).toBeVisible()
    await expect(page.getByLabel('Chofer', { exact: true })).toHaveValue(driverA.publicId ?? '') // vuelve al chofer de la ruta
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe2a-chofer-409', 1300, page.locator('.disp-cols').first())
    await page.getByLabel('Chofer', { exact: true }).selectOption({ value: driverC.publicId ?? '' })
    await expect.poll(async () => (await detailOf(routeA.publicId ?? '')).driverPublicId).toBe(driverC.publicId)

    // ---- 7. Reasignar por zona: la zona A pasa a su chofer estándar (y una zona sin rutas abiertas no cambia nada) ----
    await header.getByRole('button', { name: 'Reasignar por zona' }).click()
    const reassign = page.getByRole('dialog', { name: 'Reasignar por zona' })
    await reassign.getByRole('button', { name: 'Reasignar' }).click()
    await expect(reassign).toContainText('Indique al menos una zona.')
    await expect(reassign).toContainText('Indique el chofer.')
    await reassign.getByLabel(/^Zonas/).click()
    await reassign.getByLabel(new RegExp(`EA${suffix}`)).check()
    await dialog().locator('p.d').click() // cierra la lista de zonas
    await reassign.getByLabel(/^Nuevo chofer/).selectOption({ value: driverA.publicId ?? '' })
    await shot(page, isMobile, 'fe2a-reasignar', 800, reassign)
    await reassign.getByRole('button', { name: 'Reasignar' }).click()
    const reassignResult = page.getByRole('dialog', { name: 'Reasignación terminada' })
    await expect(reassignResult).toContainText(`1 rutas pasaron al chofer DA${suffix}`)
    await reassignResult.getByRole('button', { name: 'Listo' }).click()
    expect((await detailOf(routeA.publicId ?? '')).driverPublicId).toBe(driverA.publicId)
    await expect(page.getByLabel('Chofer', { exact: true })).toHaveValue(driverA.publicId ?? '')

    // ---- 8. Despachar: la ruta C (sin vehículo ni paradas) bloquea; la A trae avisos y se confirma; B y D van en lote ----
    const routeC = await ok<TripDetailDto>(request.post(`${API_URL}/api/v1/trips`, { headers, data: { planDate: day, dispatchZoneId: zoneA.id } }), 'ruta C sin vehículo ni paradas')
    const routeD = await ok<TripDetailDto>(request.post(`${API_URL}/api/v1/trips`, { headers, data: { planDate: day, dispatchZoneId: zoneB.id, driverPublicId: driverC.publicId, vehiclePublicId: v3.publicId } }), 'ruta D')
    await ok(request.post(`${API_URL}/api/v1/trips/${routeD.publicId}/orders`, { headers, data: { orderPublicIds: [b4.publicId, b5.publicId] } }), 'órdenes de la ruta D')
    await page.reload()
    await expect(todayList.getByRole('button')).toHaveCount(4)
    await pick(codeA)
    await header.getByRole('button', { name: 'Despachar ruta' }).click()
    const dispatchDialog = page.getByRole('dialog', { name: 'Rutas por despachar' })
    await expect(dispatchDialog.getByLabel(routeC.code ?? '')).toBeDisabled()
    await expect(dispatchDialog.getByLabel(routeC.code ?? '').locator('xpath=ancestor::label')).toContainText('La ruta no tiene vehículo asignado.')
    await expect(dispatchDialog.getByLabel(routeC.code ?? '').locator('xpath=ancestor::label')).toContainText('La ruta no tiene paradas.')
    await expect(dispatchDialog.getByLabel(codeA)).toBeChecked() // la ruta activa llega marcada
    await expect(dispatchDialog.getByLabel(codeA).locator('xpath=ancestor::label')).toContainText('Aviso')
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe2a-despachar', 1100, dispatchDialog)
    await dispatchDialog.getByRole('button', { name: /Despachar seleccionadas \(1\)/ }).click()
    await expect(dispatchDialog).toContainText('traen avisos que no bloquean')
    expect((await detailOf(routeA.publicId ?? '')).statusCode).not.toBe('DISPATCHED') // el primer clic solo pide confirmar
    await dispatchDialog.getByRole('button', { name: /Despachar con avisos \(1\)/ }).click()
    const batch1 = page.getByRole('dialog', { name: 'Resultado del despacho' })
    await expect(batch1).toContainText('Se despacharon 1 de 1 rutas.')
    await expect(batch1).toContainText(`${codeA} — despachada`)
    await batch1.getByRole('button', { name: 'Listo' }).click()
    await expect(activeHeading).toContainText('Despachado')
    // lote B + D
    await header.getByRole('button', { name: 'Despachar ruta' }).click()
    const batchDialog = page.getByRole('dialog', { name: 'Rutas por despachar' })
    await batchDialog.getByLabel('Seleccionar todas').check()
    await expect(batchDialog.getByRole('button', { name: /Despachar seleccionadas \(2\)/ })).toBeVisible()
    await batchDialog.getByRole('button', { name: /Despachar seleccionadas \(2\)/ }).click()
    const confirmWarnings = batchDialog.getByRole('button', { name: /Despachar con avisos \(2\)/ })
    if (await confirmWarnings.isVisible()) await confirmWarnings.click() // con avisos pide confirmar; sin ellos despacha directo
    const batch2 = page.getByRole('dialog', { name: 'Resultado del despacho' })
    await expect(batch2).toContainText('Se despacharon 2 de 2 rutas.')
    await shot(page, isMobile, 'fe2a-despacho-lote', 800, batch2)
    await batch2.getByRole('button', { name: 'Listo' }).click()
    expect(await tripsOfDay().then((l) => l.filter((t) => t.statusCode === 'DISPATCHED').length)).toBe(3)

    // ---- 9. Iniciar la ruta A y cerrarla: el 422 por órdenes sin terminar ----
    await pick(codeA)
    await expect(page.getByRole('button', { name: 'Eliminar ruta' })).toHaveCount(0) // despachada: ya no se elimina
    await expect(page.getByRole('button', { name: /Bajar el envío/ })).toHaveCount(0)
    await page.getByRole('button', { name: 'Iniciar ruta' }).click()
    const startDialog = page.getByRole('dialog', { name: `Iniciar la ruta ${codeA}` })
    await startDialog.getByLabel('Comentario (opcional)').fill('Sale a las 8:00')
    await shot(page, isMobile, 'fe2a-iniciar', 700, startDialog)
    await startDialog.getByRole('button', { name: 'Iniciar ruta' }).click()
    await expect(activeHeading).toContainText('En curso')
    await page.getByRole('button', { name: 'Cerrar ruta' }).click()
    const closeDialog = page.getByRole('dialog', { name: `Cerrar la ruta ${codeA}` })
    await closeDialog.getByRole('button', { name: 'Cerrar ruta' }).click()
    await expect(closeDialog.getByRole('alert')).toContainText(`La ruta ${codeA} no se puede cerrar: hay envíos sin terminar (`)
    await expect(closeDialog.getByRole('alert')).toContainText(aOrders[0].orderNumber ?? '')
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe2a-cerrar-422', 800, closeDialog)
    await closeDialog.getByRole('button', { name: 'Volver' }).click()
    await expect(activeHeading).toContainText('En curso')

    // ---- 10. Eliminar una ruta abierta (la C, sin paradas) con comentario ----
    await pick(routeC.code ?? '')
    await page.getByRole('button', { name: 'Eliminar ruta' }).click()
    const deleteDialog = page.getByRole('dialog', { name: `Eliminar la ruta ${routeC.code}` })
    await deleteDialog.getByLabel('Comentario (opcional)').fill('Duplicada')
    await shot(page, isMobile, 'fe2a-eliminar', 700, deleteDialog)
    await deleteDialog.getByRole('button', { name: 'Eliminar ruta' }).click()
    await expect(todayList.getByRole('button', { name: new RegExp(routeC.code ?? '') })).toHaveCount(0)
    await expect(todayList.getByRole('button')).toHaveCount(3)

    // ---- 11. Reasignar una zona sin rutas abiertas: no es un error ----
    await header.getByRole('button', { name: 'Reasignar por zona' }).click()
    const reassign2 = page.getByRole('dialog', { name: 'Reasignar por zona' })
    await reassign2.getByLabel(/^Zonas/).click()
    await reassign2.getByLabel(new RegExp(`EB${suffix}`)).check()
    await dialog().locator('p.d').click() // cierra la lista de zonas
    await reassign2.getByLabel(/^Nuevo chofer/).selectOption({ value: driverB.publicId ?? '' })
    await reassign2.getByRole('button', { name: 'Reasignar' }).click()
    await expect(page.getByRole('dialog', { name: 'Reasignación terminada' })).toContainText('No había rutas abiertas en esas zonas y fecha; no se cambió nada.')
    await page.getByRole('dialog', { name: 'Reasignación terminada' }).getByRole('button', { name: 'Listo' }).click()
    if (isMobile) await expectNoHorizontalScroll(page)
  })
})
