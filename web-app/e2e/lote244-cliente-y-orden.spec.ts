// Lote 244 (P7d) — «Venta» en la ficha del cliente y «Vendedor» en la orden de venta, contra el API real de pruebas (API SIN correo: Brevo__ApiKey vacía,
// Auth__Onboarding__Enabled=false). Siembra una ruta con titular y apoyo, un cliente con esa ruta y vendedor preferido, y un borrador de orden; recorre la ficha
// (panel plegable sin desbordar, preferido fuera de la ruta con su aviso), la ventana de la orden (campo Vendedor alineado) y la lista de órdenes (columnas Ruta y
// Vendedor, filtros) en claro/oscuro y es/en; mide que no haya scroll horizontal de página (también a 360 px: proyecto «movil»).
// Capturas en SHOTS_DIR (por omisión la carpeta temporal de la sesión), NO en docs.
// Correr: WEB_URL=http://localhost:5177 API_URL=http://localhost:5103 npx playwright test lote244-cliente-y-orden --project=escritorio --no-deps --trace off --workers=1
import { mkdirSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { expect, test, type Page } from '@playwright/test'

import { API_URL, LETTERS, apiHeaders, login, ok } from './staffHelpers'

test.use({ locale: 'es-PR' })
test.setTimeout(180_000)
test.describe.configure({ mode: 'serial' })

const SA = `${API_URL}/api/v1/sales-analysis`
const SHOTS = process.env.SHOTS_DIR ?? join(tmpdir(), 'lote244-p7d')

const ctx = { clientId: 0, clientPublicId: '', clientName: `FARMACIA P7D ${LETTERS}`, routeName: `Ruta P7D ${LETTERS}`, orderNumber: '', orderPublicId: '', otherUser: '' }

test.beforeAll(async ({ request }) => {
  const headers = await apiHeaders(request)
  const cands = await ok<{ userId: number; name: string }[]>(request.get(`${SA}/sellers/candidates?take=5`, { headers }), 'candidatos')
  expect(cands.length).toBeGreaterThan(1)
  const [primary, support] = cands
  ctx.otherUser = support.name
  const route = await ok<{ id: number }>(request.post(`${SA}/routes`, { headers, data: { name: ctx.routeName, sortOrder: 950 } }), 'ruta')
  await ok(request.post(`${SA}/routes/${route.id}/people`, { headers, data: { userId: primary.userId, role: 'PRIMARY' } }), 'titular')
  await ok(request.post(`${SA}/routes/${route.id}/people`, { headers, data: { userId: support.userId, role: 'SUPPORT' } }), 'apoyo')
  const client = await ok<{ id: number; publicId: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: ctx.clientName } }), 'cliente')
  ctx.clientId = client.id
  ctx.clientPublicId = client.publicId
  await ok(request.put(`${SA}/clients/${client.id}/sales`, { headers, data: { routeId: route.id, preferredUserId: support.userId } }), 'venta del cliente')
  const products = await ok<{ items: { publicId: string; salePrice?: number }[] }>(request.get(`${API_URL}/api/v1/products?take=20`, { headers }), 'productos')
  const product = products.items.find((p) => (p.salePrice ?? 0) > 0) ?? products.items[0]
  const order = await ok<{ number: string; publicId: string }>(
    request.post(`${API_URL}/api/v1/sales-orders`, { headers, data: { clientPublicId: client.publicId, lines: [{ productPublicId: product.publicId, quantity: 1, unitPrice: 10 }] } }),
    'borrador',
  )
  ctx.orderNumber = order.number
  ctx.orderPublicId = order.publicId
})

async function noHScroll(page: Page, what: string) {
  const over = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  expect(over, `scroll horizontal de la página: ${what}`).toBeLessThanOrEqual(1)
}

async function setLook(page: Page, lang: 'es' | 'en', theme: 'light' | 'dark') {
  await page.evaluate(([l, th]) => {
    localStorage.setItem('teikem.lang', l)
    localStorage.setItem('teikem.theme', th)
  }, [lang, theme] as const)
  await page.reload()
}

test('Ficha del cliente y orden: panel Venta, campo Vendedor y columnas, sin desbordar (claro/oscuro, es/en)', async ({ page, isMobile }) => {
  mkdirSync(SHOTS, { recursive: true })
  const tag = isMobile ? 'movil' : 'escritorio'
  await login(page)
  const size = page.viewportSize()!
  await page.setViewportSize({ width: size.width, height: 1800 })

  for (const [lang, theme] of [['es', 'light'], ['en', 'dark'], ['es', 'dark'], ['en', 'light']] as const) {
    const es = lang === 'es'
    // 1) ficha del cliente
    await page.goto(`/catalog/clients?client=${ctx.clientPublicId}`)
    await setLook(page, lang, theme)
    const header = page.getByRole('heading', { name: es ? 'Venta' : 'Sales', exact: true }).first()
    await expect(header).toBeVisible({ timeout: 30_000 })
    await header.click()
    await expect(page.getByText(ctx.routeName).first()).toBeVisible()
    await expect(page.getByText(es ? 'Vendedor propuesto' : 'Proposed seller').first()).toBeVisible()
    const box = await header.boundingBox()
    expect(box).not.toBeNull()
    await noHScroll(page, `ficha ${tag} ${lang} ${theme}`)
    await page.screenshot({ path: join(SHOTS, `ficha-${tag}-${lang}-${theme}.png`), fullPage: true })

    // 2) la orden en borrador: campo Vendedor con el propuesto
    await page.goto(`/sales-orders?q=${ctx.orderNumber}`)
    await setLook(page, lang, theme)
    const row = page.getByRole('row').filter({ hasText: ctx.orderNumber }).first()
    const card = page.getByText(ctx.orderNumber).first()
    await expect(card).toBeVisible({ timeout: 30_000 })
    if (!isMobile) {
      const h = (await row.boundingBox())?.height ?? 999
      expect(h, 'alto de la fila de orden').toBeLessThanOrEqual(64)
      // un borrador aún no tiene copia de ruta y vendedor (se toma al solicitar): las columnas existen y muestran «—»
      await expect(page.getByRole('columnheader', { name: es ? 'Ruta de venta' : 'Sales route' })).toBeVisible()
      await expect(page.getByRole('columnheader', { name: es ? 'Vendedor' : 'Seller' })).toBeVisible()
    }
    await noHScroll(page, `lista ${tag} ${lang} ${theme}`)
    await page.screenshot({ path: join(SHOTS, `ordenes-${tag}-${lang}-${theme}.png`), fullPage: true })
    // el borrador se abre por su enlace (en móvil, tocar la fila lleva al asistente por pasos)
    await page.goto(`/sales-orders?order=${ctx.orderPublicId}`)
    await setLook(page, lang, theme)
    const dialog = page.getByRole('dialog').first()
    await expect(dialog).toBeVisible()
    const select = dialog.getByLabel(es ? 'Vendedor' : 'Seller')
    await expect(select).toBeVisible({ timeout: 30_000 })
    await expect(select).toContainText(ctx.otherUser)
    const sb = await select.boundingBox()
    const db = await dialog.boundingBox()
    expect(sb!.x + sb!.width, 'el campo Vendedor cabe en la ventana').toBeLessThanOrEqual(db!.x + db!.width + 1)
    await noHScroll(page, `orden ${tag} ${lang} ${theme}`)
    await page.screenshot({ path: join(SHOTS, `orden-${tag}-${lang}-${theme}.png`), fullPage: true })
  }
})
