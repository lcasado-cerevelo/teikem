// Recorrido de «Devoluciones de venta» (Lote 82) contra el API real (db-init hecho; módulo SALES_ORDERS encendido; API en API_URL).
//
// 1) Por API: cliente, producto, existencia y una orden de venta despachada (8 de 10 piezas).
// 2) En la ficha de la orden: «Pedir devolución» (no deja pasar de lo despachado), motivo y cantidad; queda Solicitada.
// 3) Autorizar con nota, recibir 2 buenas y 1 dañada (la descripción es obligatoria) y acreditar: se crea la nota de crédito en borrador.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l82-<paso>.png (escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const PASSWORD = process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: PASSWORD }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
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
  if (target) await target.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  else await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
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

test('Devoluciones: pedir desde la orden, autorizar, recibir buenas y dañadas y acreditar', async ({ page, request, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const clientName = `Devuelve ${suffix}`
  const sku = `DEV-${suffix}`

  // 1) datos de apoyo por API: una orden de 10 piezas con 8 despachadas
  const client = await request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName } })
  expect(client.ok(), await client.text()).toBeTruthy()
  const clientId = ((await client.json()) as { publicId: string }).publicId
  const product = await request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Medidor ${suffix}`, trackingType: 'NONE', salePrice: 20 } })
  expect(product.ok(), await product.text()).toBeTruthy()
  const productId = ((await product.json()) as { product: { publicId: string } }).product.publicId
  const warehouses = (await (await request.get(`${API_URL}/api/v1/warehouses`, { headers })).json()) as { publicId: string }[]
  const adj = await request.post(`${API_URL}/api/v1/inventory/adjustments`, {
    headers,
    data: { productPublicId: productId, warehousePublicId: warehouses[0].publicId, binId: 2, quantity: 12, reason: 'FOUND', notes: 'e2e' },
  })
  expect(adj.ok(), await adj.text()).toBeTruthy()
  const order = await request.post(`${API_URL}/api/v1/sales-orders`, { headers, data: { clientPublicId: clientId, warehousePublicId: warehouses[0].publicId, lines: [{ productPublicId: productId, quantity: 10 }] } })
  expect(order.ok(), await order.text()).toBeTruthy()
  const orderDto = (await order.json()) as { publicId: string; number: string }
  const asked = await request.post(`${API_URL}/api/v1/sales-orders/${orderDto.publicId}/request`, { headers, data: {} })
  expect(asked.ok(), await asked.text()).toBeTruthy()
  const lineId = ((await (await request.get(`${API_URL}/api/v1/sales-orders/${orderDto.publicId}`, { headers })).json()) as { lines: { id: number }[] }).lines[0].id
  const disp = await request.post(`${API_URL}/api/v1/sales-orders/${orderDto.publicId}/dispatch`, { headers, data: { lines: [{ lineId, quantity: 8 }] } })
  expect(disp.ok(), await disp.text()).toBeTruthy()
  // lo despachado se factura (corrida aprobada con reautenticación): sin factura no hay nada que acreditar
  const run = await request.post(`${API_URL}/api/v1/billing/runs`, { headers, data: { clientPublicId: clientId, periodStart: '2020-01-01', periodEnd: '2099-12-31' } })
  expect(run.ok(), await run.text()).toBeTruthy()
  const runId = ((await run.json()) as { id: number }).id
  expect((await request.post(`${API_URL}/api/v1/billing/runs/${runId}/review`, { headers, data: {} })).ok()).toBeTruthy()
  const reauth = await request.post(`${API_URL}/api/v1/auth/reauth`, { headers, data: { password: ADMIN.password } })
  expect(reauth.ok(), await reauth.text()).toBeTruthy()
  const strong = { Authorization: `Bearer ${((await reauth.json()) as { accessToken: string }).accessToken}` }
  const approved = await request.post(`${API_URL}/api/v1/billing/runs/${runId}/approve`, { headers: strong, data: {} })
  expect(approved.ok(), await approved.text()).toBeTruthy()

  if (!isMobile) await page.setViewportSize({ width: 1280, height: 900 })
  await login(page)
  await page.goto(`/sales-orders?order=${orderDto.publicId}`)

  // 2) desde la ficha de la orden despachada: pedir la devolución
  const orderDialog = page.getByRole('dialog', { name: new RegExp(orderDto.number) })
  await orderDialog.getByRole('button', { name: 'Pedir devolución' }).click()
  const editor = page.getByRole('dialog', { name: 'Pedir una devolución' })
  await expect(editor.getByText(`Orden ${orderDto.number}`)).toBeVisible({ timeout: 20_000 })
  const qty = editor.getByLabel(`Cantidad a devolver de ${sku}`)
  await editor.getByLabel('Motivo').selectOption('DAMAGED')
  await qty.fill('9')
  await editor.getByRole('button', { name: 'Pedir devolución' }).click()
  await expect(editor.getByRole('alert')).toContainText('pasa de lo que se puede devolver')
  await qty.fill('3')
  await editor.getByLabel('Nota', { exact: true }).fill('Cajas rotas')
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l82-pedir', editor)
  await editor.getByRole('button', { name: 'Pedir devolución' }).click()

  // la ficha queda Solicitada → autorizar con nota
  const ret = page.getByRole('dialog', { name: /Devolución DV-/ })
  await expect(ret).toContainText('Solicitada', { timeout: 20_000 })
  await shot(page, isMobile, 'l82-ficha', ret)
  await ret.getByRole('button', { name: 'Autorizar' }).click()
  const auth = page.getByRole('dialog', { name: /Autorizar DV-/ })
  await auth.getByLabel('Nota de la autorización').fill('La recoge el chofer')
  await auth.getByRole('button', { name: 'Autorizar' }).click()
  await expect(ret).toContainText('Autorizada', { timeout: 20_000 })

  // 3) recibir 2 buenas y 1 dañada: la descripción del daño es obligatoria
  await ret.getByRole('button', { name: 'Recibir' }).click()
  const rec = page.getByRole('dialog', { name: /Recibir DV-/ })
  await rec.getByLabel('Bueno de', { exact: false }).first().fill('2')
  await rec.getByLabel('Dañado de', { exact: false }).first().fill('1')
  await rec.getByRole('button', { name: 'Recibir' }).click()
  await expect(rec.getByRole('alert')).toContainText('Describe qué le pasó')
  await rec.getByLabel('Descripción del daño de', { exact: false }).first().fill('Caja rota')
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l82-recibir', rec)
  await rec.getByRole('button', { name: 'Recibir' }).click()
  await expect(ret).toContainText('Recibida', { timeout: 20_000 })
  await expect(ret).toContainText('REC-')

  // acreditar: se crea la nota de crédito en borrador
  await ret.getByRole('button', { name: 'Acreditar' }).click()
  const credit = page.getByRole('dialog', { name: /Acreditar DV-/ })
  await credit.getByRole('button', { name: 'Acreditar' }).click()
  await expect(ret).toContainText('Acreditada', { timeout: 20_000 })
  await expect(ret).toContainText('NC-')
  await shot(page, isMobile, 'l82-acreditada', ret)
  await ret.getByRole('button', { name: 'Cerrar' }).first().click()

  // la lista y la ventana de devolución
  await page.getByRole('tab', { name: 'Devoluciones' }).click()
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: orderDto.number }).first()).toContainText('Acreditada', { timeout: 20_000 })
  await expect(page.getByLabel(/Ventana de devolución/)).toBeVisible()
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l82-lista')
})
