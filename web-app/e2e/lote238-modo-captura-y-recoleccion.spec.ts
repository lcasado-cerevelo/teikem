// Lote 238 — recorrido contra el API real (db-update hecho; API SIN correo: Brevo__ApiKey vacía). Cuatro cosas:
//  1) Orden de venta: UN botón «Nueva orden» + el botón del otro modo; el modo se recuerda por persona en el servidor (GET /me/preferences/sales.entryMode).
//  2) «Cambiar al asistente» desde la ventana con la orden vacía (antes no hacía nada) y «Cambiar al formulario completo» desde el asistente.
//  3) Recolección y empaque: la ficha de una orden de venta con inventario reservado muestra «Lo que hay que recolectar» (posición y cantidad) y «Recolectar»
//     llena el formulario de la izquierda SIN recolectar.
//  4) La última compañía usada es la que abre al volver a entrar (switch-tenant la guarda).
// Correr (web en :5174 apuntando al API de pruebas): WEB_URL=http://localhost:5174 API_URL=http://localhost:5733 npx playwright test lote238 --project=escritorio --no-deps --trace off --workers=1
import { expect, test, type APIRequestContext } from '@playwright/test'

import { ADMIN, API_URL, LETTERS, apiHeaders, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

interface Seed {
  sku: string
  client: string
  orderNumber: string
  orderId: string
}
let S: Seed

const PLATFORM = { email: process.env.TEIKEM_PLATFORM_EMAIL ?? 'teikem+support@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? ADMIN.password }

/** Enciende un módulo de la compañía con el usuario de la plataforma (el administrador de la compañía no puede). */
async function enableModule(request: APIRequestContext, key: string) {
  const login = { email: PLATFORM.email, password: PLATFORM.password }
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: login })
  expect(res.ok(), await res.text()).toBeTruthy()
  let body = (await res.json()) as { status?: string; tenants?: { tenantId?: number; isDefault?: boolean }[]; tokens?: { accessToken?: string } }
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((x) => x.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { ...login, tenantId: tenant?.tenantId } })
    body = (await res.json()) as typeof body
  }
  const headers = { Authorization: `Bearer ${body.tokens?.accessToken ?? ''}` }
  const st = (await (await request.get(`${API_URL}/api/v1/legal/status`, { headers })).json()) as { required?: boolean; currentVersion?: string }
  if (st.required) await request.post(`${API_URL}/api/v1/legal/accept`, { headers, data: { version: st.currentVersion, lang: 'es' } })
  const reauth = await request.post(`${API_URL}/api/v1/auth/reauth`, { headers, data: { password: PLATFORM.password } })
  expect(reauth.ok(), await reauth.text()).toBeTruthy()
  const token = ((await reauth.json()) as { accessToken: string }).accessToken
  const r = await request.put(`${API_URL}/api/v1/modules/${key}`, { headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() }, data: { isEnabled: true } })
  expect(r.ok(), `módulo ${key}: ${await r.text()}`).toBeTruthy()
}

async function seed(request: APIRequestContext): Promise<Seed> {
  await enableModule(request, 'SALES_ORDERS')
  const headers = await apiHeaders(request)
  const sfx = LETTERS
  const warehouses = await ok<{ publicId: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const warehouseId = warehouses[0].publicId
  const sku = `L238-${sfx}`
  const product = await ok<{ product: { publicId: string } }>(
    request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Producto lote 238 ${sfx}`, trackingType: 'NONE', salePrice: 5, purchaseCost: 2 } }),
    'producto',
  )
  await ok(request.post(`${API_URL}/api/v1/inventory/adjustments`, { headers, data: { productPublicId: product.product.publicId, warehousePublicId: warehouseId, binId: 2, quantity: 40, reason: 'FOUND', notes: 'e2e lote 238' } }), 'existencia')
  const client = `Cliente L238 ${sfx}`
  const c = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: client } }), 'cliente')
  const order = await ok<{ publicId: string; number: string }>(
    request.post(`${API_URL}/api/v1/sales-orders`, { headers, data: { warehousePublicId: warehouseId, clientPublicId: c.publicId, lines: [{ productPublicId: product.product.publicId, quantity: 5 }], deliveryMode: 'PICKUP' } }),
    'orden en borrador',
  )
  await ok(request.post(`${API_URL}/api/v1/sales-orders/${order.publicId}/request`, { headers, data: { allowBackorder: true } }), 'solicitar (reserva)')
  return { sku, client, orderNumber: order.number, orderId: order.publicId }
}

test.beforeAll(async ({ request }) => {
  S = await seed(request)
})

async function preference(request: APIRequestContext, key: string): Promise<string | null> {
  const headers = await apiHeaders(request)
  const r = await ok<{ value?: string | null }>(request.get(`${API_URL}/api/v1/me/preferences/${key}`, { headers }), `preferencia ${key}`)
  return r.value ?? null
}

test('Orden de venta: un botón «Nueva orden», el modo se recuerda y se puede cambiar de uno a otro', async ({ page, request, isMobile }) => {
  // parte del modo «formulario» (lo que dejó una corrida anterior no importa)
  await ok(request.put(`${API_URL}/api/v1/me/preferences/sales.entryMode`, { headers: await apiHeaders(request), data: { value: 'full' } }), 'modo inicial')
  await login(page)
  await page.goto('/sales-orders')
  // un solo botón de crear («Nueva orden») y el del otro modo; ya no existe «Nuevo pedido»
  await expect(page.getByRole('button', { name: 'Nueva orden', exact: true })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByRole('link', { name: 'Nuevo pedido' })).toHaveCount(0)
  await shot(page, isMobile, 'l238-ordenes-botones', 700)

  // «Con asistente» abre el asistente y lo deja como el modo de la persona (en el servidor)
  await page.getByRole('button', { name: 'Con asistente' }).click()
  await expect(page).toHaveURL(/\/sales-orders\/new-order/)
  await expect.poll(() => preference(request, 'sales.entryMode')).toBe('wizard')

  // desde el asistente: «Cambiar al formulario completo» abre la ventana (aún sin borrador guardado)
  await page.getByRole('link', { name: 'Cambiar al formulario completo' }).click()
  await expect(page).toHaveURL(/\/sales-orders\?new=1/)
  const dlg = page.getByRole('dialog')
  await expect(dlg).toContainText('Nueva orden')
  await expect.poll(() => preference(request, 'sales.entryMode')).toBe('full')
  await shot(page, isMobile, 'l238-formulario-cambiar', 1000)

  // desde la ventana con la orden VACÍA (antes no hacía nada): «Cambiar al asistente» abre el asistente
  await dlg.getByRole('button', { name: 'Cambiar al asistente' }).click()
  await expect(page).toHaveURL(/\/sales-orders\/new-order/)
  await expect.poll(() => preference(request, 'sales.entryMode')).toBe('wizard')

  // «Nueva orden» abre ahora el asistente (el último modo); se deja el formulario como modo final para no afectar otros recorridos
  await page.goto('/sales-orders')
  await page.getByRole('button', { name: 'Nueva orden', exact: true }).click()
  await expect(page).toHaveURL(/\/sales-orders\/new-order/)
  await page.goto('/sales-orders')
  await page.getByRole('button', { name: 'Con formulario' }).click()
  await expect(page.getByRole('dialog')).toContainText('Nueva orden')
  await expect.poll(() => preference(request, 'sales.entryMode')).toBe('full')
})

test('Recolección y empaque: la ficha dice qué recolectar y «Recolectar» llena el formulario sin recolectar', async ({ page, isMobile }) => {
  test.skip(isMobile, 'pantalla de escritorio (dos paneles)')
  await login(page)
  await page.goto('/warehouse/pick-batches')
  // el panel de documentos es angosto: sale como tarjetas (o como tabla si cabe); en los dos casos se toca el número
  const row = page.getByText(S.orderNumber, { exact: true }).first()
  await expect(row).toBeVisible({ timeout: 30_000 })
  await row.click()
  const dlg = page.getByRole('dialog')
  await expect(dlg).toContainText('Lo que hay que recolectar')
  await expect(dlg).toContainText(S.sku)
  await expect(dlg.locator('table.lst').first()).toContainText('5')   // pedido 5, por sacar 5
  await shot(page, isMobile, 'l238-ficha-que-recolectar', 900)

  await dlg.getByRole('button', { name: 'Recolectar', exact: true }).click()
  await expect(dlg).toHaveCount(0)
  await expect(page.getByText(`${S.orderNumber}: renglones cargados en el formulario`)).toBeVisible()
  // el formulario quedó con el producto y la cantidad reservada; NO se recolectó nada (la fila sigue sin movimientos)
  await expect(page.getByLabel('Cantidad de la línea 1')).toHaveValue('5')
  await expect(page.getByLabel('Producto de la línea 1')).toHaveValue(new RegExp(S.sku))
  await shot(page, isMobile, 'l238-formulario-precargado', 900)
  await row.click()
  await expect(page.getByRole('dialog')).toContainText('Todavía no tiene recolecciones')
})

test('La última compañía usada es la que abre al volver a entrar', async ({ request }) => {
  const res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  expect(res.ok()).toBeTruthy()
  const first = (await res.json()) as { status?: string; tokens?: { refreshToken?: string; accessToken?: string }; tenants?: { tenantId?: number }[] }
  const headers = await apiHeaders(request)
  const me = await ok<{ tenantId?: number; memberships?: { tenantId?: number }[] }>(request.get(`${API_URL}/api/v1/me`, { headers }), 'me')
  const other = (me.memberships ?? []).map((m) => m.tenantId).find((t) => t !== me.tenantId)
  test.skip(other == null || !first.tokens?.refreshToken, 'el administrador solo tiene una compañía')

  const sw = await request.post(`${API_URL}/api/v1/auth/switch-tenant`, { data: { refreshToken: first.tokens!.refreshToken, tenantId: other } })
  expect(sw.ok(), await sw.text()).toBeTruthy()
  const again = (await (await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })).json()) as { tokens?: { accessToken?: string } }
  const me2 = await ok<{ tenantId?: number }>(request.get(`${API_URL}/api/v1/me`, { headers: { Authorization: `Bearer ${again.tokens?.accessToken ?? ''}` } }), 'me 2')
  expect(me2.tenantId).toBe(other)

  // se vuelve a la compañía de antes para no afectar otros recorridos
  const back = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: ADMIN.email, password: ADMIN.password } })
  const b = (await back.json()) as { tokens?: { refreshToken?: string } }
  await request.post(`${API_URL}/api/v1/auth/switch-tenant`, { data: { refreshToken: b.tokens?.refreshToken, tenantId: me.tenantId } })
})
