// Recorrido del Lote F7A (docs/frontend/loteF7A-plan.json, "recorrido") contra el API real (db-init hecho, API en API_URL,
// por defecto http://localhost:5000). Proyecto 'escritorio': pasos 1-6, en serie (usan la categoría, el producto, el ajuste
// y la recolección que prepara el beforeAll / el paso 4); proyecto 'movil' (Pixel 7 a 360 px): paso 7, con sus propios datos.
// Nada de este recorrido rompe otra corrida: la categoría (CAT-{timestamp}) y el producto (PROD-{timestamp}) son nuevos
// en cada corrida y el afterAll deshace lo que movió inventario (borra la recolección, que regresa sus unidades; ajusta −3)
// y da de baja el producto y la categoría. El idioma solo vive en el localStorage del contexto del navegador.
// Capturas para el manual (docs/manual/frontend/img/f7a-<pantalla>.png): las del proyecto 'escritorio' (más la de móvil).
import { mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { expect, test, type APIRequestContext, type Locator, type Page } from '@playwright/test'
import type { components } from '../src/kernel/api/schema'

type AuthResultDto = components['schemas']['AuthResultDto']
type WarehouseDto = components['schemas']['WarehouseDto']
type WarehouseBinDto = components['schemas']['WarehouseBinDto']
type ProductCategoryDto = components['schemas']['ProductCategoryDto']
type ProductDetailDto = components['schemas']['ProductDetailDto']
type PickBatchDto = components['schemas']['PickBatchDto']

const API_URL = process.env.API_URL ?? 'http://localhost:5000'
const PASSWORD = process.env.TEIKEM_ADMIN_PASSWORD ?? 'Teikem_Admin_2026!'
const ADMIN = { email: 'admin@teikem.local', password: PASSWORD }
const DISPATCH = { email: 'despacho@teikem.local', password: PASSWORD }

const STAMP = Date.now()
const CATEGORY = `CAT-${STAMP}`
const SKU = `PROD-${STAMP}`
const PRODUCT_NAME = `Producto e2e F7A ${STAMP}`

// Interfaz en español (el idioma inicial sale del navegador si el usuario no eligió otro).
test.use({ locale: 'es-PR' })

const IMG_DIR = fileURLToPath(new URL('../../docs/manual/frontend/img/', import.meta.url))

/** Captura de la pantalla actual para el manual, cuando ya no hay peticiones pendientes. `mask` tapa datos variables. */
async function shot(page: Page, name: string, opts: { mask?: Locator[] } = {}) {
  await page.waitForLoadState('networkidle')
  mkdirSync(IMG_DIR, { recursive: true })
  await page.screenshot({
    path: `${IMG_DIR}f7a-${name}.png`,
    animations: 'disabled',
    caret: 'hide',
    mask: opts.mask,
    maskColor: '#2a3346',
  })
}

/**
 * Datos que prepara cada proyecto (y que el afterAll deshace). Cada proyecto corre en su propio worker, así que cada uno
 * tiene su copia: el de escritorio crea CAT-{timestamp}/PROD-{timestamp} y el móvil CAT-M-{timestamp}/PROD-M-{timestamp}.
 */
const data = {
  token: '',
  category: CATEGORY,
  sku: SKU,
  warehousePublicId: '',
  binId: 0,
  categoryId: 0,
  productPublicId: '',
  adjusted: false,
  pick: null as PickBatchDto | null,
}

/** Token de acceso del admin por el API (para preparar y limpiar datos del recorrido). */
async function apiToken(request: APIRequestContext): Promise<string> {
  let res = await request.post(`${API_URL}/api/v1/auth/login`, { data: ADMIN })
  expect(res.ok()).toBeTruthy()
  let body = (await res.json()) as AuthResultDto
  if (body.status === 'tenant_selection') {
    const tenant = body.tenants?.find((t) => t.isDefault) ?? body.tenants?.[0]
    res = await request.post(`${API_URL}/api/v1/auth/login`, { data: { ...ADMIN, tenantId: tenant?.tenantId } })
    body = (await res.json()) as AuthResultDto
  }
  expect(body.status).toBe('ok')
  return body.tokens?.accessToken ?? ''
}

const auth = () => ({ Authorization: `Bearer ${data.token}` })

/**
 * Categoría nueva con un producto (mínimo 5) y +3 en una posición de ALM-01: la categoría queda con 3 en mano y 1 bajo
 * mínimo. `variant` distingue los datos del proyecto móvil (prefijo, para que un SKU no contenga al otro).
 */
async function prepareData(request: APIRequestContext, variant = '') {
  data.category = variant ? `CAT-${variant}-${STAMP}` : CATEGORY
  data.sku = variant ? `PROD-${variant}-${STAMP}` : SKU
  data.token = await apiToken(request)
  const whRes = await request.get(`${API_URL}/api/v1/warehouses`, { headers: auth() })
  expect(whRes.ok()).toBeTruthy()
  const alm01 = ((await whRes.json()) as WarehouseDto[]).find((w) => w.code === 'ALM-01')
  expect(alm01?.publicId).toBeTruthy()
  data.warehousePublicId = alm01?.publicId ?? ''
  const binRes = await request.get(`${API_URL}/api/v1/warehouses/${data.warehousePublicId}/bins`, { headers: auth() })
  expect(binRes.ok()).toBeTruthy()
  const bin = ((await binRes.json()) as WarehouseBinDto[]).find((b) => b.isActive !== false)
  expect(bin?.id).toBeTruthy()
  data.binId = bin?.id ?? 0

  const catRes = await request.post(`${API_URL}/api/v1/product-categories`, { headers: auth(), data: { name: data.category } })
  expect(catRes.ok()).toBeTruthy()
  data.categoryId = ((await catRes.json()) as ProductCategoryDto).id ?? 0

  const prodRes = await request.post(`${API_URL}/api/v1/products`, {
    headers: auth(),
    data: { sku: data.sku, name: variant ? `${PRODUCT_NAME} ${variant}` : PRODUCT_NAME, trackingType: 'NONE', categoryId: data.categoryId, minQty: 5 },
  })
  expect(prodRes.ok()).toBeTruthy()
  data.productPublicId = ((await prodRes.json()) as ProductDetailDto).product?.publicId ?? ''
  expect(data.productPublicId).toBeTruthy()

  const adjRes = await request.post(`${API_URL}/api/v1/inventory/adjustments`, {
    headers: auth(),
    data: { productPublicId: data.productPublicId, warehousePublicId: data.warehousePublicId, binId: data.binId, quantity: 3, reason: 'FOUND', notes: `Recorrido e2e F7A ${STAMP}` },
  })
  expect(adjRes.ok()).toBeTruthy()
  data.adjusted = true
}

/** Deshace lo que movió inventario y da de baja lo creado (sin fallar el recorrido si algo ya no aplica). */
async function cleanupData(request: APIRequestContext) {
  if (!data.token) return
  if (data.pick?.publicId)
    await request.delete(`${API_URL}/api/v1/pick-batches/${data.pick.publicId}`, {
      headers: auth(),
      data: { comment: `Limpieza del recorrido e2e F7A ${STAMP}`, rowVersion: data.pick.rowVersion },
    })
  if (data.adjusted)
    await request.post(`${API_URL}/api/v1/inventory/adjustments`, {
      headers: auth(),
      data: { productPublicId: data.productPublicId, warehousePublicId: data.warehousePublicId, binId: data.binId, quantity: -3, reason: 'LOSS', notes: `Limpieza e2e F7A ${STAMP}` },
    })
  if (data.productPublicId) await request.post(`${API_URL}/api/v1/products/${data.productPublicId}/deactivate`, { headers: auth() })
  if (data.categoryId) await request.post(`${API_URL}/api/v1/product-categories/${data.categoryId}/deactivate`, { headers: auth() })
}

async function login(page: Page, user: { email: string; password: string }) {
  await page.goto('/login')
  await page.getByLabel('Correo electrónico').fill(user.email)
  await page.getByLabel('Contraseña').fill(user.password)
  await page.getByRole('button', { name: 'Entrar' }).click()
  // Si el usuario pertenece a varias compañías, se elige la predeterminada.
  await page.waitForURL((url) => url.pathname !== '/login')
  if (new URL(page.url()).pathname === '/select-tenant') {
    const def = page.locator('.tenant-list button', { hasText: 'Predeterminada' })
    await ((await def.count()) > 0 ? def.first() : page.locator('.tenant-list button').first()).click()
  }
  await page.waitForURL((url) => url.pathname === '/')
}

/** Panel de Pulso (`section.pal` del kit) con ese título (h2). */
function pulseSection(page: Page, title: string | RegExp): Locator {
  return page.locator('section.pal').filter({ has: page.getByRole('heading', { level: 2, name: title, exact: typeof title === 'string' }) })
}

/** Tarjeta del panel Almacén (role=group con la etiqueta de la tarjeta). */
function tile(page: Page, name: string): Locator {
  return pulseSection(page, 'Almacén').getByRole('group', { name, exact: true })
}

/** Elige una opción del control 'Categoría o producto' escribiendo en su buscador. */
async function pickCategoryOrProduct(page: Page, text: string, option: RegExp, screenshot?: string) {
  await pulseSection(page, 'Almacén').getByRole('button', { name: /^Categoría o producto/ }).click()
  await page.getByRole('combobox', { name: 'Buscar categoría o producto…' }).fill(text)
  await expect(page.getByRole('option', { name: option })).toBeVisible()
  if (screenshot) {
    // la lista abierta a la vista para la captura (el foco del buscador puede mover el scroll del contenido)
    await page.getByRole('listbox', { name: 'Categoría o producto' }).scrollIntoViewIfNeeded()
    await shot(page, screenshot)
  }
  await page.getByRole('option', { name: option }).click()
}

/** Lleva a la vista el panel de Pulso con ese título (van debajo de los indicadores del API) para la captura. */
async function scrollToPanel(page: Page, title: string) {
  await page.getByRole('heading', { level: 2, name: title, exact: true }).scrollIntoViewIfNeeded()
}

/**
 * Sin scroll horizontal de página y sin contenido recortado (igual que en loteF1.spec.ts): se mide cada contenedor que
 * recorta y cada elemento visible dentro de `.stage` y `.bar`; los contenedores con scroll propio (`.seg`) se miden ellos.
 */
async function expectNoHorizontalScroll(page: Page) {
  const { scrollWidth, innerWidth, offenders } = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth
    const out: string[] = []
    const describe = (el: Element) => `${el.tagName.toLowerCase()}${el.id ? `#${el.id}` : ''}.${String(el.getAttribute('class') ?? '').trim().replace(/\s+/g, '.')}`
    for (const sel of ['.stage', '.main', '.bar']) {
      document.querySelectorAll<HTMLElement>(sel).forEach((el) => {
        if (el.scrollWidth > el.clientWidth + 1) out.push(`${describe(el)} scrollWidth ${el.scrollWidth} > ${el.clientWidth}`)
      })
    }
    const OWN_SCROLL = '.seg'
    const roots = document.querySelectorAll('.stage, .bar')
    const scope: Element[] = roots.length > 0 ? Array.from(roots) : [document.body]
    for (const root of scope) {
      root.querySelectorAll('*').forEach((el) => {
        if (el.parentElement?.closest(OWN_SCROLL)) return
        const style = getComputedStyle(el)
        if (style.visibility === 'hidden' || style.display === 'none') return
        const r = el.getBoundingClientRect()
        if (r.width > 0 && (r.right > vw + 1 || r.left < -1)) out.push(`${describe(el)} [${Math.round(r.left)}, ${Math.round(r.right)}] fuera de 0..${vw}`)
      })
    }
    return { scrollWidth: document.documentElement.scrollWidth, innerWidth: window.innerWidth, offenders: out }
  })
  expect(scrollWidth).toBeLessThanOrEqual(innerWidth)
  expect(offenders).toEqual([])
}

test.describe('Lote F7A — escritorio', () => {
  test.skip(({ isMobile }) => isMobile, 'recorrido de escritorio')
  test.describe.configure({ mode: 'serial' })

  test.beforeAll(async ({ request }, testInfo) => {
    // en el proyecto móvil estos pasos se saltan: no se prepara nada
    if (testInfo.project.use.isMobile) return
    await prepareData(request)
  })

  test.afterAll(async ({ request }) => {
    await cleanupData(request)
  })

  test("1. Pulso: el panel Almacén trae el selector de almacén y el control 'Categoría o producto'", async ({ page }) => {
    await login(page, ADMIN)
    const panel = pulseSection(page, 'Almacén')
    await expect(panel).toBeVisible()
    await expect(panel.getByRole('combobox', { name: 'Almacén' })).toHaveValue('')
    await expect(panel.getByRole('button', { name: 'Categoría o producto' })).toBeVisible()
    await scrollToPanel(page, 'Almacén')
    await shot(page, 'pulso-almacen')
  })

  test("2. categoría: En mano 3 y Bajo mínimo 1 y 'Ver en Inventario' abre Saldos con los mismos filtros; producto: Bajo mínimo 'Sí' y el enlace abre el Kárdex filtrado", async ({ page }) => {
    await login(page, ADMIN)
    await scrollToPanel(page, 'Almacén')
    await pickCategoryOrProduct(page, CATEGORY, new RegExp(CATEGORY), 'pulso-almacen-buscador')
    await expect(tile(page, 'En mano').getByText('3', { exact: true })).toBeVisible()
    await expect(tile(page, 'En mano')).toContainText(`${CATEGORY} · 1 producto`)
    await expect(tile(page, 'Bajo mínimo').getByText('1', { exact: true })).toBeVisible()
    await scrollToPanel(page, 'Almacén')
    await shot(page, 'pulso-almacen-categoria')
    await expect(tile(page, 'Bajo mínimo').getByRole('link', { name: 'Ver en Inventario ›' })).toHaveAttribute('href', `/warehouse/inventory?categoryIds=${data.categoryId}`)

    // con almacén elegido el enlace lo lleva también: Saldos consulta al API real con almacén + categoría y cuadra con 'En mano'
    const whSelect = pulseSection(page, 'Almacén').getByRole('combobox', { name: 'Almacén' })
    await whSelect.selectOption(data.warehousePublicId)
    await expect(tile(page, 'En mano').getByText('3', { exact: true })).toBeVisible()
    const inventoryLink = tile(page, 'Bajo mínimo').getByRole('link', { name: 'Ver en Inventario ›' })
    await expect(inventoryLink).toHaveAttribute(
      'href',
      `/warehouse/inventory?categoryIds=${data.categoryId}&warehousePublicIds=${encodeURIComponent(data.warehousePublicId)}`,
    )
    const balancesRequest = page.waitForRequest((r) => {
      const u = new URL(r.url())
      return (
        u.pathname === '/api/v1/inventory/balances' &&
        u.searchParams.getAll('categoryIds').includes(String(data.categoryId)) &&
        u.searchParams.getAll('warehousePublicIds').includes(data.warehousePublicId)
      )
    })
    await inventoryLink.click()
    await balancesRequest
    await expect(page.getByRole('tab', { name: 'Saldos' })).toHaveAttribute('aria-selected', 'true')
    const balanceRows = page.getByRole('row').filter({ hasText: SKU })
    await expect(balanceRows).toHaveCount(1)
    await expect(balanceRows.getByText('3', { exact: true }).first()).toBeVisible()
    await shot(page, 'inventario-saldos-desde-pulso')

    // de vuelta en Pulso: sin almacén (el resto del recorrido usa 'Todos')
    await page.goBack()
    await whSelect.selectOption('')
    await expect(whSelect).toHaveValue('')

    await pickCategoryOrProduct(page, SKU, new RegExp(`^${SKU} · `))
    await expect(tile(page, 'En mano').getByText('3', { exact: true })).toBeVisible()
    await expect(tile(page, 'Bajo mínimo').getByText('Sí', { exact: true })).toBeVisible()
    await scrollToPanel(page, 'Almacén')
    await shot(page, 'pulso-almacen-producto')
    await tile(page, 'Bajo mínimo').getByRole('link', { name: `Ver Kárdex de ${SKU} ›` }).click()

    await expect(page).toHaveURL(/\/warehouse\/inventory\?tab=kardex&product=/)
    await expect(page.getByRole('tab', { name: 'Kárdex' })).toHaveAttribute('aria-selected', 'true')
    // el filtro Producto llega de la URL con su SKU resuelto
    await expect(page.getByRole('button', { name: `Quitar ${SKU}` })).toBeVisible()
    const rows = page.getByRole('row').filter({ hasText: SKU })
    await expect(rows).toHaveCount(1)
    await expect(rows.getByText('+3')).toBeVisible()
    await shot(page, 'inventario-kardex-desde-pulso')
  })

  test('3. al recargar se conserva la selección; ✕ vuelve a los totales generales', async ({ page }) => {
    await login(page, ADMIN)
    await expect(tile(page, 'En mano')).not.toContainText('…')
    await pickCategoryOrProduct(page, CATEGORY, new RegExp(CATEGORY))
    await expect(tile(page, 'En mano')).toContainText(`${CATEGORY} · 1 producto`)
    await page.reload()
    const trigger = pulseSection(page, 'Almacén').getByRole('button', { name: /^Categoría o producto/ })
    await expect(trigger).toHaveAccessibleName(new RegExp(`^Categoría o producto: Categoría .*${CATEGORY}$`))
    await expect(tile(page, 'En mano')).toContainText(`${CATEGORY} · 1 producto`)

    // Tras quitar la selección la tarjeta vuelve a los totales generales: se comparan con el totalOnHand de la misma
    // respuesta sin filtro que recibe el panel (no con una lectura anterior, porque los otros recorridos en paralelo
    // mueven inventario y el total general cambia mientras corre esta prueba).
    const balancesResponse = page.waitForResponse((r) => {
      const u = new URL(r.url())
      return u.pathname === '/api/v1/inventory/balances' && !u.searchParams.has('categoryIds') && !u.searchParams.has('productPublicIds')
    })
    await pulseSection(page, 'Almacén').getByRole('button', { name: 'Quitar categoría o producto' }).click()
    const general = (await (await balancesResponse).json()) as { totalOnHand: number }
    await expect(trigger).toHaveAccessibleName('Categoría o producto')
    await expect(tile(page, 'En mano')).not.toContainText(CATEGORY)
    const generalText = Number.isInteger(general.totalOnHand)
      ? general.totalOnHand.toLocaleString('en-US', { maximumFractionDigits: 0 })
      : general.totalOnHand.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    await expect(tile(page, 'En mano')).toHaveText(`En mano${generalText}`)
    await expect(tile(page, 'Bajo mínimo').getByRole('link')).toHaveCount(0)
    // sin selección no queda nada guardado: otra recarga sigue en 'Todos'
    await page.reload()
    await expect(trigger).toHaveAccessibleName('Categoría o producto')
  })

  test("4. Actividad reciente: 'Ajuste de inventario' obligatorio; 'Solo obligatorios' oculta 'Recolección creada'", async ({ page, request }) => {
    // Recolección de 1 unidad del producto (la deshace el afterAll)
    const res = await request.post(`${API_URL}/api/v1/pick-batches`, {
      headers: auth(),
      data: { warehousePublicId: data.warehousePublicId, lines: [{ productPublicId: data.productPublicId, quantity: 1, binId: data.binId }] },
    })
    expect(res.ok()).toBeTruthy()
    data.pick = (await res.json()) as PickBatchDto
    const batchNumber = data.pick.number ?? ''
    expect(batchNumber).toMatch(/^EMP-/)

    await login(page, ADMIN)
    const panel = pulseSection(page, 'Actividad reciente')
    const tabs = panel.getByRole('tablist', { name: 'Módulos de la actividad' })
    await expect(tabs.getByRole('tab', { name: 'Almacén' })).toHaveAttribute('aria-selected', 'true')
    const table = panel.getByRole('table', { name: 'Eventos recientes' })
    const adjustment = table.getByRole('row').filter({ hasText: SKU }).filter({ hasText: 'Ajuste de inventario' })
    await expect(adjustment).toHaveCount(1)
    await expect(adjustment.getByText('oblig.')).toBeVisible()
    // ajuste de inventario: el enlace va al Kárdex filtrado por el producto, no a la ficha
    await expect(adjustment.getByRole('link', { name: SKU })).toHaveAttribute(
      'href',
      `/warehouse/inventory?tab=kardex&product=${encodeURIComponent(data.productPublicId)}`,
    )
    const pick = table.getByRole('row').filter({ hasText: batchNumber })
    await expect(pick.getByText('Recolección creada')).toBeVisible()
    await expect(pick.getByText('oblig.')).toHaveCount(0)
    await scrollToPanel(page, 'Actividad reciente')
    await shot(page, 'pulso-actividad')

    // buscador libre sobre lo cargado: el SKU deja solo el ajuste
    await panel.getByRole('searchbox').fill(SKU)
    await expect(pick).toHaveCount(0)
    await expect(adjustment).toHaveCount(1)
    await panel.getByRole('searchbox').fill('')
    await expect(pick).toHaveCount(1)

    await panel.getByRole('switch', { name: 'Solo obligatorios' }).check()
    await expect(pick).toHaveCount(0)
    await expect(adjustment).toHaveCount(1)
    await scrollToPanel(page, 'Actividad reciente')
    await shot(page, 'pulso-actividad-obligatorios')
  })

  test('5. ventana Hoy e idioma inglés sin recargar: las etiquetas de los eventos llegan del servidor en inglés', async ({ page }) => {
    await login(page, ADMIN)
    // marca en la ventana: si la página se recargara, desaparecería
    await page.evaluate(() => Object.assign(window, { __f7aNoReload: true }))
    const panel = pulseSection(page, 'Actividad reciente')
    const table = panel.getByRole('table', { name: 'Eventos recientes' })
    await expect(table.getByRole('row').filter({ hasText: SKU }).getByText('Ajuste de inventario')).toBeVisible()

    const [today] = await Promise.all([
      page.waitForRequest((r) => r.url().includes('/api/v1/analytics/activity') && new URL(r.url()).searchParams.get('window') === 'today'),
      panel.getByRole('combobox', { name: 'Ventana de tiempo' }).selectOption('today'),
    ])
    expect(today.headers()['accept-language']).toMatch(/^es/)
    await expect(table.getByRole('row').filter({ hasText: SKU }).getByText('Ajuste de inventario')).toBeVisible()

    const [english] = await Promise.all([
      page.waitForRequest((r) => r.url().includes('/api/v1/analytics/activity') && /^en/.test(r.headers()['accept-language'] ?? '')),
      (async () => {
        await page.getByRole('button', { name: 'Idioma' }).click()
        await page.getByRole('menuitemradio', { name: 'English' }).click()
      })(),
    ])
    expect(new URL(english.url()).searchParams.get('window')).toBe('today')
    const panelEn = pulseSection(page, 'Recent activity')
    const tableEn = panelEn.getByRole('table', { name: 'Recent events' })
    await expect(tableEn.getByRole('row').filter({ hasText: SKU }).getByText('Inventory adjustment')).toBeVisible()
    // la ventana elegida se conserva y la página no se recargó
    await expect(panelEn.getByRole('combobox', { name: 'Time window' })).toHaveValue('today')
    await expect(pulseSection(page, 'Warehouse')).toBeVisible()
    expect(await page.evaluate(() => (window as unknown as Record<string, unknown>).__f7aNoReload)).toBe(true)
    await scrollToPanel(page, 'Recent activity')
    await shot(page, 'pulso-actividad-ingles')

    // vuelve al español (solo vive en el localStorage de este contexto, pero se deja como estaba)
    await page.getByRole('button', { name: 'Language' }).click()
    await page.getByRole('menuitemradio', { name: 'Español' }).click()
    await expect(pulseSection(page, 'Actividad reciente')).toBeVisible()
  })

  test('6. despacho@ (sin inventory.view): Pulso sin panel Almacén y sin panel Actividad reciente', async ({ page }) => {
    const activityResponse = page.waitForResponse((r) => new URL(r.url()).pathname === '/api/v1/analytics/activity')
    await login(page, DISPATCH)
    await page.waitForLoadState('networkidle')
    await expect(page.getByRole('heading', { level: 2, name: 'Almacén', exact: true })).toHaveCount(0)
    await expect(page.getByRole('group', { name: 'Filtros del panel Almacén' })).toHaveCount(0)
    // el servidor no devuelve ningún módulo visible: el panel de Actividad reciente no se pinta
    const activityRes = await activityResponse
    expect(activityRes.status()).toBe(200)
    const activityBody = (await activityRes.json()) as { visibleModules?: string[] | null }
    expect(activityBody.visibleModules).toEqual([])
    await expect(pulseSection(page, 'Actividad reciente')).toHaveCount(0)
    await shot(page, 'pulso-sin-almacen')
  })
})

test.describe('Lote F7A — móvil (360 px)', () => {
  test.skip(({ isMobile }) => !isMobile, 'recorrido móvil (360 px)')

  // Datos propios (CAT-M-/PROD-M-{timestamp}): la actividad del móvil no depende de lo que haga el proyecto de escritorio.
  test.beforeAll(async ({ request }, testInfo) => {
    if (!testInfo.project.use.isMobile) return
    await prepareData(request, 'M')
  })

  test.afterAll(async ({ request }) => {
    await cleanupData(request)
  })

  test('7. panel Almacén y Actividad reciente en tarjetas, sin scroll horizontal (también con el selector abierto)', async ({ page }) => {
    await login(page, ADMIN)
    expect(page.viewportSize()?.width).toBe(360)
    const warehouse = pulseSection(page, 'Almacén')
    await expect(warehouse).toBeVisible()
    // tarjetas del panel Almacén en una columna: todas del mismo ancho y apiladas
    const onHand = warehouse.getByRole('group', { name: 'En mano', exact: true })
    const available = warehouse.getByRole('group', { name: 'Disponible', exact: true })
    await expect(onHand).toBeVisible()
    const [a, b] = [await onHand.boundingBox(), await available.boundingBox()]
    expect(a && b && Math.abs(a.x - b.x) < 1 && b.y > a.y).toBeTruthy()
    await expectNoHorizontalScroll(page)

    // el control 'Categoría o producto' ocupa todo el ancho y su lista abierta cabe en la pantalla
    await warehouse.getByRole('button', { name: 'Categoría o producto' }).click()
    await expect(page.getByRole('listbox', { name: 'Categoría o producto' })).toBeVisible()
    await expectNoHorizontalScroll(page)
    await page.keyboard.press('Escape')
    await expect(page.getByRole('listbox', { name: 'Categoría o producto' })).toHaveCount(0)

    // con la categoría elegida la píldora larga tampoco desborda
    await pickCategoryOrProduct(page, data.category, new RegExp(data.category))
    await expect(onHand).toContainText(`${data.category} · 1 producto`)
    await expectNoHorizontalScroll(page)

    // bajo 720 px la actividad son tarjetas, nunca una tabla: el ajuste del producto aparece como tarjeta
    const activity = pulseSection(page, 'Actividad reciente')
    await expect(activity).toBeVisible()
    await expect(activity.locator('table')).toHaveCount(0)
    const cards = activity.getByRole('list', { name: 'Eventos recientes' })
    const card = cards.getByRole('listitem').filter({ hasText: data.sku }).filter({ hasText: 'Ajuste de inventario' })
    await expect(card).toHaveCount(1)
    await expect(card.getByText('oblig.')).toBeVisible()
    await expectNoHorizontalScroll(page)
    await scrollToPanel(page, 'Actividad reciente')
    await shot(page, 'pulso-movil')

    // se deja sin selección (la preferencia vive en el localStorage del contexto, pero se deja como estaba)
    await warehouse.getByRole('button', { name: 'Quitar categoría o producto' }).click()
  })
})
