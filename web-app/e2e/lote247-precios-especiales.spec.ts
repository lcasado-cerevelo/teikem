// Lote 247 (Lote C) — Precios especiales y ofertas por cliente y por cadena, contra el API real de pruebas (SIN correo: Brevo__ApiKey vacía, Auth__Onboarding__Enabled=false).
// Prepara por API una cadena con dos clientes, tres productos, una lista general, precios especiales de la cadena y de un cliente, una oferta para la cadena y una orden en
// borrador (los precios salen del motor: cadena, lista general, oferta y uno manual). Recorre la ficha de la cadena, la del cliente, «Precios y ofertas» (lista dedicada,
// editor de oferta con «Incluir» y «Excluir») y la orden, midiendo: sin scroll horizontal, filas ≤ 56 px en escritorio y paneles que no se desbordan.
// 1440 px es/claro y en/oscuro; 390 px es/oscuro y en/claro (deviceScaleFactor 2). Capturas en SHOTS_DIR (por omisión docs/manual/frontend/img/l247-*.png).
// Correr: WEB_URL=http://localhost:5175 API_URL=http://localhost:5101 npx playwright test lote247-precios-especiales --project=escritorio --no-deps --trace off --workers=1
import { mkdirSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test, type Page } from '@playwright/test'

import { API_URL, apiHeaders, LETTERS, login, ok, tenantDay } from './staffHelpers'

test.use({ locale: 'es-PR', deviceScaleFactor: 2 })
test.setTimeout(240_000)
test.describe.configure({ mode: 'serial' })

const SHOTS = process.env.SHOTS_DIR ?? fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))
const CHAIN = 'Walgreens SPX'
const CLIENT_1 = 'FARMACIA SPX UNO'
const CLIENT_2 = 'FARMACIA SPX DOS'
const SKUS = [
  { sku: 'SPX-A', name: 'Tirillas de glucosa SPX', price: 12 },
  { sku: 'SPX-B', name: 'Lancetas SPX', price: 15 },
  { sku: 'SPX-C', name: 'Monitor SPX', price: 20 },
]

const ids = { chain: '', client1: '', client2: '', order: '', orderNumber: '', offer: '' }

test.beforeAll(async ({ request }) => {
  const headers = await apiHeaders(request)
  const today = tenantDay(0)
  const from = tenantDay(-30)
  // productos propios
  const prod: Record<string, string> = {}
  for (const p of SKUS) {
    const found = (await ok<{ items: { publicId: string; sku: string }[] }>(request.get(`${API_URL}/api/v1/products?search=${p.sku}`, { headers }), 'productos')).items.find((x) => x.sku === p.sku)
    prod[p.sku] = found?.publicId ?? (await ok<{ product: { publicId: string } }>(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku: p.sku, name: p.name, trackingType: 'NONE', salePrice: p.price, purchaseCost: 1 } }), p.sku)).product.publicId
  }
  // cadena y clientes
  const chains = await ok<{ publicId: string; name: string; isActive: boolean }[]>(request.get(`${API_URL}/api/v1/client-chains?includeInactive=true`, { headers }), 'cadenas')
  // una corrida anterior con otros nombres: se renombran por API (la cadena lleva rowVersion)
  const oldChain = chains.find((c) => c.name === 'Cadena SPX' && c.isActive)
  if (oldChain && !chains.some((c) => c.name === CHAIN)) {
    const detail = await ok<{ rowVersion: string }>(request.get(`${API_URL}/api/v1/client-chains/${oldChain.publicId}`, { headers }), 'cadena anterior')
    await ok(request.patch(`${API_URL}/api/v1/client-chains/${oldChain.publicId}`, { headers, data: { name: CHAIN, rowVersion: detail.rowVersion } }), 'renombrar cadena')
    chains.splice(0, chains.length, ...(await ok<typeof chains>(request.get(`${API_URL}/api/v1/client-chains?includeInactive=true`, { headers }), 'cadenas')))
  }
  ids.chain = chains.find((c) => c.name === CHAIN && c.isActive)?.publicId ?? (await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/client-chains`, { headers, data: { name: CHAIN, notes: 'Cadena de prueba de precios especiales' } }), 'cadena')).publicId
  const client = async (name: string) => {
    const find = async () => (await ok<{ publicId: string; name: string }[]>(request.get(`${API_URL}/api/v1/clients?search=${encodeURIComponent(name)}`, { headers }), 'clientes')).find((c) => c.name === name)
    let c = await find()
    if (!c) {
      await ok(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name } }), name)
      c = await find()
    }
    return c!.publicId
  }
  ids.client1 = await client(CLIENT_1)
  ids.client2 = await client(CLIENT_2)
  await ok(request.post(`${API_URL}/api/v1/client-chains/${ids.chain}/clients`, { headers, data: { clientPublicIds: [ids.client1, ids.client2] } }), 'asignar')
  // lista general (A=10, B=14, C=18) publicada, una sola vez
  const lists = await ok<{ publicId: string; name: string }[]>(request.get(`${API_URL}/api/v1/price-lists?includeInactive=true`, { headers }), 'listas')
  if (!lists.some((l) => l.name === 'Lista general SPX')) {
    const list = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/price-lists`, { headers, data: { name: 'Lista general SPX', validFrom: from, appliesToAll: true } }), 'lista')
    await ok(request.post(`${API_URL}/api/v1/price-lists/${list.publicId}/items`, { headers, data: { items: [{ productPublicId: prod['SPX-A'], price: 10 }, { productPublicId: prod['SPX-B'], price: 14 }, { productPublicId: prod['SPX-C'], price: 18 }] } }), 'renglones')
    await ok(request.post(`${API_URL}/api/v1/price-lists/${list.publicId}/publish`, { headers }), 'publicar')
  }
  // precios especiales: cadena (A=8, B=15 más caro que la general, C=17) y del cliente 2 (A=7.5); se vuelven a poner si ya existen (cambia el precio)
  const special = async (kind: 'chains' | 'clients', owner: string, sku: string, price: number, validTo?: string) =>
    ok(request.post(`${API_URL}/api/v1/special-prices/${kind}/${owner}`, { headers, data: { productPublicId: prod[sku], price, validFrom: from, validTo: validTo ?? null } }), `precio especial ${sku}`)
  await special('chains', ids.chain, 'SPX-A', 8)
  await special('chains', ids.chain, 'SPX-B', 15)
  await special('chains', ids.chain, 'SPX-C', 17, tenantDay(5))
  await special('clients', ids.client2, 'SPX-A', 7.5)
  // oferta para la cadena (C a 16)
  const offers = await ok<{ publicId: string; name: string }[]>(request.get(`${API_URL}/api/v1/offers`, { headers }), 'ofertas')
  const old = offers.find((o) => o.name === 'Oferta SPX')
  if (old && !offers.some((o) => o.name === 'Verano SPX')) {
    await ok(request.patch(`${API_URL}/api/v1/offers/${old.publicId}`, { headers, data: { name: 'Verano SPX' } }), 'renombrar oferta')
    old.name = 'Verano SPX'
  }
  const existing = offers.find((o) => o.name === 'Verano SPX')
  if (existing) ids.offer = existing.publicId
  else {
    const offer = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/offers`, { headers, data: { name: 'Verano SPX', validFrom: from, audienceMode: 'INCLUDE' } }), 'oferta')
    ids.offer = offer.publicId
    await ok(request.post(`${API_URL}/api/v1/offers/${ids.offer}/items`, { headers, data: { items: [{ productPublicId: prod['SPX-C'], price: 16 }] } }), 'renglón de oferta')
    await ok(request.post(`${API_URL}/api/v1/offers/${ids.offer}/chains`, { headers, data: { chainPublicIds: [ids.chain] } }), 'cadena de la oferta')
  }
  // base limpia: sin tasa de impuesto por omisión no se puede crear el borrador
  const tax = await ok<{ defaultRateCode?: string | null }>(request.get(`${API_URL}/api/v1/tax/settings`, { headers }), 'impuestos')
  if (!tax.defaultRateCode) {
    await ok(request.post(`${API_URL}/api/v1/tax/rates`, { headers, data: { code: `IVU247${LETTERS}`, name: 'IVU', effectiveFrom: '2020-01-01', components: [{ code: 'STATE', name: 'Estatal', percent: 10.5 }, { code: 'CITY', name: 'Municipal', percent: 1 }] } }), 'tasa')
    await ok(request.patch(`${API_URL}/api/v1/tax/settings`, { headers, data: { defaultRateCode: `IVU247${LETTERS}` } }), 'tasa por omisión')
  }
  // orden en borrador del cliente 1: A por cadena, B por lista general (el especial es más caro), C por oferta, y una línea con precio manual
  const warehouses = await ok<{ publicId: string; code: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const order = await ok<{ publicId: string; number: string }>(
    request.post(`${API_URL}/api/v1/sales-orders`, {
      headers,
      data: {
        clientPublicId: ids.client1,
        warehousePublicId: warehouses[0]?.publicId,
        requiredDate: today,
        lines: [{ productPublicId: prod['SPX-A'], quantity: 2 }, { productPublicId: prod['SPX-B'], quantity: 1 }, { productPublicId: prod['SPX-C'], quantity: 1 }],
      },
    }),
    'orden',
  )
  ids.order = order.publicId
  ids.orderNumber = order.number
})

async function noHScroll(page: Page, what: string) {
  const over = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
  expect(over, `scroll horizontal de la página: ${what}`).toBeLessThanOrEqual(1)
}

async function setLook(page: Page, lang: 'es' | 'en', theme: 'light' | 'dark') {
  await page.evaluate(([l, th]) => {
    localStorage.setItem('teikem.lang', l)
    localStorage.setItem('teikem.theme', th)
    // los paneles recuerdan abierto/cerrado y el interruptor de listas dedicadas: el recorrido parte de lo de fábrica
    Object.keys(localStorage).filter((k) => k.startsWith('teikem.panel.special-prices') || k.startsWith('teikem.panel.chain.offers')).forEach((k) => localStorage.removeItem(k))
    localStorage.removeItem('teikem.priceLists.includeSpecial')
  }, [lang, theme] as const)
  await page.reload()
}

/** Todas las filas de la tabla miden ≤ 56 px (solo escritorio, donde la tabla es tabla). */
async function rowsAtMost(page: Page, name: string, max = 56) {
  // en un panel angosto la tabla pasa a tarjetas (no hay filas que medir)
  if ((await page.getByRole('table', { name }).count()) === 0) return
  const rows = page.getByRole('table', { name }).locator('tbody tr')
  const n = await rows.count()
  expect(n, `filas de «${name}»`).toBeGreaterThan(0)
  for (let i = 0; i < n; i++) expect((await rows.nth(i).boundingBox())!.height, `alto de la fila ${i} de «${name}»`).toBeLessThanOrEqual(max)
}

async function noOverflow(page: Page, selector: string, what: string) {
  const over = await page.locator(selector).first().evaluate((el) => el.scrollWidth - el.clientWidth)
  expect(over, `${what} no se desborda`).toBeLessThanOrEqual(1)
}

const LOOKS = [
  { lang: 'es', theme: 'light', width: 1440, height: 2600, tag: 'escritorio' },
  { lang: 'en', theme: 'dark', width: 1440, height: 2600, tag: 'escritorio' },
  { lang: 'es', theme: 'dark', width: 390, height: 3600, tag: 'movil' },
  { lang: 'en', theme: 'light', width: 390, height: 3600, tag: 'movil' },
] as const

for (const look of LOOKS) {
  test(`Precios especiales ${look.tag} ${look.lang}/${look.theme}`, async ({ page, isMobile }) => {
    test.skip(isMobile, 'el recorrido fija sus propios anchos (1440 y 390)')
    mkdirSync(SHOTS, { recursive: true })
    const { lang, theme, tag } = look
    const L = lang === 'es'
    const desktop = look.width >= 720
    const shot = (name: string) => page.screenshot({ path: join(SHOTS, `l247-${name}-${tag}-${lang}-${theme}.png`), fullPage: true })
    await login(page)
    await page.setViewportSize({ width: look.width, height: look.height })

    // ---- ficha de la cadena: Precios especiales y Ofertas
    await page.goto(`/catalog/chains?id=${ids.chain}`)
    await setLook(page, lang, theme)
    await expect(page.getByText('SPX-A').first()).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText(L ? 'Más caro que la lista general' : 'Higher than the general list').first()).toBeVisible()
    await expect(page.getByText('Verano SPX').first()).toBeVisible()
    if (desktop) await rowsAtMost(page, L ? 'Precios especiales' : 'Special prices')
    await noOverflow(page, '.chn-stack', 'la ficha de la cadena')
    await noHScroll(page, `cadena ${tag} ${lang} ${theme}`)
    await shot('cadena')

    // ---- ficha del cliente 2: su precio propio gana; el de la cadena en solo lectura
    await page.goto(`/catalog/clients?client=${ids.client2}`)
    await setLook(page, lang, theme)
    // la ficha arranca con los paneles plegados: se abre el de precios especiales (el ayudante openClientPanels solo conoce los títulos en español)
    await page.getByRole('heading', { name: L ? 'Precios especiales' : 'Special prices', exact: true }).click({ timeout: 30_000 })
    const own = page.getByText('SPX-A').first()
    await expect(own).toBeVisible({ timeout: 30_000 })
    await expect(page.getByRole('region', { name: L ? `De su cadena «${CHAIN}»` : `From its chain "${CHAIN}"` })).toBeVisible()
    if (desktop) await rowsAtMost(page, L ? 'Precios especiales' : 'Special prices')
    await noHScroll(page, `cliente ${tag} ${lang} ${theme}`)
    await own.scrollIntoViewIfNeeded()
    await shot('cliente')

    // ---- Precios y ofertas: listas dedicadas con el interruptor, y el editor de la oferta con «Incluir» y «Excluir»
    await page.goto('/money/prices')
    await setLook(page, lang, theme)
    await expect(page.getByText('Lista general SPX').first()).toBeVisible()
    await expect(page.getByText(L ? 'Precio especial de' : 'Special price of')).toHaveCount(0)
    await page.getByRole('switch', { name: L ? 'Ver también los precios especiales' : 'Also show special prices' }).check({ force: true })
    await expect(page.getByText(L ? `Precio especial de ${CHAIN}` : `Special price of ${CHAIN}`).first()).toBeVisible({ timeout: 30_000 })
    if (desktop) await rowsAtMost(page, L ? 'Listas de precios' : 'Price lists')
    await noHScroll(page, `listas ${tag} ${lang} ${theme}`)
    await shot('listas')

    // lista dedicada: nombre bloqueado y nota con enlace
    await page.getByText(L ? `Precio especial de ${CHAIN}` : `Special price of ${CHAIN}`).first().click()
    const listDialog = page.getByRole('dialog').first()
    await expect(listDialog.getByRole('link', { name: L ? 'Abrir la ficha' : 'Open the page' })).toBeVisible({ timeout: 30_000 })
    await expect(listDialog.getByLabel(L ? 'Nombre' : 'Name')).toBeDisabled()
    await noHScroll(page, `lista dedicada ${tag} ${lang} ${theme}`)
    await page.screenshot({ path: join(SHOTS, `l247-lista-dedicada-${tag}-${lang}-${theme}.png`) })
    await listDialog.getByRole('button', { name: L ? 'Cerrar' : 'Close' }).first().click()

    // oferta: «Incluir» (con la cadena) y «Excluir» deshabilitada
    await page.getByRole('article', { name: 'Verano SPX' }).getByRole('button').click()
    const offerDialog = page.getByRole('dialog', { name: 'Verano SPX' })
    const include = offerDialog.getByRole('region', { name: L ? 'Incluir' : 'Include' })
    await expect(include).toBeVisible({ timeout: 30_000 })
    await expect(include.getByText(L ? `Cadena ${CHAIN}` : `Chain ${CHAIN}`)).toBeVisible()
    await expect(offerDialog.getByRole('region', { name: L ? 'Excluir' : 'Exclude' }).getByText(L ? 'Esta oferta es solo para los escogidos y no se queda sin ellos: para que sea para todos menos algunos, desactívela y cree otra.' : 'This offer is only for the chosen ones and cannot be left without them: to make it for everyone except some, deactivate it and create another one.')).toBeVisible()
    await noOverflow(page, '.pal', 'el editor de la oferta')
    await noHScroll(page, `oferta ${tag} ${lang} ${theme}`)
    await page.screenshot({ path: join(SHOTS, `l247-oferta-${tag}-${lang}-${theme}.png`) })
    await offerDialog.getByRole('button', { name: L ? 'Cerrar' : 'Close' }).first().click()

    // ---- la orden en borrador: el origen guardado de cada precio
    await page.goto(`/sales-orders?q=${ids.orderNumber}`)
    await setLook(page, lang, theme)
    await page.getByText(ids.orderNumber).first().click()
    if (desktop) {
      const orderDialog = page.getByRole('dialog', { name: new RegExp(ids.orderNumber) })
      await expect(orderDialog.getByText(L ? `Cadena ${CHAIN}` : `Chain ${CHAIN}`)).toBeVisible({ timeout: 30_000 })
      await expect(orderDialog.getByText(L ? 'Oferta «Verano SPX»' : 'Offer «Verano SPX»')).toBeVisible()
      await noOverflow(page, '.pal', 'la orden')
    } else {
      // en el móvil el borrador abre el pedido guiado (paso de entrega): «Atrás» deja el carrito con el origen de cada precio
      await page.getByRole('button', { name: L ? 'Atrás' : 'Back' }).click({ timeout: 30_000 })
      await expect(page.getByText(L ? `Cadena ${CHAIN}` : `Chain ${CHAIN}`).first()).toBeVisible({ timeout: 30_000 })
    }
    await noHScroll(page, `orden ${tag} ${lang} ${theme}`)
    await page.screenshot({ path: join(SHOTS, `l247-orden-${tag}-${lang}-${theme}.png`) })
  })
}
