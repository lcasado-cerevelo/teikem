// Recorrido de «Cobros» (Lote 76) contra el API real (db-init hecho; los módulos SALES_ORDERS y BILLING encendidos para la compañía de prueba con SQL;
// API en API_URL, por defecto http://localhost:5000; compañía demo, administrador).
//
// 1) Por API: cliente, producto con precio, existencia, orden de venta solicitada y despachada; corrida generada y revisada. Se aprueba desde la pantalla (reautenticación) y la factura queda Emitida.
// 2) Ficha de la factura: registrar un pago parcial con cheque (no deja pasar del saldo), anularlo con motivo (reautenticación) y volver a pagar.
// 3) Pestaña Cobros: el cliente aparece con su saldo y antigüedad; su estado de cuenta muestra la factura y el pago con saldo corrido. Se paga el resto y la factura queda Pagada.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l76-<paso>.png (escritorio).
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

/** Si la acción pidió reautenticación (AAL2), confirma con la contraseña del administrador. */
async function maybeReauth(page: Page) {
  const dialog = page.getByRole('dialog', { name: 'Confirme su identidad' })
  const appeared = await dialog.waitFor({ state: 'visible', timeout: 4000 }).then(() => true).catch(() => false)
  if (appeared) {
    await dialog.getByLabel('Contraseña', { exact: true }).fill(ADMIN.password)
    await dialog.getByRole('button', { name: 'Confirmar' }).click()
  }
}

test('Cobros: pagar una factura emitida, anular un pago, cuentas por cobrar y estado de cuenta', async ({ page, request, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const clientName = `Cobros ${suffix}`
  const sku = `COB-${suffix}`

  // 1) datos de apoyo por API: una orden despachada de 3 piezas a 20 = 60 y su corrida revisada
  const client = await request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName } })
  expect(client.ok(), await client.text()).toBeTruthy()
  const clientId = ((await client.json()) as { publicId: string }).publicId
  const product = await request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Medidor ${suffix}`, trackingType: 'NONE', salePrice: 20 } })
  expect(product.ok(), await product.text()).toBeTruthy()
  const productId = ((await product.json()) as { product: { publicId: string } }).product.publicId
  const warehouses = (await (await request.get(`${API_URL}/api/v1/warehouses`, { headers })).json()) as { publicId: string }[]
  const adj = await request.post(`${API_URL}/api/v1/inventory/adjustments`, {
    headers,
    data: { productPublicId: productId, warehousePublicId: warehouses[0].publicId, binId: 2, quantity: 5, reason: 'FOUND', notes: 'e2e' },
  })
  expect(adj.ok(), await adj.text()).toBeTruthy()
  const order = await request.post(`${API_URL}/api/v1/sales-orders`, { headers, data: { clientPublicId: clientId, warehousePublicId: warehouses[0].publicId, lines: [{ productPublicId: productId, quantity: 3 }] } })
  expect(order.ok(), await order.text()).toBeTruthy()
  const orderId = ((await order.json()) as { publicId: string }).publicId
  const asked = await request.post(`${API_URL}/api/v1/sales-orders/${orderId}/request`, { headers, data: {} })
  expect(asked.ok(), await asked.text()).toBeTruthy()
  const lineId = ((await (await request.get(`${API_URL}/api/v1/sales-orders/${orderId}`, { headers })).json()) as { lines: { id: number }[] }).lines[0].id
  const disp = await request.post(`${API_URL}/api/v1/sales-orders/${orderId}/dispatch`, { headers, data: { lines: [{ lineId, quantity: 3 }] } })
  expect(disp.ok(), await disp.text()).toBeTruthy()
  const run = await request.post(`${API_URL}/api/v1/billing/runs`, { headers, data: { clientPublicId: clientId, periodStart: '2020-01-01', periodEnd: '2099-12-31' } })
  expect(run.ok(), await run.text()).toBeTruthy()
  const runId = ((await run.json()) as { id: number }).id
  const reviewed = await request.post(`${API_URL}/api/v1/billing/runs/${runId}/review`, { headers, data: {} })
  expect(reviewed.ok(), await reviewed.text()).toBeTruthy()

  if (!isMobile) await page.setViewportSize({ width: 1280, height: 900 })
  await login(page)
  await page.goto('/money/billing')
  await expect(page.getByRole('heading', { level: 1, name: 'Facturación' })).toBeVisible({ timeout: 20_000 })

  // aprobar desde la pantalla (reautenticación): la factura queda Emitida
  await page.locator('tr, .dt-cards > li').filter({ hasText: `#${runId}` }).first().click()
  const runDialog = page.getByRole('dialog', { name: new RegExp(`Corrida #${runId}`) })
  await runDialog.getByRole('button', { name: 'Aprobar y emitir' }).click()
  await maybeReauth(page)
  await expect(runDialog).toContainText('Aprobada', { timeout: 20_000 })
  await runDialog.getByRole('button', { name: 'Cerrar' }).first().click()

  // 2) la ficha de la factura y sus pagos
  await page.getByRole('tab', { name: 'Facturas' }).click()
  const row = () => page.locator('tr, .dt-cards > li').filter({ has: page.getByText(clientName) }).first()
  await expect(row()).toContainText('Emitida')
  await row().click()
  const invoice = page.getByRole('dialog', { name: /^Factura / })
  await expect(invoice.getByText('Todavía no hay pagos.')).toBeVisible()
  await invoice.getByRole('button', { name: 'Registrar pago' }).click()
  const pay = page.getByRole('dialog', { name: 'Registrar pago' })
  await pay.getByLabel('Monto').fill('500')
  await pay.getByRole('button', { name: 'Guardar pago' }).click()
  await expect(pay.getByRole('alert')).toContainText('no puede pasar del saldo')
  await pay.getByLabel('Monto').fill('25')
  await pay.getByLabel('Referencia').fill('CH-1001')
  await shot(page, isMobile, 'l76-pago', pay)
  await pay.getByRole('button', { name: 'Guardar pago' }).click()
  await expect(invoice.getByText('CH-1001')).toBeVisible({ timeout: 20_000 })
  await expect(invoice).toContainText('Emitida')
  await shot(page, isMobile, 'l76-factura-pagos', invoice)

  // anular el pago con motivo (reautenticación) y volver a pagar
  await invoice.getByRole('button', { name: 'Anular el pago' }).click()
  const voiding = page.getByRole('dialog', { name: /¿Anular el pago de/ })
  await voiding.getByRole('button', { name: 'Anular el pago' }).click()
  await expect(voiding.getByText('Escriba el motivo para anular el pago.').first()).toBeVisible()
  await voiding.getByLabel('Motivo').fill('Cheque devuelto')
  await voiding.getByRole('button', { name: 'Anular el pago' }).click()
  await maybeReauth(page)
  await expect(invoice.getByText('Anulado')).toBeVisible({ timeout: 20_000 })
  await invoice.getByRole('button', { name: 'Registrar pago' }).click()
  await pay.getByLabel('Monto').fill('25')
  await pay.getByLabel('Método').selectOption('ACH')
  await pay.getByLabel('Referencia').fill('ACH-77')
  await pay.getByRole('button', { name: 'Guardar pago' }).click()
  await expect(invoice.getByText('ACH-77')).toBeVisible({ timeout: 20_000 })
  await invoice.getByRole('button', { name: 'Cerrar' }).first().click()

  // 3) cuentas por cobrar y estado de cuenta: queda por cobrar 60 − 25
  await page.getByRole('tab', { name: 'Cobros' }).click()
  const rec = () => page.locator('tr, .dt-cards > li').filter({ has: page.getByText(clientName) }).first()
  await expect(rec()).toContainText('35')
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l76-cobros')
  await rec().click()
  const stmt = page.getByRole('dialog', { name: /Estado de cuenta/ })
  await expect(stmt).toContainText('Pago')
  await expect(stmt).toContainText('Saldo final')
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l76-estado-de-cuenta', stmt)

  // pagar el resto desde el estado de cuenta: la factura queda Pagada
  await stmt.getByText(/^Factura FAC-/).first().click()
  const again = page.getByRole('dialog', { name: /^Factura / })
  await again.getByRole('button', { name: 'Registrar pago' }).click()
  await pay.getByRole('button', { name: 'Guardar pago' }).click()   // el monto por omisión es el saldo
  await expect(again).toContainText('Pagada', { timeout: 20_000 })
  await expect(again.getByRole('button', { name: 'Registrar pago' })).toHaveCount(0)
  await expect(again.getByRole('button', { name: 'Anular el pago' })).toHaveCount(0)
})
