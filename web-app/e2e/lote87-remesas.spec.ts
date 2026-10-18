// Recorrido de «Remesas COD» (Lote 87) contra el API real (db-init hecho; módulo COD encendido; API en API_URL).
//
// 1) Por API: un cliente con dos órdenes con COD (100 y 50, entregadas con SQL: hace falta E2E_SQL_DB, la base del API; sin ella el recorrido se salta), cobradas (60 + 40 con cheque y 50) y cuadradas.
// 2) En la pantalla: Remesas → Nueva remesa (cliente y período: salen los 3 cobros, bruto 150, comisión 8 % = 12 y neto 138), crear, cuadrar, aprobar (reautenticación), exportar el archivo (se descarga)
//    y remitir con la referencia del pago; la remesa queda Remitida y el COD de las órdenes Remitido.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l87-<paso>.png (escritorio).
import { execFileSync } from 'node:child_process'
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const SQL_DB = process.env.E2E_SQL_DB
const PASSWORD = process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: PASSWORD }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

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

test('Remesas COD: crear, cuadrar, aprobar, exportar y remitir', async ({ page, request, isMobile }) => {
  test.skip(!SQL_DB, 'Falta E2E_SQL_DB (la base del API) para dejar las órdenes Entregadas')
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const clientName = `Remesa ${suffix}`

  // 1) cliente con dos órdenes cobradas y cuadradas
  const client = await request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName } })
  expect(client.ok(), await client.text()).toBeTruthy()
  const clientId = ((await client.json()) as { publicId: string }).publicId
  const loc = await request.post(`${API_URL}/api/v1/locations`, {
    headers,
    data: { clientPublicId: clientId, name: `Sucursal ${suffix}`, locationType: 'DELIVERY', line1: 'Calle 5 #12', city: 'Ponce', postalCode: '00716', country: 'PR' },
  })
  const locId = ((await loc.json()) as { publicId: string }).publicId
  const orders: { publicId: string; orderNumber: string }[] = []
  for (const cod of [100, 50]) {
    const o = await request.post(`${API_URL}/api/v1/orders`, { headers, data: { clientPublicId: clientId, consigneeLocationPublicId: locId, packages: [{ pieces: 1 }], codAmount: cod } })
    expect(o.ok(), await o.text()).toBeTruthy()
    const dto = (await o.json()) as { publicId: string; orderNumber: string }
    execFileSync('sqlcmd', [
      '-S', process.env.E2E_SQL_SERVER ?? 'localhost,1433', '-E', '-C', '-I', '-d', SQL_DB!, '-Q',
      `UPDATE o SET StatusCodeId = (SELECT StatusCodeId FROM dbo.StatusCode WHERE Entity='OrderStatus' AND InternalCode='DELIVERED') FROM dbo.TransportOrder o WHERE o.PublicId = '${dto.publicId}'`,
    ])
    orders.push(dto)
  }
  const pay = async (id: string, method: string, amount: number, checkNumber?: string) => {
    const r = await request.post(`${API_URL}/api/v1/cod/orders/${id}/collections`, { headers, data: { method, amount, checkNumber } })
    expect(r.ok(), await r.text()).toBeTruthy()
  }
  await pay(orders[0].publicId, 'CASH', 60)
  await pay(orders[0].publicId, 'CHECK', 40, '5521')
  await pay(orders[1].publicId, 'CASH', 50)
  const rec = await request.post(`${API_URL}/api/v1/cod/reconcile`, { headers, data: { codes: orders.map((o) => ({ code: o.orderNumber })), clientPublicId: clientId } })
  expect(((await rec.json()) as { reconciled: number }).reconciled).toBe(2)

  if (!isMobile) await page.setViewportSize({ width: 1280, height: 900 })
  await login(page)
  await page.goto('/money/cod')
  await expect(page.getByRole('heading', { level: 1, name: 'COD' })).toBeVisible({ timeout: 20_000 })
  await page.getByRole('tab', { name: 'Remesas' }).click()

  // 2) nueva remesa: cliente y período
  await page.getByRole('button', { name: 'Nueva remesa' }).click()
  const nueva = page.getByRole('dialog', { name: 'Nueva remesa' })
  await nueva.getByLabel('Cliente').fill(clientName)
  await page.getByRole('option', { name: new RegExp(clientName) }).click()
  await expect(nueva.getByText('Cobros que entrarían (3)')).toBeVisible({ timeout: 20_000 })
  await expect(nueva).toContainText('$150.00')
  await expect(nueva).toContainText('$12.00')
  await expect(nueva).toContainText('$138.00')
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l87-nueva', nueva)
  await nueva.getByRole('button', { name: 'Crear remesa' }).click()

  // la ficha: cuadrar → aprobar (reautenticación) → exportar → remitir
  const ficha = page.getByRole('dialog', { name: /^Remesa REM-/ })
  await expect(ficha).toContainText('Abierta', { timeout: 20_000 })
  await shot(page, isMobile, 'l87-ficha', ficha)
  await ficha.getByRole('button', { name: 'Cuadrar' }).click()
  await expect(ficha).toContainText('Cuadrada', { timeout: 20_000 })
  await ficha.getByRole('button', { name: 'Aprobar' }).click()
  await maybeReauth(page)
  await expect(ficha).toContainText('Aprobada', { timeout: 20_000 })
  await ficha.getByRole('button', { name: 'Exportar archivo' }).click()
  const exp = page.getByRole('dialog', { name: /^Exportar REM-/ })
  const [download] = await Promise.all([page.waitForEvent('download'), exp.getByRole('button', { name: 'Descargar' }).click()])
  expect(download.suggestedFilename()).toMatch(/^remesa-REM-\d+\.csv$/)
  await expect(exp).toHaveCount(0)   // el diálogo de exportar se cerró
  await ficha.getByRole('button', { name: 'Remitir', exact: true }).click()
  const remitir = page.getByRole('dialog', { name: /^Remitir REM-/ })
  await remitir.getByLabel('Referencia del pago (opcional)').fill('CHK-9001')
  await shot(page, isMobile, 'l87-remitir', remitir)
  await remitir.getByRole('button', { name: 'Remitir', exact: true }).click()
  await expect(ficha).toContainText('Remitida', { timeout: 20_000 })
  await expect(ficha).toContainText('CHK-9001')
  await shot(page, isMobile, 'l87-remitida', ficha)
  await ficha.getByRole('button', { name: 'Cerrar' }).first().click()

  // las órdenes quedaron con el COD Remitido
  await page.getByRole('tab', { name: 'Órdenes con COD' }).click()
  await page.getByPlaceholder(/Buscar/).first().fill(clientName)
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: clientName }).first()).toContainText('Remitido', { timeout: 20_000 })

  // la lista de remesas y la comisión por omisión
  await page.getByRole('tab', { name: 'Remesas' }).click()
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: clientName }).first()).toContainText('Remitida', { timeout: 20_000 })
  await expect(page.getByLabel('Comisión por omisión de la compañía (%)')).toBeVisible()
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l87-lista')
})
