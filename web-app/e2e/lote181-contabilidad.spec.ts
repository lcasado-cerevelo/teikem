// Lote 181 (H1) contra el API real: Contabilización de compras y de despachos. Un recibo ciego de un producto PROPIO (con costo) y una recolección dejan líneas por contabilizar; la corrida se genera viendo
// antes lo que entraría, se acepta y se exporta con la plantilla (descarga). Lo de un cliente no entra. Capturas (solo escritorio): docs/manual/frontend/img/l181-<paso>.png.
import { expect, test } from '@playwright/test'

import { API_URL, apiHeaders, createClient, LETTERS, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

test('Contabilización de compras y de despachos: generar, aceptar y exportar lo propio', async ({ page, request, isMobile }) => {
  const headers = await apiHeaders(request)
  const sfx = `K${LETTERS}`
  const wh = await ok<{ warehouse: { publicId: string } }>(request.post(`${API_URL}/api/v1/warehouses`, { headers, data: { code: `W${sfx}`.slice(0, 20), name: `Acct ${sfx}` } }), 'almacén')
  const W = wh.warehouse.publicId
  const zone = async (code: string, zoneType: string) => (await ok<{ id: number }>(request.post(`${API_URL}/api/v1/warehouses/${W}/zones`, { headers, data: { code, name: code, zoneType } }), 'zona')).id
  const bin = async (zoneId: number, code: string) => (await ok<{ id: number }>(request.post(`${API_URL}/api/v1/warehouses/${W}/bins`, { headers, data: { zoneId, code } }), 'posición')).id
  const staging = await bin(await zone('STG', 'STAGING'), 'S-01')
  const picking = await bin(await zone('PCK', 'PICKING'), 'P-01')
  const client = await createClient(request, headers, `Acct ${sfx}`)
  const product = async (sku: string, extra: object) =>
    (await ok<{ product: { publicId: string } }>(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Product ${sku}`, purchaseCost: 5, ...extra } }), 'producto')).product.publicId
  const own = await product(`KO${sfx}`, {})
  const theirs = await product(`KC${sfx}`, { ownerClientPublicId: client.publicId })
  for (const [p, qty] of [[own, 10], [theirs, 4]] as const)
    await ok(request.post(`${API_URL}/api/v1/receipts`, { headers, data: { warehousePublicId: W, type: 'BLIND', stagingBinId: staging, confirm: true, lines: [{ productPublicId: p, receivedQty: qty }] } }), 'recibo')
  for (const p of [own, theirs])
    await ok(request.post(`${API_URL}/api/v1/inventory/adjustments`, { headers, data: { productPublicId: p, warehousePublicId: W, binId: picking, quantity: 8, reason: 'FOUND', notes: 'e2e' } }), 'existencia')
  for (const [p, qty] of [[own, 3], [theirs, 2]] as const)
    await ok(request.post(`${API_URL}/api/v1/pick-batches`, { headers, data: { warehousePublicId: W, lines: [{ productPublicId: p, quantity: qty }] } }), 'recolección')

  await login(page)

  // ---- compras
  await page.goto('/money/purchases')
  await expect(page.getByRole('heading', { level: 1, name: 'Contabilización de compras' })).toBeVisible()
  await page.getByRole('button', { name: 'Generar corrida' }).click()
  const nueva = page.getByRole('dialog', { name: 'Generar corrida de compras' })
  await expect(nueva.getByText(`KO${sfx}`)).toBeVisible()
  await expect(nueva.getByText(`KC${sfx}`)).toHaveCount(0)   // lo de un cliente no se contabiliza
  await shot(page, isMobile, 'l181-compras-nueva', 800)
  await nueva.getByRole('button', { name: 'Generar' }).click()

  const ficha = page.getByRole('dialog', { name: /Corrida de compras CMP-/ })
  await expect(ficha.getByText(`KO${sfx}`)).toBeVisible()
  await expect(ficha.getByRole('button', { name: 'Exportar' })).toHaveCount(0)
  await shot(page, isMobile, 'l181-compras-ficha', 800)
  await ficha.getByRole('button', { name: 'Aceptar' }).click()
  await ficha.getByRole('button', { name: 'Exportar' }).click()
  const exportar = page.getByRole('dialog', { name: /Exportar la corrida CMP-/ })
  await exportar.getByRole('button', { name: 'Ver archivo' }).click()
  await expect(exportar.locator('pre.exp-pre')).toContainText(`KO${sfx}`)
  await shot(page, isMobile, 'l181-compras-exportar', 900)
  const download = page.waitForEvent('download')
  await exportar.getByRole('button', { name: 'Descargar y marcar exportada' }).click()
  expect((await download).suggestedFilename()).toMatch(/^contabilizacion-compras-CMP-\d+\.csv$/)
  await expect(ficha.getByText('Exportada').first()).toBeVisible()

  // ---- despachos
  await page.goto('/money/dispatch')
  await expect(page.getByRole('heading', { level: 1, name: 'Contabilización de despachos' })).toBeVisible()
  await page.getByRole('button', { name: 'Generar corrida' }).click()
  const dnueva = page.getByRole('dialog', { name: 'Generar corrida de despachos' })
  await expect(dnueva.getByText(`KO${sfx}`)).toBeVisible()
  await expect(dnueva.getByText(`KC${sfx}`)).toHaveCount(0)
  await dnueva.getByRole('button', { name: 'Generar' }).click()
  const dficha = page.getByRole('dialog', { name: /Corrida de despachos DSP-/ })
  await expect(dficha.getByText(`KO${sfx}`)).toBeVisible()
  await shot(page, isMobile, 'l181-despachos-ficha', 800)
})
