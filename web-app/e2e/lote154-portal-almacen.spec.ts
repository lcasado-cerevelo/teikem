// Recorrido del portal del cliente de ALMACÉN (Lotes 154-159) en la app del cliente contra el API real: el cliente crea un producto, lo edita, crea y edita y cancela un aviso de llegada, y crea,
// edita (datos y cantidad de la línea) y cancela un aviso de salida con existencia. Cada paso se comprueba también por el API del portal (lo que la pantalla muestra es lo que quedó guardado).
//
// Datos propios por API: un cliente con persona administradora, (la compañía de prueba ya trae el módulo de almacén) las tres banderas del portal (crear productos, llegadas y salidas) y un almacén nuevo. Requiere el API con
// Portal:ReturnInviteTokenInResponse y la app del cliente (CLIENT_APP_URL, por omisión http://localhost:8081). Capturas (solo escritorio): docs/manual/frontend/img/l154-<paso>.png.
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'

import { API_URL, APP_URL, createClientWithAdmin, loginAsClientAdmin, post, shot } from './portalHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)

const LETTERS = String(Date.now()).slice(-6).replace(/\d/g, (d) => 'ABCDEFGHJK'[Number(d)])

async function get<T>(request: APIRequestContext, path: string, headers: Record<string, string>): Promise<T> {
  const r = await request.get(`${API_URL}${path}`, { headers })
  expect(r.ok(), `${path}: ${await r.text()}`).toBeTruthy()
  return (await r.json()) as T
}

/** Lo que el portal devuelve en una lista: un arreglo o una página con items. */
const itemsOf = <T,>(body: T[] | { items?: T[] }): T[] => (Array.isArray(body) ? body : (body.items ?? []))

/** Cambia de sección del almacén por la clave de su chip (products, arrivals, exits…). */
async function section(page: Page, key: 'stock' | 'products' | 'movements' | 'value' | 'arrivals' | 'exits') {
  await page.getByTestId(`segment-${key}`).click()
}

/** Escoge el producto en el buscador de una línea (escribe el SKU y toca el resultado). */
async function pickProduct(page: Page, sku: string, lineIndex = 0) {
  const box = page.getByLabel('Producto', { exact: true }).nth(lineIndex)
  await box.fill(sku)
  await page.getByRole('button', { name: new RegExp(`^${sku} ·`) }).first().click()
}

test('Portal de almacén: producto, llegada y salida — crear, editar y cancelar de punta a punta', async ({ page, request, isMobile }, testInfo) => {
  test.skip(testInfo.project.name.startsWith('movil') && !process.env.PORTAL_MOBILE, 'Capturas y recorrido largo: escritorio (el móvil se prueba con PORTAL_MOBILE=1).')
  // sin la app del cliente servida (el CI de la web no la levanta) no hay nada que recorrer
  const appUp = await request.get(`${APP_URL}/login`, { timeout: 5_000 }).then((r) => r.ok(), () => false)
  test.skip(!appUp, `La app del cliente no responde en ${APP_URL}`)
  const suffix = `W${LETTERS}`
  const sku = `PA${LETTERS}`
  const c = await createClientWithAdmin(request, suffix, 'Portal almacén')
  // un almacén propio, con zona y posición, para que el cliente escoja y el personal pueda dejar existencia
  const wh = await post<{ warehouse: { publicId: string } }>(request, '/api/v1/warehouses', c.staff, { code: `W${suffix}`.slice(0, 20), name: `Almacén ${suffix}` })
  const W = wh.warehouse.publicId
  const zone = await post<{ id: number }>(request, `/api/v1/warehouses/${W}/zones`, c.staff, { code: 'PCK', name: 'Picking', zoneType: 'PICKING' })
  const bin = await post<{ id: number }>(request, `/api/v1/warehouses/${W}/bins`, c.staff, { zoneId: zone.id, code: 'P-01' })
  const patch = await request.patch(`${API_URL}/api/v1/clients/${c.client.publicId}/profile`, { headers: c.staff, data: { portalCanCreateProducts: true, portalCanCreateArrivals: true, portalCanCreateExits: true } })
  expect(patch.ok(), await patch.text()).toBeTruthy()

  await page.setViewportSize(isMobile ? { width: 390, height: 844 } : { width: 1440, height: 900 })
  await page.goto(`${APP_URL}/login`)
  await loginAsClientAdmin(page, c)
  await page.goto(`${APP_URL}/warehouse`)

  // ------------------------------------------------------------------ 1) PRODUCTO: crear y editar
  await section(page, 'products')
  await page.getByTestId('create-product').click()
  await page.getByTestId('create-sku').fill(sku)
  await page.getByTestId('create-name').fill(`Producto portal ${suffix}`)
  await shot(page, isMobile, 'l154-producto-nuevo')
  await page.getByTestId('create-save').click()
  await expect(page.getByText(`Producto portal ${suffix}`)).toBeVisible({ timeout: 30_000 })
  let products = itemsOf(await get<{ sku: string; name: string; publicId: string }[]>(request, '/api/v1/portal/warehouse/products', c.clientApi))
  expect(products.find((p) => p.sku === sku)?.name).toBe(`Producto portal ${suffix}`)

  await page.getByTestId(`edit-product-${sku}`).first().click()
  await page.getByTestId('edit-name').fill(`Producto editado ${suffix}`)
  await shot(page, isMobile, 'l154-producto-editar')
  await page.getByTestId('create-save').click()
  await expect(page.getByText(`Producto editado ${suffix}`)).toBeVisible({ timeout: 30_000 })
  products = itemsOf(await get<{ sku: string; name: string; publicId: string }[]>(request, '/api/v1/portal/warehouse/products', c.clientApi))
  const product = products.find((p) => p.sku === sku)!
  expect(product.name, 'la edición quedó guardada en el servidor').toBe(`Producto editado ${suffix}`)

  // el personal deja existencia (el cliente no mueve inventario): 10 unidades en la posición
  await post(request, '/api/v1/inventory/adjustments', c.staff, { productPublicId: product.publicId, warehousePublicId: W, binId: bin.id, quantity: 10, reason: 'FOUND', notes: 'e2e portal' })

  // ------------------------------------------------------------------ 2) LLEGADA: crear, editar, cancelar
  await section(page, 'arrivals')
  await page.getByTestId('create-arrival').click()
  await page.getByRole('button', { name: `W${suffix}`.slice(0, 20), exact: true }).click()
  await page.getByLabel('Referencia', { exact: true }).fill(`OC-${suffix}`)
  await pickProduct(page, sku)
  await page.getByTestId('create-qty-0').fill('5')
  await shot(page, isMobile, 'l154-llegada-nueva')
  await page.getByTestId('create-save').click()
  await expect(page.getByText(`OC-${suffix}`)).toBeVisible({ timeout: 30_000 })
  let arrivals = itemsOf(await get<{ id: number; reference: string; statusCode: string; lines: { expectedQty: number }[] }[]>(request, '/api/v1/portal/warehouse/arrivals', c.clientApi))
  let arrival = arrivals.find((a) => a.reference === `OC-${suffix}`)!
  expect(arrival.lines[0].expectedQty).toBe(5)

  await page.getByRole('button', { name: 'Editar', exact: true }).first().click()
  await page.getByLabel('Referencia', { exact: true }).fill(`OC-${suffix}-B`)
  await page.getByTestId('create-qty-0').fill('7')
  await shot(page, isMobile, 'l154-llegada-editar')
  await page.getByTestId('create-save').click()
  await expect(page.getByText(`OC-${suffix}-B`)).toBeVisible({ timeout: 30_000 })
  arrivals = itemsOf(await get<{ id: number; reference: string; statusCode: string; lines: { expectedQty: number }[] }[]>(request, '/api/v1/portal/warehouse/arrivals', c.clientApi))
  arrival = arrivals.find((a) => a.reference === `OC-${suffix}-B`)!
  expect(arrival.lines[0].expectedQty, 'la cantidad editada quedó guardada').toBe(7)
  expect(arrivals.filter((a) => a.reference?.startsWith(`OC-${suffix}`)).length, 'editar no duplica el aviso').toBe(1)

  await page.getByRole('button', { name: 'Cancelar aviso', exact: true }).first().click()
  await expect(page.getByText(/^Cancelad[ao]$/).first()).toBeVisible({ timeout: 30_000 })
  arrivals = itemsOf(await get<{ id: number; reference: string; statusCode: string; lines: { expectedQty: number }[] }[]>(request, '/api/v1/portal/warehouse/arrivals', c.clientApi))
  expect(arrivals.find((a) => a.reference === `OC-${suffix}-B`)?.statusCode).toBe('CANCELLED')
  await shot(page, isMobile, 'l154-llegada-cancelada')

  // ------------------------------------------------------------------ 3) SALIDA: crear (reserva), editar datos y cantidad, cancelar (libera)
  await section(page, 'exits')
  await page.getByTestId('create-exit').click()
  await page.getByRole('button', { name: `W${suffix}`.slice(0, 20), exact: true }).click()
  await page.getByLabel('Referencia', { exact: true }).fill(`RET-${suffix}`)
  await page.getByLabel('Destino (retiro, dirección o nota)', { exact: true }).fill('Muelle 3')
  await pickProduct(page, sku)
  await page.getByTestId('create-qty-0').fill('4')
  await shot(page, isMobile, 'l154-salida-nueva')
  await page.getByTestId('create-save').click()
  await expect(page.getByText(`RET-${suffix}`)).toBeVisible({ timeout: 30_000 })
  type ExitItem = { publicId: string; reference: string; destination: string; statusCode: string; qtyOrdered: number }
  let exits = itemsOf(await get<ExitItem[]>(request, '/api/v1/portal/warehouse/exits', c.clientApi))
  let exit = exits.find((e) => e.reference === `RET-${suffix}`)!
  expect(exit.qtyOrdered).toBe(4)
  const available = async () => (await get<{ items: { sku: string; available: number }[] }>(request, '/api/v1/portal/warehouse/stock?take=200', c.clientApi)).items.find((i) => i.sku === sku)?.available
  expect(await available(), 'el aviso reservó 4 de las 10').toBe(6)

  await page.getByRole('button', { name: 'Editar', exact: true }).first().click()
  await page.getByLabel('Destino (retiro, dirección o nota)', { exact: true }).fill('Muelle 9')
  await page.getByLabel(new RegExp(`^${sku} ·`)).fill('6')
  await shot(page, isMobile, 'l154-salida-editar')
  await page.getByTestId('create-save').click()
  await expect(page.getByText(/Muelle 9/)).toBeVisible({ timeout: 30_000 })
  exits = itemsOf(await get<ExitItem[]>(request, '/api/v1/portal/warehouse/exits', c.clientApi))
  exit = exits.find((e) => e.reference === `RET-${suffix}`)!
  expect(exit.destination).toBe('Muelle 9')
  expect(exit.qtyOrdered, 'la cantidad editada quedó guardada').toBe(6)
  expect(await available(), 'la reserva se reajustó a 6: quedan 4').toBe(4)

  await page.getByRole('button', { name: 'Cancelar aviso', exact: true }).first().click()
  await expect(page.getByText(/^Cancelad[ao]$/).first()).toBeVisible({ timeout: 30_000 })
  exits = itemsOf(await get<ExitItem[]>(request, '/api/v1/portal/warehouse/exits', c.clientApi))
  expect(exits.find((e) => e.reference === `RET-${suffix}`)?.statusCode).toBe('CANCELLED')
  expect(await available(), 'al cancelar se liberó la reserva: vuelven las 10').toBe(10)
  await shot(page, isMobile, 'l154-salida-cancelada')

  // ------------------------------------------------------------------ 4) lo ajeno no existe: otro cliente no ve ni toca estos avisos
  const other = await createClientWithAdmin(request, `${suffix}X`.slice(0, 12), 'Otro portal')
  const foreign = await request.patch(`${API_URL}/api/v1/portal/warehouse/arrivals/${arrival.id}`, { headers: other.clientApi, data: { reference: 'x', lines: [] } })
  expect([403, 404], 'un aviso ajeno no se puede editar').toContain(foreign.status())
  const none = itemsOf(await get<{ reference: string }[]>(request, '/api/v1/portal/warehouse/arrivals', other.clientApi))
  expect(none.length).toBe(0)
})
