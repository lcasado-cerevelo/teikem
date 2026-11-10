// Lote 234 — recorrido contra el API real (db-init hecho; API SIN correo: Brevo__ApiKey vacía). Tres pedidos de Luis:
//
// 1) LAS FOTOS CARGAN SIEMPRE. Con datos propios por API (un producto con foto, una oferta con foto propia y un kit de oferta con foto de renglón; los archivos se
//    suben con nombres con acentos y eñe, como «Diseño sin título.png»), se comprueba que CADA <img> cargó de verdad (`naturalWidth > 0`, no solo que el elemento esté):
//    la tarjeta de la oferta, el editor (foto de la oferta y de los renglones), después de CAMBIAR la foto de la oferta y la de un renglón, al cerrar y reabrir el
//    editor varias veces (una de ellas esperando más de 60 s: lo que tarda en vaciarse la caché de una foto que nadie mira), al ir a otra pantalla y volver, y en el
//    pedido guiado (el cuadrito del producto, la tarjeta de la oferta y la foto grande al pasar el mouse).
// 2) LAS OFERTAS NO TIENEN BORRADOR: una oferta nueva (por API y por pantalla) nace publicada; la pantalla no ofrece «Publicar» ni «Volver a borrador».
// 3) EL KIT DE OFERTA SALE IGUAL EN «NUEVA ORDEN» Y EN «NUEVO PEDIDO»: un kit de la biblioteca que no está en ninguna oferta vigente no sale en ninguno de los dos;
//    al ponerlo en una oferta recién creada sale en los dos de inmediato, al precio de la oferta; el de una oferta vencida no sale en ninguno.
//
// Capturas (deviceScaleFactor 1): L234_SHOTS (por omisión test-results/lote234/). Correr: npx playwright test lote234-fotos --project=escritorio --no-deps --trace off
import { mkdirSync } from 'node:fs'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'

import { ADMIN, API_URL, LETTERS, apiHeaders, login, ok, tenantDay } from './staffHelpers'

test.use({ locale: 'es-PR', deviceScaleFactor: 1, actionTimeout: 30_000, viewport: { width: 1280, height: 900 } })
test.setTimeout(900_000)
test.describe.configure({ mode: 'serial' })

const SHOTS = process.env.L234_SHOTS ?? 'test-results/lote234/'
/** Lo que tarda en salir de la caché una foto que ya nadie mira (gcTime de `useStoredFileUrl`), y un poco más. */
const GC_WAIT_MS = 66_000

type H = Record<string, string>
interface Seed {
  sfx: string
  headers: H
  brand: string
  product: { publicId: string; sku: string; name: string }
  piece: { publicId: string; sku: string }
  client: { publicId: string; name: string }
  offer: { publicId: string; name: string }
  kit: { publicId: string; sku: string; price: number }
  /** Kit de la biblioteca que todavía no está en ninguna oferta. */
  libKit: { publicId: string; sku: string }
  /** Kit que solo está en una oferta vencida. */
  expiredKit: { publicId: string; sku: string }
}
let S: Seed

// ------------------------------------------------------------------ ayudantes

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

/** Una foto PNG de color con un letrero (cada una distinta, para ver en las capturas cuál es cuál). */
async function pngOf(page: Page, text: string, color: string, w = 640, h = 420): Promise<Buffer> {
  const ctx = await page.context().browser()!.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: 1 })
  const p = await ctx.newPage()
  await p.setContent(`<html><body style="margin:0"><div style="width:${w}px;height:${h}px;display:flex;align-items:center;justify-content:center;background:${color};color:#fff;font:700 44px sans-serif;text-align:center">${text}</div></body></html>`)
  const buf = await p.screenshot()
  await ctx.close()
  return buf
}

const file = (name: string, buffer: Buffer) => ({ file: { name, mimeType: 'image/png', buffer } })

/**
 * TODAS las imágenes de `scope` cargaron de verdad: terminaron (`complete`) y tienen tamaño (`naturalWidth > 0`). Una imagen rota (la URL ya no sirve, o el servidor
 * no devolvió la foto) queda con `naturalWidth === 0` aunque el elemento esté en pantalla. Exige al menos `min` imágenes.
 */
async function expectImagesLoaded(scope: Locator, min: number, what: string) {
  await expect
    .poll(
      async () => {
        const imgs = await scope.locator('img').evaluateAll((els) => els.map((e) => ({ alt: (e as HTMLImageElement).alt, ok: (e as HTMLImageElement).complete && (e as HTMLImageElement).naturalWidth > 0 })))
        if (imgs.length < min) return `${what}: ${imgs.length} imagen(es), se esperaban ${min}`
        const broken = imgs.filter((i) => !i.ok)
        return broken.length ? `${what}: rota(s) → ${broken.map((b) => b.alt || '(sin alt)').join(', ')}` : 'ok'
      },
      { timeout: 20_000, message: what },
    )
    .toBe('ok')
}

async function shoot(page: Page, name: string) {
  mkdirSync(SHOTS, { recursive: true })
  await page.mouse.move(0, 0)
  await page.screenshot({ path: `${SHOTS}${name}.png`, animations: 'disabled', caret: 'hide' })
}

const offerCard = (page: Page) => page.locator('article.of-card', { hasText: S.offer.name })

async function openEditor(page: Page): Promise<Locator> {
  await offerCard(page).getByRole('button', { name: 'Abrir oferta' }).click()
  const dlg = page.getByRole('dialog', { name: S.offer.name })
  await expect(dlg.getByRole('heading', { name: 'Foto de la oferta' })).toBeVisible({ timeout: 20_000 })
  return dlg
}

/** El editor abierto: la foto de la oferta y la de cada uno de sus dos renglones (producto y kit). */
async function expectEditorPhotos(dlg: Locator, what: string) {
  await expectImagesLoaded(dlg.locator('.of-photo-row'), 1, `${what}: foto de la oferta`)
  await expectImagesLoaded(dlg.locator('.of-item-photo'), 2, `${what}: fotos de los renglones`)
}

async function closeEditor(page: Page, dlg: Locator) {
  await dlg.getByRole('button', { name: 'Cerrar' }).last().click()
  await expect(dlg).toBeHidden()
  await expectImagesLoaded(offerCard(page), 1, 'tarjeta de la oferta al cerrar el editor')
}

/** Paso 1 del pedido guiado: busca al cliente del recorrido (con un solo resultado el paso lo escoge solo). */
async function pickWizardClient(page: Page) {
  await page.getByLabel('Buscar cliente').fill(S.client.name)
  const chosen = page.getByRole('button', { name: 'Cambiar cliente' })
  const found = page.getByRole('list', { name: 'Clientes encontrados' }).getByRole('button', { name: new RegExp(S.client.name) })
  await expect(chosen.or(found).first()).toBeVisible({ timeout: 20_000 })
  if (!(await chosen.isVisible())) await found.click()
  await expect(chosen).toBeVisible()
}

/** Las peticiones de archivos que el servidor no contestó con 200 (una foto rota por culpa del servidor se ve aquí). */
function watchFiles(page: Page): string[] {
  const failed: string[] = []
  page.on('response', (r) => {
    if (/\/api\/v1\/files\//.test(r.url()) && r.request().method() === 'GET' && r.status() !== 200) failed.push(`${r.status()} ${r.url()}`)
  })
  page.on('requestfailed', (r) => {
    if (/\/api\/v1\/files\//.test(r.url())) failed.push(`falló ${r.url()}`)
  })
  return failed
}

// ------------------------------------------------------------------ datos

test.beforeAll(async ({ request, browser }) => {
  const page = await browser.newPage()
  await enableModules(request, ['SALES_ORDERS'])
  const headers = await apiHeaders(request)
  const sfx = `F${LETTERS}`
  const brand = `FOTOS${sfx}`
  const product = async (sku: string, name: string, salePrice: number) => {
    const p = await ok<{ product: { publicId: string } }>(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name, brand, trackingType: 'NONE', salePrice, purchaseCost: 1 } }), `producto ${sku}`)
    return { publicId: p.product.publicId, sku, name }
  }
  const prod = await product(`FOTO-${sfx}`, `Monitor con foto ${sfx}`, 9.95)
  const piece = await product(`PIEZA-${sfx}`, `Tirillas ${sfx}`, 6.5)
  // la foto del producto, con un nombre de archivo con eñe y acentos (así los guarda Canva en español: «Diseño sin título.png»)
  await ok(request.post(`${API_URL}/api/v1/products/${prod.publicId}/image`, { headers, multipart: file('Diseño sin título.png', await pngOf(page, 'PRODUCTO', '#0b5cad')) }), 'foto del producto')

  const client = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: `Farmacia Fotos ${sfx}` } }), 'cliente')

  // Lote 234: la oferta nace PUBLICADA (no se llama a /publish)
  const offerName = `Oferta fotos ${sfx}`
  const created = await ok<{ publicId: string; isPublished: boolean; state: string; inEffect: boolean }>(request.post(`${API_URL}/api/v1/offers`, { headers, data: { name: offerName, validFrom: tenantDay(-1) } }), 'oferta')
  expect([created.isPublished, created.state, created.inEffect]).toEqual([true, 'ACTIVE', true])
  const oid = created.publicId
  await ok(request.post(`${API_URL}/api/v1/offers/${oid}/image`, { headers, multipart: file('Promoción año nuevo.png', await pngOf(page, 'OFERTA', '#b4232a')) }), 'foto de la oferta')
  await ok(request.post(`${API_URL}/api/v1/offers/${oid}/items`, { headers, data: { items: [{ productPublicId: prod.publicId, price: 8.5 }] } }), 'renglón del producto')
  const components = [{ productPublicId: piece.publicId, quantity: 6 }, { productPublicId: prod.publicId, quantity: 1 }]
  const kit = await ok<{ productPublicId: string; sku: string; offer: { items: { id: number; productPublicId: string }[] } }>(
    request.post(`${API_URL}/api/v1/offers/${oid}/kits`, { headers, data: { sku: `KITF-${sfx}`, name: `Kit con foto ${sfx}`, price: 41.94, brand, components } }),
    'kit de oferta',
  )
  const kitItem = kit.offer.items.find((i) => i.productPublicId === kit.productPublicId)!
  await ok(request.post(`${API_URL}/api/v1/offers/${oid}/items/${kitItem.id}/image`, { headers, multipart: file('Renglón del kit ñ.png', await pngOf(page, 'KIT', '#1f7a3a')) }), 'foto del renglón del kit')

  // un kit de la biblioteca que todavía no está en ninguna oferta, y otro que solo está en una oferta vencida
  const libKit = await ok<{ productPublicId: string }>(request.post(`${API_URL}/api/v1/offers/kits`, { headers, data: { sku: `KITL-${sfx}`, name: `Kit de biblioteca ${sfx}`, brand, components } }), 'kit de biblioteca')
  const expired = await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/offers`, { headers, data: { name: `Oferta vencida ${sfx}`, validFrom: tenantDay(-10), validTo: tenantDay(-1) } }), 'oferta vencida')
  const expiredKit = await ok<{ productPublicId: string }>(request.post(`${API_URL}/api/v1/offers/${expired.publicId}/kits`, { headers, data: { sku: `KITV-${sfx}`, name: `Kit vencido ${sfx}`, price: 30, brand, components } }), 'kit vencido')

  S = {
    sfx, headers, brand, product: prod, piece, client: { publicId: client.publicId, name: `Farmacia Fotos ${sfx}` }, offer: { publicId: oid, name: offerName },
    kit: { publicId: kit.productPublicId, sku: `KITF-${sfx}`, price: 41.94 }, libKit: { publicId: libKit.productPublicId, sku: `KITL-${sfx}` },
    expiredKit: { publicId: expiredKit.productPublicId, sku: `KITV-${sfx}` },
  }
  await page.close()
})

// ------------------------------------------------------------------ 1) las fotos

test('las fotos de la oferta y de sus renglones cargan siempre: tarjeta, editor, cambiar foto, cerrar y reabrir, esperar, ir y volver', async ({ page, request, isMobile }) => {
  test.skip(isMobile, 'recorrido de escritorio')
  const failed = watchFiles(page)
  await login(page)
  await page.goto('/money/prices')
  await expect(offerCard(page)).toBeVisible({ timeout: 30_000 })
  await expectImagesLoaded(offerCard(page), 1, 'tarjeta de la oferta')
  // sin borrador: la tarjeta dice «Vigente» y «Se aplica hoy»
  await expect(offerCard(page)).toContainText('Vigente')
  await expect(offerCard(page)).not.toContainText('Borrador')
  await shoot(page, '01-tarjeta')

  let dlg = await openEditor(page)
  await expectEditorPhotos(dlg, 'al abrir')
  // Lote 234: ni «Publicar» ni «Volver a borrador»
  await expect(dlg.getByRole('button', { name: 'Desactivar' })).toBeVisible()
  await expect(dlg.getByRole('button', { name: 'Publicar' })).toHaveCount(0)
  await expect(dlg.getByRole('button', { name: 'Volver a borrador' })).toHaveCount(0)
  await shoot(page, '02-editor')

  // CAMBIAR la foto de la oferta: la nueva carga y las de los renglones siguen
  await dlg.getByLabel('Archivo de la foto de la oferta').setInputFiles({ name: 'Otra promoción ñandú.png', mimeType: 'image/png', buffer: await pngOf(page, 'OFERTA 2', '#7a3fb0') })
  await expect(page.locator('.toast.on', { hasText: 'Foto guardada.' })).toBeVisible()
  await expectEditorPhotos(dlg, 'después de cambiar la foto de la oferta')
  // las URL anteriores se sueltan a los 30 s del cambio: las fotos tienen que seguir ahí después
  await page.waitForTimeout(32_000)
  await expectEditorPhotos(dlg, '32 s después de cambiar la foto de la oferta')
  await shoot(page, '03-editor-foto-de-la-oferta-cambiada')

  // CAMBIAR la foto del renglón del kit: carga y las demás siguen
  await dlg.getByLabel(`Archivo de foto de ${S.kit.sku}`).setInputFiles({ name: 'Renglón 2 – niño.png', mimeType: 'image/png', buffer: await pngOf(page, 'KIT 2', '#c26a00') })
  await expect(page.locator('.toast.on', { hasText: 'Foto guardada.' }).last()).toBeVisible()
  await expectEditorPhotos(dlg, 'después de cambiar la foto del renglón')
  await shoot(page, '04-editor-foto-del-renglon-cambiada')

  // CERRAR y REABRIR varias veces
  for (let i = 1; i <= 3; i++) {
    await closeEditor(page, dlg)
    dlg = await openEditor(page)
    await expectEditorPhotos(dlg, `reabrir ${i}`)
  }
  // cerrar, esperar a que la caché suelte las fotos que nadie mira (más de 60 s) y reabrir
  await closeEditor(page, dlg)
  await page.waitForTimeout(GC_WAIT_MS)
  await expectImagesLoaded(offerCard(page), 1, 'tarjeta después de esperar')
  dlg = await openEditor(page)
  await expectEditorPhotos(dlg, 'reabrir después de esperar más de 60 s')
  await shoot(page, '05-editor-reabierto-despues-de-esperar')
  await closeEditor(page, dlg)

  // ir a otra pantalla (sin recargar la página) y volver enseguida
  await page.locator('a[href="/sales-orders"]').first().click()
  await expect(page).toHaveURL(/\/sales-orders$/)
  await page.goBack()
  await expect(page).toHaveURL(/\/money\/prices/)
  await expectImagesLoaded(offerCard(page), 1, 'tarjeta al volver de otra pantalla')
  dlg = await openEditor(page)
  await expectEditorPhotos(dlg, 'editor al volver de otra pantalla')
  await closeEditor(page, dlg)
  // …y volver después de más de 60 s en la otra pantalla (todas las fotos ya salieron de la caché)
  await page.locator('a[href="/sales-orders"]').first().click()
  await expect(page).toHaveURL(/\/sales-orders$/)
  await page.waitForTimeout(GC_WAIT_MS)
  await page.goBack()
  await expectImagesLoaded(offerCard(page), 1, 'tarjeta al volver después de esperar')
  dlg = await openEditor(page)
  await expectEditorPhotos(dlg, 'editor al volver después de esperar')
  await shoot(page, '06-editor-al-volver')
  await closeEditor(page, dlg)

  // recargar la página entera
  await page.reload()
  await expectImagesLoaded(offerCard(page), 1, 'tarjeta al recargar')
  dlg = await openEditor(page)
  await expectEditorPhotos(dlg, 'editor al recargar')

  // el servidor sirve cada foto como imagen, con un encabezado válido aunque el nombre del archivo tenga eñe y acentos
  const offer = await ok<{ imagePublicId: string; items: { imagePublicId?: string | null; effectiveImagePublicId?: string | null }[] }>(request.get(`${API_URL}/api/v1/offers/${S.offer.publicId}`, { headers: S.headers }), 'oferta')
  const ids = [offer.imagePublicId, ...offer.items.map((i) => i.effectiveImagePublicId ?? i.imagePublicId)].filter((x): x is string => !!x)
  expect(ids).toHaveLength(3)
  for (const id of ids) {
    const res = await request.get(`${API_URL}/api/v1/files/${id}`, { headers: S.headers })
    expect(res.status(), `GET /files/${id}`).toBe(200)
    expect(res.headers()['content-type']).toBe('image/png')
    expect((await res.body()).length).toBeGreaterThan(500)
    expect(res.headers()['content-disposition']).toMatch(/^inline; filename="[ -~]+"/)
  }
  expect(failed, 'peticiones de fotos que no contestaron 200').toEqual([])
})

test('el pedido guiado muestra el cuadrito del producto con foto, la tarjeta de la oferta y la foto grande al pasar el mouse', async ({ page, isMobile }) => {
  test.skip(isMobile, 'recorrido de escritorio')
  const failed = watchFiles(page)
  await login(page)
  await page.goto('/sales-orders/new-order')
  await pickWizardClient(page)
  await page.getByRole('button', { name: 'Siguiente' }).click()
  await expect(page.getByText('Paso 2 de 4: Productos')).toBeVisible({ timeout: 20_000 })
  await page.getByRole('tab', { name: S.brand }).click()

  const row = page.locator('li.wz-prod', { hasText: S.product.sku })
  const kitCard = page.locator('li.wz-offer', { hasText: S.kit.sku })
  const prodCard = page.locator('li.wz-offer', { hasText: S.product.sku })
  const check = async (what: string, withKit = true) => {
    await expect(row.locator('button.wz-thumb')).toBeVisible({ timeout: 20_000 })
    await expectImagesLoaded(row, 1, `${what}: cuadrito del producto`)
    if (withKit) await expectImagesLoaded(kitCard, 1, `${what}: tarjeta del kit de oferta`)
    await expectImagesLoaded(prodCard, 1, `${what}: tarjeta del producto en oferta`)
  }
  await check('al entrar')
  // la foto grande al pasar el mouse
  await row.locator('button.wz-thumb').hover()
  await expect(page.locator('img.wz-thumb-big')).toBeVisible()
  await expectImagesLoaded(page.locator('body'), 4, 'con la foto grande')
  await expect.poll(() => page.locator('img.wz-thumb-big').evaluate((e) => (e as HTMLImageElement).naturalWidth)).toBeGreaterThan(0)
  mkdirSync(SHOTS, { recursive: true })
  await page.screenshot({ path: `${SHOTS}07-pedido-productos-foto-grande.png`, animations: 'disabled', caret: 'hide' })
  await page.mouse.move(0, 0)

  // buscar (la lista se vuelve a montar), limpiar, cambiar de marca y volver: las fotos siguen
  const search = page.getByLabel('Buscar por SKU o nombre')
  await search.fill(S.product.sku)
  await check('buscando', false) // el buscador deja solo lo que coincide con el SKU del producto
  await search.fill('zzzz-no-existe')
  await expect(row).toHaveCount(0)
  await search.fill('')
  await check('al limpiar el buscador')
  const tabs = page.getByRole('tab')
  if ((await tabs.count()) > 1) {
    const other = tabs.filter({ hasNotText: S.brand }).first()
    await other.click()
    await expect(row).toHaveCount(0)
    await page.waitForTimeout(GC_WAIT_MS)
    await page.getByRole('tab', { name: S.brand }).click()
    await check('al volver a la marca después de esperar más de 60 s')
  }
  // ir al paso anterior y volver
  await page.getByRole('button', { name: 'Atrás' }).click()
  await expect(page.getByText('Paso 1 de 4: Cliente')).toBeVisible()
  await page.getByRole('button', { name: 'Siguiente' }).click()
  await page.getByRole('tab', { name: S.brand }).click()
  await check('al volver del paso 1')
  await shoot(page, '08-pedido-productos')
  expect(failed, 'peticiones de fotos que no contestaron 200').toEqual([])
})

// ------------------------------------------------------------------ 2 y 3) sin borrador; el kit de oferta sale igual en «Nueva orden» y en «Nuevo pedido»

/** Los SKU de kits de oferta que el pedido guiado ofrece al cliente (las tarjetas de la marca). */
async function wizardKits(page: Page): Promise<string[]> {
  await page.goto('/sales-orders/new-order')
  await pickWizardClient(page)
  await page.getByRole('button', { name: 'Siguiente' }).click()
  await expect(page.getByText('Paso 2 de 4: Productos')).toBeVisible({ timeout: 20_000 })
  await page.getByRole('tab', { name: S.brand }).click()
  await expect(page.locator('li.wz-offer', { hasText: S.kit.sku })).toBeVisible()
  const texts = await page.locator('li.wz-offer').allInnerTexts()
  return [S.kit.sku, S.libKit.sku, S.expiredKit.sku].filter((sku) => texts.some((t) => t.includes(sku)))
}

/** Los SKU de kits de oferta que la ventana «Nueva orden» ofrece al cliente (buscando «KIT» + sufijo en el selector de producto). */
async function orderModalKits(page: Page, shot?: string): Promise<{ skus: string[]; dialog: Locator }> {
  await page.goto('/sales-orders')
  await page.getByRole('button', { name: 'Nueva orden' }).click()
  const dialog = page.getByRole('dialog', { name: 'Nueva orden' })
  await dialog.getByLabel('Cliente que compra').click()
  await dialog.getByLabel('Cliente que compra').fill(S.client.name)
  await page.getByRole('option', { name: new RegExp(S.client.name) }).click()
  const picker = dialog.getByRole('combobox', { name: 'Producto de la línea 1' })
  await picker.click()
  await picker.fill(S.sfx)
  // la lista de productos del sufijo ya llegó (la pieza es un producto normal: siempre sale)
  await expect(dialog.getByRole('option', { name: new RegExp(S.piece.sku) })).toBeVisible({ timeout: 20_000 })
  await expect(dialog.getByRole('option', { name: new RegExp(S.kit.sku) })).toBeVisible()
  const texts = await dialog.getByRole('option').allInnerTexts()
  if (shot) await shoot(page, shot)
  return { skus: [S.kit.sku, S.libKit.sku, S.expiredKit.sku].filter((sku) => texts.some((t) => t.includes(sku))), dialog }
}

test('sin borrador: una oferta creada en pantalla ya vale, y su kit sale de inmediato en «Nueva orden» y en «Nuevo pedido» (la misma condición en los dos)', async ({ page, isMobile }) => {
  test.skip(isMobile, 'recorrido de escritorio')
  await login(page)

  // antes: solo el kit de la oferta vigente; ni el de la biblioteca sin oferta ni el de la oferta vencida, en NINGUNO de los dos
  expect(await wizardKits(page)).toEqual([S.kit.sku])
  expect((await orderModalKits(page)).skus).toEqual([S.kit.sku])

  // una oferta nueva por pantalla: queda publicada al crearla (sin «Borrador», sin «Publicar»)
  const name = `Oferta nueva ${S.sfx}`
  await page.goto('/money/prices')
  await page.getByRole('button', { name: 'Nueva oferta' }).click()
  const nd = page.getByRole('dialog', { name: 'Nueva oferta' })
  await nd.getByLabel('Nombre de la oferta').fill(name)
  await nd.getByRole('button', { name: 'Crear oferta' }).click()
  const dlg = page.getByRole('dialog', { name })
  await expect(dlg.getByText('Vigente')).toBeVisible({ timeout: 20_000 })
  await expect(dlg.getByText('Se aplica hoy')).toBeVisible()
  await expect(dlg.getByText('Borrador')).toHaveCount(0)
  await expect(dlg.getByRole('button', { name: 'Publicar' })).toHaveCount(0)
  await expect(dlg.getByRole('button', { name: 'Volver a borrador' })).toHaveCount(0)
  await expect(dlg.getByRole('button', { name: 'Desactivar' })).toBeVisible()

  // se le pone el kit de la biblioteca a $33.33
  await dlg.getByRole('button', { name: 'Usar un kit anterior' }).click()
  const kd = page.getByRole('dialog', { name: new RegExp(`Agregar un kit a la oferta «${name}»`) })
  await kd.getByLabel('Buscar en la biblioteca (SKU o nombre)').fill(S.libKit.sku)
  await kd.getByRole('button', { name: `Usar el kit ${S.libKit.sku}` }).click()
  await kd.getByLabel('Precio de oferta del kit').fill('33.33')
  await kd.getByRole('button', { name: 'Agregar a la oferta' }).click()
  await expect(dlg.locator('tr, .dt-card', { hasText: S.libKit.sku }).first()).toBeVisible({ timeout: 20_000 })
  await shoot(page, '09-oferta-nueva-sin-borrador')
  await dlg.getByRole('button', { name: 'Cerrar' }).last().click()
  const card = page.locator('article.of-card', { hasText: name })
  await expect(card).toContainText('Vigente')
  await expect(card).toContainText('Se aplica hoy')

  // de inmediato sale en los dos sitios; el de la oferta vencida sigue sin salir en ninguno
  expect(await wizardKits(page)).toEqual([S.kit.sku, S.libKit.sku])
  await expect(page.locator('li.wz-offer', { hasText: S.libKit.sku })).toContainText('33.33')
  await shoot(page, '10-nuevo-pedido-kit-recien-creado')
  const { skus, dialog } = await orderModalKits(page, '11-nueva-orden-kit-recien-creado')
  expect(skus).toEqual([S.kit.sku, S.libKit.sku])
  // y su precio en «Nueva orden» es el de la oferta
  await dialog.getByRole('option', { name: new RegExp(S.libKit.sku) }).click()
  await expect(dialog.getByLabel('Precio de la línea 1')).toHaveAttribute('placeholder', /33\.33/)
  await dialog.getByLabel('Cantidad de la línea 1').fill('2')
  await expect(dialog.getByLabel('Importe de la línea 1')).toContainText('66.66')
  await dialog.getByRole('button', { name: 'Guardar borrador' }).click()
  await expect(page.locator('.toast.on').first()).toContainText(/guardad/i)
})
