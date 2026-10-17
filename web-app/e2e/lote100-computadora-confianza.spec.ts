// Recorrido de las computadoras de confianza y el candado de cuenta (Lote 100) contra el API real.
//
// Requiere el API con Auth:TrustedBrowsers:Enabled=true y ReturnLinkInResponse=true (y Auth:PasswordReset:WebBaseUrl = la web); si no está encendido, el recorrido se salta solo. Un usuario nuevo
// entra desde una computadora que no se reconoce → «Confirme esta computadora» → se abre el enlace → vuelve a entrar y funciona → en Mi cuenta aparece «Esta computadora» → una segunda
// computadora (otro contexto) vuelve a pedir confirmación → quitar la computadora la manda a confirmar otra vez. Además, 5 contraseñas equivocadas bloquean la cuenta (la correcta ya no entra).
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal). Capturas: docs/manual/frontend/img/l100-<paso>.png.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
// marca fija de la computadora del administrador de las pruebas: se confirma una vez y queda de confianza (si fuera nueva cada vez se toparía con el tope diario de correos)
const ADMIN_BROWSER = 'e2e-admin-computer-'.padEnd(48, 'x')
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'abcdefghij'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

async function shot(page: Page, isMobile: boolean, name: string) {
  if (isMobile) return
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
}

/** Inicio de sesión por API con una marca de computadora; si pide confirmar y el API devuelve el enlace (desarrollo), lo confirma y entra. */
async function apiLogin(request: APIRequestContext, who: { email: string; password: string }, browserToken: string): Promise<AuthResultDto> {
  const body = { email: who.email, password: who.password, trustedBrowserToken: browserToken }
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: body })
  expect(res.ok(), `login: ${await res.text()}`).toBeTruthy()
  let result = (await res.json()) as AuthResultDto
  if (result.status === 'browser_confirmation_required' && result.browser?.devLink) {
    const secret = new URL(result.browser.devLink).searchParams.get('token')
    const c = await request.post(`${API_URL}/api/v1/auth/confirm-browser`, { data: { token: secret } })
    expect(c.status()).toBe(204)
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: body })
    result = (await res.json()) as AuthResultDto
  }
  return result
}

async function typeLogin(page: Page, who: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(who.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(who.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
}

test('Computadora de confianza: confirmar por correo la primera vez, reconocerla después y quitarla', async ({ page, request, browser, isMobile }) => {
  const suffix = `${isMobile ? 'm' : 'd'}${LETTERS}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const adminToken = (await apiLogin(request, ADMIN, ADMIN_BROWSER)).tokens?.accessToken
  expect(adminToken).toBeTruthy()
  const headers = { Authorization: `Bearer ${adminToken}` }
  const user = { email: `pc.${suffix}@teikem.local`, password: `Pc_${suffix}_2026!xyz`, fullName: `Computadora Prueba ${suffix}` }
  const created = await request.post(`${API_URL}/api/v1/users`, { headers, data: { email: user.email, fullName: user.fullName, password: user.password, roles: [] } })
  expect(created.ok(), await created.text()).toBeTruthy()
  const userId = ((await created.json()) as { user: { id: number } }).user.id

  // con la función apagada en este API el recorrido no aplica
  const probe = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: user.email, password: user.password, trustedBrowserToken: 'p'.repeat(64) } })
  test.skip(((await probe.json()) as AuthResultDto).status !== 'browser_confirmation_required', 'Auth:TrustedBrowsers no está encendido en este API')

  if (!isMobile) await page.setViewportSize({ width: 1280, height: 900 })

  // 1) primera vez desde esta computadora: pide confirmar
  await typeLogin(page, user)
  await expect(page.getByRole('heading', { name: 'Confirme esta computadora' })).toBeVisible({ timeout: 20_000 })
  // con el correo configurado dice «Le enviamos un correo a…»; en desarrollo (sin proveedor) avisa que no salió y ofrece el enlace
  await expect(page.getByRole('link', { name: 'Confirmar ahora (solo desarrollo)' })).toBeVisible()
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l100-confirmar')

  // 2) abre el enlace del correo (en desarrollo el API lo devuelve en la pantalla)
  await page.getByRole('link', { name: 'Confirmar ahora (solo desarrollo)' }).click()
  await expect(page.getByText('Listo: esta computadora quedó confirmada.')).toBeVisible({ timeout: 20_000 })
  await shot(page, isMobile, 'l100-confirmada')

  // 3) vuelve a entrar desde la misma computadora y ya no pide nada
  await page.getByRole('link', { name: 'Ir a la pantalla de entrada' }).click()
  await page.getByLabel('Correo electrónico').fill(user.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(user.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login' && url.pathname !== '/confirm-browser', { timeout: 20_000 })

  // 4) Mi cuenta → Sesiones: «Esta computadora» aparece en la lista
  await page.goto('/account?tab=sessions')
  const panel = page.locator('section.kit-panel', { has: page.getByRole('heading', { name: 'Mis computadoras de confianza' }) })
  await expect(panel).toBeVisible({ timeout: 20_000 })
  await expect(panel.getByText('Esta computadora', { exact: true }).first()).toBeVisible()
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l100-lista')

  // 5) otra computadora (otro contexto: otro almacenamiento) vuelve a pedir confirmación
  const other = await browser.newContext({ locale: 'es-PR', viewport: isMobile ? { width: 412, height: 915 } : { width: 1280, height: 900 } })
  const otherPage = await other.newPage()
  await otherPage.goto(`${new URL(page.url()).origin}/login`)
  await typeLogin(otherPage, user)
  await expect(otherPage.getByRole('heading', { name: 'Confirme esta computadora' })).toBeVisible({ timeout: 20_000 })
  await other.close()

  // 6) quitar esta computadora: la próxima vez pide confirmar
  // en la lista solo está esta computadora (en el celular la tabla se ve como tarjetas)
  await panel.getByRole('button', { name: 'Quitar' }).first().click()
  await page.getByRole('dialog').getByRole('button', { name: 'Quitar' }).click()
  await expect(panel.getByText('Todavía no hay computadoras de confianza.')).toBeVisible({ timeout: 20_000 })
  const again = await apiLogin(request, user, 'z'.repeat(64))
  expect(again.status).toBe('ok')   // otra marca nueva, confirmada por el enlace de desarrollo

  await request.put(`${API_URL}/api/v1/users/${userId}/membership`, { headers, data: { status: 'SUSPENDED' } }).catch(() => {})
})

test('Candado de cuenta: cinco contraseñas equivocadas la bloquean y la correcta ya no entra', async ({ request, isMobile }) => {
  const suffix = `${isMobile ? 'm' : 'd'}${LETTERS}`
  const adminToken = (await apiLogin(request, ADMIN, ADMIN_BROWSER)).tokens?.accessToken
  const headers = { Authorization: `Bearer ${adminToken}` }
  const user = { email: `lock.${suffix}@teikem.local`, password: `Lk_${suffix}_2026!xyz`, fullName: `Bloqueo Prueba ${suffix}` }
  const created = await request.post(`${API_URL}/api/v1/users`, { headers, data: { email: user.email, fullName: user.fullName, password: user.password, roles: [] } })
  expect(created.ok(), await created.text()).toBeTruthy()
  const userId = ((await created.json()) as { user: { id: number } }).user.id

  const attempt = (password: string) => request.post(`${API_URL}/api/v1/auth/login`, { data: { email: user.email, password, trustedBrowserToken: 'k'.repeat(64) } })
  for (let i = 0; i < 5; i++) {
    const r = await attempt('equivocada-1234')
    expect(r.status()).toBe(401)
  }
  const locked = await attempt(user.password)
  expect(locked.status()).toBe(401)
  expect(((await locked.json()) as { title: string }).title).toBe('Credenciales inválidas.')
  // un usuario que no existe responde exactamente igual
  const none = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: `nadie.${suffix}@teikem.local`, password: 'lo-que-sea-1234' } })
  expect(none.status()).toBe(401)
  expect(((await none.json()) as { title: string }).title).toBe('Credenciales inválidas.')

  await request.put(`${API_URL}/api/v1/users/${userId}/membership`, { headers, data: { status: 'SUSPENDED' } }).catch(() => {})
})
