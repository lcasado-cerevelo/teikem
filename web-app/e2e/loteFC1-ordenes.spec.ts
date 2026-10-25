// Recorrido de Órdenes (Lote F-C1) contra el API real (db-init hecho, API en API_URL, por defecto http://localhost:5000; compañía demo,
// admin con todos los permisos, módulos LTL_GROUND y CATALOG encendidos).
//
// 1) Alta detallada desplegable: «Nueva orden» → cliente → consignatario del directorio (campos de solo lectura) → dos tipos de paquete
//    y COD → validaciones sin llamar al servidor → «Guardar» (nace Sometida) → el formulario se cierra, aviso con
//    «Abrir la orden» y la lista muestra UNA FILA POR TIPO DE PAQUETE con «↳».
// 2) Filtros de la lista (Cliente múltiple, Factura, ZIP) y Limpiar.
// 3) Un segundo formulario se llena sin guardarlo y queda abierto con lo capturado (Lote 106: ya no hay «Guardar y confirmar»).
// 4) Entrega especial: el interruptor cambia la sección 03; un cliente sin servicios especiales lo avisa.
// 5) Papelera: eliminar una orden en su estatus inicial (Sometida) con confirmación.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página), cada uno con sus propios datos.
// Capturas: docs/manual/frontend/img/fc1-<paso>.png (solo en escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang } from './manualLang'
import type { components } from '../src/kernel/api/schema'
import { contractData, withRates } from './rates'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']
type OrderPageDto = components['schemas']['OrderPageDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
/** Sufijo solo de letras (los dígitos del sello pasan a letras). */
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(150_000)

/** Captura de documentación (solo escritorio, para no pisarla con la móvil). */
async function shot(page: Page, isMobile: boolean, name: string) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  // ventana más alta solo para la captura: los formularios y modales largos salen completos
  const size = page.viewportSize()
  if (size) await page.setViewportSize({ width: size.width, height: 1000 })
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
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

/** Aviso (toast) con ese texto. */
const toast = (page: Page, text: string | RegExp): Locator => page.locator('.toast.on', { hasText: text })

/** Elige un cliente en el combobox «Cliente» del formulario por su nombre. */
async function pickClient(page: Page, name: string) {
  const box = page.getByRole('combobox', { name: /^Cliente/ }).first()
  await box.click()
  await box.fill(name)
  await page.getByRole('option', { name: new RegExp(name) }).first().click()
}

async function pickConsignee(page: Page, name: string) {
  const box = page.getByRole('combobox', { name: /^Nombre/ })
  await box.click()
  await page.getByRole('option', { name: new RegExp(name) }).first().click()
}

test.describe('Órdenes — alta detallada y lista (Lote F-C1)', () => {
  test('alta de una orden con dos tipos de paquete, fila por paquete, filtros, entrega especial y papelera', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const clientName = `Cliente FC1 ${suffix}`
    const consigneeName = `Consignatario FC1 ${suffix}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    const client = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName, contract: contractData() } }), 'cliente')
    await withRates(request, headers, client)
    await ok(
      request.post(`${API_URL}/api/v1/locations`, {
        headers,
        data: { clientPublicId: client.publicId, locationType: 'DELIVERY', name: consigneeName, line1: 'Calle Sol 123', city: 'Ponce', postalCode: '00716', country: 'PR' },
      }),
      'consignatario',
    )

    await login(page)
    await page.goto('/orders')
    await expect(page.getByRole('heading', { level: 1, name: 'Envíos' })).toBeVisible()
    await shot(page, isMobile, 'fc1-lista')

    // ---- Formulario desplegable ----
    await page.getByRole('button', { name: /Nuevo envío/ }).click()
    // desde F-C3 la entrada abre en Rápida (segmentado Rápida/Detallada); este recorrido es el de la Detallada
    await expect(page.getByRole('button', { name: /Rápida/ })).toHaveCount(1)
    await page.getByRole('button', { name: /Detallada/ }).click()
    await expect(page.getByText('Ticket del envío')).toBeVisible()
    // F-G2: «Label al guardar» junto a los botones de guardar, y los labels en el pie de la lista
    await expect(page.getByRole('switch', { name: 'Label al guardar' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Todos los labels' })).toBeVisible()
    // validaciones: sin cliente ni consignatario el servidor no se llama
    await page.getByRole('button', { name: 'Guardar', exact: true }).click()
    await expect(page.getByText('El cliente es obligatorio.')).toBeVisible()
    await expect(page.getByText('El consignatario es obligatorio: elija uno del directorio o capture uno nuevo.')).toBeVisible()

    await pickClient(page, clientName)
    // la factura la asigna Teikem: deshabilitada y marcada «(automático)»
    await expect(page.getByLabel(/N\.º de factura/)).toBeDisabled()
    await pickConsignee(page, consigneeName)
    await expect(page.getByLabel('Dirección')).toHaveValue('Calle Sol 123')
    await expect(page.getByRole('textbox', { name: 'Pueblo' })).toHaveValue('Ponce')
    // servicio y tipos de paquete: el primero del catálogo si la compañía no tiene predeterminado
    const service = page.getByRole('combobox', { name: /^Servicio/ })
    if ((await service.inputValue()) === '') await service.selectOption({ index: 1 })
    const types = page.getByRole('combobox', { name: /^Tipo de paquete/ })
    if ((await types.first().inputValue()) === '') await types.first().selectOption({ index: 1 })
    // captura del arranque del formulario (cliente, servicio y consignatario), con la página arriba
    await page.getByRole('heading', { level: 1, name: 'Envíos' }).scrollIntoViewIfNeeded()
    await shot(page, isMobile, 'fc1-alta-inicio')
    await page.getByLabel(/^Piezas/).first().fill('2')
    await page.getByLabel('Descripción').first().fill('Ropa')
    await page.getByRole('button', { name: /Añadir paquete/ }).click()
    await types.nth(1).selectOption({ index: 2 })
    await page.getByLabel(/^Piezas/).nth(1).fill('1')
    await page.getByLabel('Monto COD').fill('50')
    // el ticket no muestra precio: solo el COD capturado
    const ticket = page.getByRole('complementary', { name: 'Ticket del envío' })
    await expect(ticket).toContainText(clientName)
    await expect(ticket).toContainText('$50.00')
    await shot(page, isMobile, 'fc1-alta')
    if (isMobile) await expectNoHorizontalScroll(page)

    // ---- Guardar ----
    await page.getByRole('button', { name: 'Guardar', exact: true }).click()
    await expect(toast(page, /^Envío .+ guardado\.$/)).toBeVisible()
    await expect(page.getByText('Ticket del envío')).toHaveCount(0)
    const banner = page.locator('.oe-created')
    await expect(banner).toContainText('guardado')
    const page1 = await ok<OrderPageDto>(request.get(`${API_URL}/api/v1/orders?clientId=${client.publicId}`, { headers }), 'órdenes del cliente')
    expect(page1.total).toBe(1)
    const created = page1.items?.[0]
    expect(created?.status).toBe('SUBMITTED')
    expect(created?.isInitialStatus).toBe(true)
    expect(created?.packages).toHaveLength(2)

    // ---- Lista: una fila por tipo de paquete ----
    const clientFilter = page.getByLabel('Cliente', { exact: true })
    await clientFilter.click()
    await page.getByLabel(new RegExp(clientName)).check()
    await page.keyboard.press('Escape')
    const orderNumber = created?.orderNumber ?? ''
    if (isMobile) {
      // en móvil la tabla pasa a tarjetas: una por orden, con el resumen de sus paquetes (sin fila «↳»)
      const cards = page.locator('ul.dt-cards li.dt-card')
      await expect(cards).toHaveCount(1)
      await expect(cards.first()).toContainText(orderNumber)
    } else {
      const rows = page.getByRole('table', { name: 'Envíos' }).getByRole('row').filter({ hasText: /\S/ })
      await expect(rows).toHaveCount(3) // encabezado + 2 filas de la misma orden
      await expect(rows.nth(1)).toContainText(orderNumber)
      await expect(rows.nth(2)).toContainText('↳')
    }
    await expect(page.getByText('1–1 de 1', { exact: true })).toBeVisible()
    await shot(page, isMobile, 'fc1-lista-filas')

    // ---- Otros filtros y Limpiar ----
    await page.getByLabel('ZIP', { exact: true }).fill('999')
    await expect(page.getByText('Sin resultados')).toBeVisible()
    await page.getByRole('button', { name: 'Limpiar' }).click()
    await expect(page.getByLabel('ZIP', { exact: true })).toHaveValue('')

    // ---- Segundo formulario lleno y sin guardar: sigue abierto con lo capturado ----
    await page.getByRole('button', { name: /Nuevo envío/ }).click()
    await page.getByRole('button', { name: /Detallada/ }).click()
    await pickClient(page, clientName)
    await pickConsignee(page, consigneeName)
    const service2 = page.getByRole('combobox', { name: /^Servicio/ })
    if ((await service2.inputValue()) === '') await service2.selectOption({ index: 1 })
    const types2 = page.getByRole('combobox', { name: /^Tipo de paquete/ })
    if ((await types2.first().inputValue()) === '') await types2.first().selectOption({ index: 1 })
    await expect(page.getByText('Ticket del envío')).toBeVisible()
    await expect(page.getByLabel(/^Piezas/).first()).toHaveValue('1')
    const after = await ok<OrderPageDto>(request.get(`${API_URL}/api/v1/orders?clientId=${client.publicId}`, { headers }), 'órdenes sin guardar')
    expect(after.total).toBe(1)

    // ---- Entrega especial: el cliente no tiene servicios especiales ----
    await page.getByRole('switch', { name: 'Es una entrega especial' }).check()
    await expect(page.getByLabel('Monto COD')).toHaveCount(0)
    await expect(page.getByRole('textbox', { name: /N\.º de envío/ })).toBeVisible()
    // el aviso es la única opción del selector «Servicio especial»
    await expect(page.getByRole('combobox', { name: /^Servicio especial/ })).toContainText('Este cliente no tiene servicios especiales configurados')
    await shot(page, isMobile, 'fc1-alta-especial')
    await page.getByRole('button', { name: 'Ocultar formulario' }).click()
    await expect(page.getByText('Ticket del envío')).toHaveCount(0)

    // ---- Papelera (la lista vuelve a filtrarse por el cliente: «Limpiar» la dejó con todas las órdenes) ----
    await clientFilter.click()
    await page.getByLabel(new RegExp(clientName)).check()
    await page.keyboard.press('Escape')
    await expect(page.getByText('1–1 de 1', { exact: true })).toBeVisible()
    await page.getByRole('button', { name: 'Eliminar el envío' }).click()
    const dialog = page.getByRole('dialog')
    await expect(dialog).toContainText(orderNumber)
    await shot(page, isMobile, 'fc1-eliminar')
    await dialog.getByRole('button', { name: 'Eliminar' }).click()
    await expect(toast(page, `Envío ${orderNumber} eliminado.`)).toBeVisible()
    await expect(page.getByText('Sin resultados')).toBeVisible()
    const gone = await ok<OrderPageDto>(request.get(`${API_URL}/api/v1/orders?clientId=${client.publicId}`, { headers }), 'órdenes tras eliminar')
    expect(gone.total).toBe(0)
  })

  test('factura repetida y crédito excedido: avisos al guardar, con autorización', async ({ page, request }, testInfo) => {
    const isMobile = testInfo.project.name.startsWith('movil')
    const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
    const clientName = `Cliente Cred FC1 ${suffix}`
    const consigneeName = `Consignatario Cred FC1 ${suffix}`
    const headers = { Authorization: `Bearer ${await apiToken(request)}` }
    // cliente con límite de crédito bajo, contrato activo con una tarifa y número de factura capturado por el cliente
    const client = await ok<ClientDetailDto>(
      request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName, creditLimit: 10, contract: { startDate: tenantDay(-30), title: 'Contrato crédito' } } }),
      'cliente',
    )
    await ok(request.patch(`${API_URL}/api/v1/clients/${client.publicId}/number-settings`, { headers, data: { clientAssignsInvoiceNumber: true } }), 'numeración')
    const contractId = client.currentContract?.publicId ?? ''
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType: 'BOX', rate: 100, effectiveFrom: tenantDay(-30) } }), 'tarifa')
    await ok(request.post(`${API_URL}/api/v1/contracts/${contractId}/status`, { headers, data: { toCode: 'ACTIVE' } }), 'contrato activo')
    await ok(
      request.post(`${API_URL}/api/v1/locations`, {
        headers,
        data: { clientPublicId: client.publicId, locationType: 'DELIVERY', name: consigneeName, line1: 'Calle Luna 45', city: 'Ponce', postalCode: '00716', country: 'PR', allowDupInvoice: true },
      }),
      'consignatario',
    )
    const invoice = `FAC-${suffix}`

    async function fillOrder() {
      await page.getByRole('button', { name: /Nuevo envío/ }).click()
      await page.getByRole('button', { name: /Detallada/ }).click()
      await pickClient(page, clientName)
      await pickConsignee(page, consigneeName)
      await page.getByRole('combobox', { name: /^Servicio/ }).selectOption('STANDARD')
      await page.getByRole('combobox', { name: /^Tipo de paquete/ }).first().selectOption('BOX')
      await page.getByLabel(/N\.º de factura/).fill(invoice)
    }

    await login(page)
    await page.goto('/orders')
    await expect(page.getByRole('heading', { level: 1, name: 'Envíos' })).toBeVisible()

    // primera orden con la factura: ya al guardar se cotiza y se mide el crédito (límite 10, tarifa 100) → autorizar
    await fillOrder()
    await page.getByRole('button', { name: 'Guardar', exact: true }).click()
    const credit1 = page.getByRole('dialog', { name: 'El cliente excede su límite de crédito' })
    await expect(credit1).toBeVisible()
    await credit1.getByRole('button', { name: 'Autorizar y guardar' }).click()
    await expect(toast(page, /^Envío .+ guardado\.$/)).toBeVisible()
    await expect(page.getByText('Ticket del envío')).toHaveCount(0)

    // segunda con la misma factura: el consignatario la admite repetida, avisa y deja decidir
    await fillOrder()
    await page.getByRole('button', { name: 'Guardar', exact: true }).click()
    const dup = page.getByRole('dialog', { name: 'Factura repetida para este consignatario' })
    await expect(dup).toContainText(invoice)
    await shot(page, isMobile, 'fc1-factura-repetida')
    await dup.getByRole('button', { name: 'Crear de todos modos' }).click()

    // al guardar, el cliente excede su límite: aviso con la opción de autorizar
    const credit = page.getByRole('dialog', { name: 'El cliente excede su límite de crédito' })
    await expect(credit).toBeVisible()
    await shot(page, isMobile, 'fc1-credito')
    const before = await ok<OrderPageDto>(request.get(`${API_URL}/api/v1/orders?clientId=${client.publicId}`, { headers }), 'órdenes antes de autorizar')
    expect(before.total).toBe(1)
    await credit.getByRole('button', { name: 'Autorizar y guardar' }).click()
    await expect(credit).toBeHidden()
    const after = await ok<OrderPageDto>(request.get(`${API_URL}/api/v1/orders?clientId=${client.publicId}`, { headers }), 'órdenes tras autorizar')
    expect(after.total).toBe(2)
    expect(after.items?.every((o) => o.status === 'SUBMITTED')).toBe(true)
  })
})
