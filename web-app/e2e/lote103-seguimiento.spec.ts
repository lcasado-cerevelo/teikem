// Recorrido de «¿Cómo va mi orden?» (Lote 103) contra el API real: la compañía enciende el seguimiento público en Ajustes y la página pública (app del cliente, sin sesión) muestra SOLO el avance de un conduce con la barra de
// progreso (tantas etapas como estatus tiene el camino normal de la compañía), sin nombres ni direcciones.
//
// Requiere el API (API_URL), la web (WEB_URL, baseURL de Playwright) y la app del cliente (CLIENT_APP_URL, `npm run web -- --port 8081` en app-cliente). Escritorio y móvil.
// Capturas (solo escritorio): docs/manual/frontend/img/l103-<paso>.png.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const APP_URL = process.env.CLIENT_APP_URL ?? 'http://localhost:8081'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
const SLUG = 'e2e-seguimiento'
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

async function shot(page: Page, isMobile: boolean, name: string) {
  if (isMobile) return
  await page.waitForTimeout(400)
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
}

/** El color de fondo que de verdad se ve: el del primer ascendiente con fondo propio del elemento. */
async function paintedBackground(page: Page, testId: string): Promise<string> {
  return page.getByTestId(testId).evaluate((el) => {
    let n: Element | null = el
    while (n) {
      const bg = getComputedStyle(n).backgroundColor
      if (bg && bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent') return bg
      n = n.parentElement
    }
    return 'transparent'
  })
}

async function adminToken(request: APIRequestContext): Promise<string> {
  const res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  expect(res.ok(), `login: ${await res.text()}`).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    const again = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password, tenantId: tenant?.tenantId } })
    body = (await again.json()) as AuthResultDto
  }
  const token = body.tokens?.accessToken ?? ''
  const headers = { Authorization: `Bearer ${token}` }
  const status = (await (await request.get(`${API_URL}/api/v1/legal/status`, { headers })).json()) as { required?: boolean; currentVersion?: string }
  if (status.required) await request.post(`${API_URL}/api/v1/legal/accept`, { headers, data: { version: status.currentVersion, lang: 'es' } })
  return token
}

async function newOrder(request: APIRequestContext, headers: Record<string, string>, clientId: string, locId: string) {
  const o = await request.post(`${API_URL}/api/v1/orders`, { headers, data: { clientPublicId: clientId, consigneeLocationPublicId: locId, packages: [{ pieces: 1 }] } })
  expect(o.ok(), await o.text()).toBeTruthy()
  const made = (await o.json()) as { publicId: string; orderNumber: string }
  const detail = (await (await request.get(`${API_URL}/api/v1/orders/${made.publicId}`, { headers })).json()) as { waybillNumber: string }
  return { publicId: made.publicId, orderNumber: made.orderNumber, waybill: detail.waybillNumber }
}

test('Seguimiento público: la compañía lo enciende y la página muestra solo el avance con la barra', async ({ page, request, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const noHScroll = () => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await adminToken(request)}` }

  // 0) la compañía enciende el seguimiento público (ajuste) y hay tres órdenes: en borrador, en espera y cancelada
  const put = await request.put(`${API_URL}/api/v1/tenant/tracking`, { headers, data: { slug: SLUG } })
  expect(put.ok(), await put.text()).toBeTruthy()
  const clientName = `Seguimiento ${suffix}`
  const c = await request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName } })
  const clientId = ((await c.json()) as { publicId: string }).publicId
  const consignee = `Sucursal Secreta ${suffix}`
  const loc = await request.post(`${API_URL}/api/v1/locations`, { headers, data: { clientPublicId: clientId, name: consignee, locationType: 'DELIVERY', line1: 'Calle Privada 77', city: 'Ponce', postalCode: '00716', country: 'PR' } })
  const locId = ((await loc.json()) as { publicId: string }).publicId
  const draft = await newOrder(request, headers, clientId, locId)
  const held = await newOrder(request, headers, clientId, locId)
  const cancelled = await newOrder(request, headers, clientId, locId)
  expect((await request.post(`${API_URL}/api/v1/orders/${held.publicId}/status`, { headers, data: { toCode: 'ON_HOLD' } })).ok()).toBeTruthy()
  expect((await request.post(`${API_URL}/api/v1/orders/${cancelled.publicId}/cancel`, { headers, data: { reason: 'prueba' } })).ok()).toBeTruthy()

  // 1) la página pública, sin sesión: solo el número de conduce
  if (!isMobile) await page.setViewportSize({ width: 1100, height: 760 })
  await page.goto(`${APP_URL}/track/${SLUG}`)
  await expect(page.getByRole('heading', { name: '¿Cómo va mi orden?' })).toBeVisible({ timeout: 60_000 })
  expect(await noHScroll()).toBe(true)
  await shot(page, isMobile, 'l103-publica-vacia')
  await page.getByLabel('Número de conduce').fill(draft.waybill.toLowerCase())
  await page.getByRole('button', { name: 'Consultar' }).click()
  await expect(page.getByTestId('track-result-waybill')).toHaveText(draft.waybill, { timeout: 30_000 })
  await expect(page.getByText(/^Paso 1 de \d+ · /).first()).toBeVisible()
  const total = await page.getByTestId(/^segment-/).count()
  expect(total).toBeGreaterThanOrEqual(3)                                   // tantas etapas como estatus del camino normal de la compañía
  await expect(page.getByTestId('segment-current')).toHaveCount(1)
  await expect(page.getByTestId('segment-todo')).toHaveCount(total - 1)
  expect(await noHScroll()).toBe(true)
  await shot(page, isMobile, 'l103-publica')
  // nada de personas, direcciones, números del cliente ni del cliente mismo
  const text = (await page.locator('body').innerText()) + (await page.content())
  for (const secret of [consignee, 'Calle Privada', clientName, draft.orderNumber, '00716']) expect(text).not.toContain(secret)

  // 1b) los dos temas y los dos idiomas, con su opción en la misma página (por omisión: automático y español)
  await expect(page.getByTestId('theme-auto')).toBeChecked()
  await expect(page.getByTestId('lang-es')).toBeChecked()
  await page.emulateMedia({ colorScheme: 'light' })
  expect(await paintedBackground(page, 'track-result-waybill')).toBe('rgb(255, 255, 255)')   // la tarjeta, en claro
  await page.emulateMedia({ colorScheme: 'dark' })
  await expect.poll(() => paintedBackground(page, 'track-result-waybill')).toBe('rgb(21, 29, 46)')   // Automático sigue al sistema: en oscuro
  await shot(page, isMobile, 'l103-publica-oscuro')
  await page.getByTestId('theme-light').click()                                  // Claro manda sobre el sistema
  await expect.poll(() => paintedBackground(page, 'track-result-waybill')).toBe('rgb(255, 255, 255)')
  await page.getByTestId('theme-dark').click()
  await expect.poll(() => paintedBackground(page, 'track-result-waybill')).toBe('rgb(21, 29, 46)')
  await page.reload()                                                           // la elección se recuerda
  await expect(page.getByTestId('theme-dark')).toBeChecked({ timeout: 30_000 })
  await page.getByTestId('theme-auto').click()
  await page.getByTestId('lang-en').click()
  await expect(page.getByRole('heading', { name: 'Where is my order?' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Check' })).toBeVisible()
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Where is my order?' })).toBeVisible({ timeout: 30_000 })   // el idioma también se recuerda
  await shot(page, isMobile, 'l103-publica-ingles')
  await page.getByTestId('lang-es').click()
  await expect(page.getByRole('heading', { name: '¿Cómo va mi orden?' })).toBeVisible()
  await page.emulateMedia({ colorScheme: 'light' })
  await page.getByLabel('Número de conduce').fill(draft.waybill)
  await page.getByRole('button', { name: 'Consultar' }).click()
  await expect(page.getByTestId('track-result-waybill')).toHaveText(draft.waybill, { timeout: 30_000 })

  // 2) en espera: la barra se queda donde iba y avisa; cancelada: toda apagada
  await page.getByLabel('Número de conduce').fill(held.waybill)
  await page.getByRole('button', { name: 'Consultar' }).click()
  await expect(page.getByTestId('track-result-waybill')).toHaveText(held.waybill, { timeout: 30_000 })
  await expect(page.getByText(/^En espera · va por el paso \d+ de \d+$/)).toBeVisible()
  await expect(page.getByTestId('segment-alert')).toHaveCount(1)
  await shot(page, isMobile, 'l103-publica-espera')
  await page.getByLabel('Número de conduce').fill(cancelled.waybill)
  await page.getByRole('button', { name: 'Consultar' }).click()
  await expect(page.getByTestId('track-result-waybill')).toHaveText(cancelled.waybill, { timeout: 30_000 })
  await expect(page.getByTestId('segment-cancelled')).toHaveCount(total)

  // 3) un número que no existe, y una compañía que no existe: el mismo mensaje
  await page.getByLabel('Número de conduce').fill('CND-9999999')
  await page.getByRole('button', { name: 'Consultar' }).click()
  await expect(page.getByText('No encontramos ese número de conduce.')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByTestId('track-result-waybill')).toHaveCount(0)
  await page.goto(`${APP_URL}/track/no-existe-${suffix.toLowerCase()}`)
  await page.getByLabel('Número de conduce').fill(draft.waybill)
  await page.getByRole('button', { name: 'Consultar' }).click()
  await expect(page.getByText('No encontramos ese número de conduce.')).toBeVisible({ timeout: 30_000 })

  // 4) el enlace con ?n=… abre ya con la consulta hecha
  await page.goto(`${APP_URL}/track/${SLUG}?n=${draft.waybill}`)
  await expect(page.getByTestId('track-result-waybill')).toHaveText(draft.waybill, { timeout: 30_000 })

  // 5) la compañía ve su ajuste en la web (Ajustes de la compañía → General)
  const web = await page.context().newPage()
  await web.setViewportSize(isMobile ? { width: 412, height: 915 } : { width: 1280, height: 900 })
  await web.goto('/login')
  await web.getByLabel('Correo electrónico').fill(ADMIN.email)
  await web.getByLabel('Contraseña', { exact: true }).fill(ADMIN.password)
  await web.getByRole('button', { name: 'Entrar' }).click()
  await web.waitForURL((url) => url.pathname !== '/login')
  if (new URL(web.url()).pathname === '/select-tenant') {
    const def = web.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : web.locator('.tenant-list button').first()).click()
  }
  await web.goto('/system/settings')
  const panel = web.locator('section.kit-panel', { has: web.getByRole('heading', { name: 'Seguimiento público' }) })
  await expect(panel).toBeVisible({ timeout: 30_000 })
  await expect(panel.getByText('Encendido')).toBeVisible()
  await expect(panel.getByLabel('Identificador de la compañía')).toHaveValue(SLUG)
  expect(await web.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  await shot(web, isMobile, 'l103-ajuste')
  await web.getByRole('button', { name: 'Oscuro' }).click()   // el selector de tema de la web de la compañía
  await expect(web.locator('html')).toHaveAttribute('data-theme', 'dark')
  await web.waitForTimeout(500)
  await shot(web, isMobile, 'l103-ajuste-oscuro')
  await web.close()
})
