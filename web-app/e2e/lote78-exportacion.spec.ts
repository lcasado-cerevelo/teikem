// Recorrido de «Exportar a contabilidad» (Lote 78) contra el API real (db-init hecho; los módulos SALES_ORDERS y BILLING encendidos para la compañía de prueba con SQL; API en API_URL).
//
// 1) Por API: cliente, producto, existencia, orden de venta despachada y una corrida revisada; se aprueba desde la pantalla (reautenticación).
// 2) En la corrida aprobada: la sección «Exportar a contabilidad» con la plantilla estándar; «Configurar…» crea una plantilla nueva (TXT, tabulador, fecha MM/DD/YYYY, columnas elegidas y en orden).
// 3) «Archivo de exportación» muestra la vista previa con esa plantilla (la corrida sigue Aprobada); «Descargar y marcar como exportada» baja el archivo y la corrida queda Exportada.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l78-<paso>.png (escritorio).
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

test('Exportar: plantilla nueva, vista previa y descarga que marca la corrida como exportada', async ({ page, request, isMobile }) => {
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

  
  // 2) la corrida aprobada: la sección de exportación con la plantilla estándar
  await page.locator('tr, .dt-cards > li').filter({ hasText: `#${runId}` }).first().click()
  const run2 = page.getByRole('dialog', { name: new RegExp(`Corrida #${runId}`) })
  await expect(run2.getByText('Exportar a contabilidad')).toBeVisible({ timeout: 20_000 })
  await expect(run2.getByLabel('Plantilla de exportación')).toContainText('Formato estándar')
  await run2.getByLabel('Plantilla de exportación').selectOption({ label: 'Formato estándar' })   // otras corridas del mismo recorrido dejan plantillas propias
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l78-seccion', run2)

  // configurar: una plantilla nueva (TXT, tabulador, fecha MM/DD/YYYY, tres columnas en orden)
  await run2.getByRole('button', { name: 'Configurar…' }).click()
  const editor = page.getByRole('dialog', { name: 'Configurar la plantilla' })
  await editor.getByLabel('Nombre').fill(`Contable ${suffix}`)
  await editor.getByLabel('Formato', { exact: true }).selectOption('TXT')
  await editor.getByLabel('Delimitador').selectOption('\t')
  await editor.getByLabel('Formato de fecha').selectOption('MM/DD/YYYY')
  // dejar solo tres columnas: número de factura, fecha de emisión y total de la línea (en ese orden)
  for (const name of ['Fecha de vencimiento', 'Código del cliente', 'Cliente', 'Moneda', 'Número de línea', 'Tipo de cargo', 'Descripción', 'Número de envío', 'Código del producto', 'Cantidad', 'Monto unitario',
    'Subtotal de la línea', 'Tasa de impuesto (%)', 'Impuesto de la línea', 'Subtotal de la factura', 'Impuesto de la factura', 'Total de la factura', 'Número de la corrida', 'Inicio del período', 'Fin del período']) {
    await editor.getByRole('button', { name: `Quitar ${name}`, exact: true }).click()
  }
  await editor.getByLabel('Encabezado de Número de factura').fill('Ref')
  await shot(page, isMobile, 'l78-configurar', editor)
  await editor.getByRole('button', { name: 'Guardar como plantilla nueva' }).click()
  await expect(editor).toHaveCount(0, { timeout: 20_000 })
  await expect(run2.getByLabel('Plantilla de exportación')).toContainText(`Contable ${suffix}`)

  // 3) vista previa con la plantilla (la corrida sigue Aprobada) y descarga que la marca
  await run2.getByRole('button', { name: 'Archivo de exportación' }).click()
  const preview = page.getByRole('dialog', { name: /Archivo de la corrida/ })
  const text = preview.getByLabel('Contenido del archivo')
  await expect(text).toContainText('Ref\tInvoiceDate\tLineTotal', { timeout: 20_000 })
  await expect(text).toContainText('FAC-')
  await expect(text).toContainText('60.00')
  await expect(preview).toContainText(`Contable ${suffix}`)
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l78-vista-previa', preview)
  const downloading = page.waitForEvent('download')
  await preview.getByRole('button', { name: 'Descargar y marcar como exportada' }).click()
  const file = await downloading
  expect(file.suggestedFilename()).toBe(`facturacion-corrida-${runId}.txt`)
  await expect(preview).toHaveCount(0, { timeout: 20_000 })
  await expect(run2).toContainText('Exportada', { timeout: 20_000 })
  await expect(run2.getByText(/Exportada el/)).toBeVisible()

  // ya exportada: se puede volver a bajar sin cambiar nada más
  await run2.getByRole('button', { name: 'Archivo de exportación' }).click()
  await expect(page.getByRole('dialog', { name: /Archivo de la corrida/ }).getByRole('button', { name: 'Descargar de nuevo' })).toBeVisible({ timeout: 20_000 })
})
