// Recorrido de «Notas de crédito» (Lote 80) contra el API real (db-init hecho; los módulos SALES_ORDERS y BILLING encendidos para la compañía de prueba con SQL; API en API_URL).
//
// 1) Por API: cliente, producto, existencia, orden de venta despachada y una corrida revisada; se aprueba desde la pantalla (reautenticación): la factura queda Emitida (60).
// 2) En la ficha de la factura: «Crear nota de crédito» (ajuste de precio de 20 por pieza), guardar el borrador, aprobarla (reautenticación) y aplicarla a la factura.
// 3) La factura muestra lo acreditado y su saldo; la lista de notas, el estado de cuenta y Cobros lo reflejan; la nota queda Aplicada cuando se agota.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l80-<paso>.png (escritorio).
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
  const appeared = await dialog.waitFor({ state: 'visible', timeout: 20_000 }).then(() => true).catch(() => false)
  if (appeared) {
    await dialog.getByLabel('Contraseña', { exact: true }).fill(ADMIN.password)
    await dialog.getByRole('button', { name: 'Confirmar' }).click()
  }
}

test('Notas de crédito: crear sobre una factura, aprobar, aplicar y ver el saldo', async ({ page, request, isMobile }) => {
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

  
  // 2) la factura emitida (3 piezas a 20 = 60) y su nota de crédito: un ajuste de precio de 5 por pieza = 15
  await page.getByRole('tab', { name: 'Facturas' }).click()
  const row = () => page.locator('tr, .dt-cards > li').filter({ has: page.getByText(clientName) }).first()
  await expect(row()).toContainText('Emitida', { timeout: 20_000 })
  await row().click()
  const invoice = page.getByRole('dialog', { name: /^Factura / })
  await invoice.getByRole('button', { name: 'Crear nota de crédito' }).click()
  const editor = page.getByRole('dialog', { name: 'Nueva nota de crédito' })
  await expect(editor.getByText(/Sobre la factura FAC-/)).toBeVisible({ timeout: 20_000 })
  await editor.getByLabel('Motivo').selectOption('PRICE_ADJUSTMENT')
  await editor.getByLabel('Monto unitario de la línea 1').fill('5')
  await editor.getByLabel('Nota', { exact: true }).fill('Descuento pactado')
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l80-editor', editor)
  // no se acredita más de lo facturado: 100 por pieza no cabe
  await editor.getByLabel('Monto unitario de la línea 1').fill('100')
  await editor.getByRole('button', { name: 'Guardar borrador' }).click()
  await expect(editor.getByRole('alert')).toContainText('No se puede acreditar más de lo facturado')
  await editor.getByLabel('Monto unitario de la línea 1').fill('5')
  await editor.getByRole('button', { name: 'Guardar borrador' }).click()

  // la ficha de la nota: borrador → aprobar (reautenticación)
  const note = page.getByRole('dialog', { name: /Nota de crédito NC-/ })
  await expect(note).toContainText('Borrador', { timeout: 20_000 })
  await expect(note).toContainText('Descuento pactado')
  await shot(page, isMobile, 'l80-ficha', note)
  await note.getByRole('button', { name: 'Aprobar' }).click()
  await maybeReauth(page)
  await expect(note).toContainText('Aprobada', { timeout: 20_000 })

  // aplicarla a la factura: 10 ahora y 5 después (se agota)
  await note.getByRole('button', { name: 'Aplicar a una factura' }).click()
  const apply = page.getByRole('dialog', { name: /Aplicar la nota/ })
  await apply.getByLabel('Factura').selectOption({ index: 1 })
  await expect(apply.getByLabel('Monto')).toHaveValue('15')   // el menor entre el saldo de la nota y el de la factura
  await apply.getByLabel('Monto').fill('20')
  await apply.getByRole('button', { name: 'Aplicar', exact: true }).click()
  await expect(apply.getByRole('alert')).toContainText('no puede pasar del saldo de la nota')
  await apply.getByLabel('Monto').fill('10')
  await shot(page, isMobile, 'l80-aplicar', apply)
  await apply.getByRole('button', { name: 'Aplicar', exact: true }).click()
  await expect(note.getByText('Aplicaciones')).toBeVisible({ timeout: 20_000 })
  await expect(note).toContainText('Aprobada')
  await note.getByRole('button', { name: 'Aplicar a una factura' }).click()
  await apply.getByLabel('Factura').selectOption({ index: 1 })
  await apply.getByRole('button', { name: 'Aplicar', exact: true }).click()
  await expect(note).toContainText('Aplicada', { timeout: 20_000 })
  await expect(note.getByRole('button', { name: 'Anular la nota' })).toHaveCount(0)

  // 3) la factura muestra lo acreditado y su saldo; la lista de notas lo refleja
  await note.getByRole('button', { name: /^FAC-/ }).first().click()
  const again = page.getByRole('dialog', { name: /^Factura / })
  await expect(again).toContainText('acreditado', { timeout: 20_000 })
  await expect(again).toContainText('saldo $45.00')
  await again.getByRole('button', { name: 'Cerrar' }).first().click()
  await note.getByRole('button', { name: 'Cerrar' }).first().click()
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: 'Aplicada' }).first()).toBeVisible({ timeout: 20_000 })
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l80-lista')
})
