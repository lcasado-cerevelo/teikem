// Recorrido (trabajo autónomo, 2026-10-21) contra el API real:
// 1) Encabezado de tarjetas y paneles = el tercer color de la marca, clarísimo (claro y oscuro): distinto del fondo del panel pero apenas.
// 2) Recolección maestra en la web (Lotes 176-177): «Recolectar juntos» dos avisos del mismo cliente, ficha con los movimientos de dos posiciones y Empacar/Eliminar disponibles.
// Capturas (solo escritorio): docs/manual/frontend/img/l180-<paso>.png.
import { expect, test, type Page } from '@playwright/test'

import { ADMIN, API_URL, apiHeaders, createClient, LETTERS, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

/** Canales 0-255 de 'rgb(r, g, b)' o de 'color(srgb r g b)' (así devuelve el navegador un color-mix: canales de 0 a 1). */
function channels(css: string): number[] {
  const nums = css.match(/\d+(\.\d+)?/g)?.map(Number) ?? [0, 0, 0]
  return css.startsWith('color(') ? nums.slice(0, 3).map((v) => v * 255) : nums.slice(0, 3)
}

async function headerColors(page: Page): Promise<{ head: string; panel: string }> {
  return page.locator('.panel .ph2').first().evaluate((el) => ({ head: getComputedStyle(el).backgroundColor, panel: getComputedStyle(el.parentElement as Element).backgroundColor }))
}

test('Encabezado de los paneles: el tercer color de la marca, clarísimo, en tema claro y oscuro', async ({ page, isMobile }) => {
  await login(page)
  for (const scheme of ['light', 'dark'] as const) {
    await page.emulateMedia({ colorScheme: scheme })
    await page.evaluate((s) => document.documentElement.setAttribute('data-theme', s), scheme)
    await page.goto('/warehouse/pick-batches?view=groups')
    await page.evaluate((s) => document.documentElement.setAttribute('data-theme', s), scheme)
    await expect(page.locator('.panel .ph2').first()).toBeVisible()
    const { head, panel } = await headerColors(page)
    expect(head, `${scheme}: el encabezado tiene fondo propio`).not.toBe('rgba(0, 0, 0, 0)')
    expect(head, `${scheme}: distinto del fondo del panel`).not.toBe(panel)
    // «bien clarito»: apenas se aparta del fondo del panel (a lo sumo 24 de 255 en cada canal)
    const [h, p] = [channels(head), channels(panel)]
    expect(Math.max(...h.map((v, i) => Math.abs(v - p[i]))), `${scheme}: apenas más gris que el panel`).toBeLessThanOrEqual(24)
    if (scheme === 'light') await shot(page, isMobile, 'l180-encabezado-claro', 700)
    else await shot(page, isMobile, 'l180-encabezado-oscuro', 700)
  }
})

test('Recolección maestra en la web: recolectar juntos dos avisos, ver los movimientos de dos posiciones', async ({ page, request, isMobile }) => {
  const headers = await apiHeaders(request)
  const sfx = `E${LETTERS}`
  const wh = await ok<{ warehouse: { publicId: string } }>(request.post(`${API_URL}/api/v1/warehouses`, { headers, data: { code: `W${sfx}`.slice(0, 20), name: `Maestra ${sfx}` } }), 'almacén')
  const W = wh.warehouse.publicId
  const zone = await ok<{ id: number }>(request.post(`${API_URL}/api/v1/warehouses/${W}/zones`, { headers, data: { code: 'PCK', name: 'Picking', zoneType: 'PICKING' } }), 'zona')
  const bin = async (code: string) => (await ok<{ id: number }>(request.post(`${API_URL}/api/v1/warehouses/${W}/bins`, { headers, data: { zoneId: zone.id, code } }), 'posición')).id
  const B1 = await bin('P-01')
  const B2 = await bin('P-02')
  const client = await createClient(request, headers, `Maestra web ${sfx}`)
  const product = await ok<{ product: { publicId: string } }>(
    request.post(`${API_URL}/api/v1/products`, { headers, data: { sku: `MW${sfx}`, name: `Producto ${sfx}`, ownerClientPublicId: client.publicId } }),
    'producto',
  )
  const P = product.product.publicId
  for (const [bin, qty] of [[B1, 6], [B2, 4]] as const)
    await ok(request.post(`${API_URL}/api/v1/inventory/adjustments`, { headers, data: { productPublicId: P, warehousePublicId: W, binId: bin, quantity: qty, reason: 'FOUND', notes: 'e2e' } }), 'existencia')
  const notice = async (qty: number) =>
    (await ok<{ number: string }>(request.post(`${API_URL}/api/v1/exit-notices`, { headers, data: { warehousePublicId: W, clientPublicId: client.publicId, reference: 'e2e', destination: 'Muelle', lines: [{ productPublicId: P, quantity: qty }] } }), 'aviso')).number
  const N1 = await notice(4)
  const N2 = await notice(5)

  await login(page)
  await page.goto('/warehouse/pick-batches')
  await page.getByRole('button', { name: 'Recolectar juntos' }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByText(N1)).toBeVisible()
  await dialog.locator('label', { hasText: N1 }).locator('input[type=checkbox]').check()
  await dialog.locator('label', { hasText: N2 }).locator('input[type=checkbox]').check()
  await shot(page, isMobile, 'l180-recolectar-juntos', 800)
  await dialog.getByRole('button', { name: /Recolectar juntos \(2\)/ }).click()

  // se abre la ficha de la maestra: 2 documentos y los movimientos de las dos posiciones
  const ficha = page.getByRole('dialog')
  await expect(ficha.getByText('Documentos (2)')).toBeVisible()
  await expect(ficha.getByText('P-01').first()).toBeVisible()
  await expect(ficha.getByText('P-02').first()).toBeVisible()
  await expect(ficha.getByRole('button', { name: 'Empacar' })).toBeVisible()
  await expect(ficha.getByRole('button', { name: 'Eliminar' })).toBeVisible()
  await shot(page, isMobile, 'l180-maestra-ficha', 900)

  // la deshace desde la ficha: el inventario vuelve
  await ficha.getByRole('button', { name: 'Eliminar' }).click()
  await page.getByRole('alertdialog').or(page.getByRole('dialog').last()).getByRole('button', { name: 'Eliminar' }).last().click()
  await expect(page.locator('.toast.on', { hasText: /eliminada/i }).first()).toBeVisible({ timeout: 15_000 })
  const bal = await ok<{ items: { qtyOnHand: number }[] }>(request.get(`${API_URL}/api/v1/inventory/balances?warehousePublicIds=${W}&productPublicIds=${P}&includeZero=true`, { headers }), 'saldos')
  expect(bal.items.reduce((n, b) => n + b.qtyOnHand, 0)).toBe(10)
  expect(ADMIN.email).toContain('@')
})
