// Escena 4 — Venta con el asistente: kit + producto, firma al final (ENSAYO). Parte del estado E03.
import { test } from '@playwright/test'
import { clicEn, escribir, nuevoReloj, paso, finEscena, prepararCursor, señalar, tokensFueraDeCamara, ponerSesion, expect, API_URL } from '../ayudas'
import { ADMIN } from '../../staffHelpers'

const ZOOM = Number(process.env.VIDEO_ZOOM ?? '1.25')
test.use({ launchOptions: { args: [`--force-device-scale-factor=${ZOOM}`] } })

test('E04 - venta con asistente', async ({ browser, baseURL, request }) => {
  const tokens = await tokensFueraDeCamara(2)
  // fuera de cámara: se cancelan las órdenes abiertas que hubiera de antes (liberan lo reservado)
  const h = { Authorization: `Bearer ${String(tokens.accessToken)}` }
  const lista = await request.get(`${API_URL}/api/v1/sales-orders?take=50`, { headers: h })
  if (lista.ok()) {
    const body = (await lista.json()) as { items?: { publicId?: string; statusCode?: string; orderNumber?: string }[] } | { publicId?: string; statusCode?: string }[]
    const items = Array.isArray(body) ? body : (body.items ?? [])
    for (const o of items) if (o.publicId && !/CANCEL|DRAFT/i.test(String(o.statusCode ?? ''))) await request.post(`${API_URL}/api/v1/sales-orders/${o.publicId}/cancel`, { headers: h, data: { reason: 'Orden de prueba anterior' } })
  }
  const ctx = await browser.newContext({
    baseURL: baseURL!, locale: 'es-PR', deviceScaleFactor: ZOOM,
    viewport: { width: Math.round(1920 / ZOOM), height: Math.round(1080 / ZOOM) },
    recordVideo: { dir: process.env.VIDEO_OUT ?? 'F:/Download/TeikemVideo/solutions-proceso/tomas/_pw', size: { width: 1920, height: 1080 } },
  })
  const tPagina = Date.now()
  const page = await ctx.newPage()
  await ponerSesion(page, tokens)
  await prepararCursor(page)
  const reloj = nuevoReloj('E04-venta', tPagina)
  const shot = (n: string) => page.screenshot({ path: `F:/Download/TeikemVideo/solutions-proceso/cuadros/E04-${n}.png` })

  await page.goto('/')
  await paso(page, reloj, 'Ventas y asistente', { l: 1 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Contabilidad' }))
    await clicEn(page, page.getByRole('link', { name: 'Ventas', exact: true }))
    await expect(page.getByRole('heading', { name: 'Órdenes de venta', level: 1 })).toBeVisible()
  })
  await paso(page, reloj, 'Abrir el asistente', { l: 2 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Nueva orden', exact: true }))
    await expect(page.getByText(/Paso 1 de 4/)).toBeVisible()
  })
  await paso(page, reloj, 'Cliente', { l: 3 }, async () => {
    const q = page.getByLabel('Buscar cliente')
    await escribir(q, 'ESPERANZA')
    await clicEn(page, page.getByRole('list', { name: 'Clientes encontrados' }).getByRole('button', { name: /ESPERANZA/ }))
    await expect(page.getByLabel('Forma de pago')).toHaveValue(/.+/)
  })
  await paso(page, reloj, 'Forma de pago', { l: 4 }, async () => {
    await señalar(page, page.getByLabel('Forma de pago'), 2500)
  })
  await shot('cliente')
  await paso(page, reloj, 'Siguiente a productos', { l: 5 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Siguiente' }))
    await expect(page.getByText(/Paso 2 de 4/)).toBeVisible()
  })
  await paso(page, reloj, 'Kit', { l: 6 }, async () => {
    const tab = page.getByRole('tab', { name: /PRODIGY|Sin marca/ }).first()
    await clicEn(page, tab)
    const kit = page.locator('li.wz-offer').first()
    await expect(kit).toBeVisible()
    await señalar(page, kit, 3500)
  })
  await paso(page, reloj, 'Agregar oferta', { l: 8 }, async () => {
    await clicEn(page, page.locator('li.wz-offer').first().getByRole('button', { name: /^Agregar uno de / }))
  })
  await shot('kit')
  await paso(page, reloj, 'Guantes', { l: 9 }, async () => {
    await clicEn(page, page.getByRole('tab', { name: 'PADIMONT' }))
    const q = page.getByLabel('Cantidad de NECH-1002')
    await clicEn(page, q); await escribir(q, '10')
  })
  await paso(page, reloj, 'Carrito', { l: 10 }, async () => {
    await clicEn(page, page.locator('.wz-cart-toggle'))
    await señalar(page, page.getByRole('region', { name: 'Carrito' }), 4000)
  })
  await shot('carrito')
  await paso(page, reloj, 'Siguiente a entrega', { l: 11 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Siguiente' }))
    await expect(page.getByText(/Paso 3 de 4/)).toBeVisible()
  })
  await paso(page, reloj, 'Recogido y resumen', { l: 12 }, async () => {
    await clicEn(page, page.getByRole('radio', { name: /^Recogido/ }))
    await señalar(page, page.locator('.wz-summary'), 4000)
  })
  await shot('entrega')
  await paso(page, reloj, 'Siguiente a autorización', { l: 13 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Siguiente' }))
    await expect(page.getByText(/Paso 4 de 4/)).toBeVisible()
  })
  await paso(page, reloj, 'Nombre y firma', { l: 14 }, async () => {
    const n = page.getByLabel('Nombre de quien autoriza')
    await clicEn(page, n); await escribir(n, 'María Rivera')
    const pad = page.locator('svg.sigpad-box')
    await pad.scrollIntoViewIfNeeded(); await page.waitForTimeout(400)
    const b = (await pad.boundingBox())!
    await page.mouse.move(b.x + b.width * 0.2, b.y + b.height * 0.6, { steps: 15 })
    await page.mouse.down()
    const pts: [number, number][] = [[.25, .3], [.32, .75], [.4, .35], [.5, .7], [.6, .3], [.72, .65], [.82, .4]]
    for (const [fx, fy] of pts) await page.mouse.move(b.x + b.width * fx, b.y + b.height * fy, { steps: 10 })
    await page.mouse.up()
    await expect(pad).toHaveAttribute('aria-label', /firmado/)
  })
  await shot('firma')
  await paso(page, reloj, 'Enviar con contraseña', { l: 15 }, async () => {
    await clicEn(page, page.getByRole('button', { name: 'Enviar pedido' }))
    const d = page.getByRole('dialog')
    const pwd = d.getByLabel('Contraseña', { exact: true })
    await expect(pwd).toBeVisible()
    await clicEn(page, pwd); await escribir(pwd, ADMIN.password)
    await clicEn(page, d.getByRole('button', { name: 'Enviar pedido' }))
    await expect(page.getByRole('heading', { name: 'Pedido enviado' })).toBeVisible({ timeout: 30_000 })
  })
  await shot('enviado')
  await paso(page, reloj, 'Ver la orden', { l: 17 }, async () => {
    await clicEn(page, page.getByRole('link', { name: 'Ver en Órdenes de venta' }))
    await expect(page.getByRole('dialog').locator('.chip', { hasText: 'Solicitada' })).toBeVisible()
    await señalar(page, page.getByRole('dialog').locator('table').first(), 3500)
  })
  await shot('orden')
  await finEscena(page, reloj, 'E04-venta')
  await ctx.close()
})
