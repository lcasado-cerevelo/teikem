// Recorrido de «Despachar contra una orden de venta» (Lote 63) contra el API real (db-init hecho; el módulo SALES_ORDERS encendido con SQL; API en API_URL, por defecto
// http://localhost:5000; compañía demo, administrador).
//
// 1) Por API: un cliente, un producto con precio, 10 unidades en existencia y una orden solicitada de 6.
// 2) Despachar 4 de las 6: «Despachar» → cantidad → el diálogo «¿Qué sigue?» → Retiro. La orden queda «Despachada parcial» con su despacho.
// 3) Crear la entrega de ese despacho con el formulario del empaque (consignatario nuevo): el despacho pasa a «Con entrega» con su orden de transporte.
// 4) Despachar lo que falta: la orden queda «Despachada» y el inventario en mano baja a 4.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l63-<paso>.png (escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang, manualTarget } from './manualLang'
import type { components } from '../src/kernel/api/schema'
import { createClient } from './staffHelpers'

type AuthResultDto = components['schemas']['AuthResultDto']
type SalesOrderDto = components['schemas']['SalesOrderDto']

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

test('Despachar contra una orden de venta: por partes, retiro, entrega con orden de transporte y orden despachada', async ({ page, request, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const clientName = `Farmacia ${suffix}`
  const sku = `MED-${suffix}`

  // 1) datos de apoyo por API
  const clientId = (await createClient(request, headers, clientName)).publicId   // con contrato y tarifa: la entrega se cotiza
  const product = await request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Medidor ${suffix}`, trackingType: 'NONE', salePrice: 12.5, purchaseCost: 4 } })
  expect(product.ok(), await product.text()).toBeTruthy()
  const productId = ((await product.json()) as { product: { publicId: string } }).product.publicId
  // la compañía tiene varios almacenes: la orden y el ajuste van al ALM-01, con una posición activa de ese almacén
  const warehouses = (await (await request.get(`${API_URL}/api/v1/warehouses`, { headers })).json()) as { publicId: string; code: string }[]
  const alm01 = warehouses.find((w) => w.code === 'ALM-01') ?? warehouses[0]
  const bins = (await (await request.get(`${API_URL}/api/v1/warehouses/${alm01.publicId}/bins`, { headers, params: { includeInactive: false, take: 1 } })).json()) as { items: { id: number }[] }
  const adjust = await request.post(`${API_URL}/api/v1/inventory/adjustments`, { headers, data: { productPublicId: productId, warehousePublicId: alm01.publicId, binId: bins.items[0].id, quantity: 10, reason: 'FOUND', notes: 'e2e' } })
  expect(adjust.ok(), await adjust.text()).toBeTruthy()
  const draft = await request.post(`${API_URL}/api/v1/sales-orders`, { headers, data: { clientPublicId: clientId, warehousePublicId: alm01.publicId, lines: [{ productPublicId: productId, quantity: 6 }] } })
  expect(draft.ok(), await draft.text()).toBeTruthy()
  const orderId = ((await draft.json()) as SalesOrderDto).publicId!
  const requested = await request.post(`${API_URL}/api/v1/sales-orders/${orderId}/request`, { headers, data: {} })
  expect(requested.ok(), await requested.text()).toBeTruthy()

  if (!isMobile) await page.setViewportSize({ width: 1280, height: 900 })   // alto suficiente para que las capturas de ventanas salgan completas
  await login(page)
  await page.goto('/sales-orders')
  await expect(page.getByRole('heading', { level: 1, name: 'Órdenes de venta' })).toBeVisible({ timeout: 20_000 })
  await page.locator('tr, .dt-cards > li').filter({ has: page.getByText(clientName) }).first().click()
  let dialog = page.getByRole('dialog', { name: /^Orden OV-/ })

  // 2) despachar 4 de las 6 y escoger «Retiro»
  await dialog.getByRole('button', { name: 'Despachar' }).click()
  const modal = page.getByRole('dialog', { name: /Despachar la orden/ })
  await expect(modal.getByLabel(`Cantidad a despachar de ${sku}`)).toHaveValue('6')
  await modal.getByLabel(`Cantidad a despachar de ${sku}`).fill('4')
  await shot(page, isMobile, 'l63-despachar', modal)
  await modal.getByRole('button', { name: 'Despachar' }).click()
  const next = page.getByRole('dialog', { name: /^Despacho EMP-/ })
  await expect(next.getByText('¿Qué sigue con lo despachado?')).toBeVisible({ timeout: 20_000 })
  await shot(page, isMobile, 'l63-que-sigue', next)
  await next.getByRole('button', { name: /Retiro/ }).click()
  const dispatchRow = () => dialog.locator('tr, .dt-cards > li').filter({ hasText: /EMP-/ }).first()
  await expect(dialog.getByText('Despachada parcial')).toBeVisible()
  await expect(dispatchRow()).toContainText('Despachado')
  await expect(dispatchRow()).toContainText('4')
  expect(await noHScroll(page)).toBe(true)

  // 3) la entrega de ese despacho, con el formulario del empaque
  await dialog.getByRole('button', { name: 'Crear entrega' }).click()
  const pack = page.getByRole('dialog', { name: /^Empacar EMP-/ })
  await pack.getByLabel('Tipo de servicio').selectOption({ index: 1 })
  await pack.locator('select', { has: page.locator('option', { hasText: 'Nuevo' }) }).selectOption({ label: 'Nuevo' })
  await pack.getByLabel(/^Nombre/).first().fill(`Farmacia Norte ${suffix}`)
  await pack.getByLabel(/^Dirección \(línea 1\)/).fill('Calle 1')
  await pack.getByLabel(/^Ciudad/).fill('San Juan')
  await pack.getByLabel(/^Tipo de paquete/).selectOption({ index: 1 })
  await shot(page, isMobile, 'l63-entrega', pack)
  await pack.getByRole('button', { name: 'Empacar y crear envío' }).click()
  await expect(page.locator('.toast.on').filter({ hasText: 'se creó el envío' })).toHaveCount(1, { timeout: 20_000 })
  await expect(dispatchRow()).toContainText('Con entrega')
  await expect(dispatchRow()).toContainText(/ORD-/)
  await shot(page, isMobile, 'l63-despachos', dialog)

  // 4) lo que falta
  await dialog.getByRole('button', { name: 'Despachar' }).click()
  const modal2 = page.getByRole('dialog', { name: /Despachar la orden/ })
  await expect(modal2.getByLabel(`Cantidad a despachar de ${sku}`)).toHaveValue('2')
  await modal2.getByRole('button', { name: 'Despachar' }).click()
  await page.getByRole('dialog', { name: /^Despacho EMP-/ }).getByRole('button', { name: 'Decidirlo después' }).click()
  await expect(dialog.locator('.chip').filter({ hasText: 'Despachada' }).first()).toBeVisible()
  await expect(dialog.getByRole('button', { name: 'Despachar' })).toHaveCount(0)   // ya no queda nada reservado
  expect(await noHScroll(page)).toBe(true)

  // el inventario en mano bajó de 10 a 4 y no queda nada reservado
  const final = (await (await request.get(`${API_URL}/api/v1/sales-orders/${orderId}`, { headers })).json()) as SalesOrderDto
  expect(final.status).toBe('SHIPPED')
  expect(final.lines?.[0].qtyShipped).toBe(6)
  const inv = (await (await request.get(`${API_URL}/api/v1/products?search=${encodeURIComponent(sku)}`, { headers })).json()) as { items: { qtyOnHand: number; qtyReserved: number }[] }
  expect(inv.items[0].qtyOnHand).toBe(4)
  expect(inv.items[0].qtyReserved).toBe(0)
})
