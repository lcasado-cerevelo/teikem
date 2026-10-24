// Recorrido del Radar de órdenes abiertas y de los avisos del Pulso (Lote 89) contra el API real (db-init hecho; módulos LTL_GROUND y COD encendidos; API en API_URL).
//
// 1) El Radar: cuántas órdenes hay abiertas, los grupos por pueblo y por cliente, y un clic en un grupo abre Órdenes ya filtrado. La búsqueda encuentra una orden abierta por su factura.
// 2) Con E2E_SQL_DB (la base del API; deja una orden nueva En almacén con SQL) aparece en «Necesita tu atención» «órdenes sin chofer» con el enlace a la Sala de despacho; sin esa variable se salta.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l89-<paso>.png (escritorio).
import { execFileSync } from 'node:child_process'
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang, manualTarget } from './manualLang'
import type { components } from '../src/kernel/api/schema'
import { contractData, withRates } from './rates'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const SQL_DB = process.env.E2E_SQL_DB
const PASSWORD = process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: PASSWORD }
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

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

test('Pulso: el Radar de órdenes abiertas y el aviso de órdenes sin chofer', async ({ page, request, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }

  // una orden nueva (borrador) de un cliente propio; con SQL pasa a «En almacén» sin chofer
  let invoice = ''
  const client = await request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Radar ${suffix}`, contract: contractData() } })
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
  const dto = (await order.json()) as { publicId: string; clientInvoiceNumber: string }
  invoice = dto.clientInvoiceNumber
  if (SQL_DB) {
    execFileSync('sqlcmd', [
      '-S', process.env.E2E_SQL_SERVER ?? 'localhost,1433', '-E', '-C', '-I', '-d', SQL_DB, '-Q',
      `UPDATE o SET StatusCodeId = (SELECT StatusCodeId FROM dbo.StatusCode WHERE Entity='OrderStatus' AND InternalCode='INBOUND') FROM dbo.TransportOrder o WHERE o.PublicId = '${dto.publicId}'`,
    ])
  }

  if (!isMobile) await page.setViewportSize({ width: 1280, height: 900 })
  await login(page)

  // 1) el Radar
  const radar = page.locator('[data-panel-body="RADAR"]')
  await expect(radar.getByRole('heading', { name: 'Radar de envíos abiertos' })).toBeVisible({ timeout: 30_000 })
  await expect(radar).toContainText(/\d+ abiertas/)
  const firstTown = radar.locator('.gchip').first()
  await expect(firstTown).toBeVisible()
  await radar.scrollIntoViewIfNeeded()
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l89-radar', radar)
  await radar.getByRole('button', { name: 'Cliente' }).click()
  await expect(radar.locator('.gchip').first()).toBeVisible()

  // la búsqueda encuentra la orden abierta por su factura (solo si quedó abierta: con SQL)
  if (SQL_DB) {
    // la factura se repite entre clientes (FAC-00001 existe en cada uno): se busca por el consignatario, que es propio de esta corrida
    await radar.getByLabel('Busca por factura, envío o consignatario…').fill(`Sucursal ${suffix}`)
    const hit = radar.getByRole('link', { name: new RegExp(invoice) })
    await expect(hit).toBeVisible({ timeout: 20_000 })
    await shot(page, isMobile, 'l89-busqueda', radar)
    await radar.getByLabel('Busca por factura, envío o consignatario…').fill('')
  }

  // un grupo abre Órdenes ya filtrado
  await radar.getByRole('button', { name: 'Pueblo' }).click()
  const chip = radar.locator('.gchip').first()
  await expect(chip).toBeVisible()
  await chip.click()
  await expect(page).toHaveURL(/\/orders\?(city|client)=.*statuses=/)
  await expect(page.getByRole('heading', { level: 1, name: 'Envíos' })).toBeVisible({ timeout: 20_000 })

  // 2) «órdenes sin chofer» en «Necesita tu atención» (con la orden En almacén que dejó SQL)
  if (SQL_DB) {
    await page.goto('/')
    const row = page.locator('.work', { hasText: 'sin chofer' }).first()
    await expect(row).toBeVisible({ timeout: 30_000 })
    await shot(page, isMobile, 'l89-aviso', row)
    await row.getByRole('link', { name: /Asignar chofer/ }).click()
    await expect(page).toHaveURL(/\/ops\/dispatch/)
  }
})
