// Lote 236 — recorrido contra el API real (db-init hecho; API SIN correo: Brevo__ApiKey vacía). Dos pedidos de Luis:
//
// 1) EL CARRITO FIJO DEL PEDIDO GUIADO NO DEJA VER NADA POR DETRÁS. En /sales-orders/new-order, paso Productos, a ancho de teléfono (390×844 y 360×740), con la
//    lista a medio scroll y al final, en tema claro y oscuro, y con lo que flota encima (la mascota Kemi y la cinta roja de TEST, que aquí se simula contestando
//    /health como un ambiente de pruebas): el bloque fijo (carrito, Atrás/Siguiente y la nota del borrador) llega hasta el borde inferior del área visible y de
//    lado a lado; por debajo y a los lados no se ve ningún elemento de la lista (se comparan los getBoundingClientRect y se pregunta qué hay en cada punto con
//    elementFromPoint); y el último renglón no queda tapado al final. Kemi y la cinta de TEST SÍ quedan por encima del bloque (decisión de Luis: la cinta
//    se ve siempre): se anota qué tapan (`covered`) pero no se exige nada.
// 2) EL DISPONIBLE DE UN KIT DE OFERTA SALE DESGLOSADO POR PIEZA: «Se pueden armar: 2,337» y debajo cada pieza con su cantidad por kit y su disponible, con la
//    que limita marcada; con muchas piezas el desglose se pliega («Ver piezas»).
//
// Capturas (deviceScaleFactor 1): L236_SHOTS (por omisión test-results/lote236/); L236_PHASE=antes solo mide y captura (no exige), para ver el defecto.
// Correr: npx playwright test lote236-carrito-fijo --project=escritorio --no-deps --trace off --workers=1
import { mkdirSync, writeFileSync } from 'node:fs'
import { expect, test, type APIRequestContext, type Page } from '@playwright/test'

import { ADMIN, API_URL, LETTERS, apiHeaders, login, ok, tenantDay } from './staffHelpers'

test.use({ locale: 'es-PR', deviceScaleFactor: 1, actionTimeout: 30_000 })
test.setTimeout(600_000)
test.describe.configure({ mode: 'serial' })

const SHOTS = process.env.L236_SHOTS ?? 'test-results/lote236/'
const PHASE = process.env.L236_PHASE === 'antes' ? 'antes' : 'despues'
const STRICT = PHASE === 'despues'
const VIEWPORTS = [{ width: 390, height: 844 }, { width: 360, height: 740 }] as const
const THEMES = ['light', 'dark'] as const

type H = Record<string, string>
interface Seed {
  sfx: string
  brand: string
  client: string
  kitSku: string
  bigKitSku: string
  stripsSku: string
  firstSku: string
}
let S: Seed

const PLATFORM = { email: process.env.TEIKEM_PLATFORM_EMAIL ?? 'teikem+support@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? ADMIN.password }

async function enableModules(request: APIRequestContext, keys: string[]) {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: PLATFORM.email, password: PLATFORM.password } })
  expect(res.ok(), await res.text()).toBeTruthy()
  let body = (await res.json()) as { status?: string; tenants?: { tenantId?: number; isDefault?: boolean }[]; tokens?: { accessToken?: string } }
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: PLATFORM.email, password: PLATFORM.password, tenantId: tenant?.tenantId } })
    body = (await res.json()) as typeof body
  }
  const headers = { Authorization: `Bearer ${body.tokens?.accessToken ?? ''}` }
  const st = (await (await request.get(`${API_URL}/api/v1/legal/status`, { headers })).json()) as { required?: boolean; currentVersion?: string }
  if (st.required) await request.post(`${API_URL}/api/v1/legal/accept`, { headers, data: { version: st.currentVersion, lang: 'es' } })
  const reauth = await request.post(`${API_URL}/api/v1/auth/reauth`, { headers, data: { password: PLATFORM.password } })
  expect(reauth.ok(), await reauth.text()).toBeTruthy()
  const token = ((await reauth.json()) as { accessToken: string }).accessToken
  for (const key of keys) {
    const r = await request.put(`${API_URL}/api/v1/modules/${key}`, { headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() }, data: { isEnabled: true } })
    expect(r.ok(), `módulo ${key}: ${await r.text()}`).toBeTruthy()
  }
}

// ------------------------------------------------------------------ datos

test.beforeAll(async ({ request }) => {
  await enableModules(request, ['SALES_ORDERS'])
  const headers: H = await apiHeaders(request)
  const sfx = `C${LETTERS}`
  const brand = `FIJO${sfx}`
  const warehouses = await ok<{ publicId: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const warehouseId = warehouses[0].publicId
  const product = async (sku: string, name: string, salePrice: number, stock: number) => {
    const p = await ok<{ product: { publicId: string } }>(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name, brand, trackingType: 'NONE', salePrice, purchaseCost: 1 } }), `producto ${sku}`)
    if (stock > 0)
      await ok(request.post(`${API_URL}/api/v1/inventory/adjustments`, { headers, data: { productPublicId: p.product.publicId, warehousePublicId: warehouseId, binId: 2, quantity: stock, reason: 'FOUND', notes: 'e2e lote 236' } }), `existencia ${sku}`)
    return { publicId: p.product.publicId, sku, name }
  }
  // las piezas del kit: 14,022 tirillas ÷ 6 = 2,337 kits (limita); 5,000 monitores
  const strips = await product(`52800-${sfx}`, 'Tirillas Prodigy No Coding (50)', 6.5, 14022)
  const monitor = await product(`070120-${sfx}`, 'Monitor Prodigy AutoCode', 9.95, 5000)
  // una lista larga (para que haya que desplazarse) con existencias distintas
  const more: { publicId: string; sku: string; name: string }[] = []
  for (let i = 1; i <= 12; i++) more.push(await product(`A${String(i).padStart(2, '0')}-${sfx}`, `Producto de la lista ${i}`, 3 + i, i === 3 ? 60 : i * 25))

  const client = `Farmacia Carrito ${sfx}`
  await ok(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: client } }), 'cliente')
  const offer = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/offers`, { headers, data: { name: `Oferta carrito ${sfx}`, validFrom: tenantDay(-1) } }), 'oferta')
  await ok(
    request.post(`${API_URL}/api/v1/offers/${offer.publicId}/kits`, {
      headers,
      data: { sku: `OFERTA1-${sfx}`, name: 'OFERTA1: 6 tirillas + 1 monitor', price: 41.94, brand, components: [{ productPublicId: strips.publicId, quantity: 6 }, { productPublicId: monitor.publicId, quantity: 1 }] },
    }),
    'kit de oferta',
  )
  // un kit con muchas piezas: el desglose sale plegado
  await ok(
    request.post(`${API_URL}/api/v1/offers/${offer.publicId}/kits`, {
      headers,
      data: { sku: `OFERTA2-${sfx}`, name: 'OFERTA2: surtido de 6 productos', price: 60, brand, components: more.slice(0, 6).map((p, i) => ({ productPublicId: p.publicId, quantity: i + 1 })) },
    }),
    'kit de muchas piezas',
  )
  S = { sfx, brand, client, kitSku: `OFERTA1-${sfx}`, bigKitSku: `OFERTA2-${sfx}`, stripsSku: strips.sku, firstSku: more[0].sku }
})

// ------------------------------------------------------------------ medición

interface Measure {
  stage: { left: number; right: number; top: number; bottom: number }
  foot: { left: number; right: number; top: number; bottom: number }
  /** px de área visible que quedan por debajo del bloque fijo (0 = llega al borde). */
  gapBelow: number
  /** px que el bloque fijo NO cubre a cada lado del área visible. */
  gapLeft: number
  gapRight: number
  /** Elementos de la lista (renglones, tarjetas, casillas, botones) que se ven por debajo o a los lados del bloque fijo. */
  leaks: string[]
  /** Kemi o la cinta de ambiente encima de los botones o del total del carrito (informativo: Luis lo acepta). */
  covered: string[]
  footOpaque: boolean
  /** Lo que mide el contenido por debajo del último renglón tapado por el bloque (al final del scroll debe ser 0). */
  lastHidden: number
  docOverflowX: number
}

/** Mide el bloque fijo contra el área visible del contenido (`.stage`): rectángulos y qué elemento hay en cada punto. */
function measure(page: Page): Promise<Measure> {
  return page.evaluate(() => {
    const r = (el: Element) => {
      const b = el.getBoundingClientRect()
      return { left: b.left, right: b.right, top: b.top, bottom: b.bottom }
    }
    const stageEl = document.querySelector('.stage') as HTMLElement
    const footEl = document.querySelector('.wz-foot') as HTMLElement
    const bodyEl = document.querySelector('.wz-body') as HTMLElement
    const sb = stageEl.getBoundingClientRect()
    // el área visible del contenido: la caja de .stage sin su barra de desplazamiento, recortada a la ventana
    const stage = { left: sb.left + stageEl.clientLeft, top: sb.top, right: sb.left + stageEl.clientLeft + stageEl.clientWidth, bottom: Math.min(sb.bottom, window.innerHeight) }
    const foot = r(footEl)
    const leaks = new Set<string>()
    const describe = (el: Element) => `${el.tagName.toLowerCase()}.${(el.getAttribute('class') ?? '').split(' ')[0]} «${(el.textContent ?? '').trim().slice(0, 28)}»`
    // 1) rectángulos: lo de la lista que queda dentro del área visible pero fuera (debajo o a los lados) del bloque fijo, a la altura del bloque o más abajo
    for (const el of Array.from(bodyEl.querySelectorAll('.wz-prod, .wz-offer, .wz-qty, .wz-stepper .btn, .sales-stock, .wz-price-wrap'))) {
      const b = el.getBoundingClientRect()
      if (b.width === 0 || b.height === 0) continue
      // recorte por la lista con scroll propio (.wz-prods)
      const scroller = el.closest('.wz-prods')
      const clip = scroller ? scroller.getBoundingClientRect() : null
      const top = Math.max(b.top, clip ? clip.top : -Infinity, foot.top)
      const bottom = Math.min(b.bottom, clip ? clip.bottom : Infinity, stage.bottom)
      if (bottom - top < 1) continue
      // a la altura del bloque: solo cuenta lo que asoma por los lados; por debajo del bloque: todo
      const below = Math.min(b.bottom, clip ? clip.bottom : Infinity, stage.bottom) - Math.max(top, foot.bottom)
      const sides = b.left < foot.left - 0.5 || b.right > foot.right + 0.5
      if (below >= 1 || sides) leaks.add(`rect ${describe(el)}`)
    }
    // 2) qué se ve de verdad: en una rejilla de puntos desde el borde superior del bloque hasta el fondo del área visible, lo de arriba no puede ser de la lista
    for (let y = foot.top + 1; y < stage.bottom; y += 4) {
      for (let x = stage.left + 1; x < stage.right; x += 6) {
        const top = document.elementFromPoint(x, y)
        if (top && bodyEl.contains(top) && top !== bodyEl) leaks.add(`punto ${describe(top.closest('.wz-prod, .wz-offer, .wz-step') ?? top)}`)
      }
    }
    // 3) lo que flota encima de los botones o del total (solo se anota)
    const covered: string[] = []
    const floats = Array.from(document.querySelectorAll('.kemi-fab, .kemi-zz, .env-ribbon'))
    const targets = Array.from(footEl.querySelectorAll('.wz-nav .btn, .wz-cart-toggle b'))
    for (const f of floats) {
      const a = f.getBoundingClientRect()
      if (a.width === 0) continue
      for (const tg of targets) {
        const b = tg.getBoundingClientRect()
        if (a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top) covered.push(`${describe(f)} sobre ${describe(tg)}`)
      }
    }
    const bg = getComputedStyle(footEl).backgroundColor
    const alpha = /rgba\(/.test(bg) ? Number(bg.replace(/^.*,\s*([\d.]+)\)$/, '$1')) : bg === 'transparent' ? 0 : 1
    const last = (bodyEl.querySelector('.wz-step') as HTMLElement).getBoundingClientRect()
    return {
      stage,
      foot,
      gapBelow: Math.round((stage.bottom - foot.bottom) * 10) / 10,
      gapLeft: Math.round((foot.left - stage.left) * 10) / 10,
      gapRight: Math.round((stage.right - foot.right) * 10) / 10,
      leaks: Array.from(leaks).slice(0, 12),
      covered,
      footOpaque: alpha === 1,
      lastHidden: Math.max(0, Math.round((last.bottom - foot.top) * 10) / 10),
      docOverflowX: document.documentElement.scrollWidth - window.innerWidth,
    }
  })
}

const results: Record<string, Measure> = {}

async function shoot(page: Page, name: string) {
  mkdirSync(SHOTS, { recursive: true })
  await page.mouse.move(0, 0)
  await page.screenshot({ path: `${SHOTS}${PHASE}-${name}.png`, animations: 'disabled', caret: 'hide' })
}

async function enterProducts(page: Page, theme: string) {
  // la cinta roja de TEST: /health contesta como un ambiente de pruebas (la web no tiene /health propio en desarrollo)
  await page.route('**/health', (route) => route.fulfill({ contentType: 'application/json', body: JSON.stringify({ status: 'ok', environment: 'TEST', database: 'Teikem_Test' }) }))
  await page.addInitScript((th) => window.localStorage.setItem('teikem.theme', th), theme)
  await login(page)
  await page.goto('/sales-orders/new-order')
  const chosen = page.getByRole('button', { name: 'Cambiar cliente' })
  const search = page.getByLabel('Buscar cliente')
  await expect(chosen.or(search).first()).toBeVisible({ timeout: 20_000 })
  if (!(await chosen.isVisible())) {
    // con un solo resultado el paso lo escoge solo (y el buscador desaparece)
    await search.pressSequentially(S.client, { delay: 5 }).catch(() => undefined)
    const found = page.getByRole('list', { name: 'Clientes encontrados' }).getByRole('button', { name: new RegExp(S.client) })
    await expect(chosen.or(found).first()).toBeVisible({ timeout: 20_000 })
    if (!(await chosen.isVisible())) await found.click()
  }
  await expect(chosen).toBeVisible()
  await page.getByRole('button', { name: 'Siguiente' }).click()
  await expect(page.getByText('Paso 2 de 4: Productos')).toBeVisible({ timeout: 20_000 })
  await page.getByRole('tab', { name: S.brand }).click()
  await expect(page.locator('li.wz-offer', { hasText: S.kitSku })).toBeVisible()
  await expect(page.locator('.env-ribbon')).toBeVisible()
  // el disponible ya llegó
  await expect(page.locator('li.wz-prod', { hasText: S.firstSku }).locator('.sales-stock')).toBeVisible({ timeout: 20_000 })
}

for (const vp of VIEWPORTS) {
  for (const theme of THEMES) {
    const tag = `${vp.width}x${vp.height}-${theme === 'light' ? 'claro' : 'oscuro'}`
    test(`carrito fijo ${tag}: nada se ve por detrás ni por debajo`, async ({ browser }) => {
      const ctx = await browser.newContext({ viewport: vp, deviceScaleFactor: 1, locale: 'es-PR', hasTouch: true, isMobile: true })
      const page = await ctx.newPage()
      await enterProducts(page, theme)
      await expect(page.locator('html')).toHaveAttribute('data-theme', theme)
      // un producto al carrito (la tarjeta del kit con +)
      await page.locator('li.wz-offer', { hasText: S.kitSku }).locator('.wz-stepper .btn').last().click()
      await expect(page.locator('.wz-cart-toggle')).toContainText('1 producto')

      const stage = page.locator('.stage')
      const scrollTo = async (fraction: number) => {
        await stage.evaluate((el, f) => el.scrollTo({ top: Math.round((el.scrollHeight - el.clientHeight) * f), behavior: 'instant' as ScrollBehavior }), fraction)
        await page.waitForTimeout(250)
      }
      for (const [name, fraction] of [['medio', 0.45], ['medio-2', 0.7], ['final', 1]] as const) {
        await scrollTo(fraction)
        const m = await measure(page)
        results[`${tag}-${name}`] = m
        await shoot(page, `carrito-${tag}-${name}`)
        if (!STRICT) continue
        expect(m.footOpaque, `${tag} ${name}: fondo opaco`).toBe(true)
        expect(m.gapBelow, `${tag} ${name}: el bloque fijo llega al borde inferior del área visible`).toBeLessThanOrEqual(0.5)
        expect(m.gapLeft, `${tag} ${name}: cubre el margen izquierdo`).toBeLessThanOrEqual(0.5)
        expect(m.gapRight, `${tag} ${name}: cubre el margen derecho`).toBeLessThanOrEqual(0.5)
        expect(m.leaks, `${tag} ${name}: nada de la lista se ve por debajo ni a los lados del bloque fijo`).toEqual([])
        expect(m.docOverflowX, `${tag} ${name}: sin desplazamiento horizontal`).toBeLessThanOrEqual(0)
        if (name === 'final') expect(m.lastHidden, `${tag}: al final, el último renglón no queda tapado`).toBe(0)
      }
      // con el carrito abierto sigue igual
      await scrollTo(0.6)
      await page.locator('.wz-cart-toggle').click()
      await expect(page.locator('#wz-cart-lines')).toBeVisible()
      const open = await measure(page)
      results[`${tag}-carrito-abierto`] = open
      await shoot(page, `carrito-${tag}-abierto`)
      if (STRICT) {
        expect(open.leaks, `${tag} carrito abierto`).toEqual([])
        expect(open.gapBelow, `${tag} carrito abierto`).toBeLessThanOrEqual(0.5)
      }
      mkdirSync(SHOTS, { recursive: true })
      writeFileSync(`${SHOTS}${PHASE}-medidas.json`, JSON.stringify(results, null, 2))
      await ctx.close()
    })
  }
}

// ------------------------------------------------------------------ 2) el desglose del kit

for (const theme of THEMES) {
  test(`el disponible del kit de oferta sale desglosado por pieza (${theme})`, async ({ browser }) => {
    test.skip(!STRICT, 'solo después del cambio')
    const ctx = await browser.newContext({ viewport: { width: 360, height: 740 }, deviceScaleFactor: 1, locale: 'es-PR', hasTouch: true, isMobile: true })
    const page = await ctx.newPage()
    await enterProducts(page, theme)
    const card = page.locator('li.wz-offer', { hasText: S.kitSku })
    await expect(card.locator('.sales-stock-avail')).toHaveText('Se pueden armar: 2,337')
    const pieces = card.locator('.sales-kitstock li')
    await expect(pieces).toHaveCount(2)
    await expect(pieces.nth(0)).toContainText(S.stripsSku)
    await expect(pieces.nth(0)).toContainText('× 6')
    await expect(pieces.nth(0)).toContainText('14,022 disp.')
    await expect(pieces.nth(0)).toContainText('Limita')
    await expect(pieces.nth(1)).toContainText('5,000 disp.')
    await expect(pieces.nth(1)).not.toContainText('Limita')
    // no desborda la tarjeta ni la página
    const fits = await card.evaluate((el) => {
      const c = el.getBoundingClientRect()
      return Array.from(el.querySelectorAll('.sales-kitstock, .sales-kitstock li')).every((x) => x.getBoundingClientRect().right <= c.right + 0.5) && document.documentElement.scrollWidth <= window.innerWidth
    })
    expect(fits, 'el desglose cabe en la tarjeta a 360 px').toBe(true)
    await card.scrollIntoViewIfNeeded()
    await page.locator('.stage').evaluate((el, top) => el.scrollBy({ top: top - 150, behavior: 'instant' as ScrollBehavior }), (await card.locator('.wz-offer-body').boundingBox())!.y)
    await shoot(page, `kit-tarjeta-360-${theme === 'light' ? 'claro' : 'oscuro'}`)

    // el kit de muchas piezas sale plegado; al abrirlo, las 6
    const big = page.locator('li.wz-offer', { hasText: S.bigKitSku })
    await big.scrollIntoViewIfNeeded()
    const fold = big.locator('details.sales-kitstock-more')
    await expect(fold.locator('summary')).toContainText('Ver piezas (6)')
    await fold.locator('summary').click()
    await expect(big.locator('.sales-kitstock li')).toHaveCount(6)
    await expect(big.locator('.sales-kitstock li.limit')).toHaveCount(1)
    await page.locator('.stage').evaluate((el, top) => el.scrollBy({ top: top - 150, behavior: 'instant' as ScrollBehavior }), (await big.locator('.wz-offer-body').boundingBox())!.y)
    await shoot(page, `kit-muchas-piezas-360-${theme === 'light' ? 'claro' : 'oscuro'}`)

    // pedir de más: el backorder sigue marcándose con la cuenta del kit
    await card.scrollIntoViewIfNeeded()
    await card.getByRole('textbox').fill('2400')
    await expect(card.locator('.sales-stock-short')).toContainText('Backorder: 63')
    // en el carrito también sale el desglose
    await page.locator('.wz-cart-toggle').click()
    await expect(page.locator('#wz-cart-lines .sales-kitstock li')).toHaveCount(2)
    await shoot(page, `kit-carrito-360-${theme === 'light' ? 'claro' : 'oscuro'}`)
    await ctx.close()
  })
}

test('el desglose del kit en «Nueva orden» (escritorio)', async ({ page }) => {
  test.skip(!STRICT, 'solo después del cambio')
  await page.setViewportSize({ width: 1280, height: 900 })
  await login(page)
  await page.goto('/sales-orders')
  await page.getByRole('button', { name: 'Nueva orden' }).click()
  const dialog = page.getByRole('dialog', { name: 'Nueva orden' })
  await dialog.getByLabel('Cliente que compra').click()
  await dialog.getByLabel('Cliente que compra').fill(S.client)
  await page.getByRole('option', { name: new RegExp(S.client) }).click()
  const picker = dialog.getByRole('combobox', { name: 'Producto de la línea 1' })
  await picker.click()
  await picker.fill(S.kitSku)
  await page.getByRole('option', { name: new RegExp(S.kitSku) }).click()
  await expect(dialog.locator('.sales-stock-avail')).toHaveText('Se pueden armar: 2,337')
  await expect(dialog.locator('.sales-kitstock li')).toHaveCount(2)
  await expect(dialog.locator('.sales-kitstock li.limit')).toContainText(S.stripsSku)
  await dialog.locator('.sales-kitstock').scrollIntoViewIfNeeded()
  await shoot(page, 'kit-nueva-orden-1280-claro')
})
