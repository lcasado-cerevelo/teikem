// Portal del cliente de almacén: el botón «Descargar inventario» junto al buscador de Existencias baja el reporte (CSV y Excel) con TODAS las filas del filtro vigente, no solo las de la pantalla.
// Datos propios por API: un cliente con administradora, un almacén con posición, un producto y 10 unidades que deja el personal. Requiere la app del cliente (CLIENT_APP_URL, por omisión http://localhost:8081).
import { readFileSync } from 'node:fs'

import { expect, test } from '@playwright/test'

import { API_URL, APP_URL, createClientWithAdmin, loginAsClientAdmin, post } from './portalHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

test('Existencias: «Descargar inventario» baja el reporte con el producto y su cantidad', async ({ page, request, isMobile }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil') && !process.env.PORTAL_MOBILE, 'Recorrido largo: escritorio (el móvil con PORTAL_MOBILE=1).')
  const appUp = await request.get(`${APP_URL}/login`, { timeout: 5_000 }).then((r) => r.ok(), () => false)
  test.skip(!appUp, `La app del cliente no responde en ${APP_URL}`)
  const letters = String(Date.now()).slice(-6).replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])
  const sku = `RI${letters}`
  const c = await createClientWithAdmin(request, `R${letters}`, 'Reporte inventario')
  const wh = await post<{ warehouse: { publicId: string } }>(request, '/api/v1/warehouses', c.staff, { code: `R${letters}`.slice(0, 20), name: `Almacén R${letters}` })
  const W = wh.warehouse.publicId
  const zone = await post<{ id: number }>(request, `/api/v1/warehouses/${W}/zones`, c.staff, { code: 'PCK', name: 'Picking', zoneType: 'PICKING' })
  const bin = await post<{ id: number }>(request, `/api/v1/warehouses/${W}/bins`, c.staff, { zoneId: zone.id, code: 'P-01' })
  const patch = await request.patch(`${API_URL}/api/v1/clients/${c.client.publicId}/profile`, { headers: c.staff, data: { portalCanCreateProducts: true } })
  expect(patch.ok(), await patch.text()).toBeTruthy()
  const product = await post<{ publicId: string }>(request, '/api/v1/portal/warehouse/products', c.clientApi, { sku, name: `Producto reporte ${letters}` })
  await post(request, '/api/v1/inventory/adjustments', c.staff, { productPublicId: product.publicId, warehousePublicId: W, binId: bin.id, quantity: 10, reason: 'FOUND', notes: 'e2e reporte' })

  await page.setViewportSize(isMobile ? { width: 390, height: 844 } : { width: 1440, height: 900 })
  await page.goto(`${APP_URL}/login`)
  await loginAsClientAdmin(page, c)
  await page.goto(`${APP_URL}/warehouse`)

  // el botón está a la vista, junto al buscador de Existencias (la sección que abre por omisión)
  const report = page.getByTestId('inventory-report')
  await expect(report).toBeVisible({ timeout: 30_000 })
  await expect(report).toContainText('Descargar inventario')
  await expect(page.getByText(sku).first()).toBeVisible({ timeout: 30_000 })

  const [csv] = await Promise.all([page.waitForEvent('download'), page.getByTestId('inventory-report-csv').click()])
  expect(csv.suggestedFilename()).toMatch(/\.csv$/i)
  const text = readFileSync((await csv.path())!, 'utf8')
  expect(text).toContain(sku)
  expect(text).toContain(`Producto reporte ${letters}`)
  expect(text).toMatch(/10/)

  const [xlsx] = await Promise.all([page.waitForEvent('download'), page.getByTestId('inventory-report-xlsx').click()])
  expect(xlsx.suggestedFilename()).toMatch(/\.xlsx$/i)
  await page.screenshot({ path: testInfo.outputPath('existencias-con-reporte.png') })
})
