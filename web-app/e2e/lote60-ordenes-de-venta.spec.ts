// Recorrido de «Órdenes de venta» (Lote 60) contra el API real (db-init hecho; el módulo SALES_ORDERS encendido para la compañía de prueba con SQL, porque
// encenderlo pide verificación de dos pasos; API en API_URL, por defecto http://localhost:5000; compañía demo, administrador).
//
// 1) Por API: un cliente, un producto con precio de venta y 6 unidades en existencia.
// 2) Nueva orden de 10 unidades: «Solicitar…» comprueba el inventario y muestra el diálogo de faltante (pedido 10 / disponible 6 / faltante 4); se reserva lo
//    disponible y el resto queda en backorder. La lista marca «Con backorder».
// 3) Backorders: la línea aparece (pendiente 4); entra inventario por API y «Surtir todo» la completa: desaparece de la vista y la orden ya no tiene backorder.
// 4) La orden solicitada se abre con lo reservado, se cambia la cantidad de la línea y se cancela (libera lo reservado).
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l60-<paso>.png (escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang, manualTarget } from './manualLang'
import type { components } from '../src/kernel/api/schema'
import { enableModule } from './staffHelpers'

type AuthResultDto = components['schemas']['AuthResultDto']
type SalesOrderDtoPaged = components['schemas']['SalesOrderListItemDtoPagedResult']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const PASSWORD = process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: PASSWORD }
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

async function shot(page: Page, isMobile: boolean, name: string, target?: Locator) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  const manualShot1 = await manualTarget(target)
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  if (manualShot1) await manualShot1.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  else await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  await manualLang(page, 'es')
}

async function apiToken(request: APIRequestContext): Promise<string> {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  expect(res.ok(), `login: ${await res.text()}`).toBeTruthy()
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

test('Órdenes de venta: borrador, faltante y backorder al solicitar, surtir todo y cancelar liberando lo reservado', async ({ page, request, isMobile }) => {
  await enableModule(request, 'TAX')   // este recorrido necesita el módulo de impuestos (los de facturación lo suponen apagado: corre después de ellos)
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const clientName = `Farmacia ${suffix}`
  const sku = `MED-${suffix}`

  // 1) datos de apoyo por API
  const client = await request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName } })
  expect(client.ok(), await client.text()).toBeTruthy()
  const product = await request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Medidor ${suffix}`, trackingType: 'NONE', salePrice: 12.5 } })
  expect(product.ok(), await product.text()).toBeTruthy()
  const productId = ((await product.json()) as { product: { publicId: string } }).product.publicId
  const warehouses = (await (await request.get(`${API_URL}/api/v1/warehouses`, { headers })).json()) as { publicId: string; name?: string; code?: string }[]
  const warehouseId = warehouses[0].publicId
  const adjust = async (qty: number) => {
    const res = await request.post(`${API_URL}/api/v1/inventory/adjustments`, { headers, data: { productPublicId: productId, warehousePublicId: warehouseId, binId: 2, quantity: qty, reason: 'FOUND', notes: 'e2e' } })
    expect(res.ok(), await res.text()).toBeTruthy()
  }
  await adjust(6)
  // con el módulo de impuestos encendido, el cálculo necesita una tasa por omisión (la crea la compañía; aquí el IVU de ejemplo de la prueba)
  const rate = await request.post(`${API_URL}/api/v1/tax/rates`, {
    headers,
    data: { code: `IVU${suffix}`, name: 'IVU', effectiveFrom: '2020-01-01', components: [{ code: 'STATE', name: 'Estatal', percent: 10.5 }, { code: 'CITY', name: 'Municipal', percent: 1 }] },
  })
  expect(rate.ok(), await rate.text()).toBeTruthy()
  const settings = await request.patch(`${API_URL}/api/v1/tax/settings`, { headers, data: { defaultRateCode: `IVU${suffix}` } })
  expect(settings.ok(), await settings.text()).toBeTruthy()

  if (!isMobile) await page.setViewportSize({ width: 1280, height: 900 })   // alto suficiente para que las capturas de ventanas salgan completas
  await login(page)
  await page.goto('/sales-orders')
  await expect(page.getByRole('heading', { level: 1, name: 'Órdenes de venta' })).toBeVisible({ timeout: 20_000 })

  // 2) nueva orden de 10 unidades con solo 6 en existencia
  await page.getByRole('button', { name: 'Nueva orden' }).click()
  let dialog = page.getByRole('dialog')
  await dialog.getByRole('combobox').first().fill(clientName.slice(0, 12))
  await page.getByRole('option').filter({ hasText: clientName }).first().click()
  // la compañía tiene más de un almacén: se escoge el primero (el mismo donde se ajustó la existencia)
  await dialog.getByRole('combobox', { name: 'Almacén' }).fill(warehouses[0].code ?? warehouses[0].name ?? '')
  await page.getByRole('listbox').getByRole('option').first().click()
  await dialog.getByLabel('Producto de la línea 1').fill(sku)
  await page.getByRole('option').filter({ hasText: sku }).first().click()
  await dialog.getByLabel('Cantidad de la línea 1').fill('10')
  await expect(dialog.getByLabel('Importe de la línea 1')).toContainText('125')
  await dialog.getByLabel('Notas').fill('Entregar el viernes')
  await shot(page, isMobile, 'l60-nueva', dialog)
  await dialog.getByRole('button', { name: 'Solicitar…' }).click()
  const check = page.getByRole('dialog', { name: /Solicitar la orden/ })
  await expect(check.getByText('No alcanza el inventario para todo lo pedido. Nada se ha reservado todavía.')).toBeVisible({ timeout: 20_000 })
  await expect(check.locator('tbody tr')).toContainText(sku)
  await expect(check.locator('tbody tr')).toContainText('10')
  await expect(check.locator('tbody tr')).toContainText('6')
  await expect(check.locator('tbody tr')).toContainText('4')
  await shot(page, isMobile, 'l60-faltante', check)
  await check.getByRole('button', { name: 'Reservar lo disponible y dejar el resto en backorder' }).click()
  await expect(page.locator('.toast.on').first()).toContainText('solicitada')
  const listRow = () => page.locator('tr, .dt-cards > li').filter({ has: page.getByText(clientName) }).first()
  await expect(listRow()).toContainText('Solicitada')
  await expect(listRow()).toContainText('Con backorder')
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l60-ordenes')

  // 3) backorders: entra inventario y «Surtir todo» completa la línea
  await page.getByRole('tab', { name: 'Backorders' }).click()
  const boRow = () => page.locator('tr, .dt-cards > li').filter({ has: page.getByText(clientName) }).first()
  await expect(boRow()).toContainText(sku)
  await expect(boRow()).toContainText('4')
  await shot(page, isMobile, 'l60-backorders')
  await adjust(10)
  await page.reload()
  await page.getByRole('tab', { name: 'Backorders' }).click()
  await expect(boRow()).toContainText(sku)
  await page.getByRole('button', { name: 'Surtir todo' }).click()
  await expect(page.locator('.toast.on').first()).toContainText('unidades')
  await expect(page.locator('tr, .dt-cards > li').filter({ has: page.getByText(clientName) })).toHaveCount(0)
  expect(await noHScroll(page)).toBe(true)

  // 4) la orden solicitada: reservado completo, cambiar la cantidad y cancelar
  await page.getByRole('tab', { name: 'Órdenes' }).click()
  await listRow().click()
  dialog = page.getByRole('dialog')
  await expect(dialog.getByText('Entregar el viernes')).toBeVisible()
  const lineRow = dialog.locator('tr, .dt-cards > li').filter({ has: page.getByText(sku) }).first()
  await expect(lineRow).toContainText('10')
  await shot(page, isMobile, 'l60-solicitada', dialog)
  await lineRow.getByRole('button', { name: 'Cambiar cantidad' }).click()
  const qty = page.getByRole('dialog', { name: /Cambiar la cantidad de/ })
  await qty.getByLabel('Cantidad pedida').fill('7')
  await qty.getByRole('button', { name: 'Guardar cantidad' }).click()
  await expect(page.locator('.toast.on').filter({ hasText: 'actualizada' })).toHaveCount(1)
  await expect(dialog.locator('tr, .dt-cards > li').filter({ has: page.getByText(sku) }).first()).toContainText('7')
  await dialog.getByRole('button', { name: 'Cancelar orden' }).click()
  await page.getByRole('dialog', { name: /Cancelar la orden/ }).getByRole('button', { name: 'Cancelar orden' }).click()
  await expect(page.locator('.toast.on').filter({ hasText: 'cancelada' })).toHaveCount(1)
  await expect(listRow()).toContainText('Cancelada')

  // lo reservado volvió a estar disponible: una orden nueva de 17 (6 + 10 − 0 ajustes, la anterior ya liberó) comprueba que alcanza
  const list = (await (await request.get(`${API_URL}/api/v1/sales-orders?q=${encodeURIComponent(clientName)}`, { headers })).json()) as SalesOrderDtoPaged
  expect(list.items?.[0].status).toBe('CANCELLED')
  const again = await request.post(`${API_URL}/api/v1/sales-orders`, { headers, data: { clientPublicId: list.items?.[0].clientPublicId, warehousePublicId: warehouseId, lines: [{ productPublicId: productId, quantity: 16 }] } })
  expect(again.ok(), await again.text()).toBeTruthy()
  const check2 = (await (await request.get(`${API_URL}/api/v1/sales-orders/${((await again.json()) as { publicId: string }).publicId}/availability`, { headers })).json()) as components['schemas']['SalesAvailabilityDto']
  expect(check2.hasShortage).toBe(false)   // 16 en mano y ya nada reservado
})
