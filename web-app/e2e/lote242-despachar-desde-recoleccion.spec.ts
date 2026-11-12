// Lote 242 — «Despachar» en la ficha de Recolección y empaque, contra el API real (SIN correo: Brevo__ApiKey vacía). Una orden de venta solicitada (5 unidades reservadas, retiro) se abre
// desde Almacén → Recolección y empaque, se despacha con el MISMO despacho de Ventas, se elige «Retiro» y la orden queda despachada con el inventario fuera del almacén.
// Correr (web en :5174 apuntando al API de pruebas): WEB_URL=http://localhost:5174 API_URL=http://localhost:5733 npx playwright test lote242 --project=escritorio --no-deps --trace off --workers=1
import { expect, test, type APIRequestContext } from '@playwright/test'

import { ADMIN, API_URL, LETTERS, apiHeaders, login, ok, shot } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

interface Seed {
  sku: string
  orderNumber: string
  orderId: string
  productId: string
  warehouseId: string
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

async function onHand(request: APIRequestContext, productId: string): Promise<number> {
  const headers = await apiHeaders(request)
  const p = await ok<{ product?: { qtyOnHand?: number }; qtyOnHand?: number }>(request.get(`${API_URL}/api/v1/products/${productId}`, { headers }), 'producto')
  return p.product?.qtyOnHand ?? p.qtyOnHand ?? 0
}

test.beforeAll(async ({ request }) => {
  await enableModule(request, 'SALES_ORDERS')
  const headers = await apiHeaders(request)
  const warehouses = await ok<{ publicId: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const warehouseId = warehouses[0].publicId
  const sku = `L242-${LETTERS}`
  const product = await ok<{ product: { publicId: string } }>(
    request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name: `Producto lote 242 ${LETTERS}`, trackingType: 'NONE', salePrice: 5, purchaseCost: 2 } }),
    'producto',
  )
  await ok(request.post(`${API_URL}/api/v1/inventory/adjustments`, { headers, data: { productPublicId: product.product.publicId, warehousePublicId: warehouseId, binId: 2, quantity: 40, reason: 'FOUND', notes: 'e2e lote 242' } }), 'existencia')
  const c = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Cliente L242 ${LETTERS}` } }), 'cliente')
  const order = await ok<{ publicId: string; number: string }>(
    request.post(`${API_URL}/api/v1/sales-orders`, { headers, data: { warehousePublicId: warehouseId, clientPublicId: c.publicId, lines: [{ productPublicId: product.product.publicId, quantity: 5 }], deliveryMode: 'PICKUP' } }),
    'orden en borrador',
  )
  await ok(request.post(`${API_URL}/api/v1/sales-orders/${order.publicId}/request`, { headers, data: { allowBackorder: true } }), 'solicitar (reserva)')
  S = { sku, orderNumber: order.number, orderId: order.publicId, productId: product.product.publicId, warehouseId }
})

test('Recolección y empaque: se despacha la orden desde su ficha y la orden queda despachada', async ({ page, request, isMobile }) => {
  test.skip(isMobile, 'pantalla de escritorio (dos paneles)')
  const before = await onHand(request, S.productId)
  expect(before).toBe(40)

  await login(page)
  await page.goto('/warehouse/pick-batches')
  const row = page.getByText(S.orderNumber, { exact: true }).first()
  await expect(row).toBeVisible({ timeout: 30_000 })
  await row.click()

  // la ficha: lo que hay que recolectar y, abajo, «Despachar» (principal) junto a «Recolectar» (formulario manual) con la nota que los distingue
  const ficha = page.getByRole('dialog')
  await expect(ficha).toContainText('Lo que hay que recolectar')
  const despachar = ficha.getByRole('button', { name: 'Despachar', exact: true })
  await expect(despachar).toBeVisible()
  await expect(ficha.getByRole('button', { name: 'Recolectar', exact: true })).toBeVisible()
  await expect(ficha).toContainText('«Despachar» saca esto contra la orden')
  // el botón principal es «Despachar» (el naranja); el pie no se sale de la ventana
  const pie = (await despachar.boundingBox())!
  const vp = page.viewportSize()!
  expect(pie.y + pie.height).toBeLessThanOrEqual(vp.height)
  await shot(page, isMobile, 'l242-ficha-despachar', 900)

  // el despacho de Ventas, con lo reservado por línea (5), y el inventario TODAVÍA no ha bajado
  await despachar.click()
  const dlg = page.getByRole('dialog', { name: new RegExp(`Despachar la orden ${S.orderNumber}`) })
  await expect(dlg).toBeVisible({ timeout: 20_000 })
  await expect(dlg.getByLabel(`Cantidad a despachar de ${S.sku}`)).toHaveValue('5')
  expect(await onHand(request, S.productId)).toBe(40)
  await shot(page, isMobile, 'l242-despachar-orden', 900)

  // despachar: sale el inventario y aparece la pregunta de después; «Retiro» termina
  await dlg.getByRole('button', { name: 'Despachar', exact: true }).click()
  const next = page.getByRole('dialog', { name: /Despacho EMP-/ })
  await expect(next).toBeVisible({ timeout: 20_000 })
  await expect(next).toContainText('¿Qué sigue con lo despachado?')
  await shot(page, isMobile, 'l242-que-sigue', 700)
  await next.getByRole('button', { name: /Retiro: el cliente lo recoge/ }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)

  // la orden quedó despachada, con 5 sacadas, y el inventario bajó de 40 a 35
  const order = await ok<{ status?: string; lines?: { qtyShipped?: number; qtyReserved?: number }[] }>(request.get(`${API_URL}/api/v1/sales-orders/${S.orderId}`, { headers: await apiHeaders(request) }), 'orden')
  expect(order.status).toBe('SHIPPED')
  expect(order.lines?.[0]?.qtyShipped).toBe(5)
  expect(order.lines?.[0]?.qtyReserved).toBe(0)
  expect(await onHand(request, S.productId)).toBe(35)

  // y la lista de documentos lo refleja sin recargar: «Sacado 5 de 5»
  await expect(page.getByText('Sacado 5 de 5').first()).toBeVisible({ timeout: 20_000 })
  await shot(page, isMobile, 'l242-lista-despues', 900)

  // la ficha de la orden despachada ya no ofrece «Despachar» (no queda nada reservado) y muestra su recolección
  await page.getByText(S.orderNumber, { exact: true }).first().click()
  const despues = page.getByRole('dialog')
  await expect(despues).toContainText('Recolecciones (1)')
  await expect(despues.getByRole('button', { name: 'Despachar', exact: true })).toHaveCount(0)
  await shot(page, isMobile, 'l242-ficha-despues', 800)
})
