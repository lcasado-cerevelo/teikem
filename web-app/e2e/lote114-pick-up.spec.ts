// Recorrido del tipo de servicio «Pick up» (Lote 114) en la web del personal contra el API real.
//
// Datos propios por API: un cliente con contrato y tarifas STANDARD y PICK_UP, y una orden de tipo PICK_UP.
// Recorre: Sistema → Listas → Tipos de servicio → Pick up → Ajustar (las marcas «Pick up (recojo y traigo)» y «Facturable», encendidas por omisión) → la orden Sometida se recoge y al «Llegó» queda
// «En almacén» (estatus final, sin ruta ni chofer) → la ficha lo muestra.
// Escritorio y móvil (360 px sin scroll horizontal). Capturas: docs/manual/frontend/img/l114-<paso>.png (solo escritorio).
import { expect, test } from '@playwright/test'

import { API_URL, apiHeaders, createClient, createConsignee, LETTERS, login, ok, scan, shot, tenantDay } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)

test('Pick up: las marcas del tipo de servicio y una orden que termina En almacén sin ruta', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const suffix = `${isMobile ? 'M' : 'D'}${LETTERS}`
  const headers = await apiHeaders(request)
  const client = await createClient(request, headers, `Cliente 114 ${suffix}`)
  // el pick up facturable se cotiza con su propia tarifa
  await ok(request.post(`${API_URL}/api/v1/contracts/${client.contractId}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'PICK_UP', packageType: 'BOX', rate: 8, effectiveFrom: tenantDay(-30) } }), 'tarifa de pick up')
  const consignee = await createConsignee(request, headers, client.publicId, `Almacén 114 ${suffix}`)
  const order = await ok<{ publicId: string; waybillNumber?: string; packages: { pieceNumbers: string[] }[] }>(
    request.post(`${API_URL}/api/v1/orders`, { headers, data: { clientPublicId: client.publicId, consigneeLocationPublicId: consignee.publicId, serviceType: 'PICK_UP', packages: [{ pieces: 1, description: 'Caja a recoger' }] } }),
    'orden pick up',
  )
  const piece = order.packages[0].pieceNumbers[0]

  await login(page)

  // 1) las marcas del tipo de servicio (Listas → Tipos de servicio → Pick up → Ajustar); se mira y se cierra sin cambiar nada (solo escritorio: en móvil las listas son tarjetas)
  if (!isMobile) {
    await page.goto('/system/catalogs')
    await page.getByPlaceholder('Buscar lista…').fill('servicio')
    await page.getByRole('button', { name: /Tipos? de servicio/ }).first().click()
    const row = page.getByRole('row', { name: /PICK_UP/ })
    await expect(row).toBeVisible({ timeout: 30_000 })
    await row.getByRole('button', { name: 'Ajustar' }).click()
    const dialog = page.getByRole('dialog')
    await expect(dialog.getByText('Pick up (recojo y traigo)')).toBeVisible()
    await expect(dialog.getByText('Facturable', { exact: true }).first()).toBeVisible()
    await expect(dialog.getByText(/el envío termina «En almacén» y no sale a ruta/)).toBeVisible()
    await shot(page, isMobile, 'l114-marcas')
    await dialog.getByRole('button', { name: /Cancelar|Cerrar/ }).first().click()
  }

  // 2) la orden: se recoge y al llegar queda En almacén (final), sin ruta ni chofer
  await scan(request, headers, 'PICKUP', piece)
  await scan(request, headers, 'ARRIVE', piece)
  await page.goto(`/orders/${order.publicId}`)
  await expect(page.locator('.chip', { hasText: 'En almacén' }).first()).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('Pick up').first()).toBeVisible()
  await shot(page, isMobile, 'l114-orden')
  // una orden que ya terminó En almacén no se enruta ni sale a ruta: el escaneo de «Enrutar» no la mueve
  const routed = await request.post(`${API_URL}/api/v1/scan/action`, { headers, data: { action: 'ROUTE', code: piece } })
  const body = (await routed.json()) as { outcome?: string; moved?: boolean }
  expect(JSON.stringify(body)).not.toContain('IN_TRANSIT')

  const { scrollWidth, innerWidth } = await page.evaluate(() => ({ scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth }))
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
})
