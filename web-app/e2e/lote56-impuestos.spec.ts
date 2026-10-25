// Recorrido de «Impuestos» (Lote 56) contra el API real (db-init hecho; el módulo TAX encendido para la compañía de prueba con SQL, porque encenderlo
// pide verificación de dos pasos; API en API_URL, por defecto http://localhost:5000; compañía demo, administrador con tax.manage).
//
// 1) Tasas: crea el IVU (estatal 10.5 % + municipal 1 %), un traslape se rechaza con el mensaje del servidor, se cierra la vigencia el 14 de octubre y
//    se crea la nueva (12.5 %) desde el 15.
// 2) Reglas: tasa por omisión y flete gravable.
// 3) Productos: un producto pasa a Exento y vuelve a Gravable.
// 4) Exenciones: un cliente con certificado vigente; se da de baja.
// 5) Simulador: el 14 usa la tasa vieja (11.5) y el 15 la nueva (12.5); con la exención vigente el impuesto sale en cero.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l56-<paso>.png (escritorio).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang, manualTarget } from './manualLang'
import type { components } from '../src/kernel/api/schema'
import { enableModule } from './staffHelpers'

type AuthResultDto = components['schemas']['AuthResultDto']
type ClientDetailDto = components['schemas']['ClientDetailDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const PASSWORD = process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: PASSWORD }
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
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
  const manualShot1 = await manualTarget(target)
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  if (manualShot1) await manualShot1.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
  else await page.screenshot({ path: `${IMG_DIR}${name}.png`, animations: 'disabled', caret: 'hide' })
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

test('Impuestos: tasas con vigencia, reglas, categoría de producto, exenciones y simulador', async ({ page, request, isMobile }) => {
  await enableModule(request, 'TAX')   // este recorrido necesita el módulo de impuestos (los de facturación lo suponen apagado: corre después de ellos)
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const code = `IVU${suffix}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }

  // datos de apoyo por API: un cliente y un producto
  const client = await request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente impuestos ${suffix}` } })
  expect(client.ok(), await client.text()).toBeTruthy()
  const clientName = ((await client.json()) as ClientDetailDto).name ?? ''
  const sku = `TAX-${suffix}`
  const product = await request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Producto impuestos ${suffix}`, trackingType: 'NONE' } })
  expect(product.ok(), await product.text()).toBeTruthy()

  if (!isMobile) await page.setViewportSize({ width: 1280, height: 900 })   // alto suficiente para que las capturas de ventanas salgan completas
  await login(page)
  await page.goto('/money/tax')
  await expect(page.getByRole('heading', { level: 1, name: 'Impuestos' })).toBeVisible({ timeout: 20_000 })

  // 1) tasas
  await page.getByRole('button', { name: 'Nueva tasa' }).first().click()
  let dialog = page.getByRole('dialog')
  await dialog.getByLabel('Código', { exact: true }).fill(code)
  await dialog.getByLabel('Nombre', { exact: true }).fill('IVU Puerto Rico')
  await dialog.getByLabel('Rige desde').fill('2026-01-01')
  await dialog.getByLabel('Rige desde').press('Escape')   // cierra el calendario que se abre al escribir la fecha
  await dialog.getByLabel('Código del componente 1').fill('STATE')
  await dialog.getByLabel('Nombre del componente 1').fill('Estatal')
  await dialog.getByLabel('Porcentaje del componente 1').fill('10,5')
  await dialog.getByRole('button', { name: 'Agregar componente' }).click()
  await dialog.getByLabel('Código del componente 2').fill('CITY')
  await dialog.getByLabel('Nombre del componente 2').fill('Municipal')
  await dialog.getByLabel('Porcentaje del componente 2').fill('1')
  await expect(dialog.getByRole('status')).toHaveText('Total: 11.5 %')
  await shot(page, isMobile, 'l56-nueva-tasa', dialog)
  await dialog.getByRole('button', { name: 'Crear tasa' }).click()
  await expect(page.locator('.toast.on').first()).toContainText(`Tasa ${code} creada.`)
  const row = (text: string) => page.locator('tr, .dt-cards > li').filter({ has: page.getByText(text, { exact: true }) }).first()
  await expect(row(code)).toContainText('11.5 %')
  await expect(row(code)).toContainText('Estatal 10.5 % + Municipal 1 %')
  await expect(row(code)).toContainText('Vigente')

  // un traslape se rechaza con el mensaje del servidor
  await page.getByRole('button', { name: 'Nueva tasa' }).first().click()
  dialog = page.getByRole('dialog')
  await dialog.getByLabel('Código', { exact: true }).fill(code)
  await dialog.getByLabel('Nombre', { exact: true }).fill('Otra')
  await dialog.getByLabel('Rige desde').fill('2026-06-01')
  await dialog.getByLabel('Rige desde').press('Escape')   // cierra el calendario que se abre al escribir la fecha
  await dialog.getByLabel('Código del componente 1').fill('STATE')
  await dialog.getByLabel('Nombre del componente 1').fill('Estatal')
  await dialog.getByLabel('Porcentaje del componente 1').fill('12')
  await dialog.getByRole('button', { name: 'Crear tasa' }).click()
  await expect(dialog.getByRole('alert')).toContainText('Ya hay una tasa con ese código que cubre esas fechas.')
  await dialog.getByRole('button', { name: 'Cancelar' }).click()

  // el cambio de tasa: se cierra la vieja el 14 y se crea la nueva desde el 15
  await row(code).getByRole('button', { name: 'Cerrar vigencia' }).click()
  dialog = page.getByRole('dialog')
  await dialog.getByLabel('Último día de vigencia').fill('2026-10-14')
  await dialog.getByLabel('Último día de vigencia').press('Escape')   // cierra el calendario que se abre al escribir la fecha
  await dialog.getByRole('button', { name: 'Cerrar vigencia' }).click()
  await expect(page.locator('.toast.on').filter({ hasText: 'cerrada' })).toHaveCount(1)
  await expect(row(code)).toContainText('Cerrada')
  await row(code).getByRole('button', { name: 'Nueva tasa desde esta' }).click()
  dialog = page.getByRole('dialog')
  await expect(dialog.getByLabel('Código', { exact: true })).toHaveValue(code)
  await expect(dialog.getByLabel('Rige desde')).toHaveValue('10/15/2026')
  await dialog.getByLabel('Porcentaje del componente 2').fill('2')   // 10.5 + 2 = 12.5
  await dialog.getByRole('button', { name: 'Crear tasa' }).click()
  await expect(page.locator('.toast.on').first()).toContainText(`Tasa ${code} creada.`)
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l56-tasas')

  // 2) reglas
  await page.getByRole('tab', { name: 'Reglas' }).click()
  await page.getByLabel('Tasa por omisión').selectOption(code)
  await page.getByRole('switch', { name: 'Flete paga impuesto' }).check({ force: true })
  await shot(page, isMobile, 'l56-reglas')
  await page.getByRole('button', { name: 'Guardar reglas' }).click()
  await expect(page.locator('.toast.on').first()).toContainText('Reglas guardadas.')
  expect(await noHScroll(page)).toBe(true)

  // 3) productos
  await page.getByRole('tab', { name: 'Productos' }).click()
  await page.getByRole('searchbox').fill(sku)
  const category = page.getByLabel(`Categoría de impuesto de ${sku}`)
  await expect(category).toHaveValue('TAXABLE')
  await category.selectOption('EXEMPT')
  await expect(page.locator('.toast.on').first()).toContainText(`${sku}: Exento.`)
  await shot(page, isMobile, 'l56-productos')
  await category.selectOption('TAXABLE')
  await expect(category).toHaveValue('TAXABLE')
  expect(await noHScroll(page)).toBe(true)

  // 4) exenciones
  await page.getByRole('tab', { name: 'Exenciones' }).click()
  await page.getByRole('button', { name: 'Nueva exención' }).first().click()
  dialog = page.getByRole('dialog')
  await dialog.getByRole('combobox').first().fill(clientName.slice(0, 12))
  await page.getByRole('option').filter({ hasText: clientName }).first().click()
  await dialog.getByLabel('Tipo de exención').fill('Sin fines de lucro')
  await dialog.getByLabel('Número de certificado').fill(`CERT-${suffix}`)
  await dialog.getByLabel('Vale desde').fill('2026-01-01')
  await dialog.getByLabel('Vale desde').press('Escape')   // cierra el calendario que se abre al escribir la fecha
  await shot(page, isMobile, 'l56-nueva-exencion', dialog)
  await dialog.getByRole('button', { name: 'Guardar exención' }).click()
  await expect(page.locator('.toast.on').first()).toContainText('Exención creada.')
  await expect(row(clientName)).toContainText('Vigente')
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l56-exenciones')

  // 5) simulador: la tasa cambia a mitad de mes y la exención vigente deja el impuesto en cero
  await page.getByRole('tab', { name: 'Simulador' }).click()
  await page.getByLabel('Fecha (opcional)').fill('2026-10-14')
  await page.getByLabel('Fecha (opcional)').press('Escape')   // cierra el calendario que se abre al escribir la fecha
  await page.getByRole('button', { name: 'Calcular' }).click()
  await expect(page.getByText(/Tasa .* al 11.5 %/)).toBeVisible()
  await expect(page.locator('.tax-table tfoot')).toContainText('$11.50')
  await page.getByLabel('Fecha (opcional)').fill('2026-10-15')
  await page.getByLabel('Fecha (opcional)').press('Escape')   // cierra el calendario que se abre al escribir la fecha
  await page.getByRole('button', { name: 'Calcular' }).click()
  await expect(page.getByText(/Tasa .* al 12.5 %/)).toBeVisible()
  await expect(page.locator('.tax-table tfoot')).toContainText('$12.50')
  await shot(page, isMobile, 'l56-simulador')
  await page.getByRole('combobox', { name: 'Cliente (opcional)' }).fill(clientName.slice(0, 12))
  await page.getByRole('option').filter({ hasText: clientName }).first().click()
  await page.getByRole('button', { name: 'Calcular' }).click()
  await expect(page.locator('.tax-table')).toContainText('Cliente exento')
  await expect(page.locator('.tax-table')).toContainText(`CERT-${suffix}`)
  await expect(page.locator('.tax-table tfoot')).toContainText('$0.00')
  expect(await noHScroll(page)).toBe(true)

  // baja de la exención: otra vez paga
  await page.getByRole('tab', { name: 'Exenciones' }).click()
  await row(clientName).getByRole('button', { name: 'Dar de baja' }).click()
  await expect(row(clientName)).toContainText('De baja')
})
