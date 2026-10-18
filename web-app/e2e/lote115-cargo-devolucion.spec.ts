// Recorrido del cargo por devolución (Lote 115) en la web del personal contra el API real.
//
// Datos propios por API: un cliente con contrato y tarifa y una orden de 2 piezas ya recogida.
// Recorre: la ficha del cliente → Contratos → pestaña Contrato → «Cargo por devolución» (Sin cargo por omisión; los tres modos: por orden, por tipo de paquete y por pieza; validaciones de monto) → guardar por pieza
// → devolver la orden desde la ficha (con una dirección registrada) → queda Devuelta (el cargo de 2 piezas × 3.50 se le suma a la orden y sale en la factura aparte del flete; el cálculo lo cubre xunit y la facturación no corre en desarrollo).
// Escritorio y móvil (360 px sin scroll horizontal). Capturas: docs/manual/frontend/img/l115-<paso>.png (solo escritorio).
import { expect, test } from '@playwright/test'

import { API_URL, apiHeaders, createClient, createConsignee, createOrder, LETTERS, login, ok, scan, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

test('Cargo por devolución: los tres modos en el contrato y el cargo en la orden devuelta', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const headers = await apiHeaders(request)
  const client = await createClient(request, headers, `Cliente 115 ${suffix}`)
  const consignee = await createConsignee(request, headers, client.publicId, `Tienda 115 ${suffix}`)
  await ok(request.post(`${API_URL}/api/v1/locations`, { headers, data: { clientPublicId: client.publicId, name: `Almacén 115 ${suffix}`, locationType: 'PICKUP', line1: 'Calle 1 #1', city: 'San Juan', postalCode: '00901', country: 'PR' } }), 'dirección del cliente')
  const order = await createOrder(request, headers, client.publicId, consignee.publicId, 2)
  await scan(request, headers, 'PICKUP', order.pieceNumbers[0])

  await login(page)
  await page.goto(`/catalog/clients?client=${client.publicId}`)
  const section = page.locator('section.kit-panel', { has: page.getByRole('heading', { name: 'Contratos' }) })
  await expect(section).toBeVisible({ timeout: 30_000 })
  const toggle = section.locator('header button, button[aria-expanded]').first()
  if ((await toggle.getAttribute('aria-expanded')) === 'false') await toggle.click()      // los paneles de la ficha vienen plegados
  const mode = section.getByLabel('Cargo por devolución', { exact: true })
  await mode.scrollIntoViewIfNeeded()
  await expect(mode).toHaveValue('')                                                    // sin cargo por omisión
  await shot(page, isMobile, 'l115-contrato')

  // por orden: pide el monto
  await mode.selectOption('ORDER')
  await section.getByRole('button', { name: 'Guardar' }).last().click()
  await expect(section.getByText('Indique el monto del cargo por devolución.')).toBeVisible()
  // por tipo de paquete: un monto por cada tipo
  await mode.selectOption('PACKAGE_TYPE')
  await expect(section.getByText('Monto por cada tipo de paquete devuelto')).toBeVisible()
  await section.getByRole('button', { name: 'Agregar tipo de paquete' }).click()
  await shot(page, isMobile, 'l115-por-tipo')
  // por pieza devuelta: se guarda
  await mode.selectOption('PIECE')
  await section.getByLabel('Monto por cada pieza devuelta').fill('3.50')
  await shot(page, isMobile, 'l115-por-pieza')
  await section.getByRole('button', { name: 'Guardar' }).last().click()
  await expect(page.getByText('Cambios guardados.')).toBeVisible({ timeout: 30_000 })
  const contract = await ok<{ returnFee?: { mode?: string; amount?: number } | null }>(request.get(`${API_URL}/api/v1/contracts/${client.contractId}`, { headers }), 'contrato')
  expect([contract.returnFee?.mode, contract.returnFee?.amount]).toEqual(['PIECE', 3.5])

  // devolver la orden desde la ficha (con el contrato ya con cargo por pieza)
  await page.goto(`/orders/${order.publicId}`)
  await page.getByRole('button', { name: 'Devolver', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Devolver la orden' }).click()
  await expect(page.locator('.chip', { hasText: 'Devuelta' }).first()).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l115-orden-devuelta')

  const { scrollWidth, innerWidth } = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth }))
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
})
