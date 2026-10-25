// Recorrido del Lote 213 (2026-10-23/24, pedido de Luis): la tabla que sale debajo de la fila de captura rápida (lo guardado en la sesión) tiene un rango Desde/Hasta (hoy = hoy por omisión; capturar una
// fecha más vieja o más adelantada lo ensancha solo) y una búsqueda libre; su pie resume paquetes por tipo y servicio, total de paquetes, facturas distintas y COD total. Y Mis envíos del portal pinta la píldora de intentos fallidos.
// Se prueba en las dos pantallas: la entrada rápida de la web del personal y la rápida del portal del cliente, contra el API real. Solo escritorio. Capturas en docs/manual/frontend/img/l213-*.png.
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'

import { API_URL, APP_URL, createClientWithAdmin, loginAsClientAdmin, post, shot as portalShot } from './portalHelpers'
import { MANUAL_LANG, manualServerEnglish } from './manualLang'
import { LETTERS, apiHeaders, createClient, createConsignee, login, ok, shot as staffShot, tenantDay } from './staffHelpers'

const EN = MANUAL_LANG === 'en'
const NM = { farm: EN ? 'Pharmacy Filter' : 'Farmacia Filtro', shop: EN ? 'Grocery Filter' : 'Colmado Filtro', clin: EN ? 'Clinic Filter' : 'Clínica Filtro' }
test.use({ locale: 'es-PR' })
test.setTimeout(420_000)

/** Al contrato del cliente le agrega la tarifa de otro servicio sobre «Caja» para poder capturar dos servicios distintos. */
async function addExpressRate(request: APIRequestContext, headers: Record<string, string>, clientPublicId: string, service: string) {
  const client = await ok<{ currentContract?: { publicId?: string } | null }>(request.get(`${API_URL}/api/v1/clients/${clientPublicId}`, { headers }), 'cliente')
  const contractId = client.currentContract?.publicId ?? ''
  const res = await request.post(`${API_URL}/api/v1/contracts/${contractId}/rate-components`, { headers, data: { kind: 'PER_SERVICE', serviceType: service, packageType: 'BOX', rate: 15, effectiveFrom: tenantDay(-30) } })
  expect(res.ok(), `tarifa ${service}: ${res.status()} ${await res.text()}`).toBeTruthy()
}

/** Cómo se teclea un día en el campo de fecha de la web del personal (MM/DD/AAAA). */
const shown = (day: string) => `${day.slice(5, 7)}/${day.slice(8, 10)}/${day.slice(0, 4)}`

/** Valores del <select> de servicio de la fila de captura (sin el vacío). */
async function serviceValues(select: ReturnType<Page['locator']>): Promise<string[]> {
  return select.locator('option').evaluateAll((o) => (o as HTMLOptionElement[]).map((x) => x.value).filter(Boolean))
}

test('Personal: la tabla de lo capturado se filtra por un día y por texto, y el pie resume paquete, servicio y COD', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil'), 'la entrada rápida es de la web ancha')
  const headers = await apiHeaders(request)
  const client = await createClient(request, headers, `${EN ? 'Client L213 filters' : 'Cliente L213 filtros'} ${LETTERS}`)
  const names = [`${NM.farm} ${LETTERS}`, `${NM.shop} ${LETTERS}`, `${NM.clin} ${LETTERS}`]
  for (const n of names) await createConsignee(request, headers, client.publicId, n)

  await manualServerEnglish(page, '**/api/v1/catalogs/**')   // las etiquetas de servicio/paquete se guardan en cada fila: pedirlas en inglés
  await login(page)
  await page.goto('/orders')
  await page.getByRole('button', { name: /Nuevo envío/ }).click()
  const qk = page.locator('.qk')
  await expect(qk.getByRole('button', { name: 'Guardar fila' })).toBeVisible()
  const clientBox = qk.getByRole('combobox', { name: 'Cliente', exact: true })
  await clientBox.click()
  await clientBox.fill(client.name)
  await page.getByRole('option', { name: new RegExp(client.name) }).first().click()
  await page.keyboard.press('Escape')
  for (const label of ['Paquete', 'Servicio']) if ((await qk.getByLabel(label, { exact: true }).inputValue()) === '') await qk.getByLabel(label, { exact: true }).selectOption({ index: 1 })
  const service = qk.getByLabel('Servicio', { exact: true })
  const services = await serviceValues(service)
  const first = await service.inputValue()
  const other = services.find((s) => s !== first)
  expect(other, 'la compañía necesita dos servicios para este recorrido').toBeTruthy()
  await addExpressRate(request, headers, client.publicId, other!)

  const pick = async (name: string) => {
    const box = qk.getByRole('combobox', { name: 'Consignatario' })
    await box.click()
    await box.fill(name)
    await page.getByRole('option', { name: new RegExp(name) }).first().click()
  }
  const countSaved = () => qk.locator('tr.saved').count()

  await pick(names[0])
  await qk.getByLabel('COD ($)').fill('10.50')
  await page.keyboard.press('Enter')
  await expect.poll(countSaved, { timeout: 30_000 }).toBe(1)
  await pick(names[1])
  await qk.getByLabel('Servicio', { exact: true }).selectOption(other!)
  await qk.getByLabel('COD ($)').fill('20.00')
  await page.keyboard.press('Enter')
  await expect.poll(countSaved, { timeout: 30_000 }).toBe(2)
  await pick(names[2])
  await qk.getByLabel('Servicio', { exact: true }).selectOption(first)
  await qk.getByLabel('COD ($)').fill('7.25')
  await page.keyboard.press('Enter')
  await expect.poll(countSaved, { timeout: 30_000 }).toBe(3)

  // el día por omisión es hoy; se ven las tres, con el resumen del pie por paquete y servicio y el COD total
  await expect(qk.locator('.qk-shown')).toHaveText('3 de 3 entradas guardadas')
  const summary = qk.getByTestId('qk-summary')
  await expect(summary.getByTestId('qk-summary-line')).toHaveCount(2)
  await expect(summary.getByTestId('qk-summary-line').first()).toContainText(/^2 × /)   // 2 × Caja · Estándar
  await expect(summary.getByTestId('qk-summary-line').nth(1)).toContainText(/^1 × /)
  await expect(summary.getByTestId('qk-summary-cod')).toContainText('COD total: $37.75')
  await page.keyboard.press('Escape')   // el calendario de la fecha queda abierto tras guardar: se cierra para la captura
  await expect(page.getByText(/^\w+ De \d{4}$/)).toHaveCount(0)
  await staffShot(page, false, 'l213-personal-rapida-filtros-resumen', 900, qk)

  // la búsqueda libre filtra la tabla y el resumen (por consignatario, acentos y mayúsculas no importan)
  await qk.getByRole('searchbox', { name: 'Buscar en las entradas guardadas' }).fill(EN ? 'CLINIC' : 'CLINICA')
  await expect.poll(countSaved).toBe(1)
  await expect(qk.locator('.qk-shown')).toHaveText('1 de 3 entradas guardadas')
  await expect(summary.getByTestId('qk-summary-cod')).toContainText('COD total: $7.25')
  await qk.getByRole('searchbox', { name: 'Buscar en las entradas guardadas' }).fill('20.00')   // también por el monto del COD
  await expect.poll(countSaved).toBe(1)
  await expect(qk.locator('tr.saved').first()).toContainText(names[1])
  await qk.getByRole('searchbox', { name: 'Buscar en las entradas guardadas' }).fill('zzz')
  await expect(qk.locator('tr.qk-none')).toContainText('Ninguna entrada guardada coincide')
  await qk.getByRole('searchbox', { name: 'Buscar en las entradas guardadas' }).fill('')

  // una orden con la fecha de mañana ensancha el rango: el desde se queda en hoy y el hasta sigue a mañana
  await pick(names[0])
  const tomorrow = tenantDay(1)
  const date = qk.getByLabel('Fecha', { exact: true })
  await date.click()
  await date.fill('')
  await page.keyboard.type(shown(tomorrow))
  await page.keyboard.press('Escape')
  await page.keyboard.press('Enter')
  await expect(qk.locator('.qk-shown')).toHaveText('4 de 4 entradas guardadas', { timeout: 30_000 })
  expect(await countSaved()).toBe(4)
  await expect(qk.getByLabel('Desde', { exact: true })).toHaveValue(shown(tenantDay(0)))
  await expect(qk.getByLabel('Hasta', { exact: true })).toHaveValue(shown(tomorrow))
  await expect(summary.getByTestId('qk-summary-packages')).toContainText('Total de paquetes: 4')
  await expect(summary.getByTestId('qk-summary-invoices')).toContainText('Facturas distintas: 4')
  await page.keyboard.press('Escape')
  await staffShot(page, false, 'l213-personal-rapida-rango-resumen', 900, qk)
  // acortar el «hasta» a hoy esconde la de mañana
  const to = qk.getByLabel('Hasta', { exact: true })
  await to.click()
  await to.fill('')
  await page.keyboard.type(shown(tenantDay(0)))
  await page.keyboard.press('Escape')
  await expect(qk.locator('.qk-shown')).toHaveText('3 de 4 entradas guardadas')
  await expect(summary.getByTestId('qk-summary-packages')).toContainText('Total de paquetes: 3')
})

test('Portal: la tabla de lo capturado se filtra por un día y por texto, y el pie resume paquete, servicio y COD', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil'), 'la entrada rápida es de la web ancha')
  const suffix = `F${String(Date.now()).slice(-6)}`
  const c = await createClientWithAdmin(request, suffix, (EN ? 'Portal 213 filters' : 'Portal 213 filtros'))
  const settings = (await (await request.get(`${API_URL}/api/v1/tenant/settings`, { headers: c.staff })).json()) as { defaultServiceType?: string | null; defaultPackageType?: string | null }
  if (!settings.defaultServiceType || !settings.defaultPackageType) {
    const put = await request.put(`${API_URL}/api/v1/tenant/settings`, { headers: c.staff, data: { defaultServiceType: settings.defaultServiceType ?? 'STANDARD', defaultPackageType: settings.defaultPackageType ?? 'BOX' } })
    expect(put.ok(), await put.text()).toBeTruthy()
  }
  const names = [`${NM.farm} ${suffix}`, `${NM.shop} ${suffix}`, `${NM.clin} ${suffix}`]
  for (const n of names) await c.locations.post(n)
  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(`${APP_URL}/login`)
  await loginAsClientAdmin(page, c)
  await manualServerEnglish(page, '**/api/v1/portal/order-form*')
  await page.goto(`${APP_URL}/order/new`)
  await expect(page.getByTestId('quick-consignee')).toBeVisible({ timeout: 60_000 })

  const service = page.getByTestId('quick-serviceType')
  const services = await serviceValues(service)
  const first = await service.inputValue()
  const other = services.find((s) => s !== first)
  expect(other, 'la compañía necesita dos servicios para este recorrido').toBeTruthy()
  await addExpressRate(request, c.staff, c.client.publicId, other!)

  const savedCount = () => page.locator('[data-testid^="quick-saved-"]:not([data-testid="quick-saved-from"]):not([data-testid="quick-saved-to"]):not([data-testid="quick-saved-search"])').count()
  const capture = async (name: string, serviceValue: string, cod: string, date?: string) => {
    const before = await savedCount()
    const box = page.getByTestId('quick-consignee')
    await box.click()
    await box.fill(name)
    await page.getByTestId('quick-consignee-suggest-0').click()
    await page.getByTestId('quick-serviceType').selectOption(serviceValue)
    await page.getByTestId('quick-cod').fill(cod)
    if (date) await page.getByTestId('quick-date').fill(date)
    await page.getByTestId('quick-cod').press('Enter')
    return before
  }
  await capture(names[0], first, '10.50')
  await expect.poll(savedCount, { timeout: 30_000 }).toBe(1)
  await capture(names[1], other!, '20')
  await expect.poll(savedCount, { timeout: 30_000 }).toBe(2)
  await capture(names[2], first, '7.25')
  await expect.poll(savedCount, { timeout: 30_000 }).toBe(3)

  await expect(page.getByTestId('quick-shown')).toHaveText('3 de 3 envíos guardados')
  const summary = page.getByTestId('quick-summary')
  await expect(summary).toContainText('2 × ')
  await expect(summary).toContainText('1 × ')
  await expect(page.getByTestId('quick-summary-cod')).toHaveText('COD total: $37.75')
  await portalShot(page, false, 'l213-portal-rapida-filtros-resumen')

  const search = page.getByTestId('quick-saved-search')
  await search.fill(EN ? 'CLINIC' : 'CLINICA')
  await expect.poll(savedCount).toBe(1)
  await expect(page.getByTestId('quick-shown')).toHaveText('1 de 3 envíos guardados')
  await expect(page.getByTestId('quick-summary-cod')).toHaveText('COD total: $7.25')
  await search.fill('20.00')
  await expect.poll(savedCount).toBe(1)
  await search.fill('zzz')
  await expect(page.getByTestId('quick-none')).toBeVisible()
  await search.fill('')

  // un envío con la fecha de mañana ensancha el rango: el desde se queda en hoy y el hasta sigue a mañana
  const tomorrow = tenantDay(1)
  await capture(names[0], first, '', tomorrow)
  await expect(page.getByTestId('quick-shown')).toHaveText('4 de 4 envíos guardados', { timeout: 30_000 })
  await expect.poll(savedCount).toBe(4)
  await expect(page.getByTestId('quick-saved-from')).toHaveValue(tenantDay(0))
  await expect(page.getByTestId('quick-saved-to')).toHaveValue(tomorrow)
  await expect(page.getByTestId('quick-summary-packages')).toHaveText('Total de paquetes: 4')
  await expect(page.getByTestId('quick-summary-invoices')).toHaveText('Facturas distintas: 4')
  await portalShot(page, false, 'l213-portal-rapida-rango-resumen')
  // acortar el «hasta» a hoy esconde el de mañana
  await page.getByTestId('quick-saved-to').fill(tenantDay(0))
  await expect(page.getByTestId('quick-shown')).toHaveText('3 de 4 envíos guardados')
  await expect.poll(savedCount).toBe(3)
})

test('Portal: Mis envíos muestra la píldora de intentos fallidos en lo que volvió al almacén', async ({ page, request }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil'), 'la tabla de Mis envíos es de la web ancha')
  const suffix = `P${String(Date.now()).slice(-6)}`
  const c = await createClientWithAdmin(request, suffix, (EN ? 'Portal 213 attempts' : 'Portal 213 intentos'))
  const loc = await c.locations.post(`${EN ? 'Attempts Store' : 'Tienda Intentos'} ${suffix}`)
  type Piece = { packages: { pieceNumbers: string[] }[] }
  const newOrder = () => post<{ publicId: string; waybillNumber?: string } & Piece>(request, '/api/v1/orders', c.staff, { clientPublicId: c.client.publicId, consigneeLocationPublicId: loc.publicId, packages: [{ pieces: 1 }] })
  const scan = (o: Piece, action: string) => post(request, '/api/v1/scan/action', c.staff, { action, code: o.packages[0].pieceNumbers[0] })
  const fail = (o: { publicId: string }) => post(request, `/api/v1/orders/${o.publicId}/status`, c.staff, { toCode: 'FAILED', comment: (EN ? 'Nobody home' : 'Nadie en casa') })
  await newOrder() // una sin intentos: no lleva píldora
  const once = await newOrder()
  const twice = await newOrder()
  // salió, no se entregó y volvió al almacén
  await scan(once, 'DEPART')
  await fail(once)
  await scan(once, 'ARRIVE')
  // y la otra dos veces
  for (let i = 0; i < 2; i++) {
    await scan(twice, 'DEPART')
    await fail(twice)
    await scan(twice, 'ARRIVE')
  }

  await page.setViewportSize({ width: 1440, height: 900 })
  await page.goto(`${APP_URL}/login`)
  await loginAsClientAdmin(page, c)
  await manualServerEnglish(page, '**/api/v1/portal/orders*')
  await page.goto(`${APP_URL}/orders`)
  await expect(page.getByText('1 intento fallido', { exact: true })).toBeVisible({ timeout: 60_000 })
  await expect(page.getByText('2 intentos fallidos', { exact: true })).toBeVisible()
  expect(await page.getByText(/intentos? fallidos?/).count(), 'solo las dos órdenes con intentos llevan píldora').toBe(2)
  await portalShot(page, false, 'l213-portal-envios-intentos-fallidos')
})

