// Recorrido de la ficha del cliente: panel «Direcciones» (física, postal, puntos de recogida y «Se recoge en»), «Perfil fiscal» con
// «Imprimir los labels automáticamente» antes que la factura, y los paneles desplegables con su resumen (Lote F-B5) contra el API real
// (db-init hecho, API en API_URL, por defecto http://localhost:5000; compañía demo, admin con todos los permisos).
//
// 1) La ficha de un cliente nuevo arranca con TODOS los paneles cerrados, cada uno con su resumen; «Direcciones» va justo después de la
//    cabecera y antes de «Perfil fiscal»; sin dirección dice «Sin dirección registrada».
// 2) UN solo botón, «Agregar dirección», que pregunta el tipo: física (CORPORATE), postal (BILLING, admite P.O. Box) y punto de recogida
//    (PICKUP); una vez que hay física y postal ya no se ofrecen. «Se recoge en» puede ser la física, la postal o el punto; quitar el
//    punto vuelve a recoger en la física. Se comprueba contra el API.
// 3) Perfil fiscal: los labels automáticos van antes que la factura y se guardan (autoPrintLabels).
// 4) Los paneles cerrados resumen lo que tienen (dirección y recogida, perfil fiscal, estatus…).
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/fb5-<paso>.png (escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang, manualTarget } from './manualLang'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!' }
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

/** Escribe el pueblo en el buscador de ciudades del modal y escoge la primera sugerencia. */
async function pickTown(page: Page, dialog: Locator, text: string) {
  await dialog.getByPlaceholder(/Buscar ciudad/).fill(text)
  await page.getByRole('listbox').getByRole('option').filter({ hasText: new RegExp(text, 'i') }).first().click()
}

test('Ficha del cliente: Direcciones, Perfil fiscal con labels automáticos y paneles con resumen', async ({ page, request, isMobile }) => {
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const token = await apiToken(request)
  const headers = { Authorization: `Bearer ${token}` }
  const client = await ok<ClientDetailDto>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente FB5 ${suffix}`, legalName: `Cliente FB5 ${suffix} LLC`, taxId: '66-1234567' } }), 'cliente')
  const detail = () => ok<ClientDetailDto>(request.get(`${API_URL}/api/v1/clients/${client.publicId}`, { headers }), 'ficha')

  await login(page)
  await page.goto(`/catalog/clients?client=${client.publicId}`)
  const addresses = page.locator('.panel', { has: page.getByRole('heading', { name: 'Direcciones', exact: true }) })
  await expect(addresses).toBeVisible({ timeout: 20_000 })
  const stack = page.locator('.cl-stack')

  // «Direcciones» justo después de la cabecera y antes de «Perfil fiscal»; todos los paneles arrancan cerrados
  const titles = await page.locator('.cl-stack > .panel > header h2').allTextContents()
  expect(titles[1]).toBe('Direcciones')
  expect(titles[2]).toBe('Perfil fiscal')
  await expect(stack.getByRole('button', { name: 'Cerrar el panel' })).toHaveCount(0)
  await expect(addresses.locator('.kit-summary')).toHaveText('Sin dirección registrada') // el resumen
  await addresses.scrollIntoViewIfNeeded()
  await addresses.getByRole('button', { name: 'Abrir el panel' }).click()
  await expect(addresses.locator('.cl-adr-list').getByText('Sin dirección registrada').first()).toBeVisible() // la física y la postal
  await shot(page, isMobile, 'fb5-direcciones-vacio', addresses)

  // UN solo botón para agregar: pregunta el tipo
  await expect(addresses.getByRole('button', { name: /Agregar|Otra dirección|punto de recogida/i })).toHaveCount(1)
  await addresses.getByRole('button', { name: 'Agregar dirección' }).click()
  let dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('heading', { name: 'Agregar dirección' })).toBeVisible()
  await expect(dialog.getByLabel('Tipo de dirección').locator('option')).toHaveText(['Dirección física', 'Dirección postal', 'Punto de recogida'])
  await dialog.getByLabel('Tipo de dirección').selectOption('physical')
  await dialog.getByLabel(/^Dirección/).first().fill('Calle Sol 5')
  await pickTown(page, dialog, 'Caguas')
  await shot(page, isMobile, 'fb5-modal')
  await dialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(dialog).toHaveCount(0, { timeout: 15_000 })
  await expect(addresses.locator('.cl-adr-list').getByText('Calle Sol 5')).toBeVisible({ timeout: 15_000 })
  await expect(addresses.locator('.cl-adr-list').getByText('Igual a la dirección física')).toBeVisible()
  let d = await detail()
  expect(d.physicalAddress?.line1).toBe('Calle Sol 5')
  expect(d.postalAddress).toBeFalsy()

  // la postal propia (BILLING) con un P.O. Box: ya no se ofrece la física
  await addresses.getByRole('button', { name: 'Agregar dirección' }).click()
  dialog = page.getByRole('dialog')
  await expect(dialog.getByLabel('Tipo de dirección').locator('option')).toHaveText(['Dirección postal', 'Punto de recogida'])
  await dialog.getByLabel('Tipo de dirección').selectOption('postal')
  await dialog.getByLabel(/^Dirección/).first().fill('P.O. Box 123')
  await pickTown(page, dialog, 'San Juan')
  await dialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(dialog).toHaveCount(0, { timeout: 15_000 })
  await expect(addresses.locator('.cl-adr-list').getByText('P.O. Box 123')).toBeVisible({ timeout: 15_000 })
  d = await detail()
  expect(d.postalAddress?.line1).toBe('P.O. Box 123')

  // un punto de recogida (PICKUP): lleva nombre
  await addresses.getByRole('button', { name: 'Agregar dirección' }).click()
  dialog = page.getByRole('dialog')
  await expect(dialog.getByLabel('Tipo de dirección').locator('option')).toHaveText(['Punto de recogida'])
  await dialog.getByLabel('Nombre del punto').fill(`Almacén Norte ${suffix}`)
  await dialog.getByLabel(/^Dirección/).first().fill('Carr. 2 km 10')
  await pickTown(page, dialog, 'Bayamon')
  await dialog.getByRole('button', { name: 'Guardar' }).click()
  await expect(dialog).toHaveCount(0, { timeout: 15_000 })
  await expect(addresses.locator('.cl-adr-list').getByText(`Almacén Norte ${suffix}`)).toBeVisible({ timeout: 15_000 })

  // «Se recoge en»: la física, la postal o el punto
  const pickup = addresses.getByLabel('Se recoge en')
  await expect(pickup.locator('option')).toHaveCount(3)
  await pickup.selectOption({ label: 'La dirección postal' })
  await expect.poll(async () => (await detail()).pickupAddress?.locationType, { timeout: 15_000 }).toBe('BILLING')
  await pickup.selectOption({ index: 2 })
  await expect.poll(async () => (await detail()).pickupAddress?.name, { timeout: 15_000 }).toBe(`Almacén Norte ${suffix}`)
  await shot(page, isMobile, 'fb5-direcciones', addresses)

  // quitar el punto: vuelve a recoger en la física
  await addresses.getByRole('button', { name: `Quitar Almacén Norte ${suffix}` }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Quitar', exact: true }).click()
  await expect(addresses.locator('.cl-adr-list').getByText(`Almacén Norte ${suffix}`)).toHaveCount(0, { timeout: 15_000 })
  await expect.poll(async () => (await detail()).pickupAddress?.isDefaultFromCorporate, { timeout: 15_000 }).toBe(true)
  await expect(pickup).toHaveValue('physical')

  // Perfil fiscal: los labels automáticos van antes que la factura y se guardan
  const profile = page.locator('.panel', { has: page.getByRole('heading', { name: 'Perfil fiscal' }) })
  await profile.scrollIntoViewIfNeeded()
  await profile.getByRole('button', { name: 'Abrir el panel' }).click()
  const names = await profile.locator('.f > label').allTextContents()
  expect(names.indexOf('Labels')).toBeGreaterThanOrEqual(0)
  expect(names.indexOf('Labels')).toBeLessThan(names.indexOf('Factura'))
  await profile.getByLabel(/Imprimir los labels automáticamente/).click()
  await profile.getByRole('button', { name: 'Guardar' }).click()
  await expect.poll(async () => (await detail()).autoPrintLabels, { timeout: 15_000 }).toBe(true)
  expect((await detail()).autoPrintInvoice).toBe(false)
  await shot(page, isMobile, 'fb5-perfil', profile)

  // recargada la ficha, todos los paneles vuelven a arrancar cerrados, cada uno con su resumen
  await page.reload()
  await expect(stack.getByRole('button', { name: 'Abrir el panel' }).first()).toBeVisible({ timeout: 20_000 })
  await expect(stack.getByRole('button', { name: 'Cerrar el panel' })).toHaveCount(0)
  await expect(stack.getByText('Estatus: Activo')).toBeVisible()
  await expect(stack.getByText(/Física: Calle Sol 5, Caguas.*Se recoge en: la dirección física/)).toBeVisible()
  await expect(stack.getByText(`Cliente FB5 ${suffix} LLC · 66-1234567`)).toBeVisible()
  const size = page.viewportSize()
  if (size && !isMobile) await page.setViewportSize({ width: size.width, height: 1500 })
  await stack.scrollIntoViewIfNeeded()
  await shot(page, isMobile, 'fb5-resumenes', stack)
  if (size && !isMobile) await page.setViewportSize(size)

  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)
  expect(overflow).toBe(false)
})
