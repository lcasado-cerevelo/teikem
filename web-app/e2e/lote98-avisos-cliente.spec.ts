// Recorrido de «Avisos al cliente» (Lote 98, P10) contra el API real (db-init hecho; API en API_URL).
//
// Un cliente nuevo por API; en su ficha el panel «Avisos al cliente» nace con todo apagado; se enciende «Entregado» por correo y bandeja con un correo extra, se guarda, se recarga la página y la
// configuración sigue; un correo mal escrito se avisa y no deja guardar. Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l98-<paso>.png.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
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

test('Avisos al cliente: todo apagado, encender Entregado, validar y guardar', async ({ page, request, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const name = `Aviso ${suffix}`
  const created = await request.post(`${API_URL}/api/v1/clients`, { headers, data: { name } })
  expect(created.ok(), await created.text()).toBeTruthy()
  const clientId = ((await created.json()) as { publicId: string }).publicId

  if (!isMobile) await page.setViewportSize({ width: 1280, height: 1000 })
  await login(page)
  await page.goto(`/catalog/clients?client=${clientId}`)
  const panel = page.locator('section.kit-panel', { has: page.getByRole('heading', { name: 'Avisos al cliente' }) })
  await expect(panel).toBeVisible({ timeout: 20_000 })
  await expect(panel).toContainText('0 aviso(s) encendido(s)')
  await panel.getByRole('button', { name: 'Abrir el panel' }).click()
  await expect(panel.getByText('Entregado', { exact: true })).toBeVisible()
  expect(await noHScroll(page)).toBe(true)

  // un correo mal escrito no deja guardar
  const delivered = panel.locator('fieldset', { has: page.locator('legend', { hasText: 'Entregado' }) })
  await delivered.getByLabel('Correo', { exact: true }).check()
  await delivered.getByLabel('Correos extra (separados por coma)').fill('nope')
  await expect(delivered).toContainText("El correo 'nope' no es válido.")
  await expect(panel.getByRole('button', { name: 'Guardar avisos' }).first()).toBeDisabled()

  // se corrige y se guarda
  await delivered.getByLabel('Correos extra (separados por coma)').fill(`gerencia-${suffix.toLowerCase()}@ejemplo.com`)
  await delivered.getByLabel('Bandeja del portal').check()
  await shot(page, isMobile, 'l98-panel', panel)
  await panel.getByRole('button', { name: 'Guardar avisos' }).first().click()
  await expect(panel.getByRole('button', { name: 'Guardar avisos' })).toHaveCount(0, { timeout: 20_000 })

  // la configuración queda guardada
  await page.reload()
  const again = page.locator('section.kit-panel', { has: page.getByRole('heading', { name: 'Avisos al cliente' }) })
  await expect(again).toContainText('1 aviso(s) encendido(s)', { timeout: 20_000 })
  await again.getByRole('button', { name: 'Abrir el panel' }).click()
  const d2 = again.locator('fieldset', { has: page.locator('legend', { hasText: 'Entregado' }) })
  await expect(d2.getByLabel('Correo', { exact: true })).toBeChecked()
  await expect(d2.getByLabel('Bandeja del portal')).toBeChecked()
  await expect(d2.getByLabel('Correos extra (separados por coma)')).toHaveValue(`gerencia-${suffix.toLowerCase()}@ejemplo.com`)
  const recogido = again.locator('fieldset', { has: page.locator('legend', { hasText: 'Recogido' }) })
  await expect(recogido.getByLabel('Correo', { exact: true })).not.toBeChecked()
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l98-guardado', again)
})
