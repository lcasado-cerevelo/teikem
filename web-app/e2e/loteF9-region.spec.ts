// Recorrido del lote F9 (región y formatos) contra el API real. CAMBIA los formatos de la compañía demo (región Estados Unidos
// con hora de 24 h y fecha Año-Mes-Día) y los RESTAURA a Puerto Rico al final (y en afterAll por API, aunque falle): corre en
// su propio proyecto (`escritorio-f9`, después de todos los demás; ver playwright.config.ts) para no cambiarle el formato a
// otro recorrido a medio camino.
// - cambiar la región a US (zona America/New_York) y personalizar hora y fecha → la vista previa lo muestra antes de guardar;
// - al guardar, OTRAS pantallas cambian sin recargar: el reloj de la cabecera (24 h), el Kárdex (fecha AAAA-MM-DD) y el
//   "Conteo de lo cambiado" (hora de la compañía: America/New_York);
// - "Puerto Rico" + Guardar restaura el juego completo de format-options (lo comprueba por API).
// Capturas para el manual: docs/manual/frontend/img/f9-<pantalla>.png.
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type TenantSettingsDto = components['schemas']['TenantSettingsDto']
type TenantFormatOptionsDto = components['schemas']['TenantFormatOptionsDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const FORMAT_FIELDS = [
  'regionCode',
  'timeZoneId',
  'currencyCode',
  'currencySymbol',
  'currencySymbolPosition',
  'currencyDecimals',
  'dateOrder',
  'dateSeparator',
  'timeFormat',
  'weekStartDay',
  'thousandsSeparator',
  'decimalSeparator',
  'phoneCountryCode',
  'phoneMask',
] as const

test.use({ locale: 'es-PR' })
test.describe.configure({ mode: 'serial' })

const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

async function shot(page: Page, name: string) {
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}f9-${name}.png`, animations: 'disabled', caret: 'hide', fullPage: true })
}

async function apiToken(request: APIRequestContext): Promise<string> {
  const res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  expect(res.ok()).toBeTruthy()
  const body = (await res.json()) as AuthResultDto
  expect(body.status).toBe('ok')
  return body.tokens?.accessToken ?? ''
}

/** Puerto Rico completo (el juego de format-options), por API. */
async function restorePuertoRico(request: APIRequestContext) {
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const options = (await (await request.get(`${API_URL}/api/v1/tenant/format-options`, { headers })).json()) as TenantFormatOptionsDto
  const pr = options.regions?.find((r) => r.regionCode === 'PR')
  expect(pr).toBeTruthy()
  const res = await request.put(`${API_URL}/api/v1/tenant/settings`, { headers, data: pr })
  expect(res.ok()).toBeTruthy()
}

async function login(page: Page) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(ADMIN.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(ADMIN.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login', { timeout: 30_000 })
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/', { timeout: 30_000 })
}

/** Navega dentro de la app (sin recargar) con la paleta de comandos, para comprobar que los formatos cambian en vivo. */
async function goInApp(page: Page, title: string) {
  await page.getByRole('button', { name: /Buscar o ejecutar/ }).click()
  const dialog = page.getByRole('dialog')
  await dialog.getByRole('combobox').fill(title)
  await dialog.getByRole('option', { name: new RegExp(`^${title}`) }).first().click()
}

test.beforeAll(async ({ request }) => {
  await restorePuertoRico(request)
})

test.afterAll(async ({ request }) => {
  await restorePuertoRico(request)
})

test('Región y formatos: US con 24 h y AAAA-MM-DD cambia otras pantallas sin recargar; Puerto Rico lo restaura', async ({ page, request }) => {
  await page.setViewportSize({ width: 1366, height: 900 })
  await login(page)
  await page.goto('/system/settings')
  await shot(page, 'general')

  await page.getByRole('tab', { name: 'Región y formatos' }).click()
  const preview = page.getByTestId('format-preview')
  await expect(preview.locator('[data-pv="pvTime"]')).toHaveText(/^\d{1,2}:\d{2}\s(a|p)\.\sm\.$/)
  // reloj de la cabecera: 12 h de Puerto Rico
  const clock = page.getByTestId('live-clock').locator('time')
  await expect(clock).toHaveText(/^\d{1,2}:\d{2}:\d{2}\s(a|p)\.\sm\.$/)
  await shot(page, 'region')

  // US: zona America/New_York (el juego de format-options) y personalizar hora y fecha
  await page.getByRole('button', { name: 'Estados Unidos' }).click()
  await expect(page.getByLabel('Zona horaria')).toHaveValue('America/New_York')
  await page.getByLabel('Hora', { exact: true }).selectOption('24')
  await page.getByLabel('Orden de la fecha').selectOption('YMD')
  await page.getByLabel('Separador', { exact: true }).selectOption('-')
  await expect(page.getByText('Personalizada')).toBeVisible()
  await expect(preview.locator('[data-pv="pvToday"]')).toHaveText(/^\d{4}-\d{2}-\d{2}$/)
  await expect(preview.locator('[data-pv="pvTime"]')).toHaveText(/^\d{2}:\d{2}$/)
  // sin guardar, el reloj de la app no cambia
  await expect(clock).toHaveText(/(a|p)\.\sm\.$/)
  await shot(page, 'region-us-personalizada')
  await page.getByRole('button', { name: 'Guardar cambios' }).click()
  await expect(page.locator('.toast').filter({ hasText: 'Ajustes guardados' }).first()).toBeVisible()

  // otras pantallas, sin recargar: el reloj en 24 h…
  await expect(clock).toHaveText(/^\d{2}:\d{2}:\d{2}$/)
  // …el Kárdex con fechas AAAA-MM-DD…
  await goInApp(page, 'Kárdex de movimientos')
  await expect(page).toHaveURL(/\/warehouse\/kardex/)
  const firstDate = page.locator('table.lst tbody tr').first().locator('td').first()
  await expect(firstDate).toHaveText(/^\d{4}-\d{2}-\d{2}$/)
  // …y la hora de la compañía con la zona nueva en "Conteo de lo cambiado"
  await goInApp(page, 'Conteo cíclico')
  await page.getByRole('button', { name: 'Conteo de lo cambiado' }).click()
  await expect(page.getByRole('dialog').getByText(/Hora de la compañía \(America\/New_York\)/)).toBeVisible()
  await page.getByRole('dialog').getByRole('button', { name: 'Cancelar' }).click()

  // el servidor guardó US personalizada
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const saved = (await (await request.get(`${API_URL}/api/v1/tenant/settings`, { headers })).json()) as TenantSettingsDto
  expect(saved).toMatchObject({ regionCode: 'US', timeZoneId: 'America/New_York', timeFormat: 24, dateOrder: 'YMD', dateSeparator: '-', isRegionCustomized: true })

  // restaurar Puerto Rico desde la pantalla
  await goInApp(page, 'Ajustes de la compañía')
  await page.getByRole('tab', { name: 'Región y formatos' }).click()
  await page.getByRole('button', { name: 'Puerto Rico' }).click()
  await expect(page.getByText('Personalizada')).toHaveCount(0)
  await page.getByRole('button', { name: 'Guardar cambios' }).click()
  await expect(page.locator('.toast').filter({ hasText: 'Ajustes guardados' }).first()).toBeVisible()
  await expect(clock).toHaveText(/(a|p)\.\sm\.$/)
  const options = (await (await request.get(`${API_URL}/api/v1/tenant/format-options`, { headers })).json()) as TenantFormatOptionsDto
  const pr = options.regions?.find((r) => r.regionCode === 'PR') as Record<string, unknown>
  const back = (await (await request.get(`${API_URL}/api/v1/tenant/settings`, { headers })).json()) as Record<string, unknown>
  for (const k of FORMAT_FIELDS) expect(back[k], k).toEqual(pr[k])
  expect(back.isRegionCustomized).toBe(false)

  // capturas del resto de pestañas para el manual (sin cambiar nada)
  for (const [tab, name] of [
    ['Calendario', 'calendario'],
    ['Módulos', 'modulos'],
    ['Operación', 'operacion'],
    ['Marca', 'marca'],
  ] as const) {
    await page.getByRole('tab', { name: tab }).click()
    if (name === 'operacion') await expect(page.getByRole('button', { name: 'Abrir almacén' }).first()).toBeVisible()
    await shot(page, name)
    if (name === 'operacion') {
      // el panel de recepción queda bajo el pliegue: captura del panel solo
      const recv = page.locator('.panel').filter({ has: page.getByRole('heading', { name: 'Recepción por almacén' }) })
      await recv.scrollIntoViewIfNeeded()
      await recv.screenshot({ path: `${IMG_DIR}f9-operacion-recepcion.png`, animations: 'disabled', caret: 'hide' })
    }
  }
})

test('móvil 360 px: Región y formatos en una columna, sin scroll horizontal', async ({ page }) => {
  await page.setViewportSize({ width: 360, height: 780 })
  await login(page)
  await page.goto('/system/settings?tab=region')
  await expect(page.getByTestId('format-preview')).toBeVisible()
  await shot(page, 'region-movil')
})
