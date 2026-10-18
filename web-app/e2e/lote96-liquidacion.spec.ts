// Recorrido de «Liquidación a choferes» (Lote 96) contra el API real (db-init hecho; módulo LTL_GROUND encendido; API en API_URL).
//
// 1) Por API: un chofer con tarifa de entrega (10) y de intento (5 y 3), un cliente con una orden y un intento fallido (Ausente).
// 2) En la pantalla: Intentos → registrar a mano el intento entregado (se busca la orden por número); Liquidaciones → Nueva liquidación (chofer y período: sale 1 línea de $18.00 = entrega 10 + intentos 5 y 3),
//    generar, ajustar la línea a $20 con su motivo, revisar, aprobar (reautenticación) y exportar el archivo (se descarga); la liquidación queda Exportada.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l96-<paso>.png (escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'
import { contractData, withRates } from './rates'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
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
  const appeared = await dialog.waitFor({ state: 'visible', timeout: 4000 }).then(() => true).catch(() => false)
  if (appeared) {
    await dialog.getByLabel('Contraseña', { exact: true }).fill(ADMIN.password)
    await dialog.getByRole('button', { name: 'Confirmar' }).click()
  }
}

/** Elige en el selector «Chofer» del diálogo la opción que contiene el nombre. */
async function pickDriver(dialog: Locator, name: string) {
  const value = await dialog.getByLabel('Chofer').locator('option', { hasText: name }).getAttribute('value')
  await dialog.getByLabel('Chofer').selectOption(value!)
}

test('Liquidación a choferes: intentos, generar, ajustar, revisar, aprobar y exportar', async ({ page, request, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const driverName = `Chofer ${suffix}`

  // 1) chofer con tarifas, cliente, orden y un intento fallido
  const client = await request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Liq ${suffix}`, contract: contractData() } })
  expect(client.ok(), await client.text()).toBeTruthy()
  const clientJson = (await client.json()) as { publicId: string; currentContract?: { publicId?: string } | null }
  const clientId = clientJson.publicId
  await withRates(request, headers, clientJson)
  const loc = await request.post(`${API_URL}/api/v1/locations`, {
    headers,
    data: { clientPublicId: clientId, name: `Sucursal ${suffix}`, locationType: 'DELIVERY', line1: 'Calle 5 #12', city: 'Ponce', postalCode: '00716', country: 'PR' },
  })
  const locId = ((await loc.json()) as { publicId: string }).publicId
  const order = await request.post(`${API_URL}/api/v1/orders`, { headers, data: { clientPublicId: clientId, consigneeLocationPublicId: locId, packages: [{ pieces: 1 }] } })
  expect(order.ok(), await order.text()).toBeTruthy()
  const orderDto = (await order.json()) as { publicId: string; orderNumber: string; serviceType?: string; packages?: { packageType?: string }[] }
  const detail = await request.get(`${API_URL}/api/v1/orders/${orderDto.publicId}`, { headers })
  const full = (await detail.json()) as { serviceType?: string; packages?: { packageType?: string }[] }

  const driver = await request.post(`${API_URL}/api/v1/drivers`, { headers, data: { code: `L${suffix}`.slice(0, 20), fullName: driverName } })
  expect(driver.ok(), await driver.text()).toBeTruthy()
  const driverId = ((await driver.json()) as { publicId: string }).publicId
  const rate = await request.post(`${API_URL}/api/v1/drivers/${driverId}/delivery-rates`, { headers, data: { serviceType: full.serviceType, packageType: full.packages?.[0]?.packageType, rate: 10 } })
  expect(rate.ok(), await rate.text()).toBeTruthy()
  for (const [n, r] of [[1, 5], [2, 3]]) {
    const a = await request.put(`${API_URL}/api/v1/drivers/${driverId}/attempt-rates/${n}`, { headers, data: { rate: r } })
    expect(a.ok(), await a.text()).toBeTruthy()
  }
  const failed = await request.post(`${API_URL}/api/v1/delivery-attempts`, { headers, data: { orderPublicId: orderDto.publicId, driverPublicId: driverId, delivered: false, failureReason: 'ABSENT' } })
  expect(failed.ok(), await failed.text()).toBeTruthy()

  if (!isMobile) await page.setViewportSize({ width: 1280, height: 900 })
  await login(page)
  await page.goto('/money/driver-pay')
  await expect(page.getByRole('heading', { level: 1, name: 'Liquidación a choferes' })).toBeVisible({ timeout: 20_000 })

  // 2) Intentos: el fallido de la API y registrar a mano el entregado
  await page.getByRole('tab', { name: 'Intentos de entrega' }).click()
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: `Liq ${suffix}` }).first()).toContainText('No entregó', { timeout: 20_000 })
  await page.getByRole('button', { name: 'Registrar intento' }).click()
  const nuevo = page.getByRole('dialog', { name: 'Registrar un intento de entrega' })
  await nuevo.getByLabel('Orden').fill(orderDto.orderNumber)
  await nuevo.getByRole('button', { name: new RegExp(`${orderDto.orderNumber} .* Sucursal ${suffix}`) }).click()
  await pickDriver(nuevo, driverName)
  await shot(page, isMobile, 'l96-intento', nuevo)
  await nuevo.getByRole('button', { name: 'Registrar', exact: true }).click()
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: `Liq ${suffix}` })).toHaveCount(2, { timeout: 20_000 })
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l96-intentos')

  // 3) Nueva liquidación del chofer: 1 línea, entrega 10 + intentos 5 y 3 = $18.00
  await page.getByRole('tab', { name: 'Liquidaciones' }).click()
  await page.getByRole('button', { name: 'Nueva liquidación' }).click()
  const nueva = page.getByRole('dialog', { name: 'Nueva liquidación' })
  await pickDriver(nueva, driverName)
  await expect(nueva.getByText('Lo que entraría (1 líneas)')).toBeVisible({ timeout: 20_000 })
  await expect(nueva).toContainText('$18.00')
  await expect(nueva).toContainText(`Liq ${suffix}`)
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l96-nueva', nueva)
  await nueva.getByRole('button', { name: 'Generar liquidación' }).click()

  // 4) la ficha: ajustar → revisar → aprobar (reautenticación) → exportar
  const ficha = page.getByRole('dialog', { name: /^Liquidación LIQ-/ })
  await expect(ficha).toContainText('Generada', { timeout: 20_000 })
  await expect(ficha).toContainText('$18.00')
  await shot(page, isMobile, 'l96-ficha', ficha)
  await ficha.getByRole('button', { name: 'Ajustar' }).click()
  const ajuste = page.getByRole('dialog', { name: /^Ajustar/ })
  await ajuste.getByLabel('A pagar').fill('20')
  await ajuste.getByLabel('Motivo del ajuste').fill('Bono por zona difícil')
  await ajuste.getByRole('button', { name: 'Guardar ajuste' }).click()
  await expect(ficha).toContainText('Ajustado: Bono por zona difícil', { timeout: 20_000 })
  await expect(ficha).toContainText('$20.00')
  await ficha.getByRole('button', { name: 'Marcar revisada' }).click()
  await expect(ficha).toContainText('Revisada', { timeout: 20_000 })
  await ficha.getByRole('button', { name: 'Aprobar' }).click()
  await maybeReauth(page)
  await expect(ficha).toContainText('Aprobada', { timeout: 20_000 })
  await ficha.getByRole('button', { name: 'Exportar', exact: true }).click()
  const exp = page.getByRole('dialog', { name: /^Exportar LIQ-/ })
  const [download] = await Promise.all([page.waitForEvent('download'), exp.getByRole('button', { name: 'Descargar' }).click()])
  expect(download.suggestedFilename()).toMatch(/^liquidacion-LIQ-\d+\.csv$/)
  await expect(exp).toHaveCount(0)
  await expect(ficha).toContainText('Exportada', { timeout: 20_000 })
  await shot(page, isMobile, 'l96-exportada', ficha)
  await ficha.getByRole('button', { name: 'Cerrar' }).first().click()

  // la lista
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: driverName }).first()).toContainText('Exportada', { timeout: 20_000 })
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l96-lista')
})
