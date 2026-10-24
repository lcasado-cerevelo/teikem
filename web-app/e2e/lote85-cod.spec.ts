// Recorrido de «COD» (Lote 85) contra el API real (db-init hecho; módulo COD encendido para la compañía de prueba; API en API_URL).
//
// 1) Por API: un cliente, su consignatario y una orden con COD de 100; la orden pasa a Entregada con SQL (confirmarla exige una tarifa de contrato que este recorrido no arma), así que hace falta
//    E2E_SQL_DB (nombre de la base del API; sqlcmd con la sesión de Windows) y el recorrido se salta sin él.
// 2) En la pantalla: se registran dos cobros (60 en efectivo y 40 con cheque) y el COD queda Cobrado.
// 3) Reconciliar con hojas escaneadas DE VERDAD: se genera un código de barras Code 128 con el número de la factura (bwip-js), un PDF de 2 páginas (la 2.ª sin código) y una imagen PNG; el navegador
//    lee los códigos (pdf.js + zxing-wasm), la vista previa dice «Se cuadra» y «Repetido», se cuadra y el COD queda Cuadrado; la corrida queda en Corridas.
//
// Escritorio y móvil (Pixel 7 a 360 px, sin scroll horizontal de página). Capturas: docs/manual/frontend/img/l85-<paso>.png (escritorio).
import { execFileSync } from 'node:child_process'
import { mkdirSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import bwipjs from 'bwip-js'
import { jsPDF } from 'jspdf'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import { manualImgDir, manualLang, manualTarget } from './manualLang'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const SQL_DB = process.env.E2E_SQL_DB
const PASSWORD = process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!'
const ADMIN = { email: process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com', password: PASSWORD }
const IMG_DIR = manualImgDir(fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url)))
const LETTERS = String(Date.now())
  .slice(-6)
  .replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

test.use({ locale: 'es-PR' })
test.setTimeout(300_000)

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

/** Una hoja «escaneada»: el código de barras Code 128 con el número de la factura. */
async function barcodePng(text: string): Promise<Buffer> {
  return bwipjs.toBuffer({ bcid: 'code128', text, scale: 3, height: 14, includetext: true, textxalign: 'center', paddingwidth: 12, paddingheight: 12, backgroundcolor: 'FFFFFF' })
}

test('COD: cobrar y reconciliar con hojas escaneadas (PDF e imagen) leídas en el navegador', async ({ page, request, isMobile }, testInfo) => {
  test.skip(!SQL_DB, 'Falta E2E_SQL_DB (la base del API) para dejar la orden Entregada')
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)
  const headers = { Authorization: `Bearer ${await apiToken(request)}` }
  const clientName = `Cobra ${suffix}`

  // 1) cliente, consignatario y una orden con COD de 100 (entregada)
  const client = await request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: clientName } })
  expect(client.ok(), await client.text()).toBeTruthy()
  const clientId = ((await client.json()) as { publicId: string }).publicId
  const loc = await request.post(`${API_URL}/api/v1/locations`, {
    headers,
    data: { clientPublicId: clientId, name: `Sucursal ${suffix}`, locationType: 'DELIVERY', line1: 'Calle 5 #12', city: 'Ponce', postalCode: '00716', country: 'PR' },
  })
  expect(loc.ok(), await loc.text()).toBeTruthy()
  const order = await request.post(`${API_URL}/api/v1/orders`, {
    headers,
    data: { clientPublicId: clientId, consigneeLocationPublicId: ((await loc.json()) as { publicId: string }).publicId, packages: [{ pieces: 1 }], codAmount: 100 },
  })
  expect(order.ok(), await order.text()).toBeTruthy()
  const orderDto = (await order.json()) as { publicId: string; orderNumber: string; clientInvoiceNumber: string }
  execFileSync('sqlcmd', [
    '-S', process.env.E2E_SQL_SERVER ?? 'localhost,1433', '-E', '-C', '-I', '-d', SQL_DB!, '-Q',
    `UPDATE o SET StatusCodeId = (SELECT StatusCodeId FROM dbo.StatusCode WHERE Entity='OrderStatus' AND InternalCode='DELIVERED') FROM dbo.TransportOrder o WHERE o.PublicId = '${orderDto.publicId}'`,
  ])
  const invoice = orderDto.clientInvoiceNumber

  // las hojas: un PDF de 2 páginas (la 2.ª sin código) y una imagen con el mismo código
  const png = await barcodePng(invoice)
  const pdfDoc = new jsPDF({ unit: 'pt', format: 'letter' })
  pdfDoc.setFontSize(18)
  pdfDoc.text(`Factura ${invoice}`, 60, 80)
  pdfDoc.addImage(`data:image/png;base64,${png.toString('base64')}`, 'PNG', 60, 110, 300, 90)
  pdfDoc.addPage()
  pdfDoc.setFontSize(14)
  pdfDoc.text('Página sin código de barras', 60, 80)
  const pdfPath = testInfo.outputPath('hojas.pdf')
  const pngPath = testInfo.outputPath('hoja.png')
  writeFileSync(pdfPath, Buffer.from(pdfDoc.output('arraybuffer')))
  writeFileSync(pngPath, png)

  if (!isMobile) await page.setViewportSize({ width: 1280, height: 900 })
  await login(page)
  await page.goto('/money/cod')
  await expect(page.getByRole('heading', { level: 1, name: 'COD' })).toBeVisible({ timeout: 20_000 })

  // 2) el cobro en dos partes
  await page.getByPlaceholder(/Buscar/).first().fill(clientName)
  const row = () => page.locator('tr, .dt-cards > li').filter({ hasText: clientName }).first()
  await expect(row()).toContainText('Por cobrar', { timeout: 20_000 })
  await row().click()
  const ficha = page.getByRole('dialog', { name: new RegExp(`COD de ${orderDto.orderNumber}`) })
  await ficha.getByRole('button', { name: 'Registrar cobro' }).click()
  const cobro = page.getByRole('dialog', { name: new RegExp(`Cobro de ${orderDto.orderNumber}`) })
  await cobro.getByLabel('Monto').fill('150')
  await cobro.getByRole('button', { name: 'Registrar' }).click()
  await expect(cobro.getByRole('alert')).toContainText('pasa de lo que falta por cobrar')
  await cobro.getByLabel('Monto').fill('60')
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l85-cobro', cobro)
  await cobro.getByRole('button', { name: 'Registrar' }).click()
  await expect(ficha).toContainText('Parcial', { timeout: 20_000 })
  await ficha.getByRole('button', { name: 'Registrar cobro' }).click()
  await cobro.getByLabel('Método').selectOption('CHECK')
  await cobro.getByLabel('Monto').fill('40')
  await cobro.getByLabel('Número del cheque').fill('5521')
  await cobro.getByRole('button', { name: 'Registrar' }).click()
  await expect(ficha).toContainText('Cobrado', { timeout: 20_000 })
  await expect(ficha).toContainText('5521')
  await shot(page, isMobile, 'l85-ficha', ficha)
  await ficha.getByRole('button', { name: 'Cerrar' }).first().click()

  // 3) reconciliar con las hojas escaneadas: el navegador lee los códigos de barras
  await page.getByRole('tab', { name: 'Reconciliar' }).click()
  await page.locator('#cod-client').fill(clientName)
  await page.getByRole('option', { name: new RegExp(clientName) }).click()
  await page.getByLabel('Archivos de hojas escaneadas').setInputFiles([pdfPath, pngPath])
  await expect(page.getByText(/1 código\(s\) en 2 página\(s\)/)).toBeVisible({ timeout: 120_000 })
  await expect(page.getByText(/sin código en las páginas 2/)).toBeVisible()
  await expect(page.getByText('Códigos por procesar (2)')).toBeVisible({ timeout: 60_000 })
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l85-archivos')
  await page.getByRole('button', { name: 'Vista previa' }).click()
  await expect(page.getByText('Esto es solo una simulación: todavía no se cuadró nada.')).toBeVisible({ timeout: 20_000 })
  await expect(page.getByText('Se cuadra').first()).toBeVisible()
  await expect(page.getByText('Repetido').first()).toBeVisible()
  expect(await noHScroll(page)).toBe(true)
  await shot(page, isMobile, 'l85-vista-previa')
  await page.getByRole('button', { name: 'Cuadrar' }).click()
  const confirm = page.getByRole('dialog', { name: 'Procesar las hojas' })
  await confirm.getByRole('button', { name: 'Cuadrar' }).click()
  await expect(page.getByText('Corrida hecha').first()).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l85-corrida')

  // el COD de la orden quedó Cuadrado y la corrida está en Corridas
  await page.getByRole('tab', { name: 'Corridas' }).click()
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: 'hojas.pdf' }).first()).toBeVisible({ timeout: 20_000 })
  await page.getByRole('tab', { name: 'Envíos con COD' }).click()
  await page.getByPlaceholder(/Buscar/).first().fill(clientName)
  await expect(row()).toContainText('Cuadrado', { timeout: 20_000 })
  expect(await noHScroll(page)).toBe(true)
})
