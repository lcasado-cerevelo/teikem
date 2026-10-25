// Vista por aparato del portal del cliente (Lote 213, 2026-10-24, regla de Luis): la web en una COMPUTADORA ve la forma ancha (barra superior, tablas, entrada rápida con teclado); una TABLETA o un celular
// (aunque sea en el navegador y mida 1194 px) ve la forma móvil (pestañas, tarjetas, «Nuevo envío» solo detallado). Y toda tabla exporta a Excel, CSV y PDF en todas las vistas (Informes incluidos).
// Contra el API real y el portal compilado (CLIENT_APP_URL). Una sola compañía de prueba; cada aparato entra con un código de recuperación distinto.
import { expect, test, type Browser, type Download, type Page } from '@playwright/test'

import { APP_URL, createClientWithAdmin, loginConfirmingTheComputer, shot, type ClientWithAdmin } from './portalHelpers'
import { pdfText } from './pdfHelpers'
import { MANUAL_LANG, manualServerEnglish } from './manualLang'

const EN = MANUAL_LANG === 'en'
import { createOrder } from './staffHelpers'
import { readFileSync } from 'node:fs'

test.use({ locale: 'es-PR' })
test.setTimeout(420_000)

type Device = { name: string; viewport: { width: number; height: number }; mobile: boolean }
const COMPUTER: Device = { name: 'computadora', viewport: { width: 1440, height: 900 }, mobile: false }
const TABLET: Device = { name: 'tableta', viewport: { width: 1194, height: 834 }, mobile: true } // iPad Pro 11 apaisada: más ancha que 900 px
const PHONE: Device = { name: 'celular', viewport: { width: 412, height: 915 }, mobile: true }

/** Un aparato nuevo entra como la persona del cliente: confirma la computadora y usa SU código de recuperación (cada código vale una sola vez). */
async function open(browser: Browser, device: Device, c: ClientWithAdmin, code: number): Promise<Page> {
  const context = await browser.newContext({
    locale: 'es-PR',
    viewport: device.viewport,
    isMobile: device.mobile,
    hasTouch: device.mobile,
    acceptDownloads: true,
  })
  const page = await context.newPage()
  await loginConfirmingTheComputer(page, c.adminMail, c.password)
  await expect(page.getByRole('heading', { name: 'Código de seguridad' })).toBeVisible({ timeout: 30_000 })
  await page.getByLabel('Código').fill(c.recovery[code])
  await page.getByRole('button', { name: 'Verificar' }).click()
  await expect(page.getByRole('heading', { name: 'Hola, Ana' })).toBeVisible({ timeout: 30_000 })
  return page
}

async function download(page: Page, testId: string): Promise<{ file: Download; buf: Buffer }> {
  const [file] = await Promise.all([page.waitForEvent('download', { timeout: 60_000 }), page.getByTestId(testId).click()])
  return { file, buf: readFileSync(await file.path()) }
}

test('Portal: computadora = vista ancha; tableta y celular = vista móvil; los tres formatos en cada una', async ({ browser, request }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil'), 'este recorrido arma sus propios aparatos')
  const suffix = `V${String(Date.now()).slice(-6)}`
  const c = await createClientWithAdmin(request, suffix, (EN ? 'Portal 213 devices' : 'Portal 213 aparatos'))
  const loc = await c.locations.post(`${EN ? 'Devices Store' : 'Tienda Aparatos'} ${suffix}`)
  await createOrder(request, c.staff, c.client.publicId, loc.publicId, 1) // con envíos la tabla ofrece «Exportar»

  // ---------------------------------------------------------------- computadora
  const pc = await open(browser, COMPUTER, c, 0)
  await expect(pc.getByTestId('top-bar')).toBeVisible()
  await manualServerEnglish(pc, '**/api/v1/portal/order-form*')
  await pc.goto(`${APP_URL}/order/new`)
  await expect(pc.getByTestId('neworder-mode-quick')).toBeVisible({ timeout: 60_000 })
  await expect(pc.getByTestId('quick-consignee')).toBeVisible()
  await shot(pc, false, 'l213-computadora-nuevo-envio')
  await pc.close()

  // ---------------------------------------------------------------- tableta (táctil, 1194 px)
  const tablet = await open(browser, TABLET, c, 1)
  expect(await tablet.evaluate(() => window.matchMedia('(pointer: coarse)').matches), 'la tableta emulada tiene el dedo como puntero principal').toBe(true)
  expect(await tablet.evaluate(() => window.innerWidth)).toBeGreaterThanOrEqual(1100)
  await expect(tablet.getByTestId('top-bar')).toHaveCount(0) // sin barra de escritorio: pestañas de abajo
  await manualServerEnglish(tablet, '**/api/v1/portal/order-form*')
  await tablet.goto(`${APP_URL}/order/new`)
  await expect(tablet.getByTestId('neworder-mode-quick')).toHaveCount(0, { timeout: 60_000 }) // sin selector Rápida/Detallada
  await expect(tablet.getByTestId('quick-consignee')).toHaveCount(0) // la rápida no existe en tabletas
  await shot(tablet, false, 'l213-tableta-nuevo-envio')
  await tablet.goto(`${APP_URL}/orders`)
  for (const f of ['xlsx', 'csv', 'pdf']) await expect(tablet.getByTestId(`orders-export-${f}`)).toBeVisible({ timeout: 60_000 })
  const pdf = await download(tablet, 'orders-export-pdf')
  expect(pdf.file.suggestedFilename()).toMatch(/\.pdf$/)
  expect(pdfText(pdf.buf)).toContain('Mis envíos')
  const xlsx = await download(tablet, 'orders-export-xlsx')
  expect(xlsx.file.suggestedFilename()).toMatch(/\.xlsx$/)
  expect([xlsx.buf[0], xlsx.buf[1]]).toEqual([0x50, 0x4b])
  const csv = await download(tablet, 'orders-export-csv')
  expect(csv.file.suggestedFilename()).toMatch(/\.csv$/)
  await tablet.close()

  // ---------------------------------------------------------------- celular
  const phone = await open(browser, PHONE, c, 2)
  await expect(phone.getByTestId('top-bar')).toHaveCount(0)
  await manualServerEnglish(phone, '**/api/v1/portal/order-form*')
  await phone.goto(`${APP_URL}/order/new`)
  await expect(phone.getByTestId('neworder-mode-quick')).toHaveCount(0, { timeout: 60_000 })
  await shot(phone, false, 'l213-celular-nuevo-envio')
  await phone.goto(`${APP_URL}/orders`)
  for (const f of ['xlsx', 'csv', 'pdf']) await expect(phone.getByTestId(`orders-export-${f}`)).toBeVisible({ timeout: 60_000 })
  await phone.close()
})

test('Portal: los Informes también exportan a Excel, CSV y PDF', async ({ browser, request }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil'), 'este recorrido arma sus propios aparatos')
  const suffix = `R${String(Date.now()).slice(-6)}`
  const c = await createClientWithAdmin(request, suffix, (EN ? 'Portal 213 reports' : 'Portal 213 informes'))
  const loc = await c.locations.post(`${EN ? 'Reports Store' : 'Tienda Informes'} ${suffix}`)
  await createOrder(request, c.staff, c.client.publicId, loc.publicId, 1)
  await createOrder(request, c.staff, c.client.publicId, loc.publicId, 2)
  const page = await open(browser, COMPUTER, c, 0)
  await page.goto(`${APP_URL}/reports`)
  // el informe de envíos, con su rango del mes (el cliente tiene dos envíos de hoy)
  await expect(page.getByTestId('report-shipments')).toBeVisible({ timeout: 60_000 })
  await page.getByTestId('report-shipments').click()
  await page.getByTestId('report-run').click()
  await expect(page.getByTestId('report-count')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByTestId('report-count')).not.toHaveText(/^0 /)
  for (const f of ['xlsx', 'csv', 'pdf']) await expect(page.getByTestId(`report-export-${f}`)).toBeVisible()
  const pdf = await download(page, 'report-export-pdf')
  expect(pdf.file.suggestedFilename()).toMatch(/\.pdf$/)
  await page.close()
})
