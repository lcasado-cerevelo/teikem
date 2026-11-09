// Lote 228 (P8 del pedido de vendedores) — recorrido de punta a punta contra el API real (db-init hecho; API SIN correo: Brevo__ApiKey vacía).
//
// Datos propios por API (beforeAll): enciende SALES_ORDERS, TAX y BILLING; IVU de 11.5 % por omisión si la compañía no tiene; envío $12 con gratis desde $120;
// mapa de términos de QuickBooks; productos con marca (PRODIGY, GLOBAL); el kit OFERTA1 (6 tirillas + 1 monitor, $41.94) como promoción vigente con foto; la lista
// general vigente con sus precios; una lista negociada para el cliente de consignación; existencia; un vendedor (rol Ventas) y un supervisor (rol Supervisor de ventas).
//
// 1) Precios y ofertas (escritorio): la pestaña con la oferta como tarjeta, la ventana de una lista y el editor del kit con la promoción.
// 2) Pedido guiado completo (escritorio): el VENDEDOR da de alta un cliente nuevo (prospecto) con sus fotos, agrega la oferta y productos de dos marcas, entrega con
//    el envío calculado ($12, regla de la compañía), firma, envía con su contraseña → queda «Espera aprobación» (el despacho se rechaza). El SUPERVISOR aprueba al
//    cliente en su ficha. La oficina (administrador) despacha desde la orden, factura (corrida → revisar → aprobar con reautenticación) y en la pestaña QuickBooks
//    ve los datos nuevos, exporta, abre el IIF y lo compara con el esperado (renglones exactos del cliente, de los productos y de la factura con el kit desglosado),
//    confirma («Sí, ya están»), le quita la marca desde el histórico y la vuelve a marcar a mano.
// 3) Consignación (escritorio): pedido en consignación (recogido) de un cliente con forma de pago Consignación y precio negociado; el despacho y la factura se encadenan
//    por API (ya probados por la pantalla en el paso 2); en Facturación → Consignación se registra un pago parcial y luego el resto: el saldo queda en cero.
// 4) Móvil (360 px): los cuatro pasos sin desplazamiento horizontal de la página y el envío.
//
// Capturas: docs/manual/frontend/img/l228-<paso>.png (y -oscuro.png donde aplica). Con MANUAL_LANG=en y MANUAL_IMG_DIR, las del manual en inglés.
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'

import { MANUAL_LANG, manualApiEnglish, manualLang, manualTarget } from './manualLang'
import { ADMIN, API_URL, IMG_DIR, LETTERS, apiHeaders, ok, openClientPanels, tenantDay } from './staffHelpers'

const EN = MANUAL_LANG === 'en'
const L = (es: string, en: string) => (EN ? en : es)

test.use({ locale: 'es-PR', deviceScaleFactor: 2, actionTimeout: 30_000 })
test.setTimeout(600_000)
test.describe.configure({ mode: 'serial' })

const TAX_PCT = 11.5
const SHIP_CHARGE = 12
const SHIP_FREE_FROM = 120
/** Alto de la ventana para las fotos de los pasos del pedido (que se vea el paso entero sobre la barra fija de abajo). */
const WZ_H = 1500
/** Alto de la ventana para las fotos de ventanas (diálogos) que, si no, se desplazan por dentro. */
const DLG_H = 2000

type H = Record<string, string>
interface Prod { publicId: string; sku: string; name: string; salePrice: number; cost: number; listPrice: number }
interface Seed {
  sfx: string
  headers: H
  warehouseId: string
  strips: Prod
  monitor: Prod
  cream: Prod
  kit: Prod
  generalListId: string
  negotiatedListId: string
  consignClient: { publicId: string; name: string; code: string }
  vendor: { email: string; password: string; fullName: string }
  supervisor: { email: string; password: string; fullName: string }
}

let S: Seed

// ------------------------------------------------------------------ ayudantes

/** Captura del manual: con `dark` también la versión oscura (`-oscuro`). La interfaz pasa a inglés solo para la foto (MANUAL_LANG=en). */
async function cap(page: Page, name: string, opts: { target?: Locator; dark?: boolean; height?: number; fullPage?: boolean } = {}) {
  await page.waitForLoadState('networkidle').catch(() => {})
  await expect(page.locator('.toast.on')).toHaveCount(0, { timeout: 15_000 })
  mkdirSync(IMG_DIR, { recursive: true })
  await page.mouse.move(0, 0)
  const size = page.viewportSize()
  if (opts.height && size) await page.setViewportSize({ width: size.width, height: opts.height })
  const target = await manualTarget(opts.target)
  await manualLang(page, 'en', `${IMG_DIR}${name}.png`)
  const shoot = async (file: string) => {
    if (target) await target.screenshot({ path: `${IMG_DIR}${file}.png`, animations: 'disabled', caret: 'hide' })
    else await page.screenshot({ path: `${IMG_DIR}${file}.png`, animations: 'disabled', caret: 'hide', fullPage: opts.fullPage })
  }
  await shoot(name)
  if (opts.dark) {
    const before = await page.evaluate(() => document.documentElement.getAttribute('data-theme'))
    await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'dark'))
    await page.waitForTimeout(300)
    await shoot(`${name}-oscuro`)
    await page.evaluate((b) => (b ? document.documentElement.setAttribute('data-theme', b) : document.documentElement.removeAttribute('data-theme')), before)
    await page.waitForTimeout(150)
  }
  await manualLang(page, 'es')
  if (opts.height && size) await page.setViewportSize(size)
}

const noHScroll = (p: Page) => p.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)

async function loginAs(page: Page, who: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(who.email)
  await page.getByLabel('Contraseña', { exact: true }).fill(who.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  await page.waitForURL((url) => url.pathname !== '/login')
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/')
}

/** Cierra la sesión del navegador (cookies y almacenamiento de la sesión) y entra con otra persona. */
async function switchUser(page: Page, who: { email: string; password: string }) {
  await page.context().clearCookies()
  await page.evaluate(() => sessionStorage.clear())
  await loginAs(page, who)
}

/** Sesión por API de cualquier usuario (acepta el documento legal si se exige). */
async function headersOf(request: APIRequestContext, who: { email: string; password: string }): Promise<H> {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: who.email, password: who.password } })
  expect(res.ok(), await res.text()).toBeTruthy()
  let body = (await res.json()) as { status?: string; tenants?: { tenantId?: number; isDefault?: boolean }[]; tokens?: { accessToken?: string } }
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { email: who.email, password: who.password, tenantId: tenant?.tenantId } })
    body = (await res.json()) as typeof body
  }
  const headers = { Authorization: `Bearer ${body.tokens?.accessToken ?? ''}` }
  const st = (await (await request.get(`${API_URL}/api/v1/legal/status`, { headers })).json()) as { required?: boolean; currentVersion?: string }
  if (st.required) await request.post(`${API_URL}/api/v1/legal/accept`, { headers, data: { version: st.currentVersion, lang: 'es' } })
  return headers
}

/** Token con verificación reciente (AAL2) del administrador: aprobar una corrida, anular… */
async function reauthHeaders(request: APIRequestContext, headers: H): Promise<H> {
  const res = await request.post(`${API_URL}/api/v1/auth/reauth`, { headers, data: { password: ADMIN.password } })
  expect(res.ok(), await res.text()).toBeTruthy()
  return { Authorization: `Bearer ${((await res.json()) as { accessToken: string }).accessToken}` }
}

async function maybeReauth(page: Page, password = ADMIN.password) {
  const dialog = page.getByRole('dialog', { name: 'Confirme su identidad' })
  const appeared = await dialog.waitFor({ state: 'visible', timeout: 15_000 }).then(() => true).catch(() => false)
  if (appeared) {
    await dialog.getByLabel('Contraseña', { exact: true }).fill(password)
    await dialog.getByRole('button', { name: 'Confirmar' }).click()
  }
}

/** Una imagen PNG dibujada en la página (la foto de la oferta, la del registro de comerciante). */
async function pngOf(page: Page, html: string, w = 480, h = 320): Promise<Buffer> {
  const ctx = await page.context().browser()!.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: 1 })
  const p = await ctx.newPage()
  await p.setContent(`<html><body style="margin:0">${html}</body></html>`)
  const buf = await p.screenshot()
  await ctx.close()
  return buf
}

/** Reparto del monto de un kit entre sus piezas (mayor residuo, cierra al centavo): el mismo cálculo que documenta el manual (cap. 49 §4). */
function prorate(amount: number, weights: number[]): number[] {
  const total = weights.reduce((a, b) => a + b, 0)
  const cents = Math.round(amount * 100)
  const raw = weights.map((w) => (cents * w) / total)
  const floor = raw.map((r) => Math.floor(r))
  let left = cents - floor.reduce((a, b) => a + b, 0)
  const order = raw.map((r, i) => ({ i, f: r - floor[i] })).sort((a, b) => b.f - a.f || a.i - b.i)
  for (const o of order) if (left-- > 0) floor[o.i]++
  return floor.map((c) => c / 100)
}
const money = (v: number) => (Math.round(v * 100) / 100).toFixed(2)
const priceText = (v: number) => {
  const s = (Math.round(v * 100000) / 100000).toString()
  const [i, d = ''] = s.split('.')
  return `${i}.${(d + '00').slice(0, Math.max(2, d.length))}`
}
const usDate = (iso: string) => `${iso.slice(5, 7)}/${iso.slice(8, 10)}/${iso.slice(0, 4)}`

// ------------------------------------------------------------------ datos de la demostración

/** Encender módulos de la compañía demo es solo del administrador de la plataforma (y pide verificación reciente). */
const PLATFORM = { email: process.env.TEIKEM_PLATFORM_EMAIL ?? 'teikem+support@cerevelo.com', password: process.env.TEIKEM_ADMIN_PASSWORD ?? ADMIN.password }
async function enableModules(request: APIRequestContext, keys: string[]) {
  const headers = await headersOf(request, PLATFORM)
  const reauth = await request.post(`${API_URL}/api/v1/auth/reauth`, { headers, data: { password: PLATFORM.password } })
  expect(reauth.ok(), await reauth.text()).toBeTruthy()
  const token = ((await reauth.json()) as { accessToken: string }).accessToken
  for (const key of keys) {
    const res = await request.put(`${API_URL}/api/v1/modules/${key}`, { headers: { Authorization: `Bearer ${token}`, 'Idempotency-Key': crypto.randomUUID() }, data: { isEnabled: true } })
    expect(res.ok(), `módulo ${key}: ${await res.text()}`).toBeTruthy()
  }
}

async function seed(request: APIRequestContext, page: Page, tag: string): Promise<Seed> {
  await enableModules(request, ['SALES_ORDERS', 'TAX', 'BILLING'])
  const headers = await apiHeaders(request)
  const sfx = `${tag}${LETTERS}`

  // IVU por omisión (si la compañía no tiene uno)
  const tax = await ok<{ defaultRateCode?: string | null }>(request.get(`${API_URL}/api/v1/tax/settings`, { headers }), 'impuestos')
  if (!tax.defaultRateCode) {
    await ok(request.post(`${API_URL}/api/v1/tax/rates`, { headers, data: { code: `IVU228${sfx}`, name: 'IVU', effectiveFrom: '2020-01-01', components: [{ code: 'STATE', name: 'Estatal', percent: 10.5 }, { code: 'CITY', name: 'Municipal', percent: 1 }] } }), 'tasa')
    await ok(request.patch(`${API_URL}/api/v1/tax/settings`, { headers, data: { defaultRateCode: `IVU228${sfx}` } }), 'tasa por omisión')
  }
  // envío de la compañía: $12, gratis desde $120 (medido después del IVU); el cargo no lleva IVU
  await ok(request.patch(`${API_URL}/api/v1/sales-orders/settings`, { headers, data: { charge: SHIP_CHARGE, freeFrom: SHIP_FREE_FROM, taxable: false } }), 'envío')
  // QuickBooks: nombres de los términos (los de QuickBooks Solutions); cuentas e ítems por omisión
  await ok(request.patch(`${API_URL}/api/v1/sales-orders/quickbooks/settings`, { headers, data: { terms: { NET30: 'Net 30', CONSIGNMENT: 'Consignment', CHEQUE: 'CHEQUE', CASH: 'CASH', ACH: 'ACH' } } }), 'ajustes QuickBooks')

  const warehouses = await ok<{ publicId: string }[]>(request.get(`${API_URL}/api/v1/warehouses`, { headers }), 'almacenes')
  const warehouseId = warehouses[0].publicId
  const product = async (sku: string, name: string, brand: string, salePrice: number, cost: number, listPrice: number): Promise<Prod> => {
    const p = await ok<{ product: { publicId: string } }>(request.post(`${API_URL}/api/v1/products`, { headers, data: { sku, name, brand, trackingType: 'NONE', salePrice, purchaseCost: cost } }), `producto ${sku}`)
    return { publicId: p.product.publicId, sku, name, salePrice, cost, listPrice }
  }
  const strips = await product(`52800-${sfx}`, L('Tirillas Prodigy No Coding (50)', 'Prodigy No Coding test strips (50)'), 'PRODIGY', 6.5, 3.1, 5.99)
  const monitor = await product(`070120-${sfx}`, L('Monitor Prodigy AutoCode', 'Prodigy AutoCode meter'), 'PRODIGY', 9.95, 4.25, 7.95)
  const cream = await product(`GL-100-${sfx}`, L('Crema Global 4 oz', 'Global cream 4 oz'), 'GLOBAL', 3.5, 1.4, 3.25)
  const kit = await product(`OFERTA1-${sfx}`, L('OFERTA1: 6 tirillas + 1 monitor Prodigy', 'OFFER1: 6 strips + 1 Prodigy meter'), 'PRODIGY', 41.94, 0, 41.94)
  await ok(request.put(`${API_URL}/api/v1/products/${kit.publicId}/kit`, { headers, data: { isKit: true, adjustable: false, components: [{ productPublicId: strips.publicId, quantity: 6 }, { productPublicId: monitor.publicId, quantity: 1 }] } }), 'kit')
  await ok(request.put(`${API_URL}/api/v1/products/${kit.publicId}/promo`, { headers, data: { isPromotion: true, promoFrom: tenantDay(-1), promoTo: tenantDay(30) } }), 'promoción')
  const photo = await pngOf(
    page,
    `<div style="width:480px;height:320px;display:flex;flex-direction:column;align-items:center;justify-content:center;background:linear-gradient(135deg,#0b5cad,#19a3c4);color:#fff;font:700 40px sans-serif">
       <div>OFERTA1</div><div style="font:600 22px sans-serif;margin-top:12px">6 × 52800 + 1 × 070120</div><div style="font:800 54px sans-serif;margin-top:18px">$41.94</div></div>`,
  )
  await ok(request.post(`${API_URL}/api/v1/products/${kit.publicId}/promo/image`, { headers, multipart: { file: { name: 'oferta1.png', mimeType: 'image/png', buffer: photo } } }), 'foto de la oferta')

  // existencia (las piezas del kit y la crema)
  for (const [p, qty] of [[strips, 120], [monitor, 30], [cream, 80]] as const)
    await ok(request.post(`${API_URL}/api/v1/inventory/adjustments`, { headers, data: { productPublicId: p.publicId, warehousePublicId: warehouseId, binId: 2, quantity: qty, reason: 'FOUND', notes: 'e2e lote 228' } }), 'existencia')

  // lista general vigente: si ya hay una publicada para todos, se le agregan los renglones; si no, se crea y se publica
  type ListItem = { publicId: string; appliesToAll?: boolean; state?: string; isActive?: boolean }
  const lists = await ok<ListItem[]>(request.get(`${API_URL}/api/v1/price-lists`, { headers }), 'listas')
  let general = lists.find((l) => l.appliesToAll && l.isActive !== false && (l.state ?? '').toUpperCase() === 'ACTIVE')?.publicId
  const items = [strips, monitor, cream, kit].map((p) => ({ productPublicId: p.publicId, price: p.listPrice }))
  if (!general) {
    general = (await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/price-lists`, { headers, data: { name: L('Lista general 2026', 'General list 2026'), validFrom: tenantDay(-1), appliesToAll: true, notes: L('Precios del formulario de los vendedores', 'Prices from the sales reps form') } }), 'lista general')).publicId
    await ok(request.post(`${API_URL}/api/v1/price-lists/${general}/items`, { headers, data: { items } }), 'renglones')
    await ok(request.post(`${API_URL}/api/v1/price-lists/${general}/publish`, { headers, data: {} }), 'publicar')
  } else {
    await ok(request.post(`${API_URL}/api/v1/price-lists/${general}/items`, { headers, data: { items } }), 'renglones')
  }

  // el cliente de consignación (forma de pago Consignación) con su lista negociada: la crema a $2.99
  const cname = L(`Colmado La Esquina ${sfx}`, `Corner Grocery ${sfx}`)
  const cc = await ok<{ publicId: string; code?: string }>(request.post(`${API_URL}/api/v1/clients`, { headers, data: { name: cname, paymentTerm: 'CONSIGNMENT' } }), 'cliente de consignación')
  const negotiated = (await ok<{ publicId: string }>(request.post(`${API_URL}/api/v1/price-lists`, { headers, data: { name: L(`Precios negociados ${sfx}`, `Negotiated prices ${sfx}`), validFrom: tenantDay(-1), appliesToAll: false } }), 'lista negociada')).publicId
  await ok(request.post(`${API_URL}/api/v1/price-lists/${negotiated}/items`, { headers, data: { items: [{ productPublicId: cream.publicId, price: 2.99 }] } }), 'renglón negociado')
  await ok(request.post(`${API_URL}/api/v1/price-lists/${negotiated}/clients`, { headers, data: { clientPublicIds: [cc.publicId] } }), 'cliente de la lista')
  await ok(request.post(`${API_URL}/api/v1/price-lists/${negotiated}/publish`, { headers, data: {} }), 'publicar negociada')

  // el vendedor (Ventas) y el supervisor (Supervisor de ventas)
  const user = async (who: string, role: string, fullName: string) => {
    const u = { email: `${who}.${sfx.toLowerCase()}@teikem.local`, password: `${who[0].toUpperCase()}${who.slice(1)}_${sfx}_2026!x`, fullName }
    await ok(request.post(`${API_URL}/api/v1/users`, { headers, data: { email: u.email, fullName: u.fullName, password: u.password, roles: [role] } }), `usuario ${who}`)
    await headersOf(request, u)
    return u
  }
  const vendor = await user('vendedor', 'Sales', L(`Carlos Vendedor ${sfx}`, `Carlos Salesrep ${sfx}`))
  const supervisor = await user('supervisor', 'SalesSupervisor', L(`Marta Supervisora ${sfx}`, `Marta Supervisor ${sfx}`))

  return { sfx, headers, warehouseId, strips, monitor, cream, kit, generalListId: general, negotiatedListId: negotiated, consignClient: { publicId: cc.publicId, name: cname, code: cc.code ?? '' }, vendor, supervisor }
}

test.beforeAll(async ({ request, browser }, testInfo) => {
  const page = await browser.newPage()
  S = await seed(request, page, testInfo.project.name.startsWith('movil') ? 'M' : 'D')
  await page.close()
})

// ------------------------------------------------------------------ 1) Precios y ofertas

test('Precios y ofertas: la oferta como tarjeta con foto, la lista de precios y el editor del kit en promoción', async ({ page, isMobile }) => {
  test.skip(isMobile, 'capturas de escritorio; el móvil recorre el pedido a 360 px')
  await manualApiEnglish(page)
  await page.setViewportSize({ width: 1280, height: 900 })
  await loginAs(page, ADMIN)
  await page.goto('/money/prices')
  const card = page.locator('article.pl-offer', { hasText: S.kit.sku })
  await expect(card).toBeVisible({ timeout: 30_000 })
  await expect(card).toContainText('PRODIGY')
  await expect(card).toContainText('41.94')
  await expect(card.locator('img')).toBeVisible()
  expect(await noHScroll(page)).toBe(true)
  await cap(page, 'l228-precios-ofertas', { dark: true, height: 1000 })

  // la lista negociada: renglón y cliente
  await page.getByText(L(`Precios negociados ${S.sfx}`, `Negotiated prices ${S.sfx}`)).first().click()
  const listDlg = page.getByRole('dialog')
  await expect(listDlg).toContainText(S.cream.sku, { timeout: 20_000 })
  await expect.poll(() => listDlg.locator('input').evaluateAll((els) => els.some((e) => (e as HTMLInputElement).value === '2.99'))).toBe(true)
  await cap(page, 'l228-lista-precios', { target: listDlg, height: DLG_H })
  await listDlg.getByRole('button', { name: /Cerrar/ }).first().click()

  // el editor del kit con la marca «Es promoción», vigencia y foto
  await card.getByRole('button', { name: 'Editar oferta' }).click()
  const kitDlg = page.getByRole('dialog')
  await expect(kitDlg).toBeVisible({ timeout: 20_000 })
  await expect(kitDlg).toContainText(/Es promoción/)
  // la ventana pone el foco en el primer componente y abre su lista: se cierra para la foto
  await kitDlg.locator('.pb > p').first().click()
  await expect(kitDlg.getByRole('combobox').first()).toHaveValue(new RegExp(S.strips.sku))
  await cap(page, 'l228-kit-promocion', { target: kitDlg, height: DLG_H })
})

// ------------------------------------------------------------------ 2) el pedido guiado de punta a punta

test('Pedido guiado: cliente nuevo → oferta y productos → entrega con envío → firma → enviar → aprobación → despacho → factura → QuickBooks', async ({ page, request, browser, isMobile }) => {
  test.skip(isMobile, 'el recorrido completo es de escritorio; el móvil recorre los pasos a 360 px')
  const H = S.headers
  const clientName = L(`Farmacia Nueva ${S.sfx}`, `New Pharmacy ${S.sfx}`)
  const legalName = `${clientName} LLC`
  const email = `compras.${S.sfx.toLowerCase()}@ejemplo.test`

  // ---------------------------------------------------------------- el vendedor
  await manualApiEnglish(page)
  await page.setViewportSize({ width: 1280, height: 900 })
  await loginAs(page, S.vendor)
  await page.goto('/sales-orders')
  await page.getByRole('link', { name: 'Nuevo pedido' }).or(page.getByRole('button', { name: 'Nuevo pedido' })).first().click()
  await expect(page).toHaveURL(/\/sales-orders\/new-order/)
  await expect(page.getByRole('heading', { level: 1, name: 'Nuevo pedido' })).toBeVisible({ timeout: 20_000 })
  await expect(page.getByText('Paso 1 de 4: Cliente')).toBeVisible()

  // Paso 1: cliente nuevo (alta mínima con las dos fotos)
  await page.getByRole('button', { name: 'Cliente nuevo' }).click()
  const nc = page.getByRole('dialog', { name: 'Cliente nuevo' })
  await nc.getByLabel('Nombre del negocio').fill(clientName)
  await nc.getByLabel('DBA / razón social').fill(legalName)
  await nc.getByLabel('Persona de contacto').fill('Ana Rivera')
  await nc.getByRole('textbox', { name: 'Teléfono', exact: true }).fill('7875551234')
  await nc.getByLabel('Extensión').fill('12')
  await nc.getByLabel('Correo electrónico').fill(email)
  await nc.getByLabel('Registro de comerciante (SURI)').fill('SURI-4471-228')
  await nc.getByRole('textbox', { name: 'Línea 1', exact: true }).fill('Calle Luna 12')
  await nc.getByRole('textbox', { name: 'Línea 2', exact: true }).fill('Local 3')
  await nc.getByRole('combobox', { name: 'Pueblo' }).fill('00961')
  await page.getByRole('option', { name: /00961/ }).first().click()
  await nc.getByLabel('Forma de pago del cliente').selectOption('NET30')
  const doc = await pngOf(page, `<div style="width:480px;height:320px;background:#f4f1e8;font:600 26px serif;padding:30px;box-sizing:border-box">Registro de Comerciante<br/><span style="font:400 20px serif">SURI-4471-228 · ${clientName}</span></div>`)
  await nc.getByLabel('Foto del registro de comerciante').setInputFiles({ name: 'registro.png', mimeType: 'image/png', buffer: doc })
  await nc.getByLabel('Foto del certificado de revendedor').setInputFiles({ name: 'certificado.png', mimeType: 'image/png', buffer: doc })
  await cap(page, 'l228-cliente-nuevo', { target: nc, height: DLG_H })
  await nc.getByRole('button', { name: 'Crear cliente' }).click()
  await expect(nc).toBeHidden({ timeout: 30_000 })
  const picked = page.locator('.wz-picked')
  await expect(picked).toContainText(clientName)
  await expect(picked).toContainText('Prospecto: espera aprobación')
  // la forma de pago se prellenó con la del cliente (crédito a 30 días)
  await expect(page.getByLabel('Forma de pago')).toHaveValue('CREDIT')
  await expect(page.getByLabel('Término del crédito')).toHaveValue('NET30')
  await page.getByRole('switch', { name: 'Pedido urgente' }).check()
  await page.getByLabel('Nota del pedido').fill(L('Llamar antes de llegar. Entregar por la puerta lateral.', 'Call before arriving. Deliver at the side door.'))
  await cap(page, 'l228-paso1-cliente', { dark: true, height: WZ_H })
  await page.getByRole('button', { name: 'Siguiente' }).click()

  // Paso 2: productos por marca, la oferta y el carrito
  await expect(page.getByText('Paso 2 de 4: Productos')).toBeVisible({ timeout: 20_000 })
  await expect(page.getByText(/Borrador OV-\d+/)).toBeVisible()
  await page.getByRole('tab', { name: 'PRODIGY' }).click()
  const offer = page.locator('li.wz-offer', { hasText: S.kit.sku })
  await expect(offer).toContainText('41.94')
  await offer.getByRole('button', { name: /^Agregar uno de / }).click()
  await expect(offer.getByLabel(`Cantidad de ${S.kit.sku}`)).toHaveValue('1')
  // el precio de cada producto sale de la lista general (no del precio de venta)
  await expect(page.locator('li.wz-prod', { hasText: S.strips.sku })).toContainText('5.99')
  await cap(page, 'l228-paso2-productos', { dark: true, height: WZ_H })
  await page.getByRole('tab', { name: 'GLOBAL' }).click()
  await page.getByLabel(`Cantidad de ${S.cream.sku}`).fill('4')
  await page.locator('.wz-cart-toggle').click()
  const cart = page.getByRole('region', { name: 'Carrito' })
  await expect(cart).toContainText(S.kit.sku)
  await expect(cart).toContainText(S.cream.sku)
  await expect(page.locator('.wz-cart-toggle')).toContainText('2 producto(s), 5 unidad(es)')
  await expect(page.locator('.wz-cart-toggle')).toContainText('54.94')
  await cap(page, 'l228-carrito', { target: page.locator('.wz-foot') })
  await page.getByRole('button', { name: 'Siguiente' }).click()

  // Paso 3: entrega a la dirección física, con el envío calculado por el servidor ($12: 54.94 + IVU no llega a $120)
  await expect(page.getByText('Paso 3 de 4: Entrega')).toBeVisible({ timeout: 20_000 })
  await expect(page.getByRole('radio', { name: /Dirección física del cliente/ })).toBeChecked()
  await expect(page.getByRole('radio', { name: /Dirección física del cliente/ })).toBeChecked()
  const summary = page.locator('.wz-summary')
  const subtotal = 41.94 + 4 * 3.25
  const taxExpected = Math.round(41.94 * TAX_PCT) / 100 + Math.round(13 * TAX_PCT) / 100
  await expect(summary).toContainText(money(subtotal))
  await expect(summary).toContainText(money(SHIP_CHARGE))
  await expect(summary.locator('.wz-reason')).toBeVisible()
  await expect(summary.locator('.wz-total')).toContainText(money(subtotal + taxExpected + SHIP_CHARGE))
  await cap(page, 'l228-paso3-entrega', { dark: true, height: WZ_H })
  await page.getByRole('button', { name: 'Siguiente' }).click()

  // Paso 4: firma de quien autoriza y «Enviar pedido» con la contraseña
  await expect(page.getByText('Paso 4 de 4: Autorización')).toBeVisible({ timeout: 20_000 })
  await page.getByLabel('Nombre de quien autoriza').fill('Ana Rivera')
  const pad = page.locator('svg.sigpad-box')
  const box = (await pad.boundingBox())!
  await page.mouse.move(box.x + box.width * 0.15, box.y + box.height * 0.6)
  await page.mouse.down()
  for (let i = 0; i <= 24; i++) await page.mouse.move(box.x + box.width * (0.15 + i * 0.028), box.y + box.height * (0.6 - 0.25 * Math.sin(i / 2.2)), { steps: 2 })
  await page.mouse.up()
  await expect(pad).toHaveAttribute('aria-label', /firmado/)
  await cap(page, 'l228-paso4-firma', { dark: true, height: WZ_H })
  await page.getByRole('button', { name: 'Enviar pedido' }).click()
  const pwd = page.getByRole('dialog', { name: 'Confirme con su contraseña' })
  await expect(pwd).toBeVisible({ timeout: 20_000 })
  await pwd.getByLabel('Contraseña', { exact: true }).fill('Clave-Equivocada-1')
  await pwd.getByRole('button', { name: 'Enviar pedido' }).click()
  await expect(pwd.locator('#wz-pwd-err')).toBeVisible({ timeout: 20_000 })
  await pwd.getByLabel('Contraseña', { exact: true }).fill(S.vendor.password)
  await cap(page, 'l228-contrasena', { target: pwd, height: DLG_H })
  await pwd.getByRole('button', { name: 'Enviar pedido' }).click()
  await expect(page.getByRole('heading', { name: 'Pedido enviado' })).toBeVisible({ timeout: 30_000 })
  const number = ((await page.locator('.wz-done-number').textContent()) ?? '').trim()
  expect(number).toMatch(/^OV-\d+$/)
  await expect(page.getByText('El cliente es prospecto: el pedido quedó reservado pero espera la aprobación del cliente')).toBeVisible()
  await cap(page, 'l228-enviado', { dark: true, height: WZ_H })

  // la orden: solicitada, urgente, espera aprobación; despachar se rechaza mientras el cliente sea prospecto
  const list = await ok<{ items: { publicId: string; number: string; status: string; clientPublicId: string }[] }>(request.get(`${API_URL}/api/v1/sales-orders?q=${encodeURIComponent(number)}`, { headers: H }), 'orden')
  const order = list.items.find((o) => o.number === number)!
  expect(order.status).toBe('REQUESTED')
  const detail = await ok<{ lines: { id: number; qtyReserved: number }[]; terms: { awaitingClientApproval: boolean; isUrgent: boolean; shippingCharge?: number; submittedWithAuth?: string; signatureFilePublicId?: string | null } }>(
    request.get(`${API_URL}/api/v1/sales-orders/${order.publicId}`, { headers: H }), 'detalle')
  expect(detail.terms.awaitingClientApproval).toBe(true)
  expect(detail.terms.isUrgent).toBe(true)
  expect(detail.terms.signatureFilePublicId).toBeTruthy()
  const blocked = await request.post(`${API_URL}/api/v1/sales-orders/${order.publicId}/dispatch`, { headers: H, data: { lines: detail.lines.map((l) => ({ lineId: l.id, quantity: l.qtyReserved })) } })
  expect(blocked.ok(), 'despachar con el cliente prospecto se rechaza').toBeFalsy()
  const blockedText = await blocked.text()

  // ---------------------------------------------------------------- el supervisor aprueba al cliente en su ficha
  const sup = await browser.newContext({ locale: 'es-PR', viewport: { width: 1280, height: 900 }, deviceScaleFactor: 2 })
  const sp = await sup.newPage()
  await manualApiEnglish(sp)
  await loginAs(sp, S.supervisor)
  await sp.goto(`/catalog/clients?client=${order.clientPublicId}`)
  await openClientPanels(sp)
  const pending = sp.getByText('Este cliente lo dio de alta un vendedor y espera su aprobación')
  await expect(pending).toBeVisible({ timeout: 30_000 })
  await pending.scrollIntoViewIfNeeded()
  await cap(sp, 'l228-ficha-prospecto', { target: sp.locator('section.kit-panel', { has: pending }).first(), height: DLG_H })
  await sp.getByRole('button', { name: 'Aprobar', exact: true }).click()
  const ap = sp.getByRole('dialog', { name: `Aprobar a ${clientName}` })
  await ap.getByLabel('Límite de crédito').fill('1000')
  await ap.getByLabel('Término de pago').selectOption('NET30')
  await ap.getByLabel('Comentario (opcional)').fill(L('Visitado por el vendedor; documentos al día.', 'Visited by the rep; documents up to date.'))
  await cap(sp, 'l228-aprobar-cliente', { target: ap, height: DLG_H })
  await ap.getByRole('button', { name: 'Aprobar', exact: true }).click()
  await expect(sp.locator('.toast.on').filter({ hasText: 'quedó aprobado' })).toHaveCount(1, { timeout: 20_000 })
  await sup.close()
  const after = await ok<{ terms: { awaitingClientApproval: boolean } }>(request.get(`${API_URL}/api/v1/sales-orders/${order.publicId}`, { headers: H }), 'detalle')
  expect(after.terms.awaitingClientApproval).toBe(false)

  // ---------------------------------------------------------------- la oficina: despachar desde la orden
  await switchUser(page, ADMIN)
  await page.goto(`/sales-orders?order=${order.publicId}`)
  const od = page.getByRole('dialog').filter({ hasText: number }).first()
  await expect(od).toBeVisible({ timeout: 30_000 })
  await expect(od).toContainText('Urgente')
  await cap(page, 'l228-orden', { target: od, height: DLG_H })
  await od.getByRole('button', { name: 'Despachar', exact: true }).click()
  const dd = page.getByRole('dialog', { name: `Despachar la orden ${number}` })
  await dd.getByRole('button', { name: 'Despachar', exact: true }).click()
  await expect(page.locator('.toast.on').filter({ hasText: /Despacho .* creado/ })).toHaveCount(1, { timeout: 30_000 })
  await page.keyboard.press('Escape')

  // ---------------------------------------------------------------- factura: corrida del cliente → revisar → aprobar (reautenticación)
  await page.goto('/money/billing')
  await expect(page.getByRole('heading', { level: 1, name: 'Facturación' })).toBeVisible({ timeout: 20_000 })
  await page.getByRole('button', { name: 'Nueva corrida' }).click()
  const form = page.getByRole('dialog', { name: 'Nueva corrida' })
  await form.getByRole('combobox').first().fill(clientName.slice(0, 14))
  await page.getByRole('option').filter({ hasText: clientName }).first().click()
  await form.getByRole('button', { name: 'Generar' }).click()
  const run = page.getByRole('dialog', { name: /^Corrida #/ })
  await expect(run).toContainText(clientName, { timeout: 30_000 })
  await run.getByRole('button', { name: 'Marcar como revisada' }).click()
  await expect(run).toContainText(/Revisada|Reviewed/)
  await run.getByRole('button', { name: 'Aprobar y emitir' }).click()
  await maybeReauth(page)
  await expect(run).toContainText(/Aprobada|Approved/, { timeout: 30_000 })
  await run.getByRole('button', { name: 'Cerrar' }).first().click()

  const invoices = await ok<{ items: { publicId: string; number: string; clientName: string; status: string }[] }>(request.get(`${API_URL}/api/v1/billing/invoices?clientPublicId=${order.clientPublicId}&take=50`, { headers: H }), 'facturas')
  const invRow = invoices.items.find((i) => i.clientName === clientName)!
  expect(invRow, 'la factura del cliente nuevo').toBeTruthy()
  type InvLine = { description: string; chargeType: string; sku?: string | null; quantity: number; unitAmount: number; lineSubtotal: number; taxAmount: number }
  const inv = await ok<{ number: string; issueDate: string; dueDate: string; subtotal: number; taxAmount: number; total: number; lines: InvLine[] }>(request.get(`${API_URL}/api/v1/billing/invoices/${invRow.publicId}`, { headers: H }), 'factura')
  expect(inv.subtotal).toBeCloseTo(subtotal + SHIP_CHARGE, 2)
  expect(inv.taxAmount).toBeCloseTo(taxExpected, 2)
  const client = await ok<{ code: string; creditLimit: number }>(request.get(`${API_URL}/api/v1/clients/${order.clientPublicId}`, { headers: H }), 'cliente')

  // ---------------------------------------------------------------- QuickBooks: ver datos, exportar, abrir el IIF y compararlo, confirmar, quitar la marca y marcar
  await page.goto('/sales-orders')
  await page.getByRole('tab', { name: 'QuickBooks' }).click()
  const qbRow = () => page.locator('tr, .dt-cards > li').filter({ hasText: inv.number }).first()
  await expect(qbRow()).toContainText(clientName, { timeout: 30_000 })
  await expect(qbRow()).toContainText('Cliente nuevo')
  await expect(qbRow()).toContainText('3 productos nuevos')
  expect(await noHScroll(page)).toBe(true)
  await cap(page, 'l228-qb-lista', { dark: true })
  await qbRow().getByRole('button', { name: `Ver los datos nuevos de la factura ${inv.number}` }).click()
  const nd = page.getByRole('dialog', { name: `Datos nuevos para QuickBooks · ${inv.number}` })
  await expect(nd).toContainText('SURI-4471-228')
  await expect(nd).toContainText('Net 30')
  await cap(page, 'l228-qb-ver-datos', { target: nd, height: DLG_H })
  await nd.getByRole('button', { name: 'Cerrar' }).first().click()

  await page.getByRole('button', { name: 'Exportar a QuickBooks' }).click()
  const ex = page.getByRole('dialog', { name: 'Exportar a QuickBooks' })
  await expect(ex.getByRole('checkbox', { name: 'Incluir los clientes y productos nuevos' })).toBeChecked()
  await cap(page, 'l228-qb-exportar', { target: ex, height: DLG_H })
  const [download] = await Promise.all([page.waitForEvent('download', { timeout: 60_000 }), ex.getByRole('button', { name: 'Exportar y bajar el archivo' }).click()])
  expect(download.suggestedFilename()).toMatch(/^teikem-quickbooks-\d{8}-\d{4}\.iif$/)
  // QuickBooks Desktop lee Windows-1252: se decodifica igual (la «ó» de Bayamón, la raya del cargo de envío)
  const iif = new TextDecoder('windows-1252').decode(readFileSync(await download.path()))
  await test.info().attach('teikem-quickbooks.iif', { body: iif, contentType: 'text/plain' })
  if (process.env.L228_IIF_OUT) writeFileSync(process.env.L228_IIF_OUT, iif)
  const rows = iif.split('\r\n')
  expect(rows[rows.length - 1], 'renglones con CRLF y el último terminado').toBe('')

  // lo esperado, armado con lo que se tecleó y con los montos de la factura
  const prodRow = (p: Prod) => ['INVITEM', p.sku, 'INVENTORY', p.name, p.name, 'Sales', 'Inventory Asset', 'Cost of Goods Sold', priceText(p.salePrice), priceText(p.cost), 'Y'].join('\t')
  for (const p of [S.strips, S.monitor, S.cream]) expect(rows, `producto nuevo ${p.sku}`).toContain(prodRow(p))
  expect(rows.some((r) => r.startsWith(`INVITEM\t${S.kit.sku}\t`)), 'el kit no viaja como ítem').toBe(false)
  const addr = [legalName, 'Calle Luna 12', 'Local 3', 'Bayamón PR 00961', '']
  const custRow = rows.find((r) => r.startsWith(`CUST\t${clientName}\t`))
  expect(custRow, 'el cliente nuevo').toBeTruthy()
  const cust = custRow!.split('\t')
  expect(cust.slice(0, 13)).toEqual(['CUST', clientName, legalName, ...addr, ...addr])
  expect(cust.slice(13, 15)).toEqual(['7875551234 x12', ''])
  expect(cust.slice(15)).toEqual([email, 'Ana Rivera', 'Net 30', money(client.creditLimit), 'SURI-4471-228', client.code])

  const start = rows.findIndex((r) => r.startsWith('TRNS\t') && r.split('\t')[7] === inv.number)
  expect(start, `la factura ${inv.number} en el archivo`).toBeGreaterThan(0)
  const end = rows.indexOf('ENDTRNS', start)
  const block = rows.slice(start, end + 1)
  const date = usDate(inv.issueDate)
  const [stripShare, monitorShare] = prorate(41.94, [6 * S.strips.salePrice, 1 * S.monitor.salePrice])
  const spl = (amount: number, memo: string, qty: number, price: number, item: string, taxable: string) =>
    ['SPL', '', 'INVOICE', date, 'Sales', '', money(-amount), inv.number, memo, 'N', String(-qty), priceText(price), item, taxable, ''].join('\t')
  const shipLine = inv.lines.find((l) => l.chargeType !== 'PRODUCT' && !l.sku)!
  const expected = [
    ['TRNS', '', 'INVOICE', date, 'Accounts Receivable', clientName, money(inv.subtotal + inv.taxAmount), inv.number, number, 'N', 'N', 'Y', usDate(inv.dueDate), 'Net 30'].join('\t'),
    spl(stripShare, `${S.strips.name} (${S.kit.sku})`, 6, Math.round((stripShare / 6) * 100000) / 100000, S.strips.sku, 'Y'),
    spl(monitorShare, `${S.monitor.name} (${S.kit.sku})`, 1, monitorShare, S.monitor.sku, 'Y'),
    spl(13, S.cream.name, 4, 3.25, S.cream.sku, 'Y'),
    spl(SHIP_CHARGE, shipLine.description, 1, SHIP_CHARGE, 'Shipping', 'N'),
    ['SPL', '', 'INVOICE', date, 'Sales Tax Payable', '', money(-inv.taxAmount), inv.number, 'IVU', 'N', '', '', 'IVU', 'N', 'AUTOSTAX'].join('\t'),
    'ENDTRNS',
  ]
  expect(block).toEqual(expected)
  expect(stripShare + monitorShare, 'el kit desglosado cierra al centavo').toBeCloseTo(41.94, 2)

  // «¿Ya importaste estas N?»: se confirma
  const ask = page.locator('section.kit-panel', { has: page.getByRole('heading', { name: /^¿Ya importaste estas \d+\?/ }) })
  await expect(ask).toBeVisible({ timeout: 20_000 })
  await cap(page, 'l228-qb-ya-importaste', { target: ask, height: DLG_H })
  await ask.getByRole('button', { name: /^Sí, ya están/ }).click()
  await expect(page.locator('.toast.on').first()).toBeVisible({ timeout: 20_000 })
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: inv.number })).toHaveCount(0, { timeout: 20_000 })

  // el histórico: quitarle la marca → vuelve a «por pasar» con «Salió el…» y el aviso; se marca a mano
  await page.getByRole('button', { name: 'Ver las ya pasadas' }).click()
  const hRow = () => page.locator('tr, .dt-cards > li').filter({ hasText: inv.number }).first()
  await expect(hRow()).toContainText(clientName, { timeout: 20_000 })
  await cap(page, 'l228-qb-historico', { dark: true })
  await hRow().getByRole('button', { name: 'Quitar la marca' }).click()
  await page.getByRole('dialog', { name: `Quitar la marca de ${inv.number}` }).getByRole('button', { name: 'Quitar la marca' }).click()
  await expect(page.locator('.toast.on').filter({ hasText: 'volvió a «por pasar»' })).toHaveCount(1, { timeout: 20_000 })
  await page.getByRole('button', { name: 'Volver a las por pasar' }).click()
  await expect(qbRow()).toContainText('Salió el', { timeout: 20_000 })
  await expect(page.locator('.qb-note').first()).toContainText('ya sali')
  await cap(page, 'l228-qb-ya-salieron')
  await qbRow().getByRole('button', { name: 'Marcar como en QuickBooks' }).click()
  await expect(page.locator('.toast.on').filter({ hasText: 'marcada como en QuickBooks' })).toHaveCount(1, { timeout: 20_000 })
  await expect(page.locator('tr, .dt-cards > li').filter({ hasText: inv.number })).toHaveCount(0, { timeout: 20_000 })

  // Ajustes → Operación: Ventas (envío) y QuickBooks
  await page.goto('/system/settings?tab=ops')
  const salesPanel = page.locator('section.kit-panel', { has: page.getByRole('heading', { name: 'Ventas', exact: true }) })
  const qbPanel = page.locator('section.kit-panel', { has: page.getByRole('heading', { name: 'QuickBooks', exact: true }) })
  await expect(qbPanel).toBeVisible({ timeout: 30_000 })
  await cap(page, 'l228-ajustes-ventas', { target: salesPanel, height: DLG_H })
  await cap(page, 'l228-ajustes-quickbooks', { target: qbPanel, height: DLG_H })

  test.info().annotations.push({ type: 'despacho bloqueado', description: blockedText.slice(0, 300) })
})

// ------------------------------------------------------------------ 3) consignación

test('Consignación: pedido en consignación, pago parcial y total, saldo en cero', async ({ page, request, isMobile }) => {
  test.skip(isMobile, 'escritorio')
  const H = S.headers
  await manualApiEnglish(page)
  await page.setViewportSize({ width: 1280, height: 900 })
  await loginAs(page, S.vendor)
  await page.goto('/sales-orders/new-order')
  await page.getByLabel('Buscar cliente').fill(S.consignClient.name.slice(0, 16))
  await page.getByRole('list', { name: 'Clientes encontrados' }).getByRole('button', { name: new RegExp(S.consignClient.name) }).click()
  await expect(page.getByLabel('Forma de pago')).toHaveValue('CONSIGNMENT', { timeout: 20_000 })
  await page.getByRole('button', { name: 'Siguiente' }).click()
  await expect(page.getByText('Paso 2 de 4: Productos')).toBeVisible({ timeout: 20_000 })
  await page.getByRole('tab', { name: 'GLOBAL' }).click()
  // el precio negociado del cliente gana a la lista general
  await expect(page.locator('li.wz-prod', { hasText: S.cream.sku })).toContainText('2.99')
  await page.getByLabel(`Cantidad de ${S.cream.sku}`).fill('10')
  await page.getByRole('button', { name: 'Siguiente' }).click()
  await expect(page.getByText('Paso 3 de 4: Entrega')).toBeVisible({ timeout: 20_000 })
  await page.getByRole('radio', { name: /^Recogido/ }).check()
  await page.getByRole('switch', { name: 'En consignación' }).check()
  const summary = page.locator('.wz-summary')
  await expect(summary).toContainText('Sin cargo', { timeout: 20_000 })
  await expect(summary).toContainText('29.90')
  await cap(page, 'l228-consignacion-entrega')
  await page.getByRole('button', { name: 'Siguiente' }).click()
  await expect(page.getByText('Paso 4 de 4: Autorización')).toBeVisible({ timeout: 20_000 })
  await page.getByRole('button', { name: 'Enviar pedido' }).click()
  const pwd = page.getByRole('dialog', { name: 'Confirme con su contraseña' })
  await pwd.getByLabel('Contraseña', { exact: true }).fill(S.vendor.password)
  await pwd.getByRole('button', { name: 'Enviar pedido' }).click()
  await expect(page.getByRole('heading', { name: 'Pedido enviado' })).toBeVisible({ timeout: 30_000 })
  const number = ((await page.locator('.wz-done-number').textContent()) ?? '').trim()

  // despacho y factura por API (la misma secuencia que el paso 2 hizo por la pantalla)
  const list = await ok<{ items: { publicId: string; number: string }[] }>(request.get(`${API_URL}/api/v1/sales-orders?q=${encodeURIComponent(number)}`, { headers: H }), 'orden')
  const orderId = list.items.find((o) => o.number === number)!.publicId
  const detail = await ok<{ lines: { id: number; qtyReserved: number }[]; terms: { isConsignment: boolean } }>(request.get(`${API_URL}/api/v1/sales-orders/${orderId}`, { headers: H }), 'detalle')
  expect(detail.terms.isConsignment).toBe(true)
  await ok(request.post(`${API_URL}/api/v1/sales-orders/${orderId}/dispatch`, { headers: H, data: { lines: detail.lines.map((l) => ({ lineId: l.id, quantity: l.qtyReserved })) } }), 'despacho')
  const runDto = await ok<{ id: number }>(request.post(`${API_URL}/api/v1/billing/runs`, { headers: H, data: { clientPublicId: S.consignClient.publicId, periodStart: tenantDay(-30), periodEnd: tenantDay(0) } }), 'corrida')
  await ok(request.post(`${API_URL}/api/v1/billing/runs/${runDto.id}/review`, { headers: H, data: {} }), 'revisar')
  await ok(request.post(`${API_URL}/api/v1/billing/runs/${runDto.id}/approve`, { headers: await reauthHeaders(request, H), data: {} }), 'aprobar')
  const cons = await ok<{ items: { publicId: string; number: string; consigned: number; balance: number }[] }>(request.get(`${API_URL}/api/v1/billing/consignment/invoices?clientPublicId=${S.consignClient.publicId}&balance=ALL&take=50`, { headers: H }), 'consignación')
  const ci = cons.items[0]
  expect(ci, 'la factura en consignación').toBeTruthy()
  const total = ci.consigned
  expect(total).toBeCloseTo(29.9 + Math.round(29.9 * TAX_PCT) / 100, 2)

  // Facturación → Consignación: pago parcial y luego el resto
  await switchUser(page, ADMIN)
  await page.goto('/money/billing')
  await page.getByRole('tab', { name: 'Consignación' }).click()
  const row = () => page.locator('tr, .dt-cards > li').filter({ hasText: ci.number }).first()
  await expect(row()).toContainText(money(total), { timeout: 30_000 })
  await cap(page, 'l228-consignacion-lista', { dark: true })
  const pay = async (amount: string, ref: string, shotName?: string) => {
    await row().getByRole('button', { name: 'Registrar pago' }).click()
    const dlg = page.getByRole('dialog').filter({ has: page.locator('#cp-amount') })
    await dlg.locator('#cp-amount').fill(amount)
    const method = dlg.locator('#cp-method')
    if ((await method.inputValue()) === '') await method.selectOption({ index: 1 })
    await dlg.locator('#cp-ref').fill(ref)
    if (shotName) await cap(page, shotName, { target: dlg })
    await dlg.getByRole('button', { name: /Guardar|Registrar/ }).last().click()
    await expect(dlg).toBeHidden({ timeout: 20_000 })
  }
  await pay('10.00', 'CHK-1001', 'l228-consignacion-pago')
  await expect(row()).toContainText(money(total - 10), { timeout: 20_000 })
  await cap(page, 'l228-consignacion-parcial')
  await pay(money(total - 10), 'CHK-1002')
  const after = await ok<{ items: { number: string; paid: number; balance: number }[] }>(request.get(`${API_URL}/api/v1/billing/consignment/invoices?clientPublicId=${S.consignClient.publicId}&balance=ALL&take=50`, { headers: H }), 'consignación')
  const done = after.items.find((i) => i.number === ci.number)!
  expect(done.balance).toBe(0)
  expect(done.paid).toBeCloseTo(total, 2)
  await page.locator('select[aria-label="Filtrar por saldo"]').selectOption('PAID')
  await expect(row()).toBeVisible({ timeout: 20_000 })
  await cap(page, 'l228-consignacion-pagada')
})

// ------------------------------------------------------------------ 4) a 360 px

test('Pedido guiado a 360 px: los cuatro pasos sin desplazamiento horizontal y el envío', async ({ page, isMobile }) => {
  test.skip(!isMobile, 'solo en el proyecto móvil (360 px)')
  await manualApiEnglish(page)
  await loginAs(page, S.vendor)
  await page.goto('/sales-orders/new-order')
  await page.getByLabel('Buscar cliente').fill(S.consignClient.name.slice(0, 16))
  await page.getByRole('list', { name: 'Clientes encontrados' }).getByRole('button', { name: new RegExp(S.consignClient.name) }).click()
  await expect(page.getByLabel('Forma de pago')).toHaveValue('CONSIGNMENT', { timeout: 20_000 })
  expect(await noHScroll(page)).toBe(true)
  await cap(page, 'l228-movil-paso1')
  await page.getByRole('button', { name: 'Siguiente' }).click()
  await expect(page.getByText('Paso 2 de 4: Productos')).toBeVisible({ timeout: 20_000 })
  await page.getByRole('tab', { name: 'PRODIGY' }).click()
  await page.locator('li.wz-offer', { hasText: S.kit.sku }).getByRole('button', { name: /^Agregar uno de / }).click()
  await page.getByRole('tab', { name: 'GLOBAL' }).click()
  await page.getByLabel(`Cantidad de ${S.cream.sku}`).fill('2')
  expect(await noHScroll(page)).toBe(true)
  // hallazgos de este recorrido (corregidos): el buscador de la marca medía ~200 px de alto en la columna del paso, y en el tema oscuro las
  // casillas de cantidad salían blancas (sin los colores del tema)
  expect((await page.locator('.wz-step > .qbox').boundingBox())!.height).toBeLessThan(60)
  const qtyBg = async (theme: string) =>
    page.evaluate((th) => {
      document.documentElement.setAttribute('data-theme', th)
      return getComputedStyle(document.querySelector('.wz-qty')!).backgroundColor
    }, theme)
  expect(await qtyBg('dark')).not.toBe('rgb(255, 255, 255)')
  await qtyBg('light')
  await cap(page, 'l228-movil-paso2')
  await page.locator('.wz-cart-toggle').click()
  await expect(page.getByRole('region', { name: 'Carrito' })).toContainText(S.kit.sku)
  expect(await noHScroll(page)).toBe(true)
  await cap(page, 'l228-movil-carrito')
  await page.getByRole('button', { name: 'Siguiente' }).click()
  await expect(page.getByText('Paso 3 de 4: Entrega')).toBeVisible({ timeout: 20_000 })
  await page.getByRole('radio', { name: /^Recogido/ }).check()
  await expect(page.locator('.wz-summary')).toContainText('Sin cargo', { timeout: 20_000 })
  expect(await noHScroll(page)).toBe(true)
  await cap(page, 'l228-movil-paso3')
  await page.getByRole('button', { name: 'Siguiente' }).click()
  await expect(page.getByText('Paso 4 de 4: Autorización')).toBeVisible({ timeout: 20_000 })
  const pad = page.locator('svg.sigpad-box')
  const box = (await pad.boundingBox())!
  await page.mouse.move(box.x + 20, box.y + box.height * 0.6)
  await page.mouse.down()
  for (let i = 0; i <= 16; i++) await page.mouse.move(box.x + 20 + i * ((box.width - 40) / 16), box.y + box.height * (0.6 - 0.2 * Math.sin(i / 1.8)), { steps: 2 })
  await page.mouse.up()
  expect(await noHScroll(page)).toBe(true)
  await cap(page, 'l228-movil-paso4')
  await page.getByRole('button', { name: 'Enviar pedido' }).click()
  const pwd = page.getByRole('dialog', { name: 'Confirme con su contraseña' })
  await pwd.getByLabel('Contraseña', { exact: true }).fill(S.vendor.password)
  await pwd.getByRole('button', { name: 'Enviar pedido' }).click()
  await expect(page.getByRole('heading', { name: 'Pedido enviado' })).toBeVisible({ timeout: 30_000 })
  expect(await noHScroll(page)).toBe(true)
  await cap(page, 'l228-movil-enviado')
})
