// Recorrido del plano de ruta, el arrastre y el pin manual de la Sala de despacho (Lote F-E2b) contra el API real (db-init hecho, API en API_URL,
// por defecto http://localhost:5000; compañía demo, admin con todos los permisos, módulos LTL_GROUND y CATALOG encendidos).
//
// Datos propios de cada corrida (por API): un cliente con contrato y tarifa, una zona con su pueblo, un chofer con licencia, un vehículo y cuatro
// órdenes recogidas ya dentro de una ruta en borrador. Todo en un día futuro.
//
// 1) Plano vacío: «Ninguna parada tiene coordenadas todavía» y las cuatro en «Sin ubicación».   2) Tres pines puestos por API: los pines aparecen,
// y elegir un pin o una fila los resalta a los dos.   3) Pin manual por pantalla: error de rango, plano de referencia, guardar; la parada pasa a
// «Pin manual» y a la API como MANUAL.   4) Reordenar ARRASTRANDO (se verifica el orden por API) y con ▲ ▼.   5) Ruta despachada: el plano se ve y
// no hay ninguna acción de edición.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/fe2b-<paso>.png (solo en escritorio).
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

test.describe('Plano de ruta, arrastrar y pin manual (Lote F-E2b)', () => {
  test('plano vacío, pines, selección pin↔fila, pin manual con error, arrastrar y ▲ ▼, y ruta despachada sin edición', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    // letras al azar por corrida: con --repeat-each y varios workers el sello de tiempo puede repetirse y chocar con los códigos de otra corrida
    const rnd = Array.from({ length: 4 }, () => 'ABCDEFGHJKLMNPQRSTUVWXYZ'[Math.floor(Math.random() * 24)]).join('')
    // el azar va primero: el servidor recorta los códigos (cliente, zona…) y el sello de tiempo queda al final
    const suffix = `${isMobile ? 'M' : 'D'}${rnd}${LETTERS}`
    const clientName = `Cliente FE2B ${suffix}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const day = tenantDay(32 + (Math.floor(Date.now() / 1000) % 27)) // rango propio (32..58); el de F-E2a es 2..29
    const license = tenantDay(300)

    // ---- Datos: cliente con contrato y tarifa, zona con su pueblo, chofer con licencia, vehículo y cuatro órdenes ya en una ruta en borrador ----
    const client = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName, contract: { startDate: tenantDay(-30), title: 'Contrato FE2B' } } }), 'cliente')
    const contractId = client.currentContract?.publicId ?? ''
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType: 'BOX', rate: 100, effectiveFrom: tenantDay(-30) } }), 'tarifa')
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/status`, { headers, data: { toCode: 'ACTIVE' } }), 'contrato activo')
    await ok(request.patch(`${API_URL}/api/v1/clients/${client.publicId}/number-settings`, { headers, data: { orderNumberFormat: `E3${LETTERS}${rnd}${isMobile ? 'M' : 'D'}-#####` } }), 'formato de número')

    const town = `Pueblo E2B ${suffix}`
    const zone = await ok<DispatchZoneDto>(request.post(`${API_URL}/api/v1/dispatch-zones`, { headers, data: { code: `EZ${suffix}`, name: `Zona E2B ${suffix}` } }), 'zona')
    await ok(request.post(`${API_URL}/api/v1/dispatch-zones/${zone.id}/members`, { headers, data: { matchType: 'MUNICIPALITY', matchValue: town } }), 'miembro')
    const driver = await ok<DriverDetailDto>(request.post(`${API_URL}/api/v1/drivers`, { headers, data: { code: `DE${suffix}`, fullName: `Elisa Mapa ${suffix}`, dispatchZoneId: zone.id } }), 'chofer')
    await ok(request.post(`${API_URL}/api/v1/drivers/${driver.publicId}/licenses`, { headers, data: { licenseClass: 'CDL_A', licenseNumber: `L-DE${suffix}`, expiryDate: license } }), 'licencia')
    const vehicle = await ok<VehicleDetailDto>(request.post(`${API_URL}/api/v1/vehicles`, { headers, data: { code: `VE${suffix}`, plateNumber: `PE${suffix}`, vin: `VINE${suffix}` } }), 'vehículo')

    const place = (name: string, zip: string) =>
      ok<LocationDto>(request.post(`${API_URL}/api/v1/locations`, { headers, data: { clientPublicId: client.publicId, locationType: 'DELIVERY', name, line1: 'Calle Luna 45', city: town, postalCode: zip, country: 'PR', allowDupInvoice: true } }), `consignatario ${name}`)
    const names = ['Farmacia Sol', 'Colmado Luna', 'Taller Díaz', 'Ferretería Mar']
    const consignees: LocationDto[] = []
    for (const [i, n] of names.entries()) consignees.push(await place(`${n} ${suffix}`, `0095${i}`))
    const orders: OrderDetailDto[] = []
    for (const cons of consignees) {
      orders.push(
        await ok<OrderDetailDto>(
          request.post(`${API_URL}/api/v1/orders`, { headers, data: { clientPublicId: client.publicId, consigneeLocationPublicId: cons.publicId, serviceType: 'STANDARD', packages: [{ packageType: 'BOX', pieces: 2, description: 'Ropa' }], confirmNow: true } }),
          'orden',
        ),
      )
    }
    const trip = await ok<TripDetailDto>(
      request.post(`${API_URL}/api/v1/trips`, { headers, data: { planDate: day, dispatchZoneId: zone.id, driverPublicId: driver.publicId, vehiclePublicId: vehicle.publicId, plannedStartUtc: `${day}T12:00:00Z` } }),
      'ruta',
    )
    const tripId = trip.publicId ?? ''
    await ok(request.post(`${API_URL}/api/v1/trips/${tripId}/orders`, { headers, data: { orderPublicIds: orders.map((o) => o.publicId) } }), 'órdenes en la ruta')
    const detailOf = () => ok<TripDetailDto>(request.get(`${API_URL}/api/v1/trips/${tripId}`, { headers }), 'ficha de la ruta')
    const first = await detailOf()
    const stops = first.stops ?? []
    expect(stops).toHaveLength(4)
    const numberOf = (i: number) => stops[i].orderNumber ?? ''
    const sequenceNow = async () => (await detailOf()).stops?.map((s) => s.orderNumber)
    // la base de pruebas no geocodifica: ninguna parada trae punto
    expect(stops.every((s) => !s.point)).toBe(true)

    await login(page)
    await page.goto(`/ops/dispatch?date=${day}&trip=${tripId}`)
    const activeHeading = page.getByRole('heading', { name: /Ruta activa/ })
    await expect(activeHeading).toContainText(trip.code ?? '')
    const panel = page.locator('.panel', { has: activeHeading })
    const plane = page.getByRole('group', { name: `Plano de la ruta ${trip.code}` })
    const stopsList = page.getByRole('list', { name: 'Envíos en esta ruta' })
    const rowOf = (n: string) => stopsList.getByRole('listitem').filter({ hasText: n })
    const pins = plane.getByRole('button', { name: /^Parada \d/ })
    // el orden del DOM es el de la secuencia mostrada; se busca por número de parada (la secuencia cambia al reordenar)
    const pin = (n: number) => plane.getByRole('button', { name: new RegExp(`^Parada ${n} · `) })

    // ---- 1. Plano vacío: sin coordenadas todavía ----
    await expect(stopsList.getByRole('listitem')).toHaveCount(4)
    await expect(plane).toBeVisible()
    await expect(panel.getByText('Ninguna parada tiene coordenadas todavía')).toBeVisible()
    await expect(panel.getByText('Ponga un pin manual a cada parada con «Poner pin» para verlas aquí.')).toBeVisible()
    await expect(panel.getByText('Sin ubicación: 4')).toBeVisible()
    await expect(pins).toHaveCount(0)
    await expect(panel.getByText(/Plano esquemático/)).toBeVisible()
    await expect(page.getByText(/^2D$/)).toHaveCount(0)
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe2b-plano-vacio', 1500, panel, 1900)

    // ---- 2. Tres pines puestos por API: aparecen; elegir pin o fila los resalta a los dos ----
    const spots = [
      { lat: 18.3985, lng: -66.1557 }, // Bayamón
      { lat: 18.2341, lng: -66.0485 }, // Caguas
      { lat: 18.4655, lng: -66.1057 }, // San Juan
    ]
    for (const [i, p] of spots.entries()) {
      const cur = await detailOf()
      await ok(request.put(`${API_URL}/api/v1/trips/${tripId}/stops/${cur.stops?.[i].id}/location`, { headers, data: { ...p, rowVersion: cur.rowVersion } }), `pin ${i + 1}`)
    }
    await page.reload()
    await expect(activeHeading).toContainText(trip.code ?? '')
    await expect(pins).toHaveCount(3)
    await expect(panel.getByText('Sin ubicación: 1')).toBeVisible()
    await expect(panel.getByText('Ninguna parada tiene coordenadas todavía')).toHaveCount(0)
    for (const i of [0, 1, 2]) await expect(rowOf(numberOf(i))).toContainText('Pin manual')
    await expect(rowOf(numberOf(3))).toContainText('Sin pin')
    // todos los pines caen dentro del plano
    const box = (await plane.boundingBox())!
    for (const pin of await pins.all()) {
      const b = (await pin.boundingBox())!
      expect(b.x).toBeGreaterThanOrEqual(box.x - 1)
      expect(b.x + b.width).toBeLessThanOrEqual(box.x + box.width + 1)
    }
    await pin(2).click()
    await expect(pin(2)).toHaveAttribute('aria-pressed', 'true')
    await expect(rowOf(numberOf(1))).toHaveClass(/\bon\b/)
    await expect(panel.locator('.disp-map-sel')).toContainText(`Parada 2 · ${numberOf(1)}`)
    await rowOf(numberOf(2)).getByRole('button', { name: `Elegir el envío ${numberOf(2)}` }).click()
    await expect(pin(3)).toHaveAttribute('aria-pressed', 'true')
    await expect(pin(2)).toHaveAttribute('aria-pressed', 'false')
    // por teclado: Tab hasta un pin y Enter
    await pin(1).focus()
    await page.keyboard.press('Enter')
    await expect(pin(1)).toHaveAttribute('aria-pressed', 'true')
    await expect(rowOf(numberOf(0))).toHaveClass(/\bon\b/)
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- 3. Pin manual por pantalla: error de rango, plano de referencia y guardar ----
    await rowOf(numberOf(3)).getByRole('button', { name: `Poner pin al envío ${numberOf(3)}` }).click()
    const dialog = page.getByRole('dialog', { name: `Pin manual · ${numberOf(3)}` })
    await expect(dialog).toBeVisible()
    await dialog.getByRole('button', { name: 'Guardar pin' }).click()
    await expect(dialog.getByText('Indique la latitud y la longitud.')).toHaveCount(2)
    await dialog.getByLabel(/^Latitud/).fill('95')
    await dialog.getByLabel(/^Longitud/).fill('-200')
    await dialog.getByRole('button', { name: 'Guardar pin' }).click()
    await expect(dialog.getByText('La latitud debe estar entre -90 y 90.')).toBeVisible()
    await expect(dialog.getByText('La longitud debe estar entre -180 y 180.')).toBeVisible()
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe2b-pin-error', 1100, dialog)
    // tomar una posición del plano de referencia (aproximada)
    const picker = dialog.getByRole('group', { name: 'Plano de referencia para tomar una posición' })
    const pb = (await picker.boundingBox())!
    await picker.locator('rect.rmap-bg').click({ position: { x: pb.width * 0.5, y: pb.height * 0.5 } })
    await expect(dialog.getByText(/Posición aproximada tomada del plano/)).toBeVisible()
    const latText = await dialog.getByLabel(/^Latitud/).inputValue()
    const lngText = await dialog.getByLabel(/^Longitud/).inputValue()
    expect(Number(latText)).toBeGreaterThan(18.2)
    expect(Number(latText)).toBeLessThan(18.5)
    expect(Number(lngText)).toBeGreaterThan(-66.2)
    expect(Number(lngText)).toBeLessThan(-66.0) // el centro del plano cae entre los pines de referencia
    await expect(dialog.getByText('La latitud debe estar entre -90 y 90.')).toHaveCount(0)
    await shot(page, isMobile, 'fe2b-pin-dialogo', 1100, dialog)
    // se escribe a mano (con coma decimal) y se guarda
    await dialog.getByLabel(/^Latitud/).fill('18,3100')
    await dialog.getByLabel(/^Longitud/).fill('-66,2500')
    await dialog.getByRole('button', { name: 'Guardar pin' }).click()
    await expect(dialog).toHaveCount(0)
    await expect(pins).toHaveCount(4)
    await expect(panel.getByText(/Sin ubicación/)).toHaveCount(0)
    await expect(rowOf(numberOf(3))).toContainText('Pin manual')
    const afterPin = (await detailOf()).stops?.[3]
    expect(afterPin?.geocodeAccuracyCode).toBe('MANUAL')
    expect(afterPin?.point?.lat).toBeCloseTo(18.31, 4)
    expect(afterPin?.point?.lng).toBeCloseTo(-66.25, 4)
    await pin(4).click()
    await expect(panel.locator('.disp-map-sel').getByRole('button', { name: 'Mover pin' })).toBeVisible()
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe2b-plano-pines', 1500, panel, 1900)

    // ---- 4. Reordenar ARRASTRANDO el asa (ratón) y con ▲ ▼ ----
    const before = await sequenceNow()
    expect(before).toEqual([numberOf(0), numberOf(1), numberOf(2), numberOf(3)])
    const grip = (n: string) => stopsList.getByRole('button', { name: `Arrastrar el envío ${n} para reordenar (o use las flechas ↑ ↓)` })
    const dragTo = async (from: string, to: string) => {
      // el ratón de Playwright solo alcanza lo que está en la ventana (y dentro del área visible de la lista)
      await stopsList.scrollIntoViewIfNeeded()
      await grip(from).scrollIntoViewIfNeeded()
      await rowOf(to).scrollIntoViewIfNeeded()
      const g = (await grip(from).boundingBox())!
      const target = (await rowOf(to).boundingBox())!
      await page.mouse.move(g.x + g.width / 2, g.y + g.height / 2)
      await page.mouse.down()
      await page.mouse.move(g.x + g.width / 2, (g.y + target.y + target.height / 2) / 2, { steps: 4 })
      await page.mouse.move(g.x + g.width / 2, target.y + target.height / 2, { steps: 6 })
      await expect(rowOf(to)).toHaveClass(/over-(up|down)/)
      await page.mouse.up()
    }
    await dragTo(numberOf(3), numberOf(0)) // la cuarta al principio
    await expect.poll(sequenceNow).toEqual([numberOf(3), numberOf(0), numberOf(1), numberOf(2)])
    await expect(stopsList.getByRole('listitem').first()).toContainText(numberOf(3))
    // el plano sigue la secuencia: el pin 1 es ahora el de la cuarta orden
    await expect(pin(1)).toHaveAccessibleName(new RegExp(`^Parada 1 · .*${names[3]}`))
    await dragTo(numberOf(0), numberOf(2)) // la segunda a la última
    await expect.poll(sequenceNow).toEqual([numberOf(3), numberOf(1), numberOf(2), numberOf(0)])
    // ▲ ▼
    await page.getByRole('button', { name: `Subir el envío ${numberOf(0)}` }).click()
    await expect.poll(sequenceNow).toEqual([numberOf(3), numberOf(1), numberOf(0), numberOf(2)])
    await page.getByRole('button', { name: `Bajar el envío ${numberOf(3)}` }).click()
    await expect.poll(sequenceNow).toEqual([numberOf(1), numberOf(3), numberOf(0), numberOf(2)])
    // teclado en el asa
    await grip(numberOf(2)).focus()
    await page.keyboard.press('ArrowUp')
    await expect.poll(sequenceNow).toEqual([numberOf(1), numberOf(3), numberOf(2), numberOf(0)])
    // ráfaga: tres flechas seguidas sin esperar (el asa no se bloquea; salen en cola, sin 409)
    await grip(numberOf(0)).focus()
    await page.keyboard.press('ArrowUp')
    await page.keyboard.press('ArrowUp')
    await page.keyboard.press('ArrowUp')
    await expect.poll(sequenceNow).toEqual([numberOf(0), numberOf(1), numberOf(3), numberOf(2)])
    await expect(stopsList.getByRole('listitem').first()).toContainText(numberOf(0))
    await expect(page.getByRole('alert')).toHaveCount(0)
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe2b-arrastrar', 1500, panel, 1900)

    // ---- 5. Ruta despachada: el plano se ve y no hay acciones de edición ----
    await ok(request.post(`${API_URL}/api/v1/trips/dispatch`, { headers, data: { tripPublicIds: [tripId] } }), 'despachar')
    await page.reload()
    await expect(activeHeading).toContainText('Despachado')
    await expect(pins).toHaveCount(4)
    await expect(stopsList.getByRole('button', { name: /Arrastrar el envío/ })).toHaveCount(0)
    await expect(stopsList.getByRole('button', { name: /Poner pin|Mover el pin/ })).toHaveCount(0)
    await expect(stopsList.getByRole('button', { name: /Subir el envío|Bajar el envío|Quitar el envío/ })).toHaveCount(0)
    await pin(1).click()
    await expect(panel.locator('.disp-map-sel')).toBeVisible()
    await expect(panel.locator('.disp-map-sel').getByRole('button')).toHaveCount(0)
    // el servidor tampoco deja poner pin en una ruta despachada
    const cur = await detailOf()
    const res = await request.put(`${API_URL}/api/v1/trips/${tripId}/stops/${cur.stops?.[0].id}/location`, { headers, data: { lat: 18.4, lng: -66.1, rowVersion: cur.rowVersion } })
    expect(res.status()).toBe(422)
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe2b-ruta-despachada', 1500, panel, 1900)
  })
})
