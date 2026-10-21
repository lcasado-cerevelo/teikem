// Lote 182-183 (C4) contra el API real: Recepción maestra en la web. «Recibir juntos» dos avisos de llegada del mismo cliente (los de otro dueño se bloquean), la ficha con sus recibos, abrir uno
// de ellos y «Confirmar todo». Capturas (solo escritorio): docs/manual/frontend/img/l183-<paso>.png.
import { expect, test } from '@playwright/test'

import { API_URL, apiHeaders, createClient, LETTERS, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

test('Recepción maestra: recibir juntos dos avisos del mismo cliente y confirmarlos juntos', async ({ page, request, isMobile }) => {
  const headers = await apiHeaders(request)
  const sfx = `M${LETTERS}`
  const wh = await ok<{ warehouse: { publicId: string } }>(request.post(`${API_URL}/api/v1/warehouses`, { headers, data: { code: `W${sfx}`.slice(0, 20), name: `Recibo ${sfx}` } }), 'almacén')
  const W = wh.warehouse.publicId
  const zone = await ok<{ id: number }>(request.post(`${API_URL}/api/v1/warehouses/${W}/zones`, { headers, data: { code: 'STG', name: 'Recepción', zoneType: 'STAGING' } }), 'zona')
  await ok(request.post(`${API_URL}/api/v1/warehouses/${W}/bins`, { headers, data: { zoneId: zone.id, code: 'S-01' } }), 'posición')
  const clientA = await createClient(request, headers, `Recibo A ${sfx}`)
  const clientB = await createClient(request, headers, `Recibo B ${sfx}`)
  const product = async (sku: string, owner: string) =>
    (await ok<{ product: { publicId: string } }>(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Producto ${sku}`, ownerClientPublicId: owner } }), 'producto')).product.publicId
  const pa = await product(`RA${sfx}`, clientA.publicId)
  const pb = await product(`RB${sfx}`, clientB.publicId)
  const asn = async (client: string, p: string, qty: number) =>
    await ok<{ id: number; number: string }>(request.post(`${API_URL}/api/v1/asns`, { headers, data: { warehousePublicId: W, clientPublicId: client, reference: `e2e-${sfx}`, lines: [{ productPublicId: p, expectedQty: qty }] } }), 'aviso')
  const a1 = await asn(clientA.publicId, pa, 4)
  const a2 = await asn(clientA.publicId, pa, 6)
  const a3 = await asn(clientB.publicId, pb, 2)

  await login(page)
  await page.goto('/warehouse/receipts?tab=groups')
  await page.getByRole('button', { name: 'Recibir juntos' }).click()
  const dialog = page.getByRole('dialog', { name: 'Recibir juntos' })
  await expect(dialog.getByText(a1.number)).toBeVisible()
  await dialog.getByRole('checkbox', { name: new RegExp(a1.number) }).check()
  await expect(dialog.getByRole('checkbox', { name: new RegExp(a3.number) })).toBeDisabled()   // otro dueño
  await dialog.getByRole('checkbox', { name: new RegExp(a2.number) }).check()
  await dialog.getByLabel('Transporte (opcional)').fill(`Transportes ${sfx}`)
  await shot(page, isMobile, 'l183-recibir-juntos', 800)
  await dialog.getByRole('button', { name: 'Recibir juntos (2)' }).click()

  // se abre la ficha: dos recibos, uno por aviso
  const ficha = page.getByRole('dialog', { name: /RCM-/ })
  await expect(ficha.getByText('Recibos (2)')).toBeVisible()
  await expect(ficha.getByText(a1.number)).toBeVisible()
  await expect(ficha.getByText(a2.number)).toBeVisible()
  await shot(page, isMobile, 'l183-maestra-ficha', 800)

  // abrir uno de los recibos lleva a la pantalla de siempre
  await ficha.getByRole('button', { name: 'Abrir recibo' }).first().click()
  await expect(page).toHaveURL(/receipt=/)
  await page.goBack()
  await page.goto('/warehouse/receipts?tab=groups')
  await page.getByText(`Recibo A ${sfx}`).first().click()
  const ficha2 = page.getByRole('dialog', { name: /RCM-/ })
  await ficha2.getByRole('button', { name: 'Confirmar todo' }).click()
  await page.getByRole('dialog', { name: '¿Confirmar todos los recibos?' }).getByRole('button', { name: 'Confirmar todo' }).click()
  await expect(ficha2.getByText('2 de 2 recibos confirmados')).toBeVisible()
  await expect(ficha2.getByRole('button', { name: 'Confirmar todo' })).toHaveCount(0)
  await shot(page, isMobile, 'l183-maestra-confirmada', 800)
})
