// Recorrido de la Estación de escaneo (Lote F-E1) contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000;
// compañía demo, admin con todos los permisos, módulos LTL_GROUND y CATALOG encendidos).
//
// Datos propios de cada corrida (por API): un cliente con contrato y tarifa, dos zonas (una con ruta abierta de hoy y otra SIN ruta),
// consignatarios por pueblo (uno sin zona), órdenes recogidas y una orden Sometida. El panel «Pendientes» se filtra por ese cliente
// para dejar a la vista solo lo de la corrida (la base trae datos de otras).
//
// 1) Escaneo con ruta abierta (FOUND_ASSIGNED) por número de envío y por número de empaque; repetir (ALREADY_ASSIGNED).
// 2) Código inexistente, código vacío, orden Sometida (se enruta brincando estatus), zona sin ruta abierta (NO_OPEN_ROUTE), pueblo sin zona (NO_ZONE).
// 3) Medidor «N de M», lista de la sesión con buscador, ventana de la orden con «Abrir», «Pendientes» con filtros y voz.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/fe1-<paso>.png (solo en escritorio).
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
type DispatchZoneDto = components['schemas']['DispatchZoneDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

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

test.describe('Estación de escaneo (Lote F-E1)', () => {
  test('asignar por orden y por empaque, repetir, inexistente, vacío, Sometida, sin ruta abierta, sin zona, medidor, sesión, ventana y filtros', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const clientName = `Client FE1 ${suffix}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }

    // ---- Datos: cliente con contrato y tarifa; zonas A (con ruta abierta de hoy) y B (sin ruta); pueblos A, B y C (sin zona) ----
    const client = await ok<ClientDetailDto>(
      request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName, contract: { startDate: tenantDay(-30), title: 'Contrato FE1' } } }),
      'cliente',
    )
    const contractId = client.currentContract?.publicId ?? ''
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType: 'BOX', rate: 100, effectiveFrom: tenantDay(-30) } }), 'tarifa')
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/status`, { headers, data: { toCode: 'ACTIVE' } }), 'contrato activo')
    // números de orden propios (los de la base de pruebas se repiten entre clientes y el escaneo por número sería ambiguo)
    await ok(request.patch(`${API_URL}/api/v1/clients/${client.publicId}/number-settings`, { headers, data: { orderNumberFormat: `E1${LETTERS}${isMobile ? 'M' : 'D'}-#####` } }), 'formato de número')

    const townA = `Town A FE1 ${suffix}`
    const townB = `Town B FE1 ${suffix}`
    const townC = `Town C FE1 ${suffix}`
    const zoneA = await ok<DispatchZoneDto>(request.post(`${API_URL}/api/v1/dispatch-zones`, { headers, data: { code: `FA${suffix}`, name: `Zone A FE1 ${suffix}` } }), 'zona A')
    const zoneB = await ok<DispatchZoneDto>(request.post(`${API_URL}/api/v1/dispatch-zones`, { headers, data: { code: `FB${suffix}`, name: `Zona B FE1 ${suffix}` } }), 'zona B')
    await ok(request.post(`${API_URL}/api/v1/dispatch-zones/${zoneA.id}/members`, { headers, data: { matchType: 'MUNICIPALITY', matchValue: townA } }), 'miembro A')
    await ok(request.post(`${API_URL}/api/v1/dispatch-zones/${zoneB.id}/members`, { headers, data: { matchType: 'MUNICIPALITY', matchValue: townB } }), 'miembro B')
    const place = (name: string, city: string, zip: string) =>
      ok<LocationDto>(
        request.post(`${API_URL}/api/v1/locations`, { headers, data: { clientPublicId: client.publicId, locationType: 'DELIVERY', name, line1: 'Calle Luna 45', city, postalCode: zip, country: 'PR', allowDupInvoice: true } }),
        `consignatario ${name}`,
      )
    const consA = await place(`Sunrise Pharmacy ${suffix}`, townA, '00959')
    const consB = await place(`Moon Market ${suffix}`, townB, '00725')
    const consC = await place(`Sea Workshop ${suffix}`, townC, '00765')
    const newOrder = (cons: LocationDto, confirmNow: boolean) =>
      ok<OrderDetailDto>(
        request.post(`${API_URL}/api/v1/orders`, { headers, data: { clientPublicId: client.publicId, consigneeLocationPublicId: cons.publicId, serviceType: 'STANDARD', packages: [{ packageType: 'BOX', pieces: 2, description: 'Ropa' }], confirmNow } }),
        'orden',
      )
    const a1 = await newOrder(consA, true)
    const a2 = await newOrder(consA, true)
    const b1 = await newOrder(consB, true)
    const c1 = await newOrder(consC, true)
    const draft = await newOrder(consA, false)
    expect(a2.packBatchNumber, 'la orden trae número de empaque').toBeTruthy()
    // la ruta abierta de hoy en la zona A (la zona B no tiene)
    const trip = await ok<TripDetailDto>(request.post(`${API_URL}/api/v1/trips`, { headers, data: { planDate: tenantDay(), dispatchZoneId: zoneA.id } }), 'ruta de la zona A')
    const tripCode = trip.code ?? ''
    expect(tripCode).not.toBe('')

    await login(page)
    await page.goto('/ops/scan')
    const input = page.getByRole('textbox', { name: 'Código a escanear' })
    const result = page.getByRole('status', { name: 'Resultado del último escaneo' })
    const feedList = page.getByRole('list', { name: 'Escaneados en esta sesión' })
    const pendingList = page.getByRole('list', { name: 'Pendientes por escanear' })
    const scan = async (code: string) => {
      await input.fill(code)
      await input.press('Enter')
    }
    const pc = (o: OrderDetailDto, k = 0) => o.packages?.[0]?.pieceNumbers?.[k] ?? ''   // Lote 110: lo que se escanea es el número de cada pieza

    // ---- 0. La pantalla: solo «Salida», entrada con foco, voz, pendientes y filtros ----
    await expect(page.getByRole('heading', { level: 1, name: 'Estación de escaneo' })).toBeVisible()
    await expect(input).toBeFocused()
    await expect(page.getByRole('radiogroup', { name: 'Acción del escaneo' })).toContainText('Enrutar')   // Lote 107: las cuatro acciones; Enrutar por omisión
    await expect(page.getByText('Procesamiento')).toHaveCount(0)
    await expect(page.getByRole('button', { name: 'Registrar' })).toBeVisible()
    await expect(result).toContainText('Listo para escanear')
    await expect(page.getByRole('button', { name: /^Voz/ })).toBeVisible()
    // filtra «Pendientes» por el cliente de la corrida
    const filters = page.getByRole('group', { name: 'Filtros del escaneo' })
    await filters.getByRole('combobox', { name: 'Cliente' }).fill(clientName)
    await page.getByRole('option', { name: new RegExp(clientName) }).click()
    await expect(pendingList.getByRole('button')).toHaveCount(4) // a1, a2, b1, c1 (la Sometida no está recogida: no es pendiente)
    await expect(pendingList.getByRole('button', { name: `Ver el envío ${a1.orderNumber}` })).toContainText(`${clientName} · ${townA} 00959 · FA${suffix}`)
    await expect(pendingList.getByRole('button', { name: `Ver el envío ${c1.orderNumber}` })).toContainText('Sin zona')
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe1-estacion-inicio', 1300)

    // ---- 1. Asignar por número de envío (FOUND_ASSIGNED) y por número de empaque ----
    await scan(pc(a1))
    await expect(result).toContainText(`Asignado a la ruta ${tripCode}.`)
    await expect(result).toContainText('Asignado')
    await expect(result).toContainText('Encontrada por número de pieza.')
    await expect(page.getByTestId('scan-count')).toContainText('1 de 2 piezas')
    await expect(result).toContainText(`Zona FA${suffix}`)
    await expect(input).toHaveValue('')
    await expect(input).toBeFocused()
    await expect(pendingList.getByRole('button', { name: `Ver el envío ${a1.orderNumber}` })).toHaveCount(0)
    await shot(page, isMobile, 'fe1-resultado-asignada', 1000)
    await scan(pc(a2))
    await expect(result).toContainText(`Asignado a la ruta ${tripCode}.`)
    await expect(result).toContainText('Encontrada por número de pieza.')

    // ---- 2. Repetir (ALREADY_ASSIGNED), inexistente, vacío, Sometida, sin ruta abierta, sin zona ----
    await scan(pc(a1))                                                                       // la misma pieza otra vez: duplicado (audio «Ok»)
    await expect(result).toContainText('ya se había escaneado con esta acción')
    await expect(page.getByTestId('scan-count')).toContainText('1 de 2 piezas')               // no se cuenta dos veces
    await scan(pc(a1, 1))                                                                    // la segunda pieza solo cuenta
    await expect(result).toContainText(`Ya estaba en la ruta ${tripCode}.`)   // la orden ya está en la ruta: la pieza solo cuenta
    await expect(page.getByTestId('scan-count')).toContainText('2 de 2 piezas')
    await scan(a1.orderNumber ?? '')                                                          // el número de la orden no se escanea
    await expect(result).toContainText('No se encontró la pieza.')
    await scan(`NOEXISTE-${suffix}`)
    await expect(result).toContainText('No se encontró la pieza.')
    await expect(input).toBeEnabled()   // el escaneo anterior ya terminó (con la app ocupada el Enter vacío se ignora)
    await expect(input).toHaveValue('')
    await input.press('Enter')
    await expect(result).toContainText('Escanee o escriba un código.')
    await scan(pc(draft))
    // Lote 106/107: una Sometida ya no se rechaza: «Enrutar» la lleva hasta Asignada brincando Recogida y Sin asignar (quedan registrados como brincados)
    await expect(result).toContainText('(brincó 2 estatus)')
    await expect(result).toContainText('Asignado')
    await scan(pc(b1))
    await expect(result).toContainText(`No hay ruta abierta para la zona FB${suffix} en la fecha ${tenantDay()}; queda sin asignar.`)
    await expect(result).toContainText('Planifique el día o cree la ruta')
    await shot(page, isMobile, 'fe1-resultado-sin-ruta', 1000)
    await scan(pc(c1))
    await expect(result).toContainText('No se pudo resolver la zona de despacho por código postal ni pueblo; queda sin asignar.')

    // ---- 3. Medidor «2 de 4», lista de la sesión con buscador, pendientes que quedan ----
    await expect(page.locator('.scanmeter .nums b')).toHaveText('3')   // a1, a2 y la Sometida que se enrutó
    await expect(page.locator('.scanmeter .nums .of')).toHaveText(/de [4-6]/)
    await expect(feedList.getByRole('button')).toHaveCount(7) // a1, a2, a1 otra vez, a1 pieza 2, Sometida, b1 y c1 (el inexistente no abre ventana)
    await expect(feedList).toContainText(`NOEXISTE-${suffix}`)
    await expect(pendingList.getByRole('button', { name: `Ver el envío ${b1.orderNumber}` })).toBeVisible()
    await expect(pendingList.getByRole('button', { name: `Ver el envío ${c1.orderNumber}` })).toBeVisible()
    await page.getByRole('searchbox', { name: 'Buscar en lo escaneado…' }).fill('no encontrada')
    await expect(feedList.getByRole('button')).toHaveCount(0)
    await expect(feedList).toContainText(`NOEXISTE-${suffix}`)
    await page.getByRole('searchbox', { name: 'Buscar en lo escaneado…' }).fill('')
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe1-sesion', 1500)

    // ---- 4. Ventana de la orden desde la sesión y desde los pendientes ----
    await feedList.getByRole('button', { name: `Ver el envío ${a1.orderNumber}` }).first().click()
    const dialog = page.getByRole('dialog')
    await expect(dialog).toContainText(clientName)
    await expect(dialog).toContainText(`Sunrise Pharmacy ${suffix}`)
    await expect(dialog).toContainText(tripCode)
    if (isMobile) await expectNoHorizontalScroll(page)
    await shot(page, isMobile, 'fe1-orden', 800)
    await dialog.getByRole('button', { name: 'Abrir' }).click()
    await page.waitForURL((url) => url.pathname === `/orders/${a1.publicId}`)
    await expect(page.getByRole('heading', { level: 1 })).toContainText(a1.orderNumber ?? '') // el expediente de la orden
    await page.goBack()
    await expect(page.getByRole('heading', { level: 1, name: 'Estación de escaneo' })).toBeVisible()
    await expect(input).toBeFocused()
    // la sesión sobrevive a abrir una orden y volver (se pierde solo al recargar)
    await expect(feedList.getByRole('button', { name: `Ver el envío ${a1.orderNumber}` }).first()).toBeVisible()
    await expect(page.locator('.scanmeter .nums b')).toHaveText('3')   // a1, a2 y la Sometida que se enrutó

    // ---- 5. Filtros de «Pendientes»: Zona, Sin zona, Código postal y Pueblo (al servidor) ----
    await page.getByRole('group', { name: 'Filtros del escaneo' }).getByLabel('Zona').selectOption({ label: `FB${suffix} · Zona B FE1 ${suffix}` })
    await expect(pendingList.getByRole('button')).toHaveCount(1)
    await expect(pendingList.getByRole('button', { name: `Ver el envío ${b1.orderNumber}` })).toBeVisible()
    await page.getByRole('group', { name: 'Filtros del escaneo' }).getByLabel('Zona').selectOption({ label: 'Sin zona' })
    await expect(pendingList.getByRole('button', { name: `Ver el envío ${c1.orderNumber}` })).toBeVisible()
    await page.getByRole('group', { name: 'Filtros del escaneo' }).getByLabel('Zona').selectOption({ label: 'Todos' })
    await page.getByRole('group', { name: 'Filtros del escaneo' }).getByLabel('Pueblo').fill(townC)
    await expect(pendingList.getByRole('button')).toHaveCount(1)
    await shot(page, isMobile, 'fe1-filtros', 1300)
    await page.getByRole('button', { name: 'Limpiar' }).click()
    await expect(page.getByRole('group', { name: 'Filtros del escaneo' }).getByLabel('Pueblo')).toHaveValue('')

    // ---- 6. Voz: se puede silenciar y se recuerda al recargar ----
    const voice = page.getByRole('button', { name: /^Voz/ })
    if (await voice.isEnabled()) {
      await voice.click()
      await expect(page.getByRole('button', { name: 'Voz silenciada' })).toBeVisible()
      await page.reload()
      await expect(page.getByRole('button', { name: 'Voz silenciada' })).toBeVisible()
      await page.getByRole('button', { name: 'Voz silenciada' }).click()
      await expect(page.getByRole('button', { name: 'Voz activada' })).toBeVisible()
    }
    // la lista de la sesión se pierde al recargar y la pantalla lo avisa
    await expect(page.getByText(/se pierde al recargar la página/)).toBeVisible()
    if (isMobile) await expectNoHorizontalScroll(page)
  })
})
