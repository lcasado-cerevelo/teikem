// Recorrido del documento legal único (Lote 99) contra el API real (db-init hecho; API en API_URL con Legal:RequireAcceptance activo, que es lo normal).
//
// Un usuario nuevo entra por primera vez: en lugar de la aplicación sale «Política de privacidad y términos de uso»; «Acepto» está deshabilitado hasta llegar al final del texto; al aceptar entra y, al recargar,
// ya no se lo vuelve a pedir. Luego, el administrador ve en Seguridad y auditoría → Aceptación legal que ese usuario aceptó. Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página).
// Capturas: docs/manual/frontend/img/l99-<paso>.png.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import { manualImgDir, manualLang } from './manualLang'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
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
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
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
}

test('Documento legal: el primer ingreso pide leer y aceptar; el administrador ve quién aceptó', async ({ page, request, isMobile }) => {
  const suffix = `${isMobile ? 'm' : 'd'}${LETTERS}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const user = { email: `legal.${suffix}@teikem.local`, password: `Legal_${suffix}_2026!x`, fullName: `Legal Prueba ${suffix}` }
  const created = await request.post(`${API_URL}/api/v1/users`, { headers, data: { email: user.email, fullName: user.fullName, password: user.password, roles: [] } })
  expect(created.ok(), await created.text()).toBeTruthy()
  const userId = ((await created.json()) as { user: { id: number } }).user.id

  // con Legal:RequireAcceptance apagado (el CI lo apaga para los demás recorridos) este recorrido no aplica
  const asUser = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: user.email, password: user.password } })
  const asUserBody = (await asUser.json()) as AuthResultDto
  const status = await request.get(`${API_URL}/api/v1/legal/status`, { headers: { Authorization: `Bearer ${asUserBody.tokens?.accessToken ?? ''}` } })
  test.skip(!((await status.json()) as { required?: boolean }).required, 'Legal:RequireAcceptance está apagado en este API')

  if (!isMobile) await page.setViewportSize({ width: 1280, height: 900 })
  await login(page, user)

  // sale el documento, no la aplicación
  await expect(page.getByRole('heading', { level: 1, name: 'Política de privacidad y términos de uso' })).toBeVisible({ timeout: 20_000 })
  await expect(page.getByRole('heading', { name: 'Parte 1 — Política de privacidad' })).toBeVisible()
  const accept = page.getByRole('button', { name: 'Acepto la política de privacidad y los términos de uso' })
  await expect(accept).toBeDisabled()
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l99-documento')

  // llegar al final del texto habilita «Acepto»
  const box = page.getByTestId('legal-scroll')
  await box.evaluate((el) => el.scrollTo({ top: el.scrollHeight / 2 }))
  await expect(accept).toBeDisabled()
  await box.evaluate((el) => el.scrollTo({ top: el.scrollHeight }))
  await expect(accept).toBeEnabled()
  await expect(page.getByRole('heading', { name: 'Parte 2 — Términos de uso' })).toBeVisible()
  await shot(page, isMobile, 'l99-final')

  await accept.click()
  await expect(page.getByRole('heading', { level: 1, name: 'Política de privacidad y términos de uso' })).toHaveCount(0, { timeout: 20_000 })
  expect(await noHScroll(page)).toBe(true)

  // ya no se vuelve a pedir
  await page.reload()
  await expect(page.locator('main, .app-shell, .shell').first()).toBeVisible({ timeout: 20_000 })
  await expect(page.getByRole('button', { name: 'Acepto la política de privacidad y los términos de uso' })).toHaveCount(0)

  // el servidor lo registró y el administrador lo ve
  const list = await request.get(`${API_URL}/api/v1/legal/acceptances?q=${encodeURIComponent(user.email)}`, { headers })
  const rows = ((await list.json()) as { items: { email: string; accepted: boolean }[] }).items
  expect(rows.find((r) => r.email.toLowerCase() === user.email)?.accepted).toBe(true)

  const ctx = await page.context().browser()!.newContext({ locale: 'es-PR', viewport: isMobile ? { width: 412, height: 915 } : { width: 1280, height: 900 } })
  const admin = await ctx.newPage()
  await admin.goto(`${new URL(page.url()).origin}/login`)
  await login(admin, ADMIN)
  await admin.goto('/system/audit?tab=legal')
  await expect(admin.getByRole('heading', { name: 'Aceptación del documento legal' })).toBeVisible({ timeout: 20_000 })
  await admin.getByPlaceholder('Buscar por nombre o correo').fill(user.email)
  const row = admin.getByText(user.email, { exact: false }).first()
  await expect(row).toBeVisible({ timeout: 20_000 })
  await expect(admin.locator('.chip', { hasText: 'Aceptó' }).first()).toBeVisible()
  expect(await noHScroll(admin)).toBe(true)
  await shot(admin, isMobile, 'l99-aceptaciones')
  await ctx.close()

  await request.put(`${API_URL}/api/v1/users/${userId}/membership`, { headers, data: { status: 'SUSPENDED' } }).catch(() => {})
})
