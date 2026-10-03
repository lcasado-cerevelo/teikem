// Recorrido del lote F10 (Seguridad y auditoría, /system/audit) contra el API real (db-init hecho, API en API_URL, por defecto
// http://localhost:5000; compañía demo "Advance Logistics").
// Escritorio:
// - Ajustes → General → "Abrir Seguridad y auditoría" abre la pestaña Sesiones y MFA (?tab=sessions);
// - una sesión creada por API (aparato "e2e-f10-…") aparece en la lista de la compañía y se revoca desde la pantalla (la propia
//   se marca "Esta sesión" y no se puede revocar); NUNCA "Cerrar las demás sesiones": cerraría las de los otros recorridos;
// - Actividad: filtro Seguridad + buscador encuentran el TOKEN_REVOKED de esa revocación y "Exportar CSV" descarga solo lo
//   filtrado (encabezados de la maqueta, sin filas de "Cambio");
// - política: cambia la duración de la sesión (30 → 29 días, inocuo para los demás recorridos: no toca MFA ni la ventana de
//   reautenticación), lo comprueba por API y la RESTAURA desde la pantalla (y en afterAll por API, aunque falle).
// Móvil (360 px): las dos pestañas sin scroll horizontal.
// Capturas para el manual: docs/manual/frontend/img/f10-<pantalla>.png.
import { mkdirSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type TenantSettingsDto = components['schemas']['TenantSettingsDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

test.use({ locale: 'es-PR' })

async function shot(page: Page, name: string) {
  await page.waitForLoadState('networkidle')
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({ path: `${IMG_DIR}f10-${name}.png`, animations: 'disabled', caret: 'hide', fullPage: true })
}

async function apiLogin(request: APIRequestContext, deviceInfo?: string): Promise<string> {
  const res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password, deviceInfo } })
  expect(res.ok()).toBeTruthy()
  const body = (await res.json()) as AuthResultDto
  expect(body.status).toBe('ok')
  return body.tokens?.accessToken ?? ''
}

async function settings(request: APIRequestContext, token: string): Promise<TenantSettingsDto> {
  const res = await request.get(`${API_URL}/api/v1/tenant/settings`, { headers: { Authorization: `Bearer ${token}` } })
  expect(res.ok()).toBeTruthy()
  return (await res.json()) as TenantSettingsDto
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

/** Sin scroll horizontal de página y sin elementos visibles fuera del ancho de la ventana (mismo criterio que loteF9). */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    const describe = (el: Element) => `${el.tagName.toLowerCase()}.${String(el.getAttribute('class') ?? '').trim().replace(/\s+/g, '.')}`
    document.querySelectorAll('.stage *, .bar *').forEach((el) => {
      if (el.parentElement?.closest('.seg')) return
      const style = getComputedStyle(el)
      if (style.visibility === 'hidden' || style.display === 'none') return
      const r = el.getBoundingClientRect()
      if (r.width > 0 && (r.right > vw + 1 || r.left < -1)) out.push(`${describe(el)} [${Math.round(r.left)}, ${Math.round(r.right)}] fuera de 0..${vw}`)
    })
    return { scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth, offenders: out }
  })
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
  expect(offenders).toEqual([])
}

// duración de la sesión al empezar (la restaura afterAll si el recorrido falla a medio camino)
let originalSessionDays: number | null = null

test.afterAll(async ({ request }) => {
  if (originalSessionDays == null) return
  const token = await apiLogin(request)
  const res = await request.put(`${API_URL}/api/v1/tenant/settings`, { headers: { Authorization: `Bearer ${token}` }, data: { sessionDays: originalSessionDays } })
  expect(res.ok()).toBeTruthy()
})

test('Seguridad y auditoría: revocar una sesión, filtrar y exportar la actividad, cambiar la política y restaurarla', async ({ page, request, isMobile }) => {
  test.skip(isMobile, 'el cambio de política y la revocación se hacen una sola vez (escritorio); el móvil solo comprueba el ancho')
  await page.setViewportSize({ width: 1366, height: 900 })
  const token = await apiLogin(request)
  originalSessionDays = (await settings(request, token)).sessionDays ?? 30
  const device = `e2e-f10-${Date.now()}`
  await apiLogin(request, device)

  await login(page)
  // Ajustes → General abre directo la pestaña Sesiones y MFA
  await page.goto('/system/settings')
  await page.getByRole('button', { name: 'Abrir Seguridad y auditoría' }).click()
  await expect(page).toHaveURL(/\/system\/audit\?tab=sessions$/)
  await expect(page.getByRole('heading', { level: 1, name: 'Seguridad y auditoría' })).toBeVisible()
  await expect(page.getByRole('tab', { name: 'Sesiones y MFA' })).toHaveAttribute('aria-selected', 'true')

  // sesiones de la compañía: la propia marcada sin "Revocar"; la del aparato de prueba se revoca
  const table = page.getByRole('table', { name: 'Sesiones activas' })
  const mine = table.getByRole('row').filter({ hasText: 'Esta sesión' })
  await expect(mine).toHaveCount(1)
  await expect(mine.getByRole('button', { name: 'Revocar' })).toHaveCount(0)
  const target = table.getByRole('row').filter({ hasText: device })
  await expect(target).toHaveCount(1)
  await shot(page, 'sesiones')
  await target.getByRole('button', { name: 'Revocar' }).click()
  const dialog = page.getByRole('dialog', { name: '¿Revocar esta sesión?' })
  await dialog.getByRole('button', { name: 'Revocar' }).click()
  await expect(page.getByText('Sesión revocada')).toBeVisible()
  await expect(table.getByRole('row').filter({ hasText: device })).toHaveCount(0)

  // política: 30 → 29 días, comprobado por API, y restaurada desde la pantalla
  const days = page.getByLabel('Duración de la sesión (días)')
  await expect(days).toHaveValue(String(originalSessionDays))
  const changed = originalSessionDays === 29 ? 28 : 29
  await days.fill(String(changed))
  await page.getByRole('button', { name: 'Guardar política' }).click()
  await expect(page.getByText('Política guardada')).toBeVisible()
  expect((await settings(request, token)).sessionDays).toBe(changed)
  // valor fuera de rango: el mensaje junto al campo y nada se manda
  await days.fill('0')
  await page.getByRole('button', { name: 'Guardar política' }).click()
  await expect(page.getByText('Entre 1 y 365 días.')).toBeVisible()
  expect((await settings(request, token)).sessionDays).toBe(changed)
  await days.fill(String(originalSessionDays))
  await page.getByRole('button', { name: 'Guardar política' }).click()
  await expect(page.getByText('Política guardada').first()).toBeVisible()
  await expect.poll(async () => (await settings(request, token)).sessionDays).toBe(originalSessionDays)

  // Actividad: Seguridad + buscador → el TOKEN_REVOKED de la revocación; Exportar CSV con lo filtrado
  await page.getByRole('tab', { name: 'Actividad' }).click()
  await expect(page).toHaveURL(/\/system\/audit$/)
  await expect(page.getByRole('table', { name: 'Actividad' })).toBeVisible()
  await shot(page, 'actividad')
  await page.getByRole('button', { name: 'Seguridad', exact: true }).click()
  await expect(page.getByRole('button', { name: 'Seguridad', exact: true })).toHaveAttribute('aria-pressed', 'true')
  await page.getByRole('searchbox', { name: 'Buscar en la actividad…' }).fill('company_session')
  const activity = page.getByRole('table', { name: 'Actividad' })
  // la búsqueda va al API 300 ms después de escribir: esperar a que TODAS las filas sean de la revocación
  await expect(activity.locator('tbody tr').filter({ hasNotText: 'company_session' })).toHaveCount(0)
  await expect(activity.locator('tbody tr').filter({ hasText: 'Token revocado' }).first()).toBeVisible()
  await expect(activity.getByText('Cambio', { exact: true })).toHaveCount(0)
  await shot(page, 'actividad-filtrada')
  const [download] = await Promise.all([page.waitForEvent('download'), page.getByRole('button', { name: /Exportar CSV/ }).click()])
  expect(download.suggestedFilename()).toMatch(/^auditoria-\d{4}-\d{2}-\d{2}\.csv$/)
  const csv = readFileSync((await download.path())!, 'utf8').replace(/^﻿/, '')
  const lines = csv.split('\r\n')
  expect(lines[0]).toBe('Cuándo,Tipo,Usuario,Detalle')
  expect(lines.length).toBeGreaterThan(1)
  expect(lines.slice(1).every((l) => !/^[^,]*,Cambio,/.test(l))).toBe(true)
  // solo lo filtrado: cada fila es un evento con company_session
  expect(lines.slice(1).filter((l) => l !== '').every((l) => l.includes('company_session'))).toBe(true)
  await expect(page.getByText(/^Descargando \d+ filas$/)).toBeVisible()
})

test('móvil 360 px: Actividad y Sesiones y MFA sin scroll horizontal', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'solo en el proyecto móvil')
  await login(page)
  await page.goto('/system/audit')
  await expect(page.getByRole('heading', { level: 1, name: 'Seguridad y auditoría' })).toBeVisible()
  await expect(page.locator('.dt-card').first()).toBeVisible()
  await expectNoHorizontalScroll(page)
  await shot(page, 'actividad-movil')
  await page.getByRole('tab', { name: 'Sesiones y MFA' }).click()
  await expect(page).toHaveURL(/tab=sessions/)
  await expect(page.getByLabel('Duración de la sesión (días)')).toBeVisible()
  await expectNoHorizontalScroll(page)
  await shot(page, 'sesiones-movil')
})
