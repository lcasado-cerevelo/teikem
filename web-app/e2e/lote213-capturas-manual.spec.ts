// Capturas del manual del Lote 213 (portal del cliente) que documenta docs/manual/frontend/l213-portal-cliente.md, generadas contra el API real (antes las hacía el simulador de app-cliente):
// inicio con «Hoy» y pie, Mis envíos (filtro de fecha, Exportar), Nuevo envío (rápida con teclado, detallada web y celular), Mi almacén con Exportar, Mensajes, marcar varios consignatarios y los PDF
// (logo de Teikem, logo de la compañía, filtrado y con la marca «Vino», en el portal y en la web del personal). Con MANUAL_LANG=en la interfaz y el PDF salen en inglés (el idioma se cambia ANTES de pedir el PDF).
// Las capturas de PDF se hacen bajando el PDF y dibujando su primera página con pdftoppm (poppler).
import { execFileSync } from 'node:child_process'
import { copyFileSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { expect, test, type Page } from '@playwright/test'

import { MANUAL_LANG, manualLang, manualServerEnglish } from './manualLang'

const EN = MANUAL_LANG === 'en'
import { API_URL, APP_URL, IMG_DIR, confirmBrowserByApi, createClientWithAdmin, createConsigneeAccess, loginAsClientAdmin, loginConfirmingTheComputer, post, shot, type ClientWithAdmin } from './portalHelpers'
import { cssToRgb, pdfUsesFill } from './pdfHelpers'
import { ADMIN, createOrder, ok, tenantDay } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(900_000)

const TMP = mkdtempSync(join(tmpdir(), 'l213-cap-'))

/** Baja el PDF que da el botón y dibuja su primera página en IMG_DIR/<name>.png. */
async function pdfShot(page: Page, trigger: () => Promise<void>, name: string): Promise<Buffer> {
  const [file] = await Promise.all([page.waitForEvent('download', { timeout: 60_000 }), trigger()])
  const buf = readFileSync(await file.path())
  expect(buf.subarray(0, 5).toString()).toBe('%PDF-')
  const pdf = join(TMP, `${name}.pdf`)
  writeFileSync(pdf, buf)
  const base = join(TMP, name)
  const bin = process.env.PDFTOPPM ?? 'pdftoppm'
  execFileSync(bin, ['-png', '-r', '88', '-f', '1', '-l', '1', '-singlefile', pdf, base])
  mkdirSync(IMG_DIR, { recursive: true })
  copyFileSync(`${base}.png`, `${IMG_DIR}${name}.png`)
  return buf
}

/** Un aparato nuevo (contexto propio) entra como la persona del cliente con su propio código de recuperación. */
async function openDevice(browser: import('@playwright/test').Browser, c: ClientWithAdmin, code: number, viewport: { width: number; height: number }, mobile: boolean): Promise<Page> {
  const context = await browser.newContext({ locale: 'es-PR', viewport, isMobile: mobile, hasTouch: mobile, acceptDownloads: true })
  const page = await context.newPage()
  await loginConfirmingTheComputer(page, c.adminMail, c.password)
  await expect(page.getByRole('heading', { name: 'Código de seguridad' })).toBeVisible({ timeout: 30_000 })
  await page.getByLabel('Código').fill(c.recovery[code])
  await page.getByRole('button', { name: 'Verificar' }).click()
  await expect(page.getByRole('heading', { name: 'Hola, Ana' })).toBeVisible({ timeout: 30_000 })
  return page
}

const activeTestId = (page: Page) => page.evaluate(() => document.activeElement?.getAttribute('data-testid') ?? '')

test('Capturas del manual del Lote 213 (portal del cliente)', async ({ page, request, browser }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil'), 'las capturas son de la web ancha y de un celular emulado propio')
  const suffix = `Q${String(Date.now()).slice(-6)}`
  const c = await createClientWithAdmin(request, suffix, (EN ? 'Portal 213 captures' : 'Portal 213 capturas'))
  void ADMIN
  void confirmBrowserByApi
  void loginConfirmingTheComputer
  const settings = (await (await request.get(`${API_URL}/api/v1/tenant/settings`, { headers: c.staff })).json()) as { defaultServiceType?: string | null; defaultPackageType?: string | null }
  if (!settings.defaultServiceType || !settings.defaultPackageType) {
    const put = await request.put(`${API_URL}/api/v1/tenant/settings`, { headers: c.staff, data: { defaultServiceType: settings.defaultServiceType ?? 'STANDARD', defaultPackageType: settings.defaultPackageType ?? 'BOX' } })
    expect(put.ok(), await put.text()).toBeTruthy()
  }
  // permisos del portal para el almacén
  const client = await ok<{ rowVersion?: string }>(request.get(`${API_URL}/api/v1/clients/${c.client.publicId}`, { headers: c.staff }), 'cliente')
  await ok(request.patch(`${API_URL}/api/v1/clients/${c.client.publicId}/profile`, { headers: c.staff, data: { rowVersion: client.rowVersion, clearDefaultPickup: false, portalCanCreateProducts: true, portalCanCreateArrivals: true, portalCanCreateExits: true } }), 'permisos del portal')

  // cuatro consignatarios con correo, cada uno con envíos
  const names = EN
    ? [`Pharmacy North ${suffix}`, `Grocery South ${suffix}`, `Clinic East ${suffix}`, `Hardware West ${suffix}`]
    : [`Farmacia Norte ${suffix}`, `Colmado Sur ${suffix}`, `Clínica Este ${suffix}`, `Ferretería Oeste ${suffix}`]
  const locs: { publicId: string; id: number }[] = []
  for (const [i, name] of names.entries()) {
    const loc = await c.locations.post(name)
    locs.push(loc)
    await post(request, `/api/v1/contacts/LOCATION/${loc.id}`, c.staff, { contactType: 'EMAIL', value: `contacto${i}.${suffix.toLowerCase()}@ejemplo.test`, isPrimary: true })
    await createOrder(request, c.staff, c.client.publicId, loc.publicId, i + 1)
    await createOrder(request, c.staff, c.client.publicId, loc.publicId, 1)
  }
  // un mensaje de un consignatario (con acceso propio) para que la bandeja de Mensajes tenga conversación
  const access = await createConsigneeAccess(request, c, locs[0].publicId, `m${suffix}`)
  await post(request, '/api/v1/portal/consignee/messages', access.api, { body: EN ? 'What time does my package arrive today?' : '¿A qué hora llega hoy mi paquete?' })
  // productos del almacén
  const warehouses = await ok<{ publicId: string }[]>(request.get(`${API_URL}/api/v1/portal/warehouse/warehouses`, { headers: c.clientApi }), 'almacenes')
  expect(warehouses.length).toBeGreaterThan(0)
  for (const n of [1, 2, 3]) await post(request, '/api/v1/portal/warehouse/products', c.clientApi, { sku: `SKU-${suffix}-${n}`, name: `${EN ? 'Sample product' : 'Producto de ejemplo'} ${n}` })

  await page.setViewportSize({ width: 1440, height: 900 })
  await manualServerEnglish(page, '**/api/v1/portal/**')   // etiquetas del servidor (estatus, servicio, paquete) en inglés
  await page.goto(`${APP_URL}/login`)
  await loginAsClientAdmin(page, c)

  // ---------------------------------------------------------------- Inicio con «Hoy» y el pie
  await page.getByTestId('indicators-days-0').click()
  await expect(page.getByTestId('home-total')).toBeVisible({ timeout: 30_000 })
  await shot(page, false, 'l213-inicio-hoy-pie')

  // ---------------------------------------------------------------- Mis envíos: Exportar, filtro de fecha
  await page.goto(`${APP_URL}/orders`)
  await expect(page.getByTestId('orders-table-count')).toHaveText('8 de 8', { timeout: 30_000 })
  await shot(page, false, 'l213-envios-exportar')
  await page.getByTestId('orders-range-preset').selectOption('custom')
  await page.getByTestId('orders-range-from').fill(tenantDay(-5))
  await page.getByTestId('orders-range-from').focus()
  await expect(page.getByTestId('orders-table-count')).toHaveText('8 de 8', { timeout: 30_000 })
  await shot(page, false, 'l213-envios-filtro-fecha')

  // ---------------------------------------------------------------- Nuevo envío: Rápida y Rápida con teclado
  await page.goto(`${APP_URL}/order/new`)
  await expect(page.getByTestId('quick-consignee')).toBeVisible({ timeout: 60_000 })
  await shot(page, false, 'l213-rapida')
  await page.getByTestId('quick-consignee').focus()
  await page.keyboard.type(EN ? 'phar' : 'farm')
  await expect(page.getByTestId('quick-consignee-suggest-0')).toContainText(names[0])
  await page.keyboard.press('Enter') // escoge el consignatario; el foco pasa al paquete
  await expect.poll(() => activeTestId(page)).toBe('quick-packageType')
  await page.keyboard.press('Tab')
  expect(await activeTestId(page)).toBe('quick-cod')
  await page.keyboard.type('40')
  await page.keyboard.press('Enter') // guarda la fila
  await expect(page.getByText(/✓ Guardad/).first()).toBeVisible({ timeout: 30_000 })
  await expect.poll(() => activeTestId(page)).toBe('quick-date')
  await page.getByTestId('quick-consignee').focus()
  await page.keyboard.type(EN ? 'groc' : 'colm')
  await expect(page.getByTestId('quick-consignee-suggest-0')).toContainText(names[1])
  await shot(page, false, 'l213-rapida-teclado')

  // ---------------------------------------------------------------- Detallada (web)
  await page.getByTestId('neworder-mode-detailed').click()
  const dcons = page.getByTestId('consignee')
  await expect(dcons).toBeVisible({ timeout: 30_000 })
  await dcons.focus()
  await page.keyboard.type('cl')
  await expect(page.getByTestId('consignee-suggest-0')).toContainText(names[2])
  await page.getByTestId('consignee-suggest-0').click()
  await expect(dcons).toHaveValue(names[2])
  await shot(page, false, 'l213-detallada')

  // ---------------------------------------------------------------- Mi almacén: Exportar (Productos)
  await page.goto(`${APP_URL}/warehouse`)
  await page.getByText('Productos', { exact: true }).click()
  await expect(page.getByTestId('products-table')).toBeVisible({ timeout: 60_000 })
  await expect(page.getByText(`SKU-${suffix}-1`).first()).toBeVisible({ timeout: 30_000 })
  await shot(page, false, 'l213-almacen-exportar')

  // ---------------------------------------------------------------- Mensajes
  await page.goto(`${APP_URL}/messages`)
  await expect(page.getByTestId('messages-desk')).toBeVisible({ timeout: 60_000 })
  await page.getByText(names[0], { exact: false }).first().click()
  await expect(page.getByTestId('conv-title')).toContainText(names[0], { timeout: 30_000 })
  await shot(page, false, 'l213-mensajes')

  // ---------------------------------------------------------------- Mis consignatarios: marcar varios
  await page.goto(`${APP_URL}/consignee-notices`)
  await expect(page.getByText(names[0]).first()).toBeVisible({ timeout: 30_000 })
  await page.getByTestId('notices-select-visible').click()
  await page.getByTestId('notices-bulk-email-on').click()
  await expect(page.getByTestId('notices-warning-continue')).toBeVisible({ timeout: 15_000 })
  await shot(page, false, 'l213-consignatarios-varios')
  await page.getByTestId('notices-warning-cancel').click()

  // ---------------------------------------------------------------- PDF del portal (con la interfaz y el PDF en inglés si MANUAL_LANG=en)
  await page.goto(`${APP_URL}/orders`)
  await expect(page.getByTestId('orders-table-count')).toHaveText(/^9 (de|of) 9$/ , { timeout: 30_000 })
  await manualLang(page, 'en', 'pdf')
  await pdfShot(page, () => page.getByTestId('orders-table-export-pdf').click(), 'l213-pdf-con-logo-teikem')
  // filtrado: solo las filas del buscador
  await page.getByTestId('orders-search').fill(names[1])
  await expect(page.getByTestId('orders-table-count')).toHaveText(/^2 (de|of) 2$/ , { timeout: 30_000 })
  await pdfShot(page, () => page.getByTestId('orders-table-export-pdf').click(), 'l213-pdf-filtrado')
  await page.getByTestId('orders-search').fill('')
  await expect(page.getByTestId('orders-table-count')).toHaveText(/^9 (de|of) 9$/ , { timeout: 30_000 })

  // con la marca «Vino»: portal
  const setBrand = async (preset: string) => {
    const put = await request.put(`${API_URL}/api/v1/tenant/settings`, { headers: c.staff, data: { brandingJson: JSON.stringify({ preset, useCustom: false, custom: { flow: '#F37123', money: '#6C757D', neutral: '#03204B' } }) } })
    expect(put.ok(), await put.text()).toBeTruthy()
  }
  await setBrand('vino')
  try {
    await page.reload()
    await expect(page.getByTestId('orders-table-count')).toHaveText(/^9 (de|of) 9$/ , { timeout: 30_000 })
    await manualLang(page, 'en', 'pdf')
    const vino = await pdfShot(page, () => page.getByTestId('orders-table-export-pdf').click(), 'l213-pdf-portal-marca-compania')
    const bar = cssToRgb(await page.getByTestId('top-bar').evaluate((el) => getComputedStyle(el).backgroundColor))
    expect(pdfUsesFill(vino, bar)).toBeTruthy()
    await manualLang(page, 'es')

    // con la marca «Vino»: web del personal (el administrador de la compañía)
    const staff = await page.context().newPage()
    await staff.setViewportSize({ width: 1440, height: 900 })
    const WEB = process.env.WEB_URL ?? 'http://localhost:3100'
    await manualServerEnglish(staff, '**/api/v1/catalogs/**')
    await staff.goto(`${WEB}/login`)
    await staff.getByLabel('Correo electrónico').fill(process.env.TEIKEM_ADMIN_EMAIL ?? 'teikem+admin@cerevelo.com')
    await staff.getByLabel('Contraseña', { exact: true }).fill(process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!')
    await staff.getByRole('button', { name: 'Entrar' }).click()
    await staff.waitForURL((u) => u.pathname !== '/login')
    if (new URL(staff.url()).pathname === '/select-tenant') {
      const def = staff.locator('.tenant-list button', { hasText: 'Predeterminada' })
      await ((await def.count()) > 0 ? def.first() : staff.locator('.tenant-list button').first()).click()
    }
    await staff.waitForURL((u) => u.pathname === '/')
    await staff.goto(`${WEB}/orders`)
    await expect(staff.getByText(names[0]).first()).toBeVisible({ timeout: 60_000 })
    await manualLang(staff, 'en', 'pdf')
    await staff.getByRole('button', { name: /Exportar|Export/ }).last().click()
    await pdfShot(staff, () => staff.getByRole('menuitem', { name: /PDF/ }).click(), 'l213-pdf-personal-marca-compania')
    await manualLang(staff, 'es')
    await staff.close()
  } finally {
    await setBrand('advance')
  }

  // con logo de la compañía: el PDF lo lleva (no el de Teikem)
  const png = readFileSync(join(import.meta.dirname, '..', '..', 'Logos', 'teikem-4-vertical-notagline.png'))
  const up = await request.put(`${API_URL}/api/v1/tenant/brand/logos/lockup`, { headers: c.staff, multipart: { file: { name: 'logo.png', mimeType: 'image/png', buffer: png } } })
  expect(up.ok(), await up.text()).toBeTruthy()
  try {
    await page.reload()
    await expect(page.getByTestId('brand-logo')).toBeVisible({ timeout: 30_000 })
    await expect(page.getByTestId('orders-table-count')).toHaveText(/^9 (de|of) 9$/ , { timeout: 30_000 })
    await manualLang(page, 'en', 'pdf')
    await pdfShot(page, () => page.getByTestId('orders-table-export-pdf').click(), 'l213-pdf-con-logo-compania')
    await manualLang(page, 'es')
  } finally {
    await request.delete(`${API_URL}/api/v1/tenant/brand/logos/lockup`, { headers: c.staff })
  }

  // ---------------------------------------------------------------- Celular: Nuevo envío detallado
  const phone = await openDevice(browser, c, 1, { width: 412, height: 915 }, true)
  await manualServerEnglish(phone, '**/api/v1/portal/**')
  await phone.goto(`${APP_URL}/order/new`)
  await expect(phone.getByTestId('consignee')).toBeVisible({ timeout: 60_000 })
  await phone.getByTestId('consignee').focus()
  await phone.keyboard.type('cl')
  await expect(phone.getByTestId('consignee-suggest-0')).toContainText(names[2])
  await phone.getByTestId('consignee-suggest-0').click()
  await shot(phone, false, 'l213-detallada-celular')
  await phone.close()
  void MANUAL_LANG
})
