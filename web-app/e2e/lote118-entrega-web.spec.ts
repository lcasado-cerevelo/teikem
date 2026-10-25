// Recorrido de «Registrar entrega» (Lote 118) en la web del personal contra el API real.
//
// Datos propios por API: un cliente con contrato y tarifa y una orden de 2 piezas que sale a ruta (En tránsito). Recorre: la ficha ofrece «Registrar entrega» → el diálogo avisa que la firma es automática y exige
// quién recibió → se registra → la orden queda Entregada, ya no se ofrece registrarla otra vez y la ficha muestra el COMPROBANTE (quién recibió, quién lo registró, firmado automáticamente por el usuario, el consignatario
// no firmó, ubicación). Por API: el comprobante dice lo mismo, registrar otra vez responde 409 y una orden Sometida también se puede entregar (brinca los estatus de en medio).
// Escritorio y móvil (360 px sin scroll horizontal). Capturas: docs/manual/frontend/img/l118-<paso>.png (solo escritorio).
import { expect, test } from '@playwright/test'

import { API_URL, apiHeaders, createClient, createConsignee, createOrder, LETTERS, login, ok, scan, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

test('Registrar entrega: la orden queda Entregada y el comprobante lleva la firma automática del usuario', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const headers = await apiHeaders(request)
  const client = await createClient(request, headers, `Cliente 118 ${suffix}`)
  const consignee = await createConsignee(request, headers, client.publicId, `Tienda 118 ${suffix}`)
  const order = await createOrder(request, headers, client.publicId, consignee.publicId, 2)
  await scan(request, headers, 'DEPART', order.pieceNumbers[0])                         // sale a ruta: En tránsito

  await login(page)
  await page.goto(`/orders/${order.publicId}`)
  const register = page.getByRole('button', { name: 'Registrar entrega', exact: true })
  await expect(register).toBeVisible({ timeout: 30_000 })
  await expect(page.getByTestId('delivery-proof')).toHaveCount(0)
  await shot(page, isMobile, 'l118-ficha-en-transito')

  await register.click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByRole('heading', { name: /Registrar la entrega de / })).toBeVisible()
  await expect(dialog.getByText(/se firma automáticamente a su nombre/i)).toBeVisible()
  await shot(page, isMobile, 'l118-dialogo')
  // sin quién recibió no se manda nada
  await dialog.getByRole('button', { name: 'Registrar la entrega' }).click()
  await expect(dialog.getByText('Indique quién recibió la entrega.')).toBeVisible()
  await shot(page, isMobile, 'l118-sin-receptor')
  await dialog.getByLabel('Quién recibió').fill(`Ana Rivera ${suffix}`)
  await dialog.getByLabel('Nota (opcional)').fill('Se dejó con la vecina')
  await dialog.getByRole('button', { name: 'Registrar la entrega' }).click()

  // la orden queda Entregada (el aviso de arriba dura poco: se comprueba el estatus de la ficha) y aparece el comprobante
  await expect(page.locator('.chip', { hasText: 'Entregado' }).first()).toBeVisible({ timeout: 30_000 })
  await expect(page.getByRole('button', { name: 'Registrar entrega', exact: true })).toHaveCount(0)
  const proof = page.getByTestId('delivery-proof')
  await expect(proof).toBeVisible({ timeout: 30_000 })
  await expect(proof).toContainText(`Ana Rivera ${suffix}`)
  await expect(proof).toContainText('Se dejó con la vecina')
  await expect(proof).toContainText('Firmada automáticamente por')
  await expect(proof).toContainText('El consignatario no firmó')
  if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  await shot(page, isMobile, 'l118-comprobante')

  // por API: el mismo comprobante, firma del usuario y sin ubicación (Google apagado en el entorno de pruebas); registrar otra vez es 409
  const dto = await ok<{ recipientName: string; signatureKind: string; source: string; signedByName: string; geoStatus: string; orderStatusCode: string }>(
    request.get(`${API_URL}/api/v1/orders/${order.publicId}/delivery`, { headers }), 'comprobante')
  expect(dto).toMatchObject({ recipientName: `Ana Rivera ${suffix}`, signatureKind: 'USER', source: 'STAFF_WEB', orderStatusCode: 'DELIVERED' })
  expect(dto.signedByName).toBeTruthy()
  expect(['NONE', 'PENDING', 'FROM_STOP']).toContain(dto.geoStatus)
  const again = await request.post(`${API_URL}/api/v1/orders/${order.publicId}/delivery`, { headers, data: { recipientName: 'Otra persona' } })
  expect(again.status()).toBe(409)
  expect(await again.text()).toContain('ya tiene registrada su entrega')

  // una orden Sometida también se entrega: brinca Recogida, Sin asignar, Asignada y En tránsito
  const second = await createOrder(request, headers, client.publicId, consignee.publicId, 1)
  const done = await ok<{ orderStatusCode: string }>(request.post(`${API_URL}/api/v1/orders/${second.publicId}/delivery`, { headers, data: { recipientName: `Luis ${suffix}` } }), 'entrega desde Sometida')
  expect(done.orderStatusCode).toBe('DELIVERED')
  const history = await ok<{ toCode: string; isSkipped?: boolean }[]>(request.get(`${API_URL}/api/v1/orders/${second.publicId}`, { headers }).then(async (r) => {
    const o = (await r.json()) as { id: number }
    return request.get(`${API_URL}/api/v1/status/history/TRANSPORT_ORDER/${o.id}`, { headers })
  }), 'historial')
  expect(history.filter((h) => h.isSkipped).length).toBeGreaterThanOrEqual(1)
})
