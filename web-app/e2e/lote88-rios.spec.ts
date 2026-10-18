// Recorrido de los ríos del Pulso (Lote 88) contra el API real (db-init hecho; módulos LTL_GROUND y COD encendidos para la compañía de prueba; API en API_URL).
//
// Un cliente con una orden con COD (entra en «Entrada» de hoy); el Pulso muestra las dos corrientes —«Paquetes en la calle» (azul) y «Dinero COD» (naranja)— con sus tarjetas y números; las barritas de 7
// días de Entrada, y un clic en «Cobrado» abre COD en la pestaña Reconciliar y en «Por despachar» la Sala de despacho.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l88-<paso>.png (escritorio).
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
test.setTimeout(120_000)

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

test('Pulso: los dos ríos con sus tarjetas y los enlaces a donde se atiende cada una', async ({ page, request, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }

  // una orden nueva con COD para que «Entrada» tenga al menos una de hoy
  const client = await request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Rio ${suffix}`, contract: contractData() } })
  expect(client.ok(), await client.text()).toBeTruthy()
  const clientJson = (await client.json()) as { publicId: string; currentContract?: { publicId?: string } | null }
  const clientId = clientJson.publicId
  await withRates(request, headers, clientJson)
  const loc = await request.post(`${API_URL}/api/v1/locations`, {
    headers,
    data: { clientPublicId: clientId, name: `Sucursal ${suffix}`, locationType: 'DELIVERY', line1: 'Calle 5 #12', city: 'Ponce', postalCode: '00716', country: 'PR' },
  })
  const locId = ((await loc.json()) as { publicId: string }).publicId
  const order = await request.post(`${API_URL}/api/v1/orders`, { headers, data: { clientPublicId: clientId, consigneeLocationPublicId: locId, packages: [{ pieces: 1 }], codAmount: 100 } })
  expect(order.ok(), await order.text()).toBeTruthy()

  if (!isMobile) await page.setViewportSize({ width: 1280, height: 900 })
  await login(page)

  // los dos ríos
  const orders = page.locator('[data-river="orders"]')
  const cod = page.locator('[data-river="cod"]')
  await expect(orders.getByRole('heading', { name: 'Paquetes en la calle' })).toBeVisible({ timeout: 30_000 })
  await expect(cod.getByRole('heading', { name: 'Dinero COD' })).toBeVisible()
  for (const name of ['Entrada', 'En almacén', 'Por despachar', 'En ruta', 'Entregado']) await expect(orders.getByText(name, { exact: true })).toBeVisible()
  for (const name of ['Por cobrar', 'Cobrado', 'Cuadrado', 'Remesa lista']) await expect(cod.getByText(name, { exact: true })).toBeVisible()
  // «Entrada» cuenta lo de hoy (al menos la orden recién creada) y lleva barritas
  const entryBig = orders.locator('[data-node="ENTRY"] .big')
  await expect(entryBig).not.toHaveText(/^0$|…|—/)
  await expect(orders.locator('[data-node="ENTRY"] .wh-mini')).toBeVisible()
  expect(await noHScroll(page)).toBe(true)
  await orders.scrollIntoViewIfNeeded()
  await shot(page, isMobile, 'l88-rios')
  await shot(page, isMobile, 'l88-rio-ordenes', orders)
  await shot(page, isMobile, 'l88-rio-cod', cod)

  // los enlaces: «Cobrado» → COD, pestaña Reconciliar; «Por despachar» → Sala de despacho
  await cod.locator('[data-node="COLLECTED"] a').click()
  await expect(page).toHaveURL(/\/money\/cod\?tab=reconcile/)
  await expect(page.getByRole('tab', { name: 'Reconciliar', selected: true })).toBeVisible({ timeout: 20_000 })
  await page.goto('/')
  await orders.locator('[data-node="TO_DISPATCH"] a').click()
  await expect(page).toHaveURL(/\/ops\/dispatch/)
})
