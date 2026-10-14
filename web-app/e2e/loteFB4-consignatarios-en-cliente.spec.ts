// Recorrido del panel «Consignatarios» de la ficha del cliente y del Exportar de la pantalla Consignatarios (Lote F-B4) contra el
// API real (db-init hecho, API en API_URL, por defecto http://localhost:5000; compañía demo, admin con todos los permisos).
//
// 1) Ficha del cliente: el panel Consignatarios va al final, antes del historial de estatus; con 12 consignatarios muestra 10 filas por
//    página y se pagina ahí mismo; los paneles de la ficha son desplegables y arrancan cerrados, cada uno con su resumen;
//    el buscador filtra todas; Exportar (CSV) baja solo lo filtrado; «Nuevo consignatario» crea uno con este cliente como dueño sin
//    salir de la ficha y la tabla se refresca (el contador sube a 13); «Importar» abre su ventana.
// 2) Consignatarios ya filtrado por el cliente (`?client=`); su Exportar de la cabecera baja lo que la lista tiene a la vista.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/fb4-<paso>.png (escritorio).
import { readFileSync, mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']

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
  expect(res.ok()).toBeTruthy()
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

async function ok<T>(res: Promise<import('@playwright/test').APIResponse>, what: string): Promise<T> {
  const r = await res
  expect(r.ok(), `${what}: ${r.status()} ${await r.text()}`).toBeTruthy()
  return (await r.json()) as T
}

test('Ficha del cliente: panel Consignatarios (10 filas, buscador, Exportar, Nuevo, Importar) y Exportar en Consignatarios', async ({ page, request, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const clientName = `Cliente FB4 ${suffix}`
  const token = await apiToken(request)
  const headers = { Authorization: `Bearer ${token}` }
  const client = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName } }), 'cliente')
  for (let i = 1; i <= 12; i++) {
    const n = String(i).padStart(2, '0')
    await ok(
      request.post(`${API_URL}/api/v1/locations`, {
        headers,
        data: { clientPublicId: client.publicId, locationType: 'DELIVERY', name: `Consignatario ${n} ${suffix}`, line1: `Calle ${n}`, city: i === 12 ? 'Ponce' : 'Caguas', postalCode: i === 12 ? '00730' : '00725', country: 'PR' },
      }),
      `consignatario ${n}`,
    )
  }

  await login(page)
  await page.goto(`/catalog/clients?client=${client.publicId}`)
  const panel = page.locator('.panel', { has: page.getByRole('heading', { name: 'Consignatarios' }) })
  await expect(panel).toBeVisible({ timeout: 20_000 })
  await panel.scrollIntoViewIfNeeded()

  // va al final: justo antes del historial de estatus
  const titles = await page.locator('.cl-stack > .panel > header h2').allTextContents()
  expect(titles.at(-1)).toMatch(/Historial/i)
  expect(titles.at(-2)).toMatch(/Consignatarios/)

  // los paneles de la ficha arrancan CERRADOS, cada uno con su resumen (aquí el de Consignatarios)
  const stack = page.locator('.cl-stack')
  await expect(stack.getByRole('button', { name: 'Cerrar el panel' })).toHaveCount(0)
  await expect(panel.getByText('Consignatarios activos: 12')).toBeVisible()
  const size = page.viewportSize()
  if (size && !isMobile) await page.setViewportSize({ width: size.width, height: 1500 })
  await stack.scrollIntoViewIfNeeded()
  await shot(page, isMobile, 'fb4-paneles', stack)
  if (size && !isMobile) await page.setViewportSize(size)
  await shot(page, isMobile, 'fb4-colapsado', panel)
  await panel.getByRole('button', { name: 'Abrir el panel' }).click()
  await expect(panel.getByText('Consignatarios activos: 12')).toHaveCount(0)
  await expect(panel.getByRole('button', { name: /Nuevo consignatario/ })).toBeVisible()

  // 10 filas por página (12 en total) y la paginación está ahí mismo
  await expect(panel.getByText(`Consignatario 01 ${suffix}`)).toBeVisible()
  await expect(panel.getByText(`Consignatario 11 ${suffix}`)).toHaveCount(0)
  await expect(panel.locator('.r').first()).toHaveText('12')
  await panel.getByRole('button', { name: /siguiente|next/i }).first().click()
  await expect(panel.getByText(`Consignatario 11 ${suffix}`)).toBeVisible()
  await expect(panel.getByText(`Consignatario 01 ${suffix}`)).toHaveCount(0)
  await panel.getByRole('button', { name: /anterior|prev/i }).first().click()
  await expect(panel.getByText(`Consignatario 01 ${suffix}`)).toBeVisible()
  await shot(page, isMobile, 'fb4-panel', panel)

  // buscador: filtra las 12, no solo las 10 visibles
  await panel.getByRole('searchbox').fill('Ponce')
  await expect(panel.getByText(`Consignatario 12 ${suffix}`)).toBeVisible()
  await expect(panel.getByText(`Consignatario 01 ${suffix}`)).toHaveCount(0)
  await expect(panel.getByText('Mostrando', { exact: false })).toHaveCount(0)

  // Exportar CSV: solo lo filtrado
  const [download] = await Promise.all([page.waitForEvent('download'), (async () => {
    await panel.getByRole('button', { name: /Exportar/ }).click()
    await page.getByRole('menuitem', { name: /CSV/ }).click()
  })()])
  expect(download.suggestedFilename()).toMatch(/^consignatarios.*\.csv$/i)
  const csv = readFileSync((await download.path()) ?? '', 'utf-8')
  expect(csv).toContain(`Consignatario 12 ${suffix}`)
  expect(csv).not.toContain(`Consignatario 01 ${suffix}`)
  expect(csv).toContain('Persona de contacto')
  await panel.getByRole('searchbox').fill('')

  // Importar abre su ventana sin salir de la ficha
  await panel.getByRole('button', { name: 'Importar' }).click()
  const importDialog = page.getByRole('dialog')
  await expect(importDialog).toBeVisible()
  await shot(page, isMobile, 'fb4-importar')
  await page.keyboard.press('Escape')
  await expect(importDialog).toHaveCount(0)
  await expect(page).toHaveURL(new RegExp(`client=${client.publicId}`))

  // Nuevo: crea con este cliente como dueño, sigue en la ficha y la tabla se refresca (12 -> 13)
  await panel.getByRole('button', { name: /Nuevo consignatario/ }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await dialog.getByLabel('Nombre').fill(`Consignatario 13 ${suffix}`)
  await dialog.getByLabel(/Dirección|Línea 1/).first().fill('Calle 13')
  // el pueblo se elige del buscador de ciudades o códigos postales
  await dialog.getByPlaceholder(/Buscar ciudad/).fill('Caguas')
  await page.getByRole('listbox').getByRole('option').first().click()
  await shot(page, isMobile, 'fb4-nuevo')
  await dialog.getByRole('button', { name: /Guardar|Crear/ }).click()
  await expect(dialog).toHaveCount(0, { timeout: 15_000 })
  await expect(page).toHaveURL(new RegExp(`client=${client.publicId}`))
  await expect(panel.locator('.r').first()).toHaveText('13', { timeout: 15_000 })

  // Consignatarios filtrado por el cliente (`?client=`); su Exportar baja lo que la lista tiene a la vista
  await page.goto(`/catalog/consignees?client=${client.publicId}`)
  await expect(page.getByRole('combobox', { name: /Cliente/ })).toHaveValue(client.publicId ?? '')
  await expect(page.getByText(`Consignatario 13 ${suffix}`).first()).toBeVisible({ timeout: 15_000 })
  // la cabecera trae, juntos y en este orden: Exportar, Importar y Nuevo consignatario
  const names = await page.locator('.head .act button').allTextContents()
  expect(names.map((n) => n.trim())).toEqual(['Exportar', 'Importar', 'Nuevo consignatario'])
  await page.locator('h1').first().scrollIntoViewIfNeeded()
  await shot(page, isMobile, 'fb4-lista')
  const [dl2] = await Promise.all([page.waitForEvent('download'), (async () => {
    await page.getByRole('button', { name: /Exportar/ }).click()
    await page.getByRole('menuitem', { name: /CSV/ }).click()
  })()])
  const csv2 = readFileSync((await dl2.path()) ?? '', 'utf-8')
  expect(csv2.split('\n').filter((l) => l.includes(suffix)).length).toBe(13)

  // sin scroll horizontal de página (también a 360 px)
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)
  expect(overflow).toBe(false)
})
