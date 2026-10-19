// Recorrido de «Procesar la factura del cliente» (Lote 126) contra el API real: en Procesar entregas → Reconciliar se teclean los números de factura de dos órdenes SIN COD
// (una entregada y otra sin entregar) con «¿Qué vas a subir?» en Detectar; la vista previa dice «Factura procesada» y «No entregada» con tipo Factura; con «Hojas de COD» avisa que
// son de órdenes sin COD; al Cuadrar la orden entregada queda con la factura procesada (ficha de la orden) y la segunda lectura sale «Ya procesada». Por API: el modo inválido da 400 y el
// contrato del cliente enciende el cobro del cotejo. Escritorio y móvil. Capturas (solo escritorio): docs/manual/frontend/img/l126-<paso>.png.
import { expect, test } from '@playwright/test'

import { API_URL, apiHeaders, createClient, createConsignee, createOrder, enableModule, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

test('Procesar entregas: la factura de una orden sin COD se coteja contra la orden entregada, una sola vez, y el modo solo advierte', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const suffix = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  await enableModule(request, 'COD')
  await enableModule(request, 'LTL_GROUND')
  const headers = await apiHeaders(request)
  const client = await createClient(request, headers, `Factura ${suffix}`)
  const consignee = await createConsignee(request, headers, client.publicId, `Colmado ${suffix}`)
  const delivered = await createOrder(request, headers, client.publicId, consignee.publicId)
  const open = await createOrder(request, headers, client.publicId, consignee.publicId)
  // la entregada: registrar la entrega desde la web (Lote 118) la lleva a Entregada brincando lo que falte
  await ok(request.post(`${API_URL}/api/v1/orders/${delivered.publicId}/delivery`, { headers, data: { recipientName: `Ana ${suffix}` } }), 'entrega')
  const detail = await ok<{ clientInvoiceNumber: string }>(request.get(`${API_URL}/api/v1/orders/${delivered.publicId}`, { headers }), 'ficha')
  const openDetail = await ok<{ clientInvoiceNumber: string }>(request.get(`${API_URL}/api/v1/orders/${open.publicId}`, { headers }), 'ficha')

  // por API: el modo inválido responde 400; el contrato del cliente puede cobrar el cotejo
  const bad = await request.post(`${API_URL}/api/v1/cod/reconcile`, { headers, data: { codes: [{ code: detail.clientInvoiceNumber }], dryRun: true, mode: 'PDF', clientPublicId: client.publicId } })
  expect(bad.status()).toBe(400)
  expect(((await bad.json()) as { title: string }).title).toContain('AUTO, COD, INVOICE o BOTH')
  const contracts = await ok<{ publicId: string }[]>(request.get(`${API_URL}/api/v1/contracts?clientId=${client.publicId}`, { headers }), 'contratos')
  if (contracts.length > 0) {
    const fee = await ok<{ invoiceCheckFee: { enabled: boolean; amount: number } }>(
      request.patch(`${API_URL}/api/v1/contracts/${contracts[0].publicId}/invoice-check-fee`, { headers, data: { enabled: true, amount: 1.5 } }), 'cotejo')
    expect(fee.invoiceCheckFee).toEqual({ enabled: true, amount: 1.5 })
  }

  await login(page)
  await page.goto('/money/cod?tab=reconcile')
  await expect(page.getByRole('radio', { name: 'Detectar' })).toBeChecked({ timeout: 30_000 })
  // por API, con el número de factura acotado al cliente (FAC-##### se repite entre clientes): la entregada se procesaría y la otra no
  const byInvoice = await ok<{ items: { result: string; kind: string }[] }>(
    request.post(`${API_URL}/api/v1/cod/reconcile`, { headers, data: { codes: [{ code: detail.clientInvoiceNumber }, { code: openDetail.clientInvoiceNumber }], dryRun: true, clientPublicId: client.publicId } }), 'por factura')
  expect(byInvoice.items.map((i) => i.result)).toEqual(['INVOICE_CHECKED', 'NOT_DELIVERED'])
  // en la pantalla: se elige el cliente de las hojas (los números se repiten entre clientes) y se teclean los números de factura
  await page.locator('#cod-client').fill(`Factura ${suffix}`)
  await page.getByRole('option', { name: new RegExp(`Factura ${suffix}`) }).click()
  await page.getByLabel('Códigos escritos a mano').fill(`${detail.clientInvoiceNumber} ${openDetail.clientInvoiceNumber}`)
  await page.getByRole('button', { name: 'Agregar' }).click()
  await expect(page.getByText('Códigos por procesar (2)')).toBeVisible()
  await page.getByRole('button', { name: 'Vista previa' }).click()
  await expect(page.getByText('Esto es solo una simulación: todavía no se cuadró nada.')).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Factura procesada').first()).toBeVisible()
  await expect(page.getByText('No entregada').first()).toBeVisible()
  await expect(page.getByText(/1 factura\(s\) procesada\(s\)/)).toBeVisible()
  await expect(page.getByRole('alert')).toHaveCount(0)
  await shot(page, isMobile, 'l126-vista-previa')

  // dice «Hojas de COD»: se procesa igual, pero avisa
  await page.getByRole('radio', { name: 'Hojas de COD' }).check()
  await page.getByRole('button', { name: 'Vista previa' }).click()
  await expect(page.getByRole('alert')).toContainText('Dijiste hojas de COD', { timeout: 30_000 })
  await shot(page, isMobile, 'l126-advertencia')

  await page.getByRole('button', { name: 'Cuadrar' }).click()
  const confirm = page.getByRole('dialog', { name: 'Procesar las hojas' })
  await expect(confirm).toContainText('procesar 1 factura(s)')
  await confirm.getByRole('button', { name: 'Cuadrar' }).click()
  await expect(page.getByText('Corrida hecha').first()).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l126-corrida')

  // la orden entregada quedó con su factura procesada (ficha) y la segunda lectura dice «Ya procesada»
  const after = await ok<{ invoiceProcessedAtUtc: string | null; invoiceProcessedByName: string | null }>(request.get(`${API_URL}/api/v1/orders/${delivered.publicId}`, { headers }), 'ficha')
  expect(after.invoiceProcessedAtUtc).not.toBeNull()
  expect((await ok<{ invoiceProcessedAtUtc: string | null }>(request.get(`${API_URL}/api/v1/orders/${open.publicId}`, { headers }), 'ficha')).invoiceProcessedAtUtc ?? null).toBeNull()
  const again = await ok<{ items: { result: string; kind: string }[]; checked: number }>(
    request.post(`${API_URL}/api/v1/cod/reconcile`, { headers, data: { codes: [{ code: detail.clientInvoiceNumber }], dryRun: true, clientPublicId: client.publicId } }), 'segunda lectura')
  expect(again.items[0]).toMatchObject({ result: 'ALREADY_PROCESSED', kind: 'INVOICE' })
  expect(again.checked).toBe(0)

  await page.goto(`/orders/${delivered.publicId}`)
  await expect(page.getByText('Factura procesada')).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l126-ficha')

  // Lote 128: la lista filtra por factura procesada (sí/no) y por fecha de procesamiento con los rangos sugeridos
  const processedToday = await ok<{ items: { publicId: string }[] }>(request.get(`${API_URL}/api/v1/orders?invoiceProcessed=true&clientIds=${client.publicId}`, { headers }), 'lista procesadas')
  expect(processedToday.items.map((i) => i.publicId)).toEqual([delivered.publicId])
  const notProcessed = await ok<{ items: { publicId: string }[] }>(request.get(`${API_URL}/api/v1/orders?invoiceProcessed=false&clientIds=${client.publicId}`, { headers }), 'lista sin procesar')
  expect(notProcessed.items.map((i) => i.publicId)).toEqual([open.publicId])
  await page.goto(`/orders?client=${client.publicId}`)
  if (!isMobile) await expect(page.getByRole('columnheader', { name: 'Factura procesada' })).toBeVisible({ timeout: 30_000 })   // en móvil la lista son tarjetas
  else await expect(page.getByLabel('Factura procesada', { exact: true })).toBeVisible({ timeout: 30_000 })
  await page.getByLabel('Factura procesada', { exact: true }).selectOption('yes')
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: delivered.orderNumber })).toHaveCount(1, { timeout: 30_000 })
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: open.orderNumber })).toHaveCount(0)
  await page.locator('.filters select[id$="-preset"]').nth(1).selectOption('today')
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: delivered.orderNumber })).toHaveCount(1, { timeout: 30_000 })
  await shot(page, isMobile, 'l128-filtros')
  await page.getByLabel('Factura procesada', { exact: true }).selectOption('no')
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: delivered.orderNumber })).toHaveCount(0, { timeout: 30_000 })
  if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
