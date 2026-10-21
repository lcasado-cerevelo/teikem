// Recorrido de Reclamaciones (Lote 111) en la web del personal contra el API real.
//
// Datos propios por API: un cliente con contrato y tarifa, una orden de 3 piezas de la que solo se escanea una (Llegó) y se cierra el conteo: las otras dos quedan «Por verificar».
// Recorre: Operación → Reclamaciones → Por verificar (la orden con 2 de 3 faltantes) → abrir → marcar las piezas como perdidas → crear la reclamación con valor y notas → pestaña Reclamaciones → resolverla pagada con monto.
// Escritorio y móvil (360 px sin scroll horizontal). Capturas: docs/manual/frontend/img/l111-<paso>.png (solo escritorio).
import { expect, test } from '@playwright/test'

import { API_URL, apiHeaders, createClient, createConsignee, createOrder, LETTERS, login, ok, scan, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

test('Reclamaciones: por verificar → perdidas → reclamación → resolver', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const headers = await apiHeaders(request)
  const client = await createClient(request, headers, `Cliente 111 ${suffix}`)
  const consignee = await createConsignee(request, headers, client.publicId, `Tienda 111 ${suffix}`)
  const order = await createOrder(request, headers, client.publicId, consignee.publicId, 3)
  await scan(request, headers, 'ARRIVE', order.pieceNumbers[0])                                           // una pieza llega: la orden se mueve y cuenta «1 de 3»
  await ok(request.post(`${API_URL}/api/v1/scan/close-count`, { headers, data: { orderPublicId: order.publicId, confirm: true } }), 'cerrar conteo')   // las otras dos quedan faltantes

  await login(page)
  await page.goto('/ops/claims')
  await expect(page.getByRole('heading', { name: 'Reclamaciones' })).toBeVisible({ timeout: 30_000 })
  await page.getByPlaceholder(/Buscar/).first().fill(order.waybillNumber!)
  // en escritorio es una tabla y en móvil tarjetas: se busca por el texto del conduce
  const row = page.getByText(order.waybillNumber!).first()
  await expect(row).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('2 de 3').first()).toBeVisible()
  await shot(page, isMobile, 'l111-por-verificar')

  // abrir la orden: sus piezas faltantes; marcarlas perdidas y crear la reclamación
  await row.click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await expect(dialog.getByText(order.pieceNumbers[1])).toBeVisible()
  await shot(page, isMobile, 'l111-orden-faltantes')
  await dialog.getByRole('button', { name: /Marcar 2 como perdidas/ }).click()
  await expect(dialog.getByRole('button', { name: /Crear reclamación \(2\)/ })).toBeVisible({ timeout: 30_000 })
  await dialog.getByLabel('Valor declarado').fill('250')
  await dialog.getByLabel('Notas').fill('Faltan dos cajas en la entrega')
  await shot(page, isMobile, 'l111-nueva-reclamacion')
  await dialog.getByRole('button', { name: /Crear reclamación \(2\)/ }).click()
  await expect(page.getByText(/Reclamación RCL-\d+ creada\./)).toBeVisible({ timeout: 30_000 })

  // al crearla se abre la reclamación: resolverla pagada con monto
  const claim = page.getByRole('dialog')
  await expect(claim.getByText('$250.00')).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l111-reclamacion')
  await claim.getByLabel('Resolución').selectOption('PAID')
  await claim.getByLabel('Monto pagado').fill('200')
  await shot(page, isMobile, 'l111-resolver')
  await claim.getByRole('button', { name: 'Resolver', exact: true }).click()
  await expect(page.getByText(/Reclamación RCL-\d+ resuelta\./)).toBeVisible({ timeout: 30_000 })

  // la pestaña Reclamaciones la lista como Resuelta
  await page.getByRole('tab', { name: 'Reclamaciones' }).click()
  await page.getByPlaceholder(/Buscar/).first().fill(client.name)
  await expect(page.getByText(client.name).first()).toBeVisible({ timeout: 30_000 })
  await expect(page.locator('.chip', { hasText: 'Resuelta' }).first()).toBeVisible()
  await shot(page, isMobile, 'l111-reclamaciones')

  // móvil: sin scroll horizontal de página
  const { scrollWidth, innerWidth } = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth }))
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
})

test('Cerrar conteo: una pieza perdida que aparece crea una orden nueva y se confirma antes', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const headers = await apiHeaders(request)
  const client = await createClient(request, headers, `Cliente 111b ${suffix}`)
  const consignee = await createConsignee(request, headers, client.publicId, `Tienda 111b ${suffix}`)
  const order = await createOrder(request, headers, client.publicId, consignee.publicId, 3)
  await scan(request, headers, 'ARRIVE', order.pieceNumbers[2])                                                // llega una de las tres
  await ok(request.post(`${API_URL}/api/v1/scan/close-count`, { headers, data: { orderPublicId: order.publicId, confirm: true } }), 'cerrar conteo')
  await ok(request.post(`${API_URL}/api/v1/claims/lost`, { headers, data: { orderPublicId: order.publicId } }), 'marcar perdidas')   // las otras dos quedan perdidas

  await login(page)
  await page.goto('/ops/scan')
  await page.getByRole('radio', { name: 'Llegó' }).check()
  const input = page.getByLabel('Código a escanear')
  await input.fill(order.pieceNumbers[0])                                                                      // una pieza perdida aparece
  await input.press('Enter')
  const closeBtn = page.getByRole('button', { name: 'Cerrar conteo' })
  await expect(closeBtn).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l111-aparecio')
  await closeBtn.click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByText(/Al cerrar el conteo se creará un envío nuevo con 1 pieza\(s\) que aparecieron/)).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l111-cerrar-conteo')
  await dialog.getByRole('button', { name: 'Confirmar y cerrar el conteo' }).click()
  await expect(dialog).toHaveCount(0, { timeout: 30_000 })
  const list = await ok<{ items: { publicId: string }[] }>(request.get(`${API_URL}/api/v1/orders?clientId=${client.publicId}&take=10`, { headers }), 'órdenes del cliente')
  expect(list.items.length).toBe(2)                                                                            // la original y la orden nueva de la pieza que apareció
  await shot(page, isMobile, 'l111-conteo-cerrado')
})

