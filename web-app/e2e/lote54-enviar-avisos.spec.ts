// Recorrido de «Enviar avisos» (Lote 54) contra el API real con DOS personas a la vez (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo: el administrador, que tiene notifications.send, y Carlos Rivera, el despachador, que NO lo tiene).
//
// 1) Carlos (sin el permiso) no ve «Enviar avisos» en el menú y el API le responde 403.
// 2) El administrador abre Sistema → Enviar avisos: el buscador lista personas con nombre y correo y roles con cuántas personas tienen;
//    «Seleccionar todo (N)» escoge SOLO lo que coincide con lo escrito.
// 3) Envía un aviso a una persona (Carlos) y otro a TODAS (con confirmación): Carlos, con la aplicación abierta, lo recibe EN VIVO: contador de
//    la campanita, globo de Kemi «Te llegó un aviso» y, al abrirla, «De Administrador Advance»; no hay cómo contestar.
// 4) El historial del administrador dice a quién se envió y, al leerlo Carlos, cuántas personas lo leyeron.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l54-<paso>.png (escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { devices, expect, test, type APIRequestContext, type Browser, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type AnnouncementDtoPagedResult = components['schemas']['AnnouncementDtoPagedResult']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const PASSWORD = process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: PASSWORD }
const CARLOS = { email: 'teikem+dispatch@cerevelo.com', password: PASSWORD }
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

async function apiToken(request: APIRequestContext, who: { email: string; password: string }): Promise<string> {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: who.email, password: who.password } })
  expect(res.ok(), `login ${who.email}: ${await res.text()}`).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: who.email, password: who.password, tenantId: tenant?.tenantId } })
    body = (await res.json()) as AuthResultDto
  }
  expect(body.status).toBe('ok')
  return body.tokens?.accessToken ?? ''
}

async function login(page: Page, who: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(who.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(who.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login')
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/')
}

async function secondPerson(browser: Browser, isMobile: boolean) {
  const context = await browser.newContext(isMobile ? { ...devices['Pixel 7'], viewport: { width: 360, height: 780 }, locale: 'es-PR' } : { locale: 'es-PR', viewport: { width: 1280, height: 720 } })
  return { context, page: await context.newPage() }
}

test('Enviar avisos: permiso, buscador con «Seleccionar todo», aviso en vivo «De …», a todos y historial', async ({ page, request, browser, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const one = `Aviso a Carlos ${suffix}`
  const everyone = `Aviso a todos ${suffix}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const carlosToken = await apiToken(request, CARLOS)
  const adminToken = await apiToken(request, ADMIN)
  const asCarlos = { Authorization: `Bearer ${carlosToken}` }
  const asAdmin = { Authorization: `Bearer ${adminToken}` }

  // 1) Carlos no tiene el permiso: el API le dice 403 y no ve la entrada del menú
  const denied = await request.get(`${API_URL}/api/v1/announcements`, { headers: asCarlos })
  expect(denied.status()).toBe(403)
  const { context: carlosCtx, page: carlos } = await secondPerson(browser, isMobile)
  await login(carlos, CARLOS)
  await carlos.goto('/system/announcements')
  await expect(carlos.getByRole('heading', { level: 1, name: 'Enviar avisos' })).toHaveCount(0)

  // Carlos vuelve al inicio y se queda con la aplicación abierta (recibe en vivo)
  await carlos.goto('/')
  await expect(carlos.getByRole('button', { name: /^Avisos/ })).toBeVisible({ timeout: 20_000 })
  await carlos.waitForTimeout(1500)   // la primera lectura del contador (para que el globo salga por lo NUEVO)
  await login(page, ADMIN)

  // 2) el buscador y «Seleccionar todo»
  await page.goto('/system/announcements')
  await expect(page.getByRole('heading', { level: 1, name: 'Enviar avisos' })).toBeVisible({ timeout: 20_000 })
  await shot(page, isMobile, 'l54-pantalla')
  const to = page.getByRole('combobox', { name: 'Para' })
  await to.fill('teikem')
  const all = page.getByRole('option', { name: /Seleccionar todo \(\d+\)/ })
  await expect(all).toBeVisible()
  await expect(page.getByRole('listbox').getByRole('option').filter({ hasText: 'teikem+dispatch@cerevelo.com' })).toBeVisible()   // nombre y correo
  await shot(page, isMobile, 'l54-buscador')
  await all.click()
  await expect(page.getByText(/\d+ personas y 0 roles escogidos\./)).toBeVisible()
  await page.getByRole('button', { name: 'Quitar todo' }).click()   // «Seleccionar todo» no escogió a nadie fuera de lo filtrado: se puede quitar de un golpe

  // 3) a UNA persona
  await to.fill('carlos')
  await page.getByRole('listbox').getByRole('option').filter({ hasText: 'Carlos Rivera' }).click()
  await page.getByLabel('Título').fill(one)
  await page.getByLabel('Texto (opcional)').fill('Por favor revise las rutas de hoy.')
  await shot(page, isMobile, 'l54-formulario')
  await page.getByRole('button', { name: 'Enviar aviso' }).click()
  await expect(page.locator('.toast.on').first()).toContainText('Aviso enviado a 1 personas')

  // Carlos lo recibe EN VIVO
  await expect(carlos.getByRole('button', { name: /^Avisos: [1-9]/ })).toBeVisible({ timeout: 15_000 })
  const bubble = carlos.locator('.kemi-bubble').filter({ hasText: /aviso/ })
  await expect(bubble).toBeVisible({ timeout: 15_000 })
  await shot(carlos, isMobile, 'l54-kemi-globo')
  await bubble.getByRole('button', { name: /Ver/ }).click()
  const pop = carlos.getByRole('dialog', { name: 'Avisos' })
  const item = pop.getByRole('button', { name: new RegExp(one) })
  await expect(item).toContainText('De Administrador Advance')
  await expect(item).toContainText('Por favor revise las rutas de hoy.')
  expect(await noHScroll(carlos)).toBe(true)
  await shot(carlos, isMobile, 'l54-campanita', pop)
  await item.click()   // al abrirlo queda leído

  // a TODAS las personas, con confirmación
  await page.getByRole('switch', { name: 'Enviar a todas las personas de la compañía' }).check({ force: true })
  await expect(page.getByText(/Se enviará a las \d+ personas activas/)).toBeVisible()
  await page.getByLabel('Título').fill(everyone)
  await page.getByRole('button', { name: 'Enviar aviso' }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByText(/El aviso le llegará a las \d+ personas activas/)).toBeVisible()
  await shot(page, isMobile, 'l54-confirmar', dialog)
  await dialog.getByRole('button', { name: 'Enviar aviso' }).click()
  await expect(page.locator('.toast.on').first()).toContainText('Aviso enviado a')
  await expect(carlos.getByRole('button', { name: /^Avisos: [1-9]/ })).toBeVisible({ timeout: 15_000 })

  // 4) historial: a quién y cuántas personas lo leyeron
  await expect
    .poll(async () => {
      const list = (await (await request.get(`${API_URL}/api/v1/announcements?take=50`, { headers: asAdmin })).json()) as AnnouncementDtoPagedResult
      return list.items?.find((a) => a.title === one)?.readCount ?? -1
    }, { timeout: 20_000 })
    .toBe(1)
  await page.reload()
  const row = page.locator('tr, .dt-cards > li').filter({ has: page.getByText(one, { exact: true }) }).first()
  await expect(row).toContainText('Carlos Rivera')
  await expect(row).toContainText('1 de 1')
  const allRow = page.locator('tr, .dt-cards > li').filter({ has: page.getByText(everyone, { exact: true }) }).first()
  await expect(allRow).toContainText('Todas las personas')
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l54-historial')
  const missing = await request.post(`${API_URL}/api/v1/announcements`, { headers: asAdmin, data: { title: ' ', userIds: [1] } })
  expect(missing.status()).toBe(400)
  expect(await missing.text()).toContain('Escriba el título del aviso.')
  await carlosCtx.close()
})
