// Captura del manual `fc2-credito` (ventana «El cliente excede su límite de crédito» con «Autorizar y marcar como recogido»). Desde el Lote 107 marcar como recogida ya no mide el crédito en el servidor (se mide al someter),
// así que el API real no puede producir hoy esa respuesta: el recorrido abre una orden Sometida real y responde la acción «Marcar como recogido» con el 422 `credit_exceeded` que daba el servidor (mensaje y código reales de
// CreditRules.Message), y la pantalla muestra su ventana de verdad. No guarda nada. Captura: docs/manual/frontend/img/fc2-credito.png (solo escritorio).
import { expect, test } from '@playwright/test'

import { API_URL, LETTERS, apiHeaders, createClient, createConsignee, createOrder, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(120_000)

test('Crédito excedido al marcar como recogido: la ventana ofrece «Autorizar y marcar como recogido»', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  test.skip(isMobile, 'captura de escritorio')
  const headers = await apiHeaders(request)
  const client = await createClient(request, headers, `Cliente crédito FC2 ${LETTERS}`)
  const loc = await createConsignee(request, headers, client.publicId, `Farmacia Sol ${LETTERS}`)
  const order = await createOrder(request, headers, client.publicId, loc.publicId, 2)
  await ok(request.patch(`${API_URL}/api/v1/clients/${client.publicId}/profile`, { headers, data: { creditLimit: 10 } }), 'límite de crédito')

  await login(page)
  await page.route(/\/api\/v1\/orders\/[^/]+\/confirm$/, (route) =>
    route.fulfill({
      status: 422,
      contentType: 'application/problem+json',
      body: JSON.stringify({ type: 'about:blank', title: 'El cliente excede su límite de crédito: límite 10.00, en curso 0.00, este envío 20.00.', status: 422, code: 'credit_exceeded', errors: { limit: ['10.00'], exposure: ['0.00'], newAmount: ['20.00'], available: ['10.00'] } }),
    }),
  )
  await page.goto(`/orders/${order.publicId}`)
  await expect(page.getByRole('heading', { level: 1 })).toContainText(order.orderNumber)
  await page.locator('.head .act').getByRole('button', { name: 'Marcar como recogido', exact: true }).click()
  await page.getByRole('dialog').getByRole('button', { name: 'Marcar como recogido', exact: true }).click()
  const credit = page.getByRole('dialog', { name: 'El cliente excede su límite de crédito' })
  await expect(credit).toBeVisible({ timeout: 15_000 })
  await expect(credit.getByRole('button', { name: 'Autorizar y marcar como recogido' })).toBeVisible()
  await shot(page, isMobile, 'fc2-credito', 1000)
})
