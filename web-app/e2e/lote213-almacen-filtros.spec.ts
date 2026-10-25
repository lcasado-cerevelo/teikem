// Almacén del portal del cliente (Lote 213, 2026-10-24: ampliar la búsqueda): movimientos, llegadas y salidas tienen un filtro de fecha; las llegadas, además, un buscador; y las existencias se buscan también por
// lote y por almacén. Contra el API real: una compañía con el módulo de almacén, un cliente al que se le permite crear productos y avisos de llegada, un producto y un aviso de llegada esperado dentro de 3 días.
import { expect, test } from '@playwright/test'

import { API_URL, APP_URL, createClientWithAdmin, loginAsClientAdmin, shot } from './portalHelpers'
import { ok, tenantDay } from './staffHelpers'
import { MANUAL_LANG } from './manualLang'

const EN = MANUAL_LANG === 'en'

test.use({ locale: 'es-PR' })
test.setTimeout(420_000)

test('Portal Almacén: filtro de fecha en movimientos, llegadas y salidas, y buscador de llegadas', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil'), 'la tabla de Almacén es de la web ancha')
  const suffix = `A${String(Date.now()).slice(-6)}`
  const c = await createClientWithAdmin(request, suffix, (EN ? 'Portal 213 warehouse' : 'Portal 213 almacén'))
  // la compañía le permite al cliente crear productos y avisos de llegada y de salida
  const client = await ok<{ rowVersion?: string }>(request.get(`${API_URL}/api/v1/clients/${c.client.publicId}`, { headers: c.staff }), 'cliente')
  await ok(request.patch(`${API_URL}/api/v1/clients/${c.client.publicId}/profile`, { headers: c.staff, data: { rowVersion: client.rowVersion, clearDefaultPickup: false, portalCanCreateProducts: true, portalCanCreateArrivals: true, portalCanCreateExits: true } }), 'permisos del portal')

  const warehouses = await ok<{ publicId: string }[]>(request.get(`${API_URL}/api/v1/portal/warehouse/warehouses`, { headers: c.clientApi }), 'almacenes')
  expect(warehouses.length, 'la compañía de prueba necesita un almacén').toBeGreaterThan(0)
  const product = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/portal/warehouse/products`, { headers: c.clientApi, data: { sku: `E2E-${suffix}`, name: `${EN ? 'Product' : 'Producto'} ${suffix}` } }), 'producto')
  await ok(request.post(`${API_URL}/api/v1/portal/warehouse/arrivals`, { headers: c.clientApi, data: { warehousePublicId: warehouses[0].publicId, reference: `OC-${suffix}`, expectedDate: tenantDay(3), lines: [{ productPublicId: product.publicId, expectedQty: 5 }] } }), 'aviso de llegada')

  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(`${APP_URL}/login`)
  await loginAsClientAdmin(page, c)
  await page.goto(`${APP_URL}/warehouse`)

  // existencias: el buscador dice que también busca por lote y almacén
  await expect(page.getByPlaceholder('SKU, nombre, código de barras, lote o almacén')).toBeVisible({ timeout: 60_000 })

  // llegadas: el aviso sale; «Hoy» (no es de hoy: se espera en 3 días) lo esconde; «Todas las fechas» lo devuelve; el buscador también filtra
  await page.getByText('Llegadas', { exact: true }).click()
  await expect(page.getByText(`OC-${suffix}`).first()).toBeVisible({ timeout: 30_000 })
  await shot(page, false, 'l213-almacen-llegadas-filtros')
  await page.getByTestId('arrivals-range-preset').selectOption('today')
  await expect(page.getByText(`OC-${suffix}`)).toHaveCount(0)
  await page.getByTestId('arrivals-range-preset').selectOption('all')
  await expect(page.getByText(`OC-${suffix}`).first()).toBeVisible()
  await page.getByTestId('arrivals-search').fill('no-existe-zzz')
  await expect(page.getByText(`OC-${suffix}`)).toHaveCount(0, { timeout: 15_000 })
  await page.getByTestId('arrivals-search').fill(`OC-${suffix}`)
  await expect(page.getByText(`OC-${suffix}`).first()).toBeVisible({ timeout: 15_000 })
  // un rango personalizado que incluye el día esperado lo deja ver
  await page.getByTestId('arrivals-range-preset').selectOption('custom')
  await page.getByTestId('arrivals-range-from').fill(tenantDay(2))
  await page.getByTestId('arrivals-range-to').fill(tenantDay(4))
  await expect(page.getByText(`OC-${suffix}`).first()).toBeVisible()
  await page.getByTestId('arrivals-range-to').fill(tenantDay(2))
  await expect(page.getByText(`OC-${suffix}`)).toHaveCount(0)

  // movimientos y salidas: traen su filtro de fecha
  await page.getByText('Movimientos', { exact: true }).click()
  await expect(page.getByTestId('movements-range-preset')).toBeVisible()
  await page.getByTestId('movements-range-preset').selectOption('last30')
  await expect(page.getByTestId('warehouse-search')).toBeVisible()
  await shot(page, false, 'l213-almacen-movimientos-filtros')
  await page.getByText('Salidas', { exact: true }).click()
  await expect(page.getByTestId('exits-range-preset')).toBeVisible()
  await page.getByTestId('exits-range-preset').selectOption('thisMonth')
  await expect(page.getByPlaceholder('Número, referencia, destino o producto')).toBeVisible()
})
