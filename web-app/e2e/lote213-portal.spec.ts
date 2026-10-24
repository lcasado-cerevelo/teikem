// Recorrido del portal del cliente (Lote 213) en la app del cliente contra el API real: consignatario NUEVO en la misma pantalla de captura SIN RATÓN (rápida y detallada: Enter lo graba en el servidor, queda
// escogido y el foco sigue en el siguiente campo) y EXPORTAR lo filtrado a Excel, CSV y PDF (el PDF lleva el logo de la compañía o, si no subió, el de Teikem).
//
// Datos propios por API: un cliente con persona administradora (MFA y documento legal ya hechos) y tres consignatarios con una orden cada uno. Requiere el API con Portal:ReturnInviteTokenInResponse y
// Auth:TrustedBrowsers:ReturnLinkInResponse, y la app del cliente (CLIENT_APP_URL, por omisión http://localhost:8081). Solo escritorio. Capturas: docs/manual/frontend/img/l213-<paso>.png.
import { copyFileSync, mkdirSync, readFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { expect, test, type Download, type Page } from '@playwright/test'
import * as XLSX from 'xlsx'

import { API_URL, APP_URL, createClientWithAdmin, loginAsClientAdmin, shot } from './portalHelpers'
import { cssToRgb, pdfImageWidths, pdfText, pdfUsesFill } from './pdfHelpers'
import { createOrder } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(420_000)

const OUT = process.env.L213_OUT ?? join(tmpdir(), 'l213-out')
mkdirSync(OUT, { recursive: true })

async function download(page: Page, testId: string, name: string): Promise<{ file: Download; path: string; buf: Buffer }> {
  const [file] = await Promise.all([page.waitForEvent('download', { timeout: 60_000 }), page.getByTestId(testId).click()])
  const path = await file.path()
  const copy = join(OUT, name)
  copyFileSync(path, copy)
  return { file, path: copy, buf: readFileSync(copy) }
}

const activeTestId = (page: Page) => page.evaluate(() => document.activeElement?.getAttribute('data-testid') ?? '')

test('Portal del cliente: consignatario nuevo en línea sin ratón y exportar lo filtrado a Excel, CSV y PDF con logo', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil'), 'La entrada rápida y los pies de tabla con Exportar son de la web ancha.')
  const suffix = `D${String(Date.now()).slice(-6)}`
  const c = await createClientWithAdmin(request, suffix, 'Portal 213')
  // una base nueva no trae el servicio ni el paquete predeterminados de la compañía (las órdenes sin ellos dan 400)
  const settings = (await (await request.get(`${API_URL}/api/v1/tenant/settings`, { headers: c.staff })).json()) as { defaultServiceType?: string | null; defaultPackageType?: string | null }
  if (!settings.defaultServiceType || !settings.defaultPackageType) {
    const put = await request.put(`${API_URL}/api/v1/tenant/settings`, { headers: c.staff, data: { defaultServiceType: settings.defaultServiceType ?? 'STANDARD', defaultPackageType: settings.defaultPackageType ?? 'BOX' } })
    expect(put.ok(), await put.text()).toBeTruthy()
  }
  const existing = [`Farmacia Norte ${suffix}`, `Colmado Sur ${suffix}`, `Clínica Este ${suffix}`]
  for (const name of existing) {
    const loc = await c.locations.post(name)
    await createOrder(request, c.staff, c.client.publicId, loc.publicId, 2)
  }
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(`${APP_URL}/login`)
  await loginAsClientAdmin(page, c)

  // ------------------------------------------------------------------ 1) RÁPIDA: consignatario nuevo sin ratón
  const quickName = `Tienda Nueva ${suffix}`
  await page.goto(`${APP_URL}/order/new`)
  const consignee = page.getByTestId('quick-consignee')
  await expect(consignee).toBeVisible({ timeout: 60_000 })
  // las MISMAS columnas que la rápida del personal: «N.º de envío» y «N.º de factura» siempre están (apagadas con «(automático)» si la compañía las asigna)
  await expect(page.getByText('N.º de envío', { exact: true })).toBeVisible()
  await expect(page.getByText('N.º de factura', { exact: true })).toBeVisible()
  await expect(page.getByTestId('quick-orderNumber')).toHaveAttribute('readonly', '')
  await expect(page.getByTestId('quick-orderNumber')).toHaveAttribute('tabindex', '-1')   // Tab los salta
  await expect(page.getByTestId('quick-invoice')).toHaveAttribute('readonly', '')
  await expect(page.getByTestId('quick-invoice')).toHaveAttribute('placeholder', '(automático)')
  // la fila completa cabe en pantalla (con las dos columnas de números, «Guardar fila» no se corta) a 1280 y a 1440 px, sin desplazamiento horizontal de la página
  for (const width of [1280, 1440]) {
    await page.setViewportSize({ width, height: 900 })
    const box = await page.getByTestId('quick-save').boundingBox()
    expect(box, `«Guardar fila» visible a ${width} px`).toBeTruthy()
    expect(box!.x + box!.width, `«Guardar fila» no se corta a ${width} px`).toBeLessThanOrEqual(width - 8)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1), `sin scroll horizontal a ${width} px`).toBeTruthy()
  }
  await consignee.focus() // el foco se pone por código: de aquí en adelante, solo teclado
  await page.keyboard.type(quickName)
  await expect(page.getByTestId('quick-consignee-new')).toBeVisible()
  await page.keyboard.press('Enter') // sin coincidencias: Enter = «+ Nuevo consignatario»
  await expect(page.getByTestId('quick-new-block')).toBeVisible()
  await expect.poll(() => activeTestId(page)).toBe('quick-new-line1') // el foco cae solo en lo que falta: la dirección (el nombre ya viene)
  await page.keyboard.type('Calle Luna 77')
  await page.keyboard.press('Tab')
  expect(await activeTestId(page)).toBe('quick-new-city')
  await page.keyboard.type('Caguas')
  await page.keyboard.press('Tab') // estado
  await page.keyboard.type('PR')
  await page.keyboard.press('Tab') // código postal
  await page.keyboard.type('00725')
  await page.keyboard.press('Tab') // teléfono
  await page.keyboard.type('787-555-0100')
  await shot(page, false, 'l213-consignatario-en-linea-rapida')
  await page.keyboard.press('Enter') // graba el consignatario en el servidor al momento
  await expect(page.getByTestId('quick-new-block')).toHaveCount(0)
  await expect(consignee).toHaveValue(quickName) // queda escogido
  await expect.poll(() => activeTestId(page)).toBe('quick-packageType') // y el foco sigue en la misma fila
  // el consignatario YA existe en el servidor (antes de guardar el envío), con su teléfono
  const form1 = (await (await request.get(`${API_URL}/api/v1/portal/order-form`, { headers: c.clientApi })).json()) as { consignees: { name: string; line1: string; city: string; postalCode?: string; phone?: string; code?: string }[] }
  const created = form1.consignees.find((x) => x.name === quickName)
  expect(created, 'el consignatario nuevo debe quedar guardado al grabarlo, antes del envío').toBeTruthy()
  expect(created).toMatchObject({ line1: 'Calle Luna 77', city: 'Caguas', postalCode: '00725' })
  expect(created?.phone).toMatch(/787/)
  // desplegable del tipo de paquete: las flechas cambian la opción sin ratón (el contrato de prueba solo tiene tarifa para «Caja»: se va y se vuelve)
  const pkg = page.getByTestId('quick-packageType')
  const initialPackage = await pkg.inputValue()
  await page.keyboard.press('ArrowDown')
  await expect(pkg).not.toHaveValue(initialPackage)
  await page.keyboard.press('ArrowUp')
  await expect(pkg).toHaveValue(initialPackage)
  await page.keyboard.press('Tab')
  expect(await activeTestId(page)).toBe('quick-cod')
  await page.keyboard.type('25')
  await page.keyboard.press('Enter') // graba el envío
  await expect(page.getByText(/✓ Guardada/).first()).toBeVisible({ timeout: 30_000 })
  await expect(page.getByTestId('quick-saved-1')).toContainText(quickName)
  await expect.poll(() => activeTestId(page)).toBe('quick-date') // la fila nueva espera con el foco en la fecha
  // escribir otra vez el mismo nombre ya lo encuentra en la lista (no hace falta crearlo de nuevo)
  await consignee.focus()
  await page.keyboard.type(quickName.slice(0, 12))
  await expect(page.getByTestId('quick-consignee-suggest-0')).toContainText(quickName)
  await shot(page, false, 'l213-rapida-guardada')

  // ------------------------------------------------------------------ 2) DETALLADA: igual, ahí mismo
  const detailedName = `Ferretería Sol ${suffix}`
  await page.getByTestId('neworder-mode-detailed').click()
  const dcons = page.getByTestId('consignee')
  await expect(dcons).toBeVisible({ timeout: 30_000 })
  await dcons.focus()
  await page.keyboard.type(detailedName)
  await page.keyboard.press('Enter')
  await expect(page.getByTestId('new-consignee-block')).toBeVisible()
  await expect.poll(() => activeTestId(page)).toBe('new-line1')
  await page.keyboard.type('Av. Sol 1')
  await page.keyboard.press('Tab')
  await page.keyboard.type('Yauco')
  await shot(page, false, 'l213-consignatario-en-linea-detallada')
  await page.keyboard.press('Enter') // graba y sigue: el foco va al primer tipo de paquete
  await expect(page.getByTestId('new-consignee-block')).toHaveCount(0)
  await expect(dcons).toHaveValue(detailedName)
  await expect.poll(() => activeTestId(page)).toBe('pkg-type-0')
  await expect(page.getByText('Yauco').first()).toBeVisible() // sus datos de solo lectura
  await page.getByTestId('neworder-submit').click()
  await expect(page).toHaveURL(/\/order\/[0-9a-f-]{36}/, { timeout: 30_000 })
  await expect(page.getByText(detailedName).first()).toBeVisible({ timeout: 30_000 })
  const form2 = (await (await request.get(`${API_URL}/api/v1/portal/order-form`, { headers: c.clientApi })).json()) as { consignees: { name: string }[] }
  expect(form2.consignees.map((x) => x.name)).toEqual(expect.arrayContaining([quickName, detailedName]))
  expect(form2.consignees.filter((x) => x.name === quickName)).toHaveLength(1) // sin duplicados

  // ------------------------------------------------------------------ 3) EXPORTAR lo filtrado: Excel, CSV y PDF
  await page.goto(`${APP_URL}/orders`)
  await expect(page.getByTestId('orders-table')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByTestId('orders-table-count')).toHaveText('5 de 5', { timeout: 30_000 })
  await shot(page, false, 'l213-exportar-formatos')
  const me = (await (await request.get(`${API_URL}/api/v1/portal/me`, { headers: c.clientApi })).json()) as { currentClient?: { tenantName?: string } }
  const company = me.currentClient?.tenantName ?? ''
  expect(company).not.toBe('')

  const xlsx = await download(page, 'orders-table-export-xlsx', 'mis-envios.xlsx')
  expect(xlsx.file.suggestedFilename()).toMatch(/^teikem-mis-envios-\d{4}-\d{2}-\d{2}\.xlsx$/)
  const sheet = XLSX.read(xlsx.buf, { cellDates: true })
  const aoa = XLSX.utils.sheet_to_json<unknown[]>(sheet.Sheets[sheet.SheetNames[0]], { header: 1, blankrows: false })
  const flat = aoa.flat().map(String)
  expect(flat).toEqual(expect.arrayContaining([company, 'Mis envíos', 'Sin filtros', 'Conduce', 'Consignatario']))
  for (const n of [...existing, quickName, detailedName]) expect(flat.some((x) => x.includes(n)), `Excel debe traer ${n}`).toBeTruthy()
  expect(flat.some((x) => /^Generado el /.test(x))).toBeTruthy()

  const pdf = await download(page, 'orders-table-export-pdf', 'mis-envios.pdf')
  expect(pdf.file.suggestedFilename()).toMatch(/^teikem-mis-envios-\d{4}-\d{2}-\d{2}\.pdf$/)
  expect(pdf.buf.subarray(0, 5).toString()).toBe('%PDF-')
  const text = pdfText(pdf.buf)
  for (const n of [company, 'Mis env', 'Sin filtros', `Colmado Sur ${suffix}`, quickName, detailedName]) expect(text, `el PDF debe decir «${n}»`).toContain(n)
  const teikemWidths = pdfImageWidths(pdf.buf)
  expect(teikemWidths.length, 'el PDF lleva un logo (el de Teikem, porque la compañía no subió ninguno)').toBeGreaterThan(0)
  expect(teikemWidths).toContain(480) // el logo horizontal de Teikem, rasterizado a 480 px de ancho

  // lo FILTRADO: buscar un consignatario y exportar solo eso (CSV)
  await page.getByTestId('orders-search').fill(`Colmado Sur ${suffix}`)
  await expect(page.getByTestId('orders-table-count')).toHaveText('1 de 1', { timeout: 30_000 })
  const csv = await download(page, 'orders-table-export-csv', 'mis-envios-filtrado.csv')
  expect(csv.file.suggestedFilename()).toMatch(/\.csv$/)
  const lines = csv.buf.toString('utf8').replace(/^﻿/, '').trim().split(/\r?\n/)
  expect(lines).toHaveLength(2) // el encabezado y UNA fila: lo filtrado, no las cinco
  expect(lines[1]).toContain(`Colmado Sur ${suffix}`)
  const pdfFiltered = await download(page, 'orders-table-export-pdf', 'mis-envios-filtrado.pdf')
  const filteredText = pdfText(pdfFiltered.buf)
  expect(filteredText).toContain('Filtros: ')
  expect(filteredText).toContain(`Colmado Sur ${suffix}`)
  expect(filteredText).not.toContain(`Farmacia Norte ${suffix}`)

  // ------------------------------------------------------------------ 4) los colores del PDF son los de la compañía (Ajustes → Marca), no otros
  const setBrand = async (preset: string) => {
    const put = await request.put(`${API_URL}/api/v1/tenant/settings`, { headers: c.staff, data: { brandingJson: JSON.stringify({ preset, useCustom: false, custom: { flow: '#F37123', money: '#6C757D', neutral: '#03204B' } }) } })
    expect(put.ok(), await put.text()).toBeTruthy()
  }
  const barColor = () => page.getByTestId('top-bar').evaluate((el) => getComputedStyle(el).backgroundColor)
  await setBrand('vino')
  try {
    await page.reload()
    await expect(page.getByTestId('orders-table-count')).toHaveText('5 de 5', { timeout: 30_000 })
    const vinoBar = cssToRgb(await barColor())
    expect(vinoBar, 'la barra del portal ya no es el azul marino de siempre').not.toEqual([3, 32, 75])
    const vinoPdf = await download(page, 'orders-table-export-pdf', 'mis-envios-marca-vino.pdf')
    expect(pdfUsesFill(vinoPdf.buf, vinoBar), `el encabezado del PDF debe usar el color de la barra de la compañía (${vinoBar.join(',')})`).toBeTruthy()
    expect(pdfUsesFill(vinoPdf.buf, [38, 50, 72]), 'ya no se usa el gris azulado fijo de antes').toBeFalsy()
    expect(pdfUsesFill(vinoPdf.buf, [11, 44, 102]), 'ni el azul marino fijo de Teikem').toBeFalsy()
  } finally {
    await setBrand('advance')
  }
  await page.reload()
  await expect(page.getByTestId('orders-table-count')).toHaveText('5 de 5', { timeout: 30_000 })

  // ------------------------------------------------------------------ 5) con logo de la compañía: el PDF lo lleva (no el de Teikem)
  const png = readFileSync(join(import.meta.dirname, '..', '..', 'Logos', 'teikem-4-vertical-notagline.png'))
  const up = await request.put(`${API_URL}/api/v1/tenant/brand/logos/lockup`, { headers: c.staff, multipart: { file: { name: 'logo.png', mimeType: 'image/png', buffer: png } } })
  expect(up.ok(), await up.text()).toBeTruthy()
  try {
    await page.reload()
    await expect(page.getByTestId('brand-logo')).toBeVisible({ timeout: 30_000 })
    await page.getByTestId('orders-search').fill('')
    await expect(page.getByTestId('orders-table-count')).toHaveText('5 de 5', { timeout: 30_000 })
    const branded = await download(page, 'orders-table-export-pdf', 'mis-envios-con-logo.pdf')
    const widths = pdfImageWidths(branded.buf)
    expect(widths.length).toBeGreaterThan(0)
    expect(widths, 'con logo propio el PDF ya no lleva el de Teikem').not.toContain(480)
  } finally {
    await request.delete(`${API_URL}/api/v1/tenant/brand/logos/lockup`, { headers: c.staff })
  }
})
