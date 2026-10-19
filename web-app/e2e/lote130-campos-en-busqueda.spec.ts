// Recorrido del Lote 130 contra el API real: un campo personalizado con «Mostrar en la búsqueda» aparece como filtro (del tipo del campo) en la lista de su entidad
// (órdenes y clientes), filtra de verdad, uno sin la opción no se puede filtrar (400) y las Vistas lo llevan como columna. Escritorio y móvil.
// Capturas: docs/manual/frontend/img/l130-<paso>.png.
import { expect, test } from '@playwright/test'

import { API_URL, apiHeaders, createClient, createConsignee, createOrder, enableModule, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

test('Campos personalizados en la búsqueda: filtro del tipo del campo en Órdenes y Clientes, 400 si no está habilitado y columna en las Vistas', async ({ page, request }, testInfo) => {
  const isMobile = testInfo.project.name.startsWith('movil')
  const s = `${isMobile ? 'M' : 'D'}${String(Date.now()).slice(-6)}`
  await enableModule(request, 'CUSTOM_FIELDS')
  await enableModule(request, 'LTL_GROUND')
  const headers = await apiHeaders(request)
  const created: number[] = []
  const def = async (entity: string, key: string, label: string, dataType: string, showInList: boolean) => {
    const d = await ok<{ id: number }>(
      request.post(`${API_URL}/api/v1/custom-fields/definitions/${entity}`, {
        headers,
        data: { fieldKey: key, labels: { es: label, en: label }, descriptions: {}, dataType, isRequired: false, isUnique: false, showInList, sortOrder: 0, options: [] },
      }),
      `campo ${key}`,
    )
    created.push(d.id)
    return d
  }
  try {
    // campos de órdenes: texto y número habilitados para la búsqueda, y un texto que no lo está
    const divKey = `div${s.toLowerCase()}`
    const pesoKey = `peso${s.toLowerCase()}`
    const secretKey = `sec${s.toLowerCase()}`
    await def('TRANSPORT_ORDER', divKey, `División ${s}`, 'TEXT', true)
    await def('TRANSPORT_ORDER', pesoKey, `Peso ${s}`, 'NUMBER', true)
    await def('TRANSPORT_ORDER', secretKey, `Interno ${s}`, 'TEXT', false)
    // campo de clientes
    const rutaKey = `ruta${s.toLowerCase()}`
    await def('CLIENT', rutaKey, `Ruta ${s}`, 'TEXT', true)

    // dos órdenes con distinta división y peso; dos clientes con distinta ruta
    const client = await createClient(request, headers, `Campos ${s}`)
    const consignee = await createConsignee(request, headers, client.publicId, `Colmado ${s}`)
    const a = await createOrder(request, headers, client.publicId, consignee.publicId)
    const b = await createOrder(request, headers, client.publicId, consignee.publicId)
    const detail = async (publicId: string) => (await ok<{ id: number }>(request.get(`${API_URL}/api/v1/orders/${publicId}`, { headers }), 'ficha')).id
    const [aId, bId] = [await detail(a.publicId), await detail(b.publicId)]
    await ok(request.put(`${API_URL}/api/v1/custom-fields/values/TRANSPORT_ORDER/${aId}`, { headers, data: { values: { [divKey]: 'Norte', [pesoKey]: 5, [secretKey]: 'x' } } }), 'valores a')
    await ok(request.put(`${API_URL}/api/v1/custom-fields/values/TRANSPORT_ORDER/${bId}`, { headers, data: { values: { [divKey]: 'Sur', [pesoKey]: 20 } } }), 'valores b')
    const client2 = await createClient(request, headers, `Campos dos ${s}`)
    const c1 = await ok<{ id: number }>(request.get(`${API_URL}/api/v1/clients/${client.publicId}`, { headers }), 'cliente 1')
    await ok(request.put(`${API_URL}/api/v1/custom-fields/values/CLIENT/${c1.id}`, { headers, data: { values: { [rutaKey]: 'Ruta Oeste' } } }), 'valor cliente')

    // API: filtros por tipo, habilitado y no habilitado
    const list = async (q: string) => (await ok<{ items: { publicId: string }[] }>(request.get(`${API_URL}/api/v1/orders?clientIds=${client.publicId}&${q}`, { headers }), 'lista')).items.map((i) => i.publicId)
    expect(await list(`cf.${divKey}=Norte`)).toEqual([a.publicId])
    expect(await list(`cf.${pesoKey}.from=10`)).toEqual([b.publicId])
    expect((await list(`cf.${pesoKey}.from=1&cf.${pesoKey}.to=30`)).sort()).toEqual([a.publicId, b.publicId].sort())
    const hidden = await request.get(`${API_URL}/api/v1/orders?clientIds=${client.publicId}&cf.${secretKey}=x`, { headers })
    expect(hidden.status()).toBe(400)
    expect(await hidden.text()).toContain('no está habilitado para la búsqueda')
    const unknown = await request.get(`${API_URL}/api/v1/orders?cf.noexiste=x`, { headers })
    expect(unknown.status()).toBe(400)
    const clients = await ok<{ publicId: string }[]>(request.get(`${API_URL}/api/v1/clients?cf.${rutaKey}=oeste`, { headers }), 'clientes por ruta')
    expect(clients.map((c) => c.publicId)).toEqual([client.publicId])
    expect(client2.publicId).not.toBe(client.publicId)

    // Vistas: el campo (aun el no habilitado para la búsqueda) es columna de la fuente de órdenes
    const sources = await ok<{ key: string; customFields?: { key: string }[] }[]>(request.get(`${API_URL}/api/v1/analytics/data-sources`, { headers }), 'fuentes')
    const keys = (sources.find((x) => x.key === 'TRANSPORT_ORDER')?.customFields ?? []).map((c) => c.key)
    expect(keys.some((k) => k.includes(secretKey))).toBe(true)

    // en pantalla: Órdenes muestra los filtros del tipo del campo y filtra
    await login(page)
    await page.goto(`/orders?client=${client.publicId}`)
    const division = page.getByLabel(`División ${s}`, { exact: true })
    await expect(division).toBeVisible({ timeout: 30_000 })
    await expect(page.getByLabel(`Peso ${s} (desde)`)).toBeVisible()
    await expect(page.getByLabel(`Peso ${s} (hasta)`)).toBeVisible()
    await expect(page.getByLabel(`Interno ${s}`)).toHaveCount(0)   // sin «Mostrar en la búsqueda» no hay filtro
    await expect(page.locator('tr, .dt-cards > li').filter({ hasText: a.orderNumber })).toHaveCount(1, { timeout: 30_000 })
    await expect(page.locator('tr, .dt-cards > li').filter({ hasText: b.orderNumber })).toHaveCount(1)
    await division.fill('Norte')
    await expect(page.locator('tr, .dt-cards > li').filter({ hasText: b.orderNumber })).toHaveCount(0, { timeout: 30_000 })
    await expect(page.locator('tr, .dt-cards > li').filter({ hasText: a.orderNumber })).toHaveCount(1)
    await shot(page, isMobile, 'l130-ordenes')
    await page.getByRole('button', { name: 'Limpiar' }).click()
    await page.getByLabel(`Peso ${s} (desde)`).fill('10')
    await expect(page.locator('tr, .dt-cards > li').filter({ hasText: a.orderNumber })).toHaveCount(0, { timeout: 30_000 })
    await expect(page.locator('tr, .dt-cards > li').filter({ hasText: b.orderNumber })).toHaveCount(1)
    if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)

    // Clientes: el filtro de la ruta
    await page.goto('/catalog/clients')
    const ruta = page.getByLabel(`Ruta ${s}`, { exact: true })
    await expect(ruta).toBeVisible({ timeout: 30_000 })
    await ruta.fill('oeste')
    await expect(page.getByText(`Campos ${s}`, { exact: true }).first()).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText(`Campos dos ${s}`, { exact: true })).toHaveCount(0)
    await shot(page, isMobile, 'l130-clientes')
    if (isMobile) expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true)
  } finally {
    for (const id of created) await request.delete(`${API_URL}/api/v1/custom-fields/definitions/${id}`, { headers })
  }
})
