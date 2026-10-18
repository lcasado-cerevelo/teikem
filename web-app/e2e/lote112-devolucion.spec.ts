// Recorrido de Devolución (Lote 112) y de «devolver tras N intentos» (Lote 113) en la web del personal contra el API real.
//
// Datos propios por API: un cliente con contrato y tarifa y una orden de 2 piezas que sale a ruta y falla dos veces (la compañía sugiere devolver con 2 intentos).
// Recorre: la ficha de la orden avisa «ya lleva 2 intentos fallidos» → Devolver (marcar la pieza que se devuelve) → la orden queda Devuelta → la orden de vuelta (de la orden devuelta) con recogido y entrega al revés.
// Aparte: Sistema → Ajustes → General → Devolución cambia el número de intentos y lo deja como estaba.
// Escritorio y móvil (360 px sin scroll horizontal). Capturas: docs/manual/frontend/img/l112-<paso>.png y l113-<paso>.png (solo escritorio).
import { expect, test } from '@playwright/test'

import { API_URL, apiHeaders, createClient, createConsignee, createOrder, LETTERS, login, ok, scan, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

test('Devolución: la ficha sugiere devolver tras 2 intentos fallidos, se devuelve y nace la orden de vuelta', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const headers = await apiHeaders(request)
  const client = await createClient(request, headers, `Cliente 112 ${suffix}`)
  const consignee = await createConsignee(request, headers, client.publicId, `Tienda 112 ${suffix}`)
  const order = await createOrder(request, headers, client.publicId, consignee.publicId, 2)
  await scan(request, headers, 'DEPART', order.pieceNumbers[0])                         // sale a ruta (el escaneo lleva la orden de una vez)
  for (let i = 0; i < 2; i++) {                                                         // dos intentos fallidos
    await ok(request.post(`${API_URL}/api/v1/orders/${order.publicId}/status`, { headers, data: { toCode: 'FAILED', comment: 'Nadie en casa' } }), 'intento fallido')
    if (i === 0) {
      await scan(request, headers, 'ARRIVE', order.pieceNumbers[0])                     // vuelve al almacén y vuelve a salir
      await scan(request, headers, 'DEPART', order.pieceNumbers[0])
    }
  }

  await login(page)
  await page.goto(`/orders/${order.publicId}`)
  await expect(page.getByTestId('suggest-return')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByTestId('suggest-return')).toContainText('2')
  await shot(page, isMobile, 'l113-sugerencia')

  await page.getByRole('button', { name: 'Devolver', exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('heading', { name: /Devolver / })).toBeVisible()
  await expect(dialog.getByText('Piezas que se devuelven')).toBeVisible()
  await shot(page, isMobile, 'l112-devolver')
  // el cliente todavía no tiene ninguna dirección a la que regresar la orden: se explica y no se devuelve
  await dialog.getByRole('button', { name: 'Devolver la orden' }).click()
  await expect(dialog.getByText('El cliente no tiene ninguna dirección registrada')).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l112-sin-direccion')
  await dialog.getByRole('button', { name: 'Volver', exact: true }).click()
  // con una dirección de recogido registrada, la orden regresa ahí
  await ok(request.post(`${API_URL}/api/v1/locations`, { headers, data: { clientPublicId: client.publicId, name: `Almacén 112 ${suffix}`, locationType: 'PICKUP', line1: 'Calle 1 #1', city: 'San Juan', postalCode: '00901', country: 'PR' } }), 'dirección del cliente')
  await page.getByRole('button', { name: 'Devolver', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Devolver la orden' }).click()
  await expect(page.locator('.chip', { hasText: 'Devuelta' }).first()).toBeVisible({ timeout: 30_000 })   // el aviso de arriba dura poco: se comprueba el estatus de la ficha
  await expect(page.getByRole('button', { name: 'Devolver', exact: true })).toHaveCount(0)               // ya no se puede devolver otra vez
  await shot(page, isMobile, 'l112-devuelta')

  // la orden de vuelta: otra orden del mismo cliente que dice de cuál es la vuelta
  const list = await ok<{ items: { publicId: string; orderNumber: string }[] }>(request.get(`${API_URL}/api/v1/orders?clientId=${client.publicId}&take=10`, { headers }), 'órdenes del cliente')
  const back = list.items.find((o) => o.publicId !== order.publicId)
  expect(back, 'debe haber una orden de vuelta').toBeTruthy()
  await page.goto(`/orders/${back!.publicId}`)
  await expect(page.getByText(/Vuelta de/)).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l112-orden-de-vuelta')
  const { scrollWidth, innerWidth } = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth }))
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
})

test('Devolver tras N intentos: el ajuste de la compañía se cambia y se deja como estaba', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const headers = await apiHeaders(request)
  const before = await ok<{ suggestAfterAttempts: number }>(request.get(`${API_URL}/api/v1/tenant/return-settings`, { headers }), 'ajuste actual')

  await login(page)
  await page.goto('/system/settings')
  const field = page.getByLabel('Sugerir devolver con este número de intentos fallidos')
  await expect(field).toBeVisible({ timeout: 30_000 })
  await field.scrollIntoViewIfNeeded()
  await shot(page, isMobile, 'l113-ajuste')
  await field.fill('21')
  await page.getByRole('button', { name: 'Guardar' }).last().click()
  await expect(page.getByText('Escriba un número de 0 a 20 (0 = sin sugerencia).')).toBeVisible()      // fuera de rango no se manda
  await field.fill(String(before.suggestAfterAttempts === 3 ? 4 : 3))
  await page.getByRole('button', { name: 'Guardar' }).last().click()
  await expect(page.getByText('Ajuste de devolución guardado.')).toBeVisible({ timeout: 30_000 })
  const changed = await ok<{ suggestAfterAttempts: number }>(request.get(`${API_URL}/api/v1/tenant/return-settings`, { headers }), 'ajuste cambiado')
  expect(changed.suggestAfterAttempts).not.toBe(before.suggestAfterAttempts)
  // se deja como estaba (los demás recorridos cuentan con el valor de siempre)
  await ok(request.put(`${API_URL}/api/v1/tenant/return-settings`, { headers, data: { suggestAfterAttempts: before.suggestAfterAttempts } }), 'restaurar')
})
