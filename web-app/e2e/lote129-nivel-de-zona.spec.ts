// Recorrido del nivel de zona (Lote 129) contra el API real: niveles METRO e ISLA (Isla con chofer por omisión), dos zonas con su nivel y un ZIP cada una; una orden a un ZIP de Isla nace con su
// zona y su nivel (ficha y lista; filtros por zona y por nivel); la tarifa del contrato con nivel gana al cotizar; la tarifa del chofer con nivel; y, en pantalla, Flota → Zonas de despacho muestra
// el panel «Niveles de zona» con su chofer por omisión y las zonas con su nivel. Escritorio y móvil. Capturas: docs/manual/frontend/img/l129-<paso>.png.
import { expect, test } from '@playwright/test'

import { API_URL, apiHeaders, createClient, createConsignee, createOrder, enableModule, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

test('Nivel de zona: la orden hereda el nivel de su zona de entrega, la lista filtra por zona y nivel y las tarifas con nivel ganan', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const suffix = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  await enableModule(request, 'CATALOG')
  await enableModule(request, 'LTL_GROUND')
  const headers = await apiHeaders(request)
  // ZIP únicos para no chocar con zonas de otros recorridos (los miembros no se repiten entre zonas activas)
  const zipIsla = `9${String(Date.now()).slice(-4)}`
  const zipMetro = `8${String(Date.now()).slice(-4)}`

  // chofer de Isla (una compañía externa registrada como chofer) y los dos niveles
  const driver = await ok<{ publicId: string; code: string }>(request.post(`${API_URL}/api/v1/drivers`, { headers, data: { code: `ISLA${suffix}`, fullName: `Transporte Isla ${suffix}` } }), 'chofer')
  const isla = await ok<{ id: number; code: string; defaultDriverCode: string }>(request.post(`${API_URL}/api/v1/zone-groups`, { headers, data: { code: `ISLA${suffix}`, name: `Isla ${suffix}`, sortOrder: 2, defaultDriverPublicId: driver.publicId } }), 'nivel isla')
  const metro = await ok<{ id: number; code: string }>(request.post(`${API_URL}/api/v1/zone-groups`, { headers, data: { code: `METRO${suffix}`, name: `Metro ${suffix}`, sortOrder: 1 } }), 'nivel metro')
  expect(isla.defaultDriverCode).toBe(driver.code)
  const dupe = await request.post(`${API_URL}/api/v1/zone-groups`, { headers, data: { code: `isla${suffix}` } })
  expect(dupe.status()).toBe(409)

  // dos zonas con su nivel y un ZIP cada una
  const zIsla = await ok<{ id: number; code: string; zoneGroupCode: string }>(request.post(`${API_URL}/api/v1/dispatch-zones`, { headers, data: { code: `ZI${suffix}`, name: `Sur ${suffix}`, zoneGroupId: isla.id } }), 'zona isla')
  expect(zIsla.zoneGroupCode).toBe(isla.code)
  const zMetro = await ok<{ id: number; code: string }>(request.post(`${API_URL}/api/v1/dispatch-zones`, { headers, data: { code: `ZM${suffix}`, name: `SJ ${suffix}` } }), 'zona metro')
  const leveled = await ok<{ zoneGroupCode: string }>(request.patch(`${API_URL}/api/v1/dispatch-zones/${zMetro.id}`, { headers, data: { zoneGroupId: metro.id } }), 'nivel de la zona')
  expect(leveled.zoneGroupCode).toBe(metro.code)
  await ok(request.post(`${API_URL}/api/v1/dispatch-zones/${zIsla.id}/members`, { headers, data: { matchType: 'POSTAL_CODE', matchValue: zipIsla } }), 'zip isla')
  await ok(request.post(`${API_URL}/api/v1/dispatch-zones/${zMetro.id}/members`, { headers, data: { matchType: 'POSTAL_CODE', matchValue: zipMetro } }), 'zip metro')

  // una orden a un consignatario del ZIP de Isla nace con su zona y su nivel
  const client = await createClient(request, headers, `Nivel ${suffix}`)
  const consignee = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/locations`, { headers, data: { clientPublicId: client.publicId, name: `Colmado ${suffix}`, locationType: 'DELIVERY', line1: 'Calle 1', city: 'Ponce', postalCode: zipIsla, country: 'PR' } }), 'consignatario')
  const order = await createOrder(request, headers, client.publicId, consignee.publicId)
  const detail = await ok<{ zoneCode: string; zoneGroupCode: string; zoneGroupName: string }>(request.get(`${API_URL}/api/v1/orders/${order.publicId}`, { headers }), 'ficha')
  expect(detail).toMatchObject({ zoneCode: zIsla.code, zoneGroupCode: isla.code })
  const other = await createConsignee(request, headers, client.publicId, `Otro ${suffix}`)
  const noZone = await createOrder(request, headers, client.publicId, other.publicId)
  // la lista filtra por zona y por nivel
  const byLevel = await ok<{ items: { publicId: string; zoneCode: string; zoneGroupCode: string }[] }>(request.get(`${API_URL}/api/v1/orders?zoneGroupIds=${isla.id}&clientIds=${client.publicId}`, { headers }), 'por nivel')
  expect(byLevel.items.map((i) => i.publicId)).toEqual([order.publicId])
  expect(byLevel.items[0]).toMatchObject({ zoneCode: zIsla.code, zoneGroupCode: isla.code })
  const byZone = await ok<{ items: { publicId: string }[] }>(request.get(`${API_URL}/api/v1/orders?zoneIds=${zMetro.id}&clientIds=${client.publicId}`, { headers }), 'por zona')
  expect(byZone.items).toEqual([])
  const without = await ok<{ items: { publicId: string }[] }>(request.get(`${API_URL}/api/v1/orders?noZone=true&clientIds=${client.publicId}`, { headers }), 'sin zona')
  expect(without.items.map((i) => i.publicId)).toEqual([noZone.publicId])
  // asignar zona a las órdenes existentes
  const assigned = await ok<{ examined: number }>(request.post(`${API_URL}/api/v1/dispatch-zones/assign-orders`, { headers }), 'asignar')
  expect(assigned.examined).toBeGreaterThanOrEqual(1)

  // tarifa del chofer con nivel (la general y la de Isla); duplicada en el mismo nivel es 409
  await ok(request.post(`${API_URL}/api/v1/drivers/${driver.publicId}/delivery-rates`, { headers, data: { serviceType: 'STANDARD', packageType: 'BOX', rate: 1.5 } }), 'tarifa general')
  const levelRate = await ok<{ zoneGroupCode: string; zoneGroup: string }>(request.post(`${API_URL}/api/v1/drivers/${driver.publicId}/delivery-rates`, { headers, data: { serviceType: 'STANDARD', packageType: 'BOX', rate: 4, zoneGroup: isla.code } }), 'tarifa isla')
  expect(levelRate.zoneGroupCode).toBe(isla.code)
  const dupRate = await request.post(`${API_URL}/api/v1/drivers/${driver.publicId}/delivery-rates`, { headers, data: { serviceType: 'STANDARD', packageType: 'BOX', rate: 5, zoneGroup: isla.code } })
  expect(dupRate.status()).toBe(409)
  expect(await dupRate.text()).toContain('en el nivel')
  const badLevel = await request.post(`${API_URL}/api/v1/drivers/${driver.publicId}/delivery-rates`, { headers, data: { serviceType: 'STANDARD', packageType: 'BOX', rate: 5, zoneGroup: 'NOEXISTE' } })
  expect(badLevel.status()).toBe(400)

  // tarifa del contrato con nivel: la cotización del nivel Isla gana sobre la general
  const contracts = await ok<{ publicId: string }[]>(request.get(`${API_URL}/api/v1/contracts?clientId=${client.publicId}`, { headers }), 'contratos')
  if (contracts.length > 0) {
    const kid = contracts[0].publicId
    await request.post(`${API_URL}/api/v1/contracts/${kid}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType: 'BOX', rate: 10 } })
    const withLevel = await ok<{ zoneGroupCode: string }>(request.post(`${API_URL}/api/v1/contracts/${kid}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: 'STANDARD', packageType: 'BOX', rate: 25, zoneGroup: isla.code } }), 'tarifa de nivel')
    expect(withLevel.zoneGroupCode).toBe(isla.code)
    const quoteIsla = await ok<{ total: number }>(request.post(`${API_URL}/api/v1/billing/contract-rate-quote`, { headers, data: { clientPublicId: client.publicId, lines: [{ serviceType: 'STANDARD', packageType: 'BOX', pieces: 1 }], zoneGroupId: isla.id } }), 'cotizar isla')
    const quoteGeneral = await ok<{ total: number }>(request.post(`${API_URL}/api/v1/billing/contract-rate-quote`, { headers, data: { clientPublicId: client.publicId, lines: [{ serviceType: 'STANDARD', packageType: 'BOX', pieces: 1 }] } }), 'cotizar general')
    expect(quoteIsla.total).toBeGreaterThan(quoteGeneral.total)
  }

  // en pantalla: Flota → Zonas de despacho muestra los niveles con su chofer y las zonas con su nivel; la ficha y la lista de órdenes muestran la zona
  await login(page)
  await page.goto('/catalog/fleet?tab=zones')
  const levels = page.getByRole('heading', { name: 'Niveles de zona' })
  if (!(await levels.isVisible({ timeout: 5_000 }).catch(() => false))) await page.getByRole('tab', { name: /Zonas/ }).click()
  await expect(page.getByText(`Isla ${suffix}`).first()).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(`Transporte Isla ${suffix}`).first()).toBeVisible()
  await shot(page, isMobile, 'l129-niveles')
  await page.goto(`/orders/${order.publicId}`)
  await expect(page.getByText(`${zIsla.code} · Sur ${suffix} (Isla ${suffix})`)).toBeVisible({ timeout: 30_000 })
  await shot(page, isMobile, 'l129-ficha')
  await page.goto(`/orders?client=${client.publicId}`)
  await page.getByLabel('Nivel de zona').click()
  await page.getByLabel(`Isla ${suffix}`).click()
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: order.orderNumber })).toHaveCount(1, { timeout: 30_000 })
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: noZone.orderNumber })).toHaveCount(0)
  await shot(page, isMobile, 'l129-filtro')
  if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
})
